using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NightGlow.Core;
using NightGlow.Core.Lighting;
using NightGlow.Core.Thermal;

namespace NightGlow;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();
    public static LightingService Lighting { get; private set; } = null!;
    public static ThermalService Thermal { get; private set; } = null!;
    public static MainWindow Window { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            // Log and keep running; hardware errors are already handled inside the services.
            CrashLog.Write("UI", e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write("domain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { CrashLog.Write("task", e.Exception); e.SetObserved(); };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // Services first: pages use them as soon as the window navigates.
            Settings = AppSettings.Load();
            Lighting = new LightingService();
            Thermal = new ThermalService(DispatcherQueue.GetForCurrentThread());

            Window = new MainWindow();
            Window.Closed += (_, _) =>
            {
                Settings.Save();
                Lighting.Dispose();
                Thermal.Dispose();
            };
            Window.Activate();

            Thermal.Start();
            await Lighting.RefreshAsync();
            Window.OnServicesReady();
        }
        catch (Exception ex)
        {
            CrashLog.Write("startup", ex);
            throw;
        }
    }
}

internal static class CrashLog
{
    public static void Write(string where, Exception? ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NightGlow");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where}: {ex}\n\n");
        }
        catch { /* never throw from the logger */ }
    }
}
