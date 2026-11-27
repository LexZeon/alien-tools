using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using NightGlow.Core;
using NightGlow.Core.Lighting;

namespace NightGlow.Pages;

/// <summary>Row view-model for one light zone.</summary>
public sealed partial class ZoneRow : ObservableObject
{
    public required LightDevice Device { get; init; }
    public required LightZone Zone { get; init; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private SolidColorBrush _brush = new(Microsoft.UI.Colors.Black);

    public string IdText => Zone.Id.ToString();
    public string FlagText => Zone.IsPowerButton ? " · 电源键" : Zone.IsIndicator ? " · 指示灯" : "";
    public Rgb Color { get; set; } = Rgb.Black;

    // region assignment: 0 auto, 1 ring, 2 logo, 3 touchpad, 4 other, 5 keyboard, 6 none
    private const int NoneIndex = 6;
    [ObservableProperty] private int _regionIndex;
    [ObservableProperty] private double _order;
    public bool HasOrder => RegionIndex is 1 or 5;

    /// <summary>Second-line hint: which region this light ends up in.</summary>
    public string RegionText => RegionIndex switch
    {
        0 => " · 自动识别为" + Regions.Name(SpatialLayout.GuessRegion(Device, Zone)),
        NoneIndex => " · 不参与灯效",
        _ => "",
    };

    public event EventHandler? AssignmentChanged;
    partial void OnRegionIndexChanged(int value)
    {
        OnPropertyChanged(nameof(HasOrder));
        OnPropertyChanged(nameof(RegionText));
        AssignmentChanged?.Invoke(this, EventArgs.Empty);
    }
    partial void OnOrderChanged(double value) => AssignmentChanged?.Invoke(this, EventArgs.Empty);

    public static readonly RegionKind?[] IndexToKind = { null, RegionKind.Ring, RegionKind.Logo, RegionKind.Touchpad, RegionKind.Other, RegionKind.Keyboard, null };

    public string? ToAssignment() => RegionIndex switch
    {
        0 => null,
        NoneIndex => "none",
        _ => SpatialLayout.FormatAssignment(IndexToKind[RegionIndex], double.IsNaN(Order) ? 0 : (int)Order),
    };

    public static (int Index, int Order) FromAssignment(string? s)
    {
        if (s is null) return (0, 0);
        if (s == "none") return (NoneIndex, 0);
        var (kind, order) = SpatialLayout.ParseAssignment(s);
        int idx = Array.IndexOf(IndexToKind, kind, 1);
        return (idx < 1 ? 0 : idx, order);
    }
}

public sealed partial class LightingPage : Page, IRefreshable
{
    private readonly ObservableCollection<ZoneRow> _rows = new();
    private LightDevice? _device;
    private ZoneRow? _editing;
    private bool _loading;

