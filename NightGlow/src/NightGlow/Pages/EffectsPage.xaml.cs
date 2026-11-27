using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using NightGlow.Core;
using NightGlow.Core.Lighting;

namespace NightGlow.Pages;

public static class EffectVisuals
{
    public static Brush MakeBrush(string[] colors)
    {
        var b = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 0) };
        for (int i = 0; i < colors.Length; i++)
            if (Rgb.TryParse(colors[i], out var c))
                b.GradientStops.Add(new GradientStop { Color = c.ToColor(), Offset = colors.Length > 1 ? (double)i / (colors.Length - 1) : 0 });
        return b;
    }
}

/// <summary>One selectable region (keyboard, ring, logo, other).</summary>
public sealed partial class RegionChip : ObservableObject
{
    public required RegionKind Kind { get; init; }
    public string Name => Regions.Name(Kind);
    public string Glyph => Regions.Glyph(Kind);
    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private bool _isAvailable = true;
    [ObservableProperty] private string _note = "";

    public event EventHandler? CheckedChanged;
    partial void OnIsCheckedChanged(bool value) => CheckedChanged?.Invoke(this, EventArgs.Empty);
}

public sealed partial class EffectsPage : Page, IRefreshable
{
    private readonly List<RegionChip> _chips = new();
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    // changes are applied right away (like a vendor control center), debounced so slider drags stay smooth
    private readonly DispatcherTimer _applyTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Dictionary<int, LayoutPoint> _centers = new();
    private EffectDefinition? _fx;
    private Rgb _c1, _c2;
    private bool _loading = true;

    public EffectsPage()
    {
        InitializeComponent();
        _previewTimer.Tick += (_, _) => RenderPreview();
        _applyTimer.Tick += async (_, _) => { _applyTimer.Stop(); await ApplyNowAsync(); };
        EffectGrid.ItemsSource = EffectCatalog.All;
        PaletteGrid.ItemsSource = Palette.All;
        foreach (var (_, name, glyph) in WaveDirections.All) DirectionBox.Items.Add($"{glyph}   {name}");
        foreach (var k in Regions.All)
        {
            var chip = new RegionChip { Kind = k };
            chip.CheckedChanged += Chip_CheckedChanged;
            _chips.Add(chip);
        }
        RegionList.ItemsSource = _chips;
        LaptopView.RegionTapped += (_, kind) =>
        {
            var chip = _chips.First(c => c.Kind == kind);
            if (chip.IsAvailable) chip.IsChecked = !chip.IsChecked;
        };
        LaptopView.SurfaceTapped += (_, pos) => App.Lighting.Ripples.Add(pos.X, pos.Y);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _loading = true;
        var s = App.Settings;
        _c1 = Rgb.TryParse(s.Color1, out var c1) ? c1 : new Rgb(0x35, 0xD6, 0xE8);
        _c2 = Rgb.TryParse(s.Color2, out var c2) ? c2 : new Rgb(0x8B, 0x5C, 0xF6);
        SpeedSlider.Value = Math.Clamp(s.Speed, 1, 10);
        BrightSlider.Value = Math.Clamp(Math.Round(s.Brightness / 2.55), 10, 100);
        var dir = Enum.TryParse<WaveDirection>(s.Direction, out var d) ? d : WaveDirection.LeftToRight;
        DirectionBox.SelectedIndex = Math.Max(0, Array.FindIndex(WaveDirections.All, x => x.Dir == dir));
        PaletteGrid.SelectedItem = Palette.All.FirstOrDefault(p => p.Id == s.Palette) ?? Palette.All[0];
        RainSwitch.IsOn = s.AutoRain;
        DurationSlider.Value = Math.Clamp(s.Duration, 1, 10);
        ModeButtons.SelectedIndex = (int)App.Lighting.Mode;
        foreach (var chip in _chips) chip.IsChecked = s.SelectedRegions.Contains(chip.Kind.ToString());
        UpdateSwatches();

        App.Lighting.LayoutChanged += Lighting_LayoutChanged;
        // opening the page shows the last effect but does not send anything to the lights
        EffectGrid.SelectedItem = EffectCatalog.Find(s.EffectId) ?? EffectCatalog.All[0];
        _loading = false;
        Refresh();
        _previewTimer.Start();
        MaybeAutoStartDemo();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        StopDemo();
        _previewTimer.Stop();
        if (_applyTimer.IsEnabled) { _applyTimer.Stop(); _ = ApplyNowAsync(); }
        App.Lighting.LayoutChanged -= Lighting_LayoutChanged;
    }

