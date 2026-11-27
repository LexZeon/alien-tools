using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using NightGlow.Core.Interop;

namespace NightGlow.Core.Thermal;

/// <summary>
/// Owns one MTA worker thread for all BIOS WMI calls (the SDK requires MTA and must not run on
/// the UI's STA thread). Polls every 2 seconds and publishes readings on the UI thread.
/// </summary>
public sealed partial class ThermalService : ObservableObject, IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private readonly DispatcherQueue _ui;
    private readonly IThermalBackend _backend;
    private Timer? _poll;

    public bool IsSimulated => _backend.IsSimulated;

    public ObservableCollection<FanReading> Fans { get; } = new();
    public ObservableCollection<SensorReading> Sensors { get; } = new();
    public ObservableCollection<PowerModeInfo> PowerModes { get; } = new();

    [ObservableProperty] private ThermalOpenResult _state = ThermalOpenResult.NoCore;
    [ObservableProperty] private PowerModeInfo? _currentMode;
    [ObservableProperty] private bool _isReady;
    [ObservableProperty] private string _statusText = "正在连接 BIOS 接口…";

    public ThermalService(DispatcherQueue ui)
    {
        _ui = ui;
        _backend = NativeMethods.TryProbe(out _) ? new NativeThermalBackend() : new DemoThermalBackend();
        _thread = new Thread(Worker) { IsBackground = true, Name = "NightGlow thermal (MTA)" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    private void Worker()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            try { work(); }
            catch (Exception ex) { Post(() => StatusText = "BIOS 调用失败：" + ex.Message); }
        }
    }

    private void Post(Action a) => _ui.TryEnqueue(() => a());

    public void Start()
    {
        _queue.Add(() =>
        {
            var result = _backend.Open();
            var modes = result == ThermalOpenResult.Ok ? _backend.PowerModes() : Array.Empty<byte>();
            Post(() =>
            {
                State = result;
                PowerModes.Clear();
                foreach (var m in modes) PowerModes.Add(PowerModeInfo.FromCode(m));
                IsReady = result == ThermalOpenResult.Ok;
                StatusText = result switch
                {
                    ThermalOpenResult.Ok when _backend.IsSimulated => "演示数据（未加载核心库，数值是模拟的）",
                    ThermalOpenResult.Ok => "已连接 BIOS 接口",
                    ThermalOpenResult.NeedAdmin => "读取温度、风扇和切换模式需要管理员权限",
                    ThermalOpenResult.NotFound => "这台电脑没有兼容的 BIOS 接口",
                    _ => "无法连接 BIOS 接口",
                };
            });
            if (result == ThermalOpenResult.Ok)
                _poll = new Timer(_ => QueuePoll(), null, 0, 2000);
        });
    }

    private int _pollPending;

    private void QueuePoll()
    {
        // Skip if the previous poll has not run yet (slow WMI), so the queue never grows.
        if (Interlocked.Exchange(ref _pollPending, 1) == 1) return;
        try { _queue.Add(() => { Interlocked.Exchange(ref _pollPending, 0); Poll(); }); }
        catch (InvalidOperationException) { /* disposed */ }
    }

    private void Poll()
    {
        var fans = _backend.ReadFans();
        var sensors = _backend.ReadSensors();
        int mode = _backend.GetPowerMode();
        Post(() =>
        {
            Sync(Fans, fans);
            Sync(Sensors, sensors);
            CurrentMode = mode > 0 ? PowerModes.FirstOrDefault(m => m.Code == mode) ?? PowerModeInfo.FromCode((byte)mode) : null;
        });
    }

    private static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> src)
    {
        // Replace in place to keep bound item containers stable.
        for (int i = 0; i < src.Count; i++)
        {
            if (i < target.Count) { if (!Equals(target[i], src[i])) target[i] = src[i]; }
            else target.Add(src[i]);
        }
        while (target.Count > src.Count) target.RemoveAt(target.Count - 1);
    }

    public Task<bool> SetPowerModeAsync(byte code)
    {
        var tcs = new TaskCompletionSource<bool>();
        _queue.Add(() =>
        {
            bool ok = _backend.SetPowerMode(code);
            tcs.SetResult(ok);
            Poll();
        });
        return tcs.Task;
    }

    public void Dispose()
    {
        _poll?.Dispose();
        _queue.Add(() => { try { _backend.Close(); } catch { } });
        _queue.CompleteAdding();
    }
}
