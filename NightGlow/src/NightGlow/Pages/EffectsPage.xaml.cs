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

public sealed partial class TargetRow : ObservableObject
{
    public required LightDevice Device { get; init; }
    public string Title => $"{Device.Kind}（{Device.IdText}）";
    [ObservableProperty] private bool _isChecked = true;
    [ObservableProperty] private bool _isSupported = true;
    [ObservableProperty] private string _note = "";
}

public sealed partial class EffectsPage : Page, IRefreshable
{
    private const int PreviewLights = 16;
    private readonly List<Border> _previewCells = new();
    private readonly List<TargetRow> _targets = new();
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DateTime _t0 = DateTime.UtcNow;
    private EffectDefinition? _fx;
    private Rgb _c1, _c2;
    private bool _loading = true;

    public EffectsPage()
    {
        InitializeComponent();
        for (int i = 0; i < PreviewLights; i++)
        {
            var cell = new Border { Width = 13, Height = 40, CornerRadius = new CornerRadius(4) };
            _previewCells.Add(cell);
            PreviewStrip.Children.Add(cell);
        }
        _previewTimer.Tick += (_, _) => RenderPreview();
        EffectGrid.ItemsSource = EffectCatalog.All;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _loading = true;
        var s = App.Settings;
        _c1 = Rgb.TryParse(s.Color1, out var c1) ? c1 : new Rgb(0x35, 0xD6, 0xE8);
        _c2 = Rgb.TryParse(s.Color2, out var c2) ? c2 : new Rgb(0x8B, 0x5C, 0xF6);
        SpeedSlider.Value = Math.Clamp(s.Speed, 1, 10);
        BrightSlider.Value = Math.Clamp(Math.Round(s.Brightness / 2.55), 10, 100);
        UpdateSwatches();
        _loading = false;

        EffectGrid.SelectedItem = EffectCatalog.Find(s.EffectId) ?? EffectCatalog.All[0];
        Refresh();
        _previewTimer.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => _previewTimer.Stop();

    public void Refresh()
    {
        _targets.Clear();
        foreach (var d in App.Lighting.Devices) _targets.Add(new TargetRow { Device = d });
        TargetList.ItemsSource = null;
        TargetList.ItemsSource = _targets;
        UpdateTargets();
    }

    private EffectParameters Params() =>
        new(_c1, _c2, (int)SpeedSlider.Value, (byte)Math.Round(BrightSlider.Value * 2.55));

    private void EffectGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _fx = EffectGrid.SelectedItem as EffectDefinition;
        if (_fx is null) return;
        FxName.Text = _fx.Name + (_fx.Kind == EffectKind.Hardware ? "（硬件）" : _fx.Kind == EffectKind.Software ? "（软件）" : "");
        FxDesc.Text = _fx.Description;
        ColorRow.Visibility = _fx.ColorCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        Color2Button.Visibility = _fx.ColorCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        SpeedRow.Visibility = _fx.HasSpeed ? Visibility.Visible : Visibility.Collapsed;
        UpdateTargets();
    }

    private void UpdateTargets()
    {
        if (_fx is null) return;
        foreach (var t in _targets)
        {
            t.IsSupported = LightingService.Supports(t.Device, _fx);
            t.Note = !t.IsSupported
                ? (t.Device.Zones.Count == 0 ? "这个设备还没标定灯区，暂不支持此灯效" : "这个设备不支持此灯效")
                : t.Device.HasHardwareEffects && _fx.KeyboardEffect is not null && _fx.Kind == EffectKind.Hardware ? "由键盘硬件运行"
                : _fx.Kind == EffectKind.Software ? "NightGlow 计算并实时下发" : "逐个灯区下发";
        }
    }

    private void Param_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Speed = (int)SpeedSlider.Value;
        App.Settings.Brightness = (int)Math.Round(BrightSlider.Value * 2.55);
    }

    private void RenderPreview()
    {
        if (_fx is null) return;
        var p = Params();
        double sec = (DateTime.UtcNow - _t0).TotalSeconds;
        for (int i = 0; i < _previewCells.Count; i++)
        {
            var c = PreviewRenderer.ColorAt(_fx, p, sec, i, _previewCells.Count);
            if (_previewCells[i].Background is SolidColorBrush sb) sb.Color = c.ToColor();
            else _previewCells[i].Background = new SolidColorBrush(c.ToColor());
        }
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
        flyout.ShowAt(btn);
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_fx is null) return;
        var targets = _targets.Where(t => t.IsChecked && t.IsSupported).Select(t => t.Device).ToList();
        if (targets.Count == 0) { ApplyStatus.Text = "没有可应用的设备。"; return; }
        ApplyButton.IsEnabled = false;
        try
        {
            await App.Lighting.ApplyEffectAsync(_fx, Params(), targets);
            App.Settings.EffectId = _fx.Id;
            App.Settings.Save();
            ApplyStatus.Text = App.Lighting.IsSimulated
                ? $"已在演示模式下应用“{_fx.Name}”（没有发送到硬件）。"
                : $"已应用“{_fx.Name}”到 {targets.Count} 个设备。";
        }
        finally { ApplyButton.IsEnabled = true; }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        App.Lighting.StopSoftwareEffect();
        ApplyStatus.Text = "软件灯效已停止，灯光保持最后一帧。";
    }
}
