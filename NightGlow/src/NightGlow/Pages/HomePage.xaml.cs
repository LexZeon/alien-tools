using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NightGlow.Core;
using NightGlow.Core.Lighting;
using NightGlow.Core.Thermal;

namespace NightGlow.Pages;

public sealed partial class HomePage : Page, IRefreshable
{
    public HomePage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        App.Thermal.PropertyChanged += Thermal_PropertyChanged;
        App.Lighting.PropertyChanged += Lighting_PropertyChanged;
        TempList.ItemsSource = App.Thermal.Sensors;
        DeviceList.ItemsSource = App.Lighting.Devices;
        Refresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        App.Thermal.PropertyChanged -= Thermal_PropertyChanged;
        App.Lighting.PropertyChanged -= Lighting_PropertyChanged;
    }

    private void Thermal_PropertyChanged(object? s, PropertyChangedEventArgs e) => UpdateThermal();
    private void Lighting_PropertyChanged(object? s, PropertyChangedEventArgs e) => EffectText.Text = App.Lighting.ActiveEffectName;

    public void Refresh()
    {
        var model = SystemInfo.MachineModel;
        ModelText.Text = (string.IsNullOrEmpty(model) ? "未知机型" : model) + (App.Lighting.IsSimulated ? "  ·  演示模式" : "");
        DemoInfo.IsOpen = App.Lighting.IsSimulated;
        VendorInfo.IsOpen = SystemInfo.VendorControlProcesses().Count > 0;
        EffectText.Text = App.Lighting.ActiveEffectName;
        NoDevices.Visibility = App.Lighting.Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateThermal();
    }

    private void UpdateThermal()
    {
        var t = App.Thermal;
        ModeText.Text = t.CurrentMode?.Name ?? "—";
        ModeHint.Text = t.State == ThermalOpenResult.Ok ? (t.CurrentMode?.Description ?? "") : t.StatusText;
        TempHint.Visibility = t.Sensors.Count == 0 || t.IsSimulated ? Visibility.Visible : Visibility.Collapsed;
        TempHint.Text = t.IsSimulated ? "演示数据，不是真实读数"
            : t.State == ThermalOpenResult.NeedAdmin ? "需要管理员权限" : t.StatusText;
    }

    private EffectParameters CurrentParams() => new(
        Rgb.TryParse(App.Settings.Color1, out var c1) ? c1 : Rgb.White,
        Rgb.TryParse(App.Settings.Color2, out var c2) ? c2 : Rgb.White,
        App.Settings.Speed, (byte)App.Settings.Brightness)
    {
        Direction = Enum.TryParse<WaveDirection>(App.Settings.Direction, out var d) ? d : WaveDirection.LeftToRight,
        PaletteId = App.Settings.Palette,
        AutoRain = App.Settings.AutoRain,
        Duration = App.Settings.Duration,
    };

    private async void QuickRainbow_Click(object sender, RoutedEventArgs e) =>
        await App.Lighting.ApplyEffectAsync(EffectCatalog.Find("sp-wave")!, CurrentParams(), App.Lighting.Devices.ToList());

    private async void QuickWhite_Click(object sender, RoutedEventArgs e) =>
        await App.Lighting.ApplyEffectAsync(EffectCatalog.Find("static")!, CurrentParams() with { Color1 = Rgb.White }, App.Lighting.Devices.ToList());

    private async void QuickOff_Click(object sender, RoutedEventArgs e) =>
        await App.Lighting.ApplyEffectAsync(EffectCatalog.Find("off")!, CurrentParams(), App.Lighting.Devices.ToList());

    private void GoEffects_Click(object sender, RoutedEventArgs e) => App.Window.NavigateTo("effects");
    private void GoPerf_Click(object sender, RoutedEventArgs e) => App.Window.NavigateTo("performance");
}
