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
        _loading = false;

        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"版本 {ver?.ToString(3) ?? "0.1.0"}（预览版）";
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

    private async void Rescan_Click(object sender, RoutedEventArgs e)
    {
        await App.Lighting.RefreshAsync();
        App.Window.OnServicesReady();
    }
}
