using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace NightGlow.Core;

/// <summary>
/// Notification-area icon. Software effects are computed by NightGlow frame by frame, so the app
/// keeps running here after the window is closed. A hidden top-level window receives the icon's
/// messages, Explorer restarts ("TaskbarCreated") and the "show" request of a second launch.
/// </summary>
internal sealed unsafe partial class TrayIcon : IDisposable
{
    private const string ClassName = "NightGlow.TrayWindow";
    private const uint CallbackMessage = 0x8000 + 1; // WM_APP + 1
    private const int CmdOpen = 1, CmdExit = 2;

    private static readonly uint TaskbarCreated = RegisterWindowMessageW("TaskbarCreated");

    /// <summary>Posted to the tray window by a second launch: show the (possibly hidden) window.</summary>
    private static readonly uint ShowMessage = RegisterWindowMessageW("NightGlow.ShowWindow");

    private static TrayIcon? _instance;
    private readonly DispatcherQueue _ui;
    private readonly IntPtr _hwnd;
    private readonly IntPtr _icon;
    private string _tip = "NightGlow";
    private bool _added, _disposed;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public TrayIcon(string iconPath)
    {
        _ui = DispatcherQueue.GetForCurrentThread();
        _instance = this;
        var hInst = GetModuleHandleW(null);
        fixed (char* cls = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = hInst,
                lpszClassName = cls,
            };
            RegisterClassExW(&wc); // fails harmlessly if already registered
            // a top-level popup that is never shown (message-only windows miss "TaskbarCreated")
            _hwnd = CreateWindowExW(0, cls, cls, 0x80000000 /* WS_POPUP */, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
        }
        // when NightGlow runs elevated, Explorer and a normal second launch are lower integrity
        foreach (var m in new[] { CallbackMessage, TaskbarCreated, ShowMessage })
            ChangeWindowMessageFilterEx(_hwnd, m, 1 /* MSGFLT_ALLOW */, IntPtr.Zero);

        int size = GetSystemMetricsForDpi(49 /* SM_CXSMICON */, GetDpiForSystem());
        _icon = LoadImageW(IntPtr.Zero, iconPath, 1 /* IMAGE_ICON */, size, size, 0x10 /* LR_LOADFROMFILE */);
        Add();
    }

    /// <summary>Asks a running NightGlow (if any) to show its window. Returns false when none was found.</summary>
    public static bool SignalRunningInstance()
    {
        var hwnd = FindWindowW(ClassName, null);
        if (hwnd == IntPtr.Zero) return false;
        AllowSetForegroundWindow(-1 /* ASFW_ANY */); // this process was just started by the user, so it may hand over the foreground
        return PostMessageW(hwnd, ShowMessage, IntPtr.Zero, IntPtr.Zero);
    }

    public string Tooltip
    {
        get => _tip;
        set { _tip = value; if (_added) Notify(1 /* NIM_MODIFY */, 0x4 /* NIF_TIP */); }
    }

    /// <summary>A notification next to the icon (Windows shows it as a toast).</summary>
    public void ShowBalloon(string title, string text)
    {
        if (!_added) return;
        var d = Data(0x10 /* NIF_INFO */);
        Copy(title, d.szInfoTitle, 64);
        Copy(text, d.szInfo, 256);
        d.dwInfoFlags = 0x4; // NIIF_USER: our icon
        Shell_NotifyIconW(1, &d);
    }

    private void Add()
    {
        _added = Notify(0 /* NIM_ADD */, 0x1 | 0x2 | 0x4 /* NIF_MESSAGE | NIF_ICON | NIF_TIP */);
    }

    private bool Notify(uint msg, uint flags)
    {
        var d = Data(flags);
        return Shell_NotifyIconW(msg, &d);
    }

    private NOTIFYICONDATAW Data(uint flags)
    {
        var d = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _hwnd,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
        };
        Copy(_tip, d.szTip, 128);
        return d;
    }

    private static void Copy(string s, char* dst, int capacity)
    {
        int n = Math.Min(s.Length, capacity - 1);
        for (int i = 0; i < n; i++) dst[i] = s[i];
        dst[n] = '\0';
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenuW(menu, 0 /* MF_STRING */, CmdOpen, "打开 NightGlow");
        AppendMenuW(menu, 0x800 /* MF_SEPARATOR */, 0, null);
        AppendMenuW(menu, 0, CmdExit, "退出（软件灯效会停止）");
        SetMenuDefaultItem(menu, CmdOpen, 0);
        GetCursorPos(out var pt);
        SetForegroundWindow(_hwnd); // otherwise the menu does not close when clicking elsewhere
        int cmd = TrackPopupMenuEx(menu, 0x100 | 0x2 /* TPM_RETURNCMD | TPM_RIGHTBUTTON */, pt.X, pt.Y, _hwnd, IntPtr.Zero);
        PostMessageW(_hwnd, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);
        if (cmd == CmdOpen) Raise(OpenRequested);
        else if (cmd == CmdExit) Raise(ExitRequested);
    }

    // run the handlers outside the window procedure (they may destroy this window)
    private void Raise(Action? handler) => _ui.TryEnqueue(() => handler?.Invoke());

    [UnmanagedCallersOnly]
    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            var self = _instance;
            if (self is not null && !self._disposed && hwnd == self._hwnd)
            {
                if (msg == CallbackMessage)
                {
                    switch ((uint)lParam & 0xFFFF)
                    {
                        case 0x202: // WM_LBUTTONUP
                            self.Raise(self.OpenRequested);
                            break;
                        case 0x205: // WM_RBUTTONUP
                            self.ShowMenu();
                            break;
                    }
                    return IntPtr.Zero;
                }
                if (msg == TaskbarCreated) { self.Add(); return IntPtr.Zero; } // Explorer restarted
                if (msg == ShowMessage) { self.Raise(self.OpenRequested); return IntPtr.Zero; }
            }
        }
        catch { /* never let an exception cross the native boundary */ }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_added) Notify(2 /* NIM_DELETE */, 0);
        _added = false;
        if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
        if (_instance == this) _instance = null;
    }

    // ---- Win32 ----

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uTimeoutOrVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Shell_NotifyIconW(uint dwMessage, NOTIFYICONDATAW* lpData);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string lpString);

    [LibraryImport("user32.dll")]
    private static partial ushort RegisterClassExW(WNDCLASSEXW* wc);

    [LibraryImport("user32.dll")]
    private static partial IntPtr CreateWindowExW(uint exStyle, char* className, char* windowName, uint style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindowW(string? className, string? windowName);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint msg, uint action, IntPtr changeFilterStruct);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hwnd);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint load);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr icon);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll")]
    private static partial IntPtr CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AppendMenuW(IntPtr menu, uint flags, nint id, string? text);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPos);

    [LibraryImport("user32.dll")]
    private static partial int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(IntPtr menu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT pt);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr GetModuleHandleW(string? moduleName);
}