    public LightingPage()
    {
        InitializeComponent();
        ZoneList.ItemsSource = _rows;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => Refresh();

    public void Refresh()
    {
        DeviceListView.ItemsSource = App.Lighting.Devices;
        if (DeviceListView.SelectedItem is null && App.Lighting.Devices.Count > 0)
            DeviceListView.SelectedIndex = 0;
        ShowDevice(DeviceListView.SelectedItem as LightDevice);
    }

    private void DeviceListView_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ShowDevice(DeviceListView.SelectedItem as LightDevice);

    private static string Key(LightDevice d, ushort id) => $"{d.IdText}/{id}";

    private void ShowDevice(LightDevice? dev)
    {
        _device = dev;
        DetailPanel.Visibility = dev is null ? Visibility.Collapsed : Visibility.Visible;
        if (dev is null) return;

        _loading = true;
        DevTitle.Text = dev.Kind;
        DevSub.Text = $"{dev.Name} · {dev.IdText} · {dev.ApiText}" + (dev.HasHardwareEffects ? " · 支持硬件整体灯效" : "");
        BrightnessSlider.Value = Math.Round(dev.Brightness / 2.55);
        _loading = false;

        _rows.Clear();
        foreach (var z in dev.Zones)
        {
            var color = App.Settings.ZoneColors.TryGetValue(Key(dev, z.Id), out var hex) && Rgb.TryParse(hex, out var c) ? c : Rgb.Black;
            var (idx, order) = ZoneRow.FromAssignment(App.Settings.LightRegions.GetValueOrDefault(Key(dev, z.Id)));
            var row = new ZoneRow
            {
                Device = dev, Zone = z, Name = z.Name, Color = color, Brush = new SolidColorBrush(color.ToColor()),
                RegionIndex = idx, Order = idx == 0 ? z.Id : order,
            };
            row.AssignmentChanged += Row_AssignmentChanged;
            _rows.Add(row);
        }
        bool hasZones = dev.Zones.Count > 0;
        bool keyboard = SpatialLayout.IsBuiltInKeyboard(dev);
        NoZonesInfo.IsOpen = !hasZones && !keyboard;
        ZonesSection.Visibility = hasZones ? Visibility.Visible : Visibility.Collapsed;
        CalibExpander.IsExpanded = false;
        ProbeStatus.Text = "";

        EndIdentify(rebuild: false);
        IdentifyExpander.Visibility = keyboard ? Visibility.Collapsed : Visibility.Visible;
        IdentifyExpander.IsExpanded = !hasZones && !keyboard;
        UpdateRingTools();

        EndKeyCalibration(rebuild: false);
        KeyCalibExpander.Visibility = keyboard ? Visibility.Visible : Visibility.Collapsed;
        UpdateKeyStatus();
    }

    // ---- light identification wizard (chassis lights) ----

    private bool _idActive;
    private int _idCur;
    private int? _idLit;
    private const int MaxLightId = 63;

    /// <summary>Ring zones of the current device, in ring order.</summary>
    private List<(ushort Id, int Order)> RingZones()
    {
        if (_device is null) return new();
        return _device.Zones
            .Select(z => (z.Id, A: SpatialLayout.ParseAssignment(App.Settings.LightRegions.GetValueOrDefault(Key(_device, z.Id)))))
            .Where(x => x.A.Kind == RegionKind.Ring)
            .OrderBy(x => x.A.Order).ThenBy(x => x.Id)
            .Select(x => (x.Id, x.A.Order)).ToList();
    }

    private void UpdateRingTools()
    {
        int n = RingZones().Count;
        RingTools.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        IdStatus.Text = n > 0 ? $"背部灯环：{n} 段。" : "";
    }

    private async void IdStart_Click(object sender, RoutedEventArgs e)
    {
        if (_device is null) return;
        App.Lighting.StopSoftwareEffect();
        // start over: forget earlier ring/logo assignments of this device
        foreach (var z in _device.Zones) App.Settings.LightRegions.Remove(Key(_device, z.Id));
        _idActive = true;
        _idCur = 0;
        _idLit = null;
        IdStartButton.IsEnabled = false;
        IdStopButton.IsEnabled = true;
        IdChoices.Visibility = Visibility.Visible;
        await App.Lighting.SetDeviceColorAsync(_device, Rgb.Black);
        await LightCurrent();
    }

    private async Task LightCurrent()
    {
        if (_device is null) return;
        if (_idCur > MaxLightId) { EndIdentify(rebuild: true); return; }
        if (_idLit is { } prev) await App.Lighting.ProbeLightAsync(_device, (ushort)prev, Rgb.Black);
        await App.Lighting.ProbeLightAsync(_device, (ushort)_idCur, Rgb.White);
        _idLit = _idCur;
        IdPrompt.Text = $"编号 {_idCur} 亮了吗？它在哪里？" + (App.Lighting.IsSimulated ? "（演示模式不会真的亮灯）" : "");
    }

    private async void IdChoice_Click(object sender, RoutedEventArgs e)
    {
        if (!_idActive || _device is null || (sender as FrameworkElement)?.Tag is not string choice) return;
        var id = (ushort)_idCur;
        if (choice != "skip")
        {
            int ringCount = RingZones().Count;
            var (name, assign) = choice switch
            {
                "ring" => ($"灯环 {ringCount + 1}", $"ring:{ringCount}"),
                "logo" => ("Logo", "logo"),
                "touchpad" => ("触控板", "touchpad"),
                _ => ($"灯 {id}", "other"),
            };
            var zone = _device.Zones.FirstOrDefault(z => z.Id == id);
            if (zone is null) await App.Lighting.AddZoneAsync(_device, id, name);
            else await App.Lighting.RenameZoneAsync(_device, zone, name);
            App.Settings.LightRegions[Key(_device, id)] = assign;
            App.Settings.Save();
            IdStatus.Text = $"编号 {id} → {name}";
        }
        _idCur++;
        await LightCurrent();
    }

    private void IdStop_Click(object sender, RoutedEventArgs e) => EndIdentify(rebuild: true);

    private void EndIdentify(bool rebuild)
    {
        if (!_idActive && !rebuild) return;
        _idActive = false;
        IdStartButton.IsEnabled = true;
        IdStopButton.IsEnabled = false;
        IdChoices.Visibility = Visibility.Collapsed;
        IdPrompt.Text = "点“开始识别”。";
        if (rebuild && _device is not null)
        {
            App.Settings.Save();
            App.Lighting.RebuildLayout();
            var dev = _device;
            ShowDevice(dev);
            IdStatus.Text = RingZones().Count > 0
                ? $"完成：背部灯环 {RingZones().Count} 段。点“测试流动”看看是不是从左上角开始顺时针走。"
                : "完成。";
        }
    }

    private void SaveRingOrder(List<(ushort Id, int Order)> ring)
    {
        if (_device is null) return;
        foreach (var (id, order) in ring) App.Settings.LightRegions[Key(_device, id)] = $"ring:{order}";
        App.Settings.Save();
        App.Lighting.RebuildLayout();
        ShowDevice(_device);
    }

    private void RingReverse_Click(object sender, RoutedEventArgs e)
    {
        var ring = RingZones();
        int n = ring.Count;
        SaveRingOrder(ring.Select((r, i) => (r.Id, n - 1 - i)).ToList());
        IdStatus.Text = "已反转灯环方向。";
    }

    private void RingRotate_Click(object sender, RoutedEventArgs e)
    {
        var ring = RingZones();
        int n = ring.Count;
        SaveRingOrder(ring.Select((r, i) => (r.Id, (i + 1) % n)).ToList());
        IdStatus.Text = "起点已后移一格。";
    }

    private async void RingTest_Click(object sender, RoutedEventArgs e)
    {
        if (_device is null) return;
        var dev = _device;
        var ring = RingZones();
        if (ring.Count == 0) return;
        App.Lighting.StopSoftwareEffect();
        await App.Lighting.SetDeviceColorAsync(dev, Rgb.Black);
        IdStatus.Text = "测试中：灯光应该从左上角开始，顺时针跑一圈。";
        ushort? prev = null;
        foreach (var (id, _) in ring)
        {
            if (prev is { } p) await App.Lighting.ProbeLightAsync(dev, p, Rgb.Black);
            await App.Lighting.ProbeLightAsync(dev, id, new Rgb(0x35, 0xD6, 0xE8));
            prev = id;
            await Task.Delay(180);
        }
        IdStatus.Text = "测试完成。方向反了点“反转方向”；起点不在左上角就点“起点后移一格”。";
    }

    private void Row_AssignmentChanged(object? sender, EventArgs e)
    {
        if (sender is not ZoneRow row) return;
        var key = Key(row.Device, row.Zone.Id);
        var a = row.ToAssignment();
        if (a is null) App.Settings.LightRegions.Remove(key);
        else App.Settings.LightRegions[key] = a;
        App.Settings.Save();
        App.Lighting.RebuildLayout();
    }

    // ---- key calibration ----

    private bool _keyActive;
    private int _keyId;
    private readonly Stack<(int Id, int? Previous)> _keyHistory = new();
    private const int MaxKeyId = 255;

    private int MappedKeyCount(LightDevice dev) =>
        App.Settings.KeyMap.Keys.Count(k => k.StartsWith(dev.IdText + "/", StringComparison.Ordinal));

    private void UpdateKeyStatus()
    {
        if (_device is null) return;
        int n = MappedKeyCount(_device);
        KeyStatus.Text = n == 0 ? "还没有标定键位。" : $"已标定 {n} 个键。";
    }

    private async void KeyStart_Click(object sender, RoutedEventArgs e)
    {
        if (_device is null) return;
        _keyActive = true;
        _keyHistory.Clear();
        _keyId = 0;
        KeyStartButton.IsEnabled = false;
        KeySkipButton.IsEnabled = KeyStopButton.IsEnabled = true;
        KeyUndoButton.IsEnabled = false;
        KeyCapture.Focus(FocusState.Programmatic);
        await ProbeCurrentKey();
    }

    private async Task ProbeCurrentKey()
    {
        if (_device is null) return;
        if (_keyId > MaxKeyId) { EndKeyCalibration(rebuild: true); return; }
        await App.Lighting.ProbeOnlyAsync(_device, (ushort)_keyId, Rgb.White);
        KeyPrompt.Text = $"编号 {_keyId} 已点亮（白色）。请按下亮着的那个键，没亮就点“跳过”。"
            + (App.Lighting.IsSimulated ? "\n演示模式下不会真的亮灯，可以随便按键试试流程。" : "");
    }

    private async void KeySkip_Click(object sender, RoutedEventArgs e)
    {
        if (!_keyActive) return;
        _keyHistory.Push((_keyId, null));
        KeyUndoButton.IsEnabled = true;
        _keyId++;
        KeyCapture.Focus(FocusState.Programmatic);
        await ProbeCurrentKey();
    }

    private async void KeyUndo_Click(object sender, RoutedEventArgs e)
    {
        if (!_keyActive || _device is null || _keyHistory.Count == 0) return;
        var (id, prev) = _keyHistory.Pop();
        var key = Key(_device, (ushort)id);
        if (prev is null) App.Settings.KeyMap.Remove(key);
        _keyId = id;
        KeyUndoButton.IsEnabled = _keyHistory.Count > 0;
        UpdateKeyStatus();
        KeyCapture.Focus(FocusState.Programmatic);
        await ProbeCurrentKey();
    }

    private void KeyStop_Click(object sender, RoutedEventArgs e) => EndKeyCalibration(rebuild: true);

    private void KeyClear_Click(object sender, RoutedEventArgs e)
    {
        if (_device is null) return;
        var prefix = _device.IdText + "/";
        foreach (var k in App.Settings.KeyMap.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            App.Settings.KeyMap.Remove(k);
        App.Settings.Save();
        App.Lighting.RebuildLayout();
        UpdateKeyStatus();
    }

    private void KeyCapture_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) =>
        KeyCapture.Focus(FocusState.Pointer);

    private async void KeyCapture_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (!_keyActive || _device is null) return;
        e.Handled = true; // keep Tab, arrows, Space etc. from moving focus while mapping
        int code = KeyboardLayout.CodeOf(e.KeyStatus.ScanCode, e.KeyStatus.IsExtendedKey);
        if (code == 0) return;
        if (!KeyboardLayout.TryGet(code, out var k))
        {
            KeyPrompt.Text = $"这个键（0x{code:X}）不在布局表里，点“跳过”继续。";
            return;
        }
        // a key belongs to one light: drop an older mapping of the same key
        var prefix = _device.IdText + "/";
        foreach (var old in App.Settings.KeyMap.Where(kv => kv.Value == code && kv.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(kv => kv.Key).ToList())
            App.Settings.KeyMap.Remove(old);

        var key = Key(_device, (ushort)_keyId);
        _keyHistory.Push((_keyId, null));
        App.Settings.KeyMap[key] = code;
        App.Settings.Save();
        KeyUndoButton.IsEnabled = true;
        KeyStatus.Text = $"编号 {_keyId} = {k.Label}。已标定 {MappedKeyCount(_device)} 个键。";
        _keyId++;
        await ProbeCurrentKey();
    }

    private void EndKeyCalibration(bool rebuild)
    {
        if (!_keyActive && !rebuild) return;
        _keyActive = false;
        KeyStartButton.IsEnabled = true;
        KeySkipButton.IsEnabled = KeyStopButton.IsEnabled = KeyUndoButton.IsEnabled = false;
        KeyPrompt.Text = "点“开始”后，在这里按键。";
        if (rebuild)
        {
            App.Settings.Save();
            App.Lighting.RebuildLayout();
            UpdateKeyStatus();
            if (_device is not null) KeyStatus.Text += " 到“灯效”页就能看到逐键效果。";
        }
    }

    private async void ApplyWhole_Click(object sender, RoutedEventArgs e)
    {
        if (_device is null) return;
        var c = Rgb.FromColor(WholePicker.Color);
        WholeSwatch.Background = new SolidColorBrush(WholePicker.Color);
        WholeFlyout.Hide();
        await App.Lighting.SetDeviceColorAsync(_device, c);
        foreach (var r in _rows)
        {
            r.Color = c;
            r.Brush = new SolidColorBrush(c.ToColor());
            App.Settings.ZoneColors[Key(_device, r.Zone.Id)] = c.ToHex();
        }
        App.Settings.Save();
    }

    private async void BrightnessSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading || _device is null) return;
        await App.Lighting.SetBrightnessAsync(_device, (byte)Math.Round(e.NewValue * 2.55));
    }

