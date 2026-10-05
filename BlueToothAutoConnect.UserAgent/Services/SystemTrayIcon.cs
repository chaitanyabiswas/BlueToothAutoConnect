using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BlueToothAutoConnect_UserAgent.Services;

public sealed class SystemTrayIcon : IDisposable
{
    private const int CallbackMessage = 0x8000 + 1;
    private const int WmCommand = 0x0111;
    private const int WmLeftButtonDoubleClick = 0x0203;
    private const int WmRightButtonUp = 0x0205;
    private const uint NimAdd = 0;
    private const uint NimModify = 1;
    private const uint NimDelete = 2;
    private const uint NifMessage = 1;
    private const uint NifIcon = 2;
    private const uint NifTip = 4;
    private const uint NifInfo = 0x10;
    private const uint NiifInfo = 1;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x10;
    private const uint LrDefaultSize = 0x40;
    private const uint TpmRightButton = 2;
    private const uint TpmReturnCommand = 0x100;
    private const uint MfString = 0;
    private const uint MfSeparator = 0x800;
    private const uint WmNull = 0;
    private const uint OpenCommand = 1;
    private const uint ExitCommand = 2;

    private readonly string _className = $"BlueToothAutoConnectTray_{Guid.NewGuid():N}";
    private readonly WindowProcedure _windowProcedure;
    private readonly IntPtr _moduleHandle;
    private readonly uint _taskbarCreatedMessage;
    private IntPtr _windowHandle;
    private IntPtr _iconHandle;
    private bool _classRegistered;
    private bool _iconAdded;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public SystemTrayIcon(string iconPath)
    {
        _windowProcedure = WindowProc;
        _moduleHandle = GetModuleHandle(null);
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

        var iconClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            WindowProcedure = _windowProcedure,
            ModuleHandle = _moduleHandle,
            ClassName = _className
        };

        if (RegisterClassEx(ref iconClass) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not register the notification-area window class.");

        _classRegistered = true;
        try
        {
            _windowHandle = CreateWindowEx(
                0,
                _className,
                "Bluetooth Auto Connect notification area",
                0,
                0, 0, 0, 0,
                IntPtr.Zero,
                IntPtr.Zero,
                _moduleHandle,
                IntPtr.Zero);
            if (_windowHandle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the notification-area window.");

            _iconHandle = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
            if (_iconHandle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not load the notification-area icon at '{iconPath}'.");

            var data = CreateIconData();
            data.Flags = NifMessage | NifIcon | NifTip;
            data.CallbackMessage = CallbackMessage;
            data.IconHandle = _iconHandle;
            data.Tip = "Bluetooth Auto Connect";
            if (!ShellNotifyIcon(NimAdd, ref data))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not add the application to the notification area.");

            _iconAdded = true;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void ShowNotification(string title, string message)
    {
        var data = CreateIconData();
        data.Flags = NifInfo;
        data.InfoTitle = Truncate(title, 63);
        data.Info = Truncate(message, 255);
        data.InfoFlags = NiifInfo;
        if (!ShellNotifyIcon(NimModify, ref data))
            DiagnosticLog.WriteException(
                "Notification-area message",
                new Win32Exception(Marshal.GetLastWin32Error(), "Could not show the notification-area message."));
    }

    private IntPtr WindowProc(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (message == _taskbarCreatedMessage)
            {
                AddIcon();
                return IntPtr.Zero;
            }

            if (message == CallbackMessage)
            {
                var notification = unchecked((int)lParam);
                if (notification == WmLeftButtonDoubleClick)
                    OpenRequested?.Invoke();
                else if (notification == WmRightButtonUp)
                    ShowContextMenu();
                return IntPtr.Zero;
            }

            if (message == WmCommand)
            {
                switch ((uint)(wParam.ToUInt64() & 0xFFFF))
                {
                    case OpenCommand:
                        OpenRequested?.Invoke();
                        return IntPtr.Zero;
                    case ExitCommand:
                        ExitRequested?.Invoke();
                        return IntPtr.Zero;
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.WriteException("Notification-area callback", ex);
        }

        return DefWindowProc(window, message, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the notification-area menu.");

        try
        {
            if (!AppendMenu(menu, MfString, OpenCommand, "Open") ||
                !AppendMenu(menu, MfSeparator, 0, null) ||
                !AppendMenu(menu, MfString, ExitCommand, "Exit"))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not populate the notification-area menu.");

            if (!GetCursorPosition(out var point))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not locate the notification-area menu.");

            SetForegroundWindow(_windowHandle);
            var command = TrackPopupMenu(
                menu,
                TpmRightButton | TpmReturnCommand,
                point.X,
                point.Y,
                0,
                _windowHandle,
                IntPtr.Zero);
            if (command == OpenCommand)
                OpenRequested?.Invoke();
            else if (command == ExitCommand)
                ExitRequested?.Invoke();

            PostMessage(_windowHandle, WmNull, UIntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void AddIcon()
    {
        var data = CreateIconData();
        data.Flags = NifMessage | NifIcon | NifTip;
        data.CallbackMessage = CallbackMessage;
        data.IconHandle = _iconHandle;
        data.Tip = "Bluetooth Auto Connect";
        if (!ShellNotifyIcon(NimAdd, ref data))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not add the application to the notification area.");
        _iconAdded = true;
    }

    private IconData CreateIconData() => new()
    {
        Size = (uint)Marshal.SizeOf<IconData>(),
        WindowHandle = _windowHandle,
        Id = 1,
        Tip = string.Empty,
        Info = string.Empty,
        InfoTitle = string.Empty
    };

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    public void Dispose()
    {
        if (_iconAdded && _windowHandle != IntPtr.Zero)
        {
            var data = CreateIconData();
            ShellNotifyIcon(NimDelete, ref data);
            _iconAdded = false;
        }

        if (_windowHandle != IntPtr.Zero)
        {
            DestroyWindow(_windowHandle);
            _windowHandle = IntPtr.Zero;
        }

        if (_classRegistered)
        {
            UnregisterClass(_className, _moduleHandle);
            _classRegistered = false;
        }

        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProcedure(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public WindowProcedure WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr ModuleHandle;
        public IntPtr IconHandle;
        public IntPtr CursorHandle;
        public IntPtr BackgroundBrush;
        public string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string ClassName;
        public IntPtr SmallIconHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct IconData
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr IconHandle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid ItemGuid;
        public IntPtr BalloonIconHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", SetLastError = true)]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "DestroyWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(
        IntPtr instance,
        string name,
        uint imageType,
        int width,
        int height,
        uint loadOptions);

    [DllImport("user32.dll", EntryPoint = "DestroyIcon", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref IconData data);

    [DllImport("user32.dll", EntryPoint = "CreatePopupMenu", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, uint itemId, string? text);

    [DllImport("user32.dll", EntryPoint = "DestroyMenu", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll", EntryPoint = "GetCursorPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPosition(out Point point);

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "TrackPopupMenu", SetLastError = true)]
    private static extern uint TrackPopupMenu(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        int reserved,
        IntPtr window,
        IntPtr rect);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);
}
