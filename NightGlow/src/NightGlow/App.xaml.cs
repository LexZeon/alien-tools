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

    private static void ActivateRunningInstance()
    {
        var self = Environment.ProcessId;
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("NightGlow"))
        {
            if (p.Id == self || p.MainWindowHandle == IntPtr.Zero) continue;
            NativeWindowMethods.ShowWindow(p.MainWindowHandle, 9); // SW_RESTORE
            NativeWindowMethods.SetForegroundWindow(p.MainWindowHandle);
            break;
        }
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!TakeSingleInstance())
        {
            ActivateRunningInstance();
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

internal static partial class NativeWindowMethods
{
    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    internal static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(IntPtr hWnd);
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
