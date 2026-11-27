using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using NightGlow.Pages;

namespace NightGlow;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "NightGlow.ico"));
        // Size in device-independent pixels so the layout is the same at 100%/125%/150% scaling.
        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        if (scale <= 0) scale = 1;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(1240 * scale), (int)(800 * scale)));
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.PreferredMinimumWidth = (int)(960 * scale);
            p.PreferredMinimumHeight = (int)(620 * scale);
        }
        ApplyTheme(App.Settings.Theme);

        // --page=<home|lighting|effects|performance|settings> opens a page directly (handy for docs/screenshots).
        var pageArg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--page=", StringComparison.OrdinalIgnoreCase));
        NavigateTo(pageArg is null ? "home" : pageArg["--page=".Length..].ToLowerInvariant());
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>Called once services have scanned hardware.</summary>
    public void OnServicesReady()
    {
        DemoBadge.Visibility = App.Lighting.IsSimulated ? Visibility.Visible : Visibility.Collapsed;
        if (ContentFrame.Content is IRefreshable r) r.Refresh();
    }

    public void ApplyTheme(string theme)
    {
        RootGrid.RequestedTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "System" => ElementTheme.Default,
            _ => ElementTheme.Dark,
        };
        if (AppWindow.TitleBar is { } tb)
        {
            bool dark = RootGrid.ActualTheme != ElementTheme.Light && theme != "Light";
            tb.ButtonForegroundColor = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        }
    }

    public void NavigateTo(string tag)
    {
        foreach (var item in Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>())
            if ((string)item.Tag == tag) { Nav.SelectedItem = item; return; }
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        Type page = tag switch
        {
            "lighting" => typeof(LightingPage),
            "effects" => typeof(EffectsPage),
            "performance" => typeof(PerformancePage),
            "settings" => typeof(SettingsPage),
            _ => typeof(HomePage),
        };
        if (ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
    }
}

/// <summary>Pages that want a refresh once hardware scanning finishes.</summary>
public interface IRefreshable
{
    void Refresh();
}