    private void ScheduleApply()
    {
        if (_loading) return;
        _applyTimer.Stop();
        _applyTimer.Start();
    }

    private void Lighting_LayoutChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(Refresh);

    public void Refresh()
    {
        var layout = App.Lighting.Layout;
        LaptopView.SetLayout(layout);
        _centers.Clear();
        UpdateRegions();
    }

    private IEnumerable<RegionKind> SelectedKinds => _chips.Where(c => c.IsChecked && c.IsAvailable).Select(c => c.Kind);

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var chip in _chips.Where(c => c.IsAvailable)) chip.IsChecked = true;
    }

    private void Chip_CheckedChanged(object? sender, EventArgs e)
    {
        LaptopView.SetSelected(SelectedKinds);
        if (_loading) return;
        App.Settings.SelectedRegions = _chips.Where(c => c.IsChecked).Select(c => c.Kind.ToString()).ToList();
        UpdateRegionNote();
        ScheduleApply();
    }

    private void UpdateRegions()
    {
        var layout = App.Lighting.Layout;
        foreach (var chip in _chips)
        {
            var region = layout.Get(chip.Kind);
            if (region is null)
            {
                chip.IsAvailable = false;
                chip.Note = chip.Kind == RegionKind.Keyboard ? "没有找到键盘灯光" : "还没标定：到“灯光”页给灯区起名并分配区域";
                continue;
            }
            var (ok, note) = _fx is null ? (true, "") : LightingService.Supports(region, _fx);
            chip.IsAvailable = ok;
            chip.Note = $"{region.Points.Count} 个灯 · {note}";
        }
        LaptopView.SetSelected(SelectedKinds);
        UpdateRegionNote();
    }

    private void UpdateRegionNote()
    {
        var missing = _chips.Where(c => !c.IsAvailable && App.Lighting.Layout.Get(c.Kind) is null).Select(c => c.Name).ToList();
        var kb = App.Lighting.Layout.Get(RegionKind.Keyboard);
        var parts = new List<string>();
        if (kb is { IsWholeDevice: true } && _fx?.Kind == EffectKind.Software)
            parts.Add("键盘还没标定键位，软件灯效下整块键盘显示同一颜色；到“灯光”页做键位标定后可以逐键流动。");
        if (missing.Count > 0) parts.Add("未标定：" + string.Join("、", missing) + "。");
        RegionNote.Text = string.Join(" ", parts);
        RegionNote.Visibility = parts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private EffectParameters Params() =>
        new(_c1, _c2, (int)SpeedSlider.Value, (byte)Math.Round(BrightSlider.Value * 2.55))
        {
            Direction = WaveDirections.All[Math.Max(0, DirectionBox.SelectedIndex)].Dir,
            PaletteId = (PaletteGrid.SelectedItem as Palette)?.Id ?? "rainbow",
            AutoRain = RainSwitch.IsOn,
            Duration = (int)DurationSlider.Value,
        };

    private void EffectGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _fx = EffectGrid.SelectedItem as EffectDefinition;
        if (_fx is null) return;
        FxName.Text = _fx.Name + (_fx.Kind == EffectKind.Hardware ? "（硬件）" : _fx.Kind == EffectKind.Software ? (_fx.Interactive ? "（互动）" : "（软件）") : "");
        FxDesc.Text = _fx.Description;
        UpdateOptionRows();
        LaptopView.InteractiveMode = _fx.Interactive;
        CanvasHint.Text = _fx.Interactive ? "点击任意位置放出波纹 · 区域在右侧选择" : "点击区域选中或取消";
        UpdateRegions();
        ScheduleApply();
    }

    private void UpdateOptionRows()
    {
        if (_fx is null) return;
        bool custom = _fx.HasPalette && (PaletteGrid.SelectedItem as Palette)?.Id == Palette.CustomId;
        int colors = Math.Max(_fx.ColorCount, custom ? 2 : 0);
        ColorRow.Visibility = colors > 0 ? Visibility.Visible : Visibility.Collapsed;
        Color2Button.Visibility = colors > 1 ? Visibility.Visible : Visibility.Collapsed;
        SpeedRow.Visibility = _fx.HasSpeed ? Visibility.Visible : Visibility.Collapsed;
        DurationRow.Visibility = _fx.HasDuration ? Visibility.Visible : Visibility.Collapsed;
        DirectionRow.Visibility = _fx.HasDirection ? Visibility.Visible : Visibility.Collapsed;
        PaletteRow.Visibility = _fx.HasPalette ? Visibility.Visible : Visibility.Collapsed;
        RainRow.Visibility = _fx.Interactive ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Option_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Direction = WaveDirections.All[Math.Max(0, DirectionBox.SelectedIndex)].Dir.ToString();
        App.Settings.Palette = (PaletteGrid.SelectedItem as Palette)?.Id ?? "rainbow";
        UpdateOptionRows();
        ScheduleApply();
    }

    private void Param_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Speed = (int)SpeedSlider.Value;
        App.Settings.Brightness = (int)Math.Round(BrightSlider.Value * 2.55);
        App.Settings.Duration = (int)DurationSlider.Value;
        ScheduleApply();
    }

    private async void ModeButtons_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // RadioButtons raises SelectionChanged late (after layout), so compare with the current
        // mode instead of relying on the _loading flag alone.
        if (_loading || ModeButtons.SelectedIndex < 0) return;
        var mode = (LightingMode)ModeButtons.SelectedIndex;
        if (mode == App.Lighting.Mode) return;
        await App.Lighting.SetModeAsync(mode);
        string what = mode switch
        {
            LightingMode.Dim => $"已调暗到正常亮度的 {LightingService.DimPercent}%",
            LightingMode.Off => "已关闭所有灯光",
            _ => "已恢复正常亮度",
        };
        ApplyStatus.Text = what + (App.Lighting.IsSimulated ? "（演示模式，没有发送到硬件）。" : "。");
    }

    private void Rain_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.AutoRain = RainSwitch.IsOn;
        ScheduleApply();
    }

    private void RenderPreview()
    {
        if (_fx is null) return;
        var fx = _fx;
        var p = Params();
        var pal = p.GetPalette(fx);
        double now = EffectClock.Now;
        var drops = fx.Interactive
            ? App.Lighting.Ripples.Frame(now, SpatialEffects.RippleLife(p.Speed), p.AutoRain, SpatialEffects.RainEvery(p.Speed))
            : Array.Empty<RippleField.Drop>();

        LaptopView.Render(pt =>
        {
            // a keyboard without key map gets one color for all keys in software effects:
            // sample it where the engine does, at the keyboard center
            var q = pt.IsWholeDevice && fx.Kind != EffectKind.Hardware ? Center(pt) : pt;
            return PreviewRenderer.ColorAt(fx, p, pal, now, q, drops);
        }, drops, pal, now, p.Speed);
    }

    private LayoutPoint Center(LayoutPoint pt)
    {
        if (!_centers.TryGetValue(pt.Device.Index, out var c))
        {
            c = App.Lighting.Layout.Get(RegionKind.Keyboard)?.Points.FirstOrDefault(x => x.Device.Index == pt.Device.Index) ?? pt;
            _centers[pt.Device.Index] = c;
        }
        return c;
    }

    private void UpdateSwatches()
    {
        Color1Swatch.Background = new SolidColorBrush(_c1.ToColor());
        Color2Swatch.Background = new SolidColorBrush(_c2.ToColor());
    }

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        var btn = (Button)sender;
        bool first = (string)btn.Tag == "1";
        var picker = new ColorPicker { IsAlphaEnabled = false, ColorSpectrumShape = ColorSpectrumShape.Ring, Color = (first ? _c1 : _c2).ToColor() };
        picker.ColorChanged += (_, a) =>
        {
            var c = Rgb.FromColor(a.NewColor);
            if (first) { _c1 = c; App.Settings.Color1 = c.ToHex(); }
            else { _c2 = c; App.Settings.Color2 = c.ToHex(); }
            UpdateSwatches();
        };
        var flyout = new Flyout { Content = picker };
        flyout.Closed += (_, _) => ScheduleApply();
        flyout.ShowAt(btn);
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        _applyTimer.Stop();
        await ApplyNowAsync();
    }

    private async Task ApplyNowAsync()
    {
        if (_fx is null) return;
        var kinds = SelectedKinds.ToList();
        var points = App.Lighting.Layout.PointsIn(kinds);
        if (points.Count == 0) { ApplyStatus.Text = "请先选择至少一个区域。"; return; }
        var fx = _fx;
        await App.Lighting.ApplyEffectAsync(fx, Params(), points);
        App.Settings.EffectId = fx.Id;
        App.Settings.Save();
        _loading = true;
        ModeButtons.SelectedIndex = (int)App.Lighting.Mode; // applying returns to normal mode
        _loading = false;
        var names = string.Join("、", kinds.Select(Regions.Name));
        ApplyStatus.Text = App.Lighting.IsSimulated
            ? $"演示模式：“{fx.Name}”→ {names}（没有发送到硬件）。"
            : $"已应用“{fx.Name}”→ {names}。" + (fx.Kind == EffectKind.Software ? "关闭 NightGlow 后灯光停在最后一帧。" : "")
              + (!kinds.Contains(RegionKind.Keyboard) && App.Lighting.Layout.Get(RegionKind.Keyboard) is not null ? " 注意：键盘没有选中，键盘不会变。" : "");
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        StopDemo();
        App.Lighting.StopSoftwareEffect();
        ApplyStatus.Text = "软件灯效已停止，灯光保持最后一帧。";
    }

    // ---- one-click showcase: plays the effects one after another, with a caption ----

    private sealed record DemoStep(string FxId, WaveDirection Dir, string Palette, double Seconds, string Caption, string CaptionZh, bool Drops = false);

    private static readonly DemoStep[] DemoSteps =
    {
        new("sp-wave", WaveDirection.LeftToRight, "rainbow", 5, "🌈 One rainbow across every zone", "彩虹波 · 键盘、触控板、Logo、背部灯环连成一片"),
        new("sp-wave", WaveDirection.Clockwise, "rainbow", 4, "🔄 Around the rear ring", "顺时针环绕"),
        new("sp-wave", WaveDirection.CenterOut, "aurora", 4, "🎨 8 directions · palettes", "8 个方向 · 一键换配色"),
        new("sp-ripple", WaveDirection.LeftToRight, "rainbow", 8, "💧 Click anywhere for ripples", "点哪里，就从哪里荡开涟漪", Drops: true),
        new("sp-nebula", WaveDirection.LeftToRight, "nebula", 5, "🌌 Nebula", "星云"),
        new("sp-aurora", WaveDirection.LeftToRight, "aurora", 5, "🌠 Aurora", "极光"),
        new("sp-laser", WaveDirection.LeftToRight, "ember", 4, "⚡ Laser scan with trails", "激光扫描 · 带拖影"),
        new("sp-fire", WaveDirection.LeftToRight, "rainbow", 5, "🔥 Fire", "火焰"),
        new("sp-plasma", WaveDirection.LeftToRight, "rainbow", 4, "🔮 Plasma", "等离子"),
        new("sp-wave", WaveDirection.LeftToRight, "sakura", 4, "⌨️ Per-key, 40 fps, mapped on real hardware", "逐键 40 帧 · 键位在真机上实测"),
        new("sp-wave", WaveDirection.LeftToRight, "rainbow", 4, "NightGlow · open source · MIT", "开源 · MIT 许可"),
    };

    /// <summary>Where the showcase drops its ripples (keyboard, rear ring, touchpad, corners).</summary>
    private static readonly (double X, double Y)[] DemoDrops =
        { (0.30, 0.50), (0.72, 0.46), (0.50, 0.12), (0.50, 0.89), (0.16, 0.66), (0.85, 0.60), (0.50, 0.55), (0.26, 0.13) };

    private DispatcherTimer? _demoTimer;
    private int _demoStep, _dropIndex;
    private DateTime _demoStepEnd, _nextDrop;
    private static bool _demoAutoStarted;

    private void Demo_Click(object sender, RoutedEventArgs e)
    {
        if (_demoTimer is null) StartDemo();
        else StopDemo();
    }

    private void StartDemo()
    {
        foreach (var chip in _chips.Where(c => c.IsAvailable)) chip.IsChecked = true;
        _demoStep = -1;
        _demoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _demoTimer.Tick += DemoTick;
        DemoButtonText.Text = "停止演示";
        DemoCaptionBox.Visibility = Visibility.Visible;
        NextDemoStep();
        _demoTimer.Start();
    }

    private void NextDemoStep()
    {
        _demoStep++;
        if (_demoStep >= DemoSteps.Length)
        {
            StopDemo();
            return;
        }
        var s = DemoSteps[_demoStep];
        RainSwitch.IsOn = false;
        DirectionBox.SelectedIndex = Math.Max(0, Array.FindIndex(WaveDirections.All, d => d.Dir == s.Dir));
        PaletteGrid.SelectedItem = Palette.All.FirstOrDefault(p => p.Id == s.Palette) ?? Palette.All[0];
        EffectGrid.SelectedItem = EffectCatalog.Find(s.FxId);
        if (EffectGrid.SelectedItem is not null) EffectGrid.ScrollIntoView(EffectGrid.SelectedItem);
        DemoCaption.Text = s.Caption;
        DemoCaptionZh.Text = s.CaptionZh;
        _demoStepEnd = DateTime.Now.AddSeconds(s.Seconds);
        _nextDrop = DateTime.Now.AddSeconds(0.6);
        ScheduleApply();
    }

    private void DemoTick(object? sender, object e)
    {
        if (_demoStep < 0 || _demoStep >= DemoSteps.Length) return;
        var s = DemoSteps[_demoStep];
        if (s.Drops && DateTime.Now >= _nextDrop)
        {
            var (x, y) = DemoDrops[_dropIndex++ % DemoDrops.Length];
            App.Lighting.Ripples.Add(x, y);
            _nextDrop = DateTime.Now.AddSeconds(1.1);
        }
        if (DateTime.Now >= _demoStepEnd) NextDemoStep();
    }

    private void StopDemo()
    {
        if (_demoTimer is null) return;
        _demoTimer.Stop();
        _demoTimer = null;
        DemoButtonText.Text = "一键演示";
        DemoCaptionBox.Visibility = Visibility.Collapsed;
    }

    /// <summary>"NightGlow.exe --showcase" starts the showcase once, a moment after the page opens.</summary>
    internal static bool IsShowcase => Environment.GetCommandLineArgs().Contains("--showcase");

    private void MaybeAutoStartDemo()
    {
        if (_demoAutoStarted || !IsShowcase) return;
        _demoAutoStarted = true;
        var once = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        once.Tick += (_, _) => { once.Stop(); StartDemo(); };
        once.Start();
    }
}
