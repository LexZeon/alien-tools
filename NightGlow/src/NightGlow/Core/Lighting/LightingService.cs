using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NightGlow.Core.Interop;

namespace NightGlow.Core.Lighting;

/// <summary>System-wide lighting state on top of the current effect: normal, dimmed or all off.</summary>
public enum LightingMode { Normal = 0, Dim = 1, Off = 2 }

/// <summary>
/// App-level lighting facade. All hardware calls are serialized on one exclusive scheduler,
/// so the UI thread never blocks on HID writes and calls never interleave.
/// </summary>
public sealed partial class LightingService : ObservableObject, IDisposable
{
    private readonly TaskFactory _hw;
    private readonly EffectEngine _engine;
    private readonly AppSettings _settings;

    public ILightingBackend Backend { get; }
    public bool IsSimulated => Backend.IsSimulated;
    public string CoreStatus { get; }

    public ObservableCollection<LightDevice> Devices { get; } = new();

    /// <summary>Every light placed on the shared canvas, grouped into selectable regions.</summary>
    public SpatialLayout Layout { get; private set; } = SpatialLayout.Empty;

    /// <summary>Water-drop ripples shared by the engine and the preview.</summary>
    public RippleField Ripples { get; } = new();

    public event EventHandler? LayoutChanged;

    [ObservableProperty] private string _activeEffectName = "未设置";
    [ObservableProperty] private LightingMode _mode = LightingMode.Normal;

    /// <summary>Dim mode keeps this share of each device's normal brightness.</summary>
    public const int DimPercent = 30;

    /// <summary>Last hardware error (set on the hardware thread, so not observable).</summary>
    public string? LastError { get; private set; }

    public bool IsSoftwareEffectRunning => _engine.IsRunning;

    public LightingService(AppSettings settings)
    {
        _settings = settings;
        _hw = new TaskFactory(new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler);
        if (NativeMethods.TryProbe(out var reason))
        {
            Backend = new NativeLightingBackend();
            CoreStatus = "核心库已加载";
        }
        else
        {
            Backend = new DemoLightingBackend();
            CoreStatus = reason + "，正在使用演示模式（不会控制硬件）";
        }
        _engine = new EffectEngine(this);
    }

    internal Task Run(Action<ILightingBackend> work) => _hw.StartNew(() =>
    {
        try { work(Backend); }
        catch (Exception ex) { LastError = ex.Message; }
    });

    internal Task<T> Run<T>(Func<ILightingBackend, T> work) => _hw.StartNew(() => work(Backend));

    public async Task RefreshAsync()
    {
        var list = await Run(b => b.Open());
        Devices.Clear();
        foreach (var d in list)
        {
            // names saved by NightGlow win: the shared alien-tools store keeps only the ANSI code page
            foreach (var z in d.Zones)
                if (_settings.ZoneNames.TryGetValue(SpatialLayout.Key(d, z.Id), out var name)) z.Name = name;
            Devices.Add(d);
        }
        RebuildLayout();
    }

    private void RememberName(LightDevice dev, ushort id, string name)
    {
        _settings.ZoneNames[SpatialLayout.Key(dev, id)] = name;
        _settings.Save();
    }