    private void ZoneColor_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ZoneRow row) return;
        _editing = row;
        ZonePicker.Color = row.Color.ToColor();
        ZoneNameBox.Text = row.Name;
        ZoneFlyout.ShowAt((FrameworkElement)sender);
    }

    private async void ApplyZone_Click(object sender, RoutedEventArgs e)
    {
        var row = _editing;
        if (row is null) return;
        ZoneFlyout.Hide();
        var c = Rgb.FromColor(ZonePicker.Color);
        row.Color = c;
        row.Brush = new SolidColorBrush(ZonePicker.Color);
        App.Settings.ZoneColors[Key(row.Device, row.Zone.Id)] = c.ToHex();
        App.Settings.Save();
        await App.Lighting.SetZoneColorAsync(row.Device, row.Zone, c);

        var newName = ZoneNameBox.Text.Trim();
        if (newName.Length > 0 && newName != row.Name)
        {
            row.Name = newName;
            await App.Lighting.RenameZoneAsync(row.Device, row.Zone, newName);
        }
    }

    private async void Probe_Click(object sender, RoutedEventArgs e)
    {
        if (_device is null || double.IsNaN(ProbeId.Value)) return;
        var id = (ushort)ProbeId.Value;
        await App.Lighting.ProbeLightAsync(_device, id, new Rgb(255, 255, 255));
        ProbeStatus.Text = $"已点亮编号 {id}（白色）。看是哪个灯，起名后保存；没有反应就点“下一个”。";
    }

    private void ProbeNext_Click(object sender, RoutedEventArgs e)
    {
        ProbeId.Value = Math.Min(255, (double.IsNaN(ProbeId.Value) ? 0 : ProbeId.Value) + 1);
        Probe_Click(sender, e);
    }

    private async void SaveZone_Click(object sender, RoutedEventArgs e)
    {
        if (_device is null || double.IsNaN(ProbeId.Value)) return;
        var name = ProbeName.Text.Trim();
        if (name.Length == 0) { ProbeStatus.Text = "请先填名称。"; return; }
        var id = (ushort)ProbeId.Value;
        if (_device.Zones.Any(z => z.Id == id)) { ProbeStatus.Text = $"编号 {id} 已经命名过了。"; return; }
        await App.Lighting.AddZoneAsync(_device, id, name);
        ProbeName.Text = "";
        ShowDevice(_device);
        CalibExpander.IsExpanded = true;
        ProbeStatus.Text = $"已保存：{id} = {name}";
    }
}
