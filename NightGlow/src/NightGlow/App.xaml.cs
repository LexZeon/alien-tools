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

    private static Mutex? _singleInstance;

    /// <summary>
    /// Only one NightGlow may drive the lights: two instances writing at the same time make
    /// the LEDs flicker. A second launch brings the running window to the front and quits.
    /// An elevated relaunch ("--elevated") waits for the instance that started it to exit.
    /// </summary>
    private static bool TakeSingleInstance()
    {
        try
        {
            _singleInstance = new Mutex(true, @"Local\NightGlow.SingleInstance", out bool created);
            if (created) return true;
            if (!Environment.GetCommandLineArgs().Contains("--elevated")) return false;
            try { return _singleInstance.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { return true; } // the old instance exited without releasing
        }
        catch (UnauthorizedAccessException) { return false; } // held by an elevated instance
    }

    /// <summary>Notification-area icon; NightGlow keeps running software effects there with the window closed.</summary>
    internal static TrayIcon? Tray { get; private set; }

    /// <summary>Set once NightGlow is really quitting (closing the window then no longer hides it).</summary>
    public static bool IsExiting { get; private set; }

    /// <summary>"--tray": start hidden in the notification area (used by "start with Windows").</summary>
    private static bool StartInTray => Environment.GetCommandLineArgs().Contains("--tray");

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!TakeSingleInstance())
        {
            // the running instance may be hidden in the tray: ask it to show its window
            TrayIcon.SignalRunningInstance();
            Exit();
            return;
        }
        try
        {
            // Services first: pages use them as soon as the window navigates.
            Settings = AppSettings.Load();
            Lighting = new LightingService(Settings);
            Thermal = new ThermalService(DispatcherQueue.GetForCurrentThread());

            Window = new MainWindow();
            Window.Closed += (_, _) => Cleanup();

            Tray = new TrayIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "NightGlow.ico"));
            Tray.OpenRequested += () => Window.ShowFromTray();
            Tray.ExitRequested += Quit;
            Lighting.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LightingService.ActiveEffectName) && Tray is { } t)
                    t.Tooltip = $"NightGlow · {Lighting.ActiveEffectName}";
            };

            if (StartInTray) Window.HideToTray(showHint: false);
            else Window.Activate();

            Thermal.Start();
            await Lighting.RefreshAsync();
            // software effects are computed by NightGlow: start the one that ran last time again
            await Lighting.RestoreLastEffectAsync();
            Window.OnServicesReady();
        }
        catch (Exception ex)
        {
            CrashLog.Write("startup", ex);
            throw;
        }
    }

    /// <summary>Quits NightGlow: software effects stop and the lights keep their last frame.</summary>
    public static void Quit()
    {
        IsExiting = true;
        Cleanup();
        Current.Exit();
    }

    private static bool _cleanedUp;

    private static void Cleanup()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;
        IsExiting = true;
        Tray?.Dispose();
        Settings.Save();
        Lighting?.Dispose();
        Thermal?.Dispose();
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