    /// <summary>Call after zone names, region assignments or the key map changed.</summary>
    public void RebuildLayout()
    {
        Layout = SpatialLayout.Build(Devices, _settings, IsSimulated, IsSimulated ? "" : SystemInfo.MachineModel);
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetZoneColorAsync(LightDevice dev, LightZone zone, Rgb color)
    {
        StopSoftwareEffect();
        await Run(b => { b.SetColor(dev.Index, zone.Id, color); b.Update(dev.Index); });
    }

    public async Task SetDeviceColorAsync(LightDevice dev, Rgb color)
    {
        StopSoftwareEffect();
        await Run(b => ApplyStatic(b, dev, color));
        ActiveEffectName = "静态";
    }

    public async Task SetBrightnessAsync(LightDevice dev, byte brightness)
    {
        dev.Brightness = brightness;
        await Run(b => b.SetBrightness(dev.Index, brightness, false));
    }

    /// <summary>Applies an effect to whole devices (used by the quick actions on the home page).</summary>
    public Task ApplyEffectAsync(EffectDefinition fx, EffectParameters p, IReadOnlyList<LightDevice> devices)
    {
        var set = devices.Select(d => d.Index).ToHashSet();
        return ApplyEffectAsync(fx, p, Layout.Points.Where(pt => set.Contains(pt.Device.Index)).ToList());
    }

    /// <summary>
    /// Applies an effect to the given lights (usually the points of the selected regions).
    /// Applying also returns the lighting mode to normal. Brightness: hardware effects set the
    /// device brightness to <see cref="EffectParameters.Brightness"/>; software effects already
    /// scale their colors by it, so the device itself is set to full brightness.
    /// </summary>
    public async Task ApplyEffectAsync(EffectDefinition fx, EffectParameters p, IReadOnlyList<LayoutPoint> points)
    {
        _engine.Stop();
        Remember(fx, p, points);
        p = p.For(fx);
        Mode = LightingMode.Normal;
        var devices = points.Select(pt => pt.Device).DistinctBy(d => d.Index).ToList();
        if (fx.Kind == EffectKind.Software)
        {
            await Run(b =>
            {
                foreach (var dev in devices)
                {
                    SetDeviceBrightness(b, dev, 255);
                    // per-key keyboards: leave the firmware effect, so per-key colors show
                    if (UsesPerKey(dev, points)) b.SetHardwareEffect(dev.Index, HardwareEffect.Off, 5, ColorMode.One, Rgb.Black, Rgb.Black);
                }
            });
            _engine.Start(fx, p, points);
            ActiveEffectName = fx.Name;
            return;
        }
        await Run(b =>
        {
            foreach (var group in points.GroupBy(pt => pt.Device.Index))
            {
                var dev = group.First().Device;
                var lights = group.ToList();
                if (fx.Kind == EffectKind.Off) { ApplyColor(b, dev, lights, Rgb.Black); continue; }
                SetDeviceBrightness(b, dev, p.Brightness);
                ApplyHardware(b, dev, lights, fx, p);
            }
        });
        ActiveEffectName = fx.Name;
    }

    /// <summary>
    /// Software effects stop with NightGlow, so the running one is saved and started again on the
    /// next launch. Hardware effects keep running in the controllers by themselves.
    /// </summary>
    private void Remember(EffectDefinition fx, EffectParameters p, IReadOnlyList<LayoutPoint> points)
    {
        _settings.LastMode = (int)LightingMode.Normal;
        _settings.LastEffect = fx.Kind != EffectKind.Software ? null : new SavedEffect
        {
            EffectId = fx.Id,
            Regions = points.Select(pt => pt.Region).Distinct().Select(r => r.ToString()).ToList(),
            Color1 = p.Color1.ToHex(),
            Color2 = p.Color2.ToHex(),
            Speed = p.Speed,
            Brightness = p.Brightness,
            Direction = p.Direction.ToString(),
            Palette = p.PaletteId,
            AutoRain = p.AutoRain,
            Duration = p.Duration,
        };
        _settings.Save();
    }

    /// <summary>Starts the software effect that was running when NightGlow last exited.</summary>
    public async Task<bool> RestoreLastEffectAsync()
    {
        var s = _settings.LastEffect;
        if (s is null || EffectCatalog.Find(s.EffectId) is not { Kind: EffectKind.Software } fx) return false;
        var kinds = s.Regions.Where(r => Enum.TryParse<RegionKind>(r, out _)).Select(Enum.Parse<RegionKind>).ToList();
        var points = Layout.PointsIn(kinds);
        if (points.Count == 0) return false;
        var p = new EffectParameters(
            Rgb.TryParse(s.Color1, out var c1) ? c1 : Rgb.White,
            Rgb.TryParse(s.Color2, out var c2) ? c2 : Rgb.White,
            Math.Clamp(s.Speed, 1, 10), (byte)Math.Clamp(s.Brightness, 0, 255))
        {
            Direction = Enum.TryParse<WaveDirection>(s.Direction, out var d) ? d : WaveDirection.LeftToRight,
            PaletteId = s.Palette,
            AutoRain = s.AutoRain,
            Duration = Math.Clamp(s.Duration, 1, 10),
        };
        var mode = (LightingMode)Math.Clamp(_settings.LastMode, 0, 2);
        await ApplyEffectAsync(fx, p, points);
        if (mode != LightingMode.Normal) await SetModeAsync(mode);
        return true;
    }

    private static void SetDeviceBrightness(ILightingBackend b, LightDevice dev, byte value)
    {
        dev.Brightness = value;
        b.SetBrightness(dev.Index, value, false);
    }

    /// <summary>
    /// Dims or turns off every light without touching the running effect. Each device's normal
    /// brightness is kept, so switching back to <see cref="LightingMode.Normal"/> restores it.
    /// The power button light is left alone.
    /// </summary>
    public async Task SetModeAsync(LightingMode mode)
    {
        Mode = mode;
        if (_settings.LastMode != (int)mode) { _settings.LastMode = (int)mode; _settings.Save(); }
        var devices = Devices.ToList();
        await Run(b =>
        {
            foreach (var dev in devices)
            {
                byte level = mode switch
                {
                    LightingMode.Off => (byte)0,
                    LightingMode.Dim => (byte)Math.Max(8, dev.Brightness * DimPercent / 100),
                    _ => dev.Brightness,
                };
                b.SetBrightness(dev.Index, level, false);
            }
        });
    }

    /// <summary>Stops the software effect (lights keep the last frame) and forgets it for the next launch.</summary>
    public void StopSoftwareEffect()
    {
        _engine.Stop();
        if (_settings.LastEffect is not null) { _settings.LastEffect = null; _settings.Save(); }
    }

    /// <summary>Static color on every zone; keyboards without named zones use their hardware static effect.</summary>
    internal static void ApplyStatic(ILightingBackend b, LightDevice dev, Rgb color)
    {
        if (dev.Zones.Count > 0)
            b.SetColors(dev.Index, dev.Zones.Select(z => (z.Id, color)).ToList());
        else if (dev.HasHardwareEffects)
            b.SetHardwareEffect(dev.Index, HardwareEffect.Static, 5, ColorMode.One, color, color);
    }

    /// <summary>A keyboard driven key by key (needs the firmware effect switched off first).</summary>
    private static bool UsesPerKey(LightDevice dev, IEnumerable<LayoutPoint> points) =>
        dev.Api == LightApi.V5 && points.Any(pt => pt.Device.Index == dev.Index && !pt.IsWholeDevice);

    private static void ApplyColor(ILightingBackend b, LightDevice dev, List<LayoutPoint> lights, Rgb color)
    {
        if (lights.Any(l => l.IsWholeDevice) || (dev.Api == LightApi.V5 && dev.HasHardwareEffects))
        {
            // keyboards: one hardware static color covers every key
            b.SetHardwareEffect(dev.Index, HardwareEffect.Static, 5, ColorMode.One, color, color);
            return;
        }
        var single = lights.Where(l => !l.IsWholeDevice).Select(l => (l.LightId, color)).ToList();
        if (single.Count > 0) b.SetColors(dev.Index, single);
    }

    /// <summary>Morph steps in a hardware wave chain. More steps = smoother colors, longer HID writes.</summary>
    private const int WaveSteps = 12;

    private static void ApplyHardware(ILightingBackend b, LightDevice dev, List<LayoutPoint> lights, EffectDefinition fx, EffectParameters p)
    {
        if (dev.HasHardwareEffects && fx.KeyboardEffect is { } ke)
        {
            // whole-device effect: the controller animates every key itself
            b.SetHardwareEffect(dev.Index, ke, p.KeyboardTempo, fx.KeyboardColorMode, p.Color1, p.Color2);
            return;
        }
        if (fx.ZoneAction is not { } za) return; // effect not available on this device
        var zones = lights.Where(l => !l.IsWholeDevice).ToList();
        if (zones.Count == 0) return;

        var pal = p.GetPalette(fx);
        foreach (var pt in zones)
        {
            LightAction[] actions;
            if (fx.Id == "rainbow-wave")
            {
                // every light runs the same morph chain, started at a different phase:
                // the controller then shows a wave that keeps running without NightGlow
                double offset = SpatialEffects.Project(pt, p.Direction);
                actions = Enumerable.Range(0, WaveSteps)
                    .Select(j => new LightAction(LightActionType.Morph, p.ZoneTime, p.ZoneTempo, pal.Sample((double)j / WaveSteps - offset)))
                    .ToArray();
            }
            else actions = za switch
            {
                LightActionType.Color => new[] { new LightAction(za, 0, 0, p.Color1) },
                LightActionType.Morph => new[] { new LightAction(za, p.ZoneTime, p.ZoneTempo, p.Color1), new LightAction(za, p.ZoneTime, p.ZoneTempo, p.Color2) },
                _ => new[] { new LightAction(za, p.ZoneTime, p.ZoneTempo, p.Color1) },
            };
            b.SetAction(dev.Index, pt.LightId, actions);
        }
        b.Update(dev.Index);
    }

    /// <summary>Whether an effect can show on a region at all, and how (for the UI).</summary>
    public static (bool Ok, string Note) Supports(LightRegion region, EffectDefinition fx)
    {
        if (fx.Kind == EffectKind.Off) return (true, "");
        if (fx.Kind == EffectKind.Software)
            return (true, region.IsWholeDevice ? "整块同色（还没标定键位）" : "NightGlow 实时计算");
        bool any = region.Points.Any(pt =>
            (pt.Device.HasHardwareEffects && fx.KeyboardEffect is not null) || (fx.ZoneAction is not null && !pt.IsWholeDevice));
        if (!any) return (false, "这个区域不支持此硬件灯效");
        return (true, region.Points.Any(pt => pt.Device.HasHardwareEffects) && fx.KeyboardEffect is not null ? "由键盘固件运行" : "写入灯光控制器");
    }

    public async Task RenameZoneAsync(LightDevice dev, LightZone zone, string name)
    {
        zone.Name = name;
        RememberName(dev, zone.Id, name);
        await Run(b => b.SetLightName(dev.Index, zone.Id, name, zone.Flags));
        RebuildLayout();
    }

    public async Task<LightZone> AddZoneAsync(LightDevice dev, ushort id, string name)
    {
        var zone = new LightZone { Id = id, Name = name };
        dev.Zones.Add(zone);
        RememberName(dev, id, name);
        await Run(b => b.SetLightName(dev.Index, id, name, 0));
        RebuildLayout();
        return zone;
    }

    /// <summary>Lights a single light id (others unchanged) so the user can find out which physical light it is.</summary>
    public Task ProbeLightAsync(LightDevice dev, ushort id, Rgb color) => Run(b =>
    {
        _engine.Stop();
        b.SetColor(dev.Index, id, color);
        b.Update(dev.Index);
    });

    /// <summary>Turns the whole device dark, then lights one id: used by key calibration.</summary>
    public Task ProbeOnlyAsync(LightDevice dev, ushort id, Rgb color) => Run(b =>
    {
        _engine.Stop();
        if (dev.HasHardwareEffects)
            b.SetHardwareEffect(dev.Index, HardwareEffect.Static, 5, ColorMode.One, Rgb.Black, Rgb.Black);
        b.SetColor(dev.Index, id, color);
        b.Update(dev.Index);
    });

    public void Dispose()
    {
        _engine.Stop();
        try { Backend.Close(); } catch { /* shutting down */ }
    }
}
