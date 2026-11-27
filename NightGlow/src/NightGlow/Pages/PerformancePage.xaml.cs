using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NightGlow.Core;
using NightGlow.Core.Thermal;

namespace NightGlow.Pages;

public sealed partial class PerformancePage : Page, IRefreshable
{
    private bool _syncing;

    public PerformancePage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ModeGrid.ItemsSource = App.Thermal.PowerModes;
        SensorList.ItemsSource = App.Thermal.Sensors;
        FanList.ItemsSource = App.Thermal.Fans;
        App.Thermal.PropertyChanged += Thermal_PropertyChanged;
        App.Thermal.Fans.CollectionChanged += Readings_Changed;
        Refresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        App.Thermal.PropertyChanged -= Thermal_PropertyChanged;
        App.Thermal.Fans.CollectionChanged -= Readings_Changed;
    }

    private void Readings_Changed(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => UpdateEmpty();
    private void Thermal_PropertyChanged(object? s, PropertyChangedEventArgs e) => Refresh();

    public void Refresh()
    {
        var t = App.Thermal;
        AdminInfo.IsOpen = t.State == ThermalOpenResult.NeedAdmin;
        StatusInfo.IsOpen = t.State != ThermalOpenResult.NeedAdmin;
        StatusInfo.Title = t.IsSimulated ? "演示数据" : "BIOS 接口";
        StatusInfo.Message = t.IsSimulated ? "没有加载核心库，下面的模式、温度和风扇都是模拟的，切换不会影响电脑。" : t.StatusText;
        StatusInfo.Severity = t.IsSimulated ? InfoBarSeverity.Warning
            : t.State == ThermalOpenResult.Ok ? InfoBarSeverity.Success : InfoBarSeverity.Informational;
        VendorInfo.IsOpen = SystemInfo.VendorControlProcesses().Count > 0;

        _syncing = true;
        ModeGrid.SelectedItem = t.CurrentMode is { } m ? t.PowerModes.FirstOrDefault(x => x.Code == m.Code) : null;
        _syncing = false;
        UpdateEmpty();
    }

    private void UpdateEmpty() =>
        NoReadings.Visibility = App.Thermal.Fans.Count == 0 && App.Thermal.Sensors.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private async void ModeGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || ModeGrid.SelectedItem is not PowerModeInfo mode) return;
        if (App.Thermal.CurrentMode?.Code == mode.Code) return;
        ModeStatus.Text = $"正在切换到“{mode.Name}”…";
        bool ok = await App.Thermal.SetPowerModeAsync(mode.Code);
        ModeStatus.Text = ok ? $"已切换到“{mode.Name}”。" : $"切换到“{mode.Name}”失败。";
    }

    private void RelaunchAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (SystemInfo.RelaunchElevated())
            Application.Current.Exit();
        else
            ModeStatus.Text = "没有获得管理员权限（UAC 被取消）。";
    }
}
