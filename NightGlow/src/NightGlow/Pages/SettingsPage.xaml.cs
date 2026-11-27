using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NightGlow.Core;

namespace NightGlow.Pages;

public sealed partial class SettingsPage : Page
{
    private static readonly string[] Themes = { "Dark", "Light", "System" };
    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _loading = true;
        ThemeRadios.SelectedIndex = Math.Max(0, Array.IndexOf(Themes, App.Settings.Theme));
        TraySwitch.IsOn = App.Settings.CloseToTray;
        StartupSwitch.IsOn = SystemInfo.StartWithWindows;
        _loading = false;

        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"版本 {ver?.ToString(3) ?? "0.1.1"}（预览版）";
        CoreText.Text = "核心库：" + App.Lighting.CoreStatus;
        AdminText.Text = SystemInfo.IsElevated ? "权限：管理员（风扇、温度、性能模式可用）" : "权限：普通用户（灯光可用；风扇、温度、性能模式需要管理员）";
        var vendor = SystemInfo.VendorControlProcesses();
        VendorText.Text = vendor.Count > 0 ? $"厂商控制中心：正在运行（检测到 {vendor.Count} 个相关进程）" : "厂商控制中心：未运行";
    }

    private void ThemeRadios_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeRadios.SelectedIndex < 0) return;
        App.Settings.Theme = Themes[ThemeRadios.SelectedIndex];
        App.Settings.Save();
        App.Window.ApplyTheme(App.Settings.Theme);
    }

    private void TraySwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.CloseToTray = TraySwitch.IsOn;
        App.Settings.Save();
    }

    private void StartupSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        try
        {
            SystemInfo.StartWithWindows = StartupSwitch.IsOn;
            StartupNote.Text = StartupSwitch.IsOn
                ? "已设置：登录 Windows 后 NightGlow 在托盘里启动，并接着运行上次的灯效。"
                : "已取消开机启动。";
        }
        catch (Exception ex)
        {
            StartupNote.Text = "设置开机启动失败：" + ex.Message;
            _loading = true;
            StartupSwitch.IsOn = SystemInfo.StartWithWindows;
            _loading = false;
        }
        StartupNote.Visibility = Visibility.Visible;
    }

    private async void Rescan_Click(object sender, RoutedEventArgs e)
    {
        await App.Lighting.RefreshAsync();
        App.Window.OnServicesReady();
    }
}
