using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Elysium.WorkStation.Services
{
    public class TrayService : ITrayService
    {
        #region Win32 API

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public int    cbSize;
            public uint   style;
            public IntPtr lpfnWndProc;
            public int    cbClsExtra;
            public int    cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int    cbSize;
            public IntPtr hWnd;
            public uint   uID;
            public uint   uFlags;
            public uint   uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint   dwState;
            public uint   dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint   uTimeout;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint   dwInfoFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint   message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint   time;
            public int    ptX, ptY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [DllImport("kernel32.dll")]                                   private static extern IntPtr GetModuleHandle(string m);
        [DllImport("kernel32.dll")]                                   private static extern uint   GetCurrentThreadId();
        [DllImport("user32.dll",   CharSet = CharSet.Unicode)]        private static extern short  RegisterClassEx(ref WNDCLASSEX wc);
        [DllImport("user32.dll",   CharSet = CharSet.Unicode)]        private static extern bool   UnregisterClass(string cls, IntPtr inst);
        [DllImport("user32.dll",   CharSet = CharSet.Unicode)]        private static extern IntPtr CreateWindowEx(uint exStyle, string cls, string wndName, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")]                                     private static extern bool   DestroyWindow(IntPtr hWnd);
        [DllImport("user32.dll",   CharSet = CharSet.Unicode)]        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]                                     private static extern bool   GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
        [DllImport("user32.dll")]                                     private static extern bool   TranslateMessage(ref MSG msg);
        [DllImport("user32.dll")]                                     private static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll")]                                     private static extern bool   PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]                                     private static extern bool   GetCursorPos(out POINT pt);
        [DllImport("user32.dll")]                                     private static extern bool   SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]                                     private static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll",   CharSet = CharSet.Unicode)]        private static extern bool   AppendMenu(IntPtr hMenu, uint flags, uint id, string text);
        [DllImport("user32.dll")]                                     private static extern bool   DestroyMenu(IntPtr hMenu);
        [DllImport("user32.dll")]                                     private static extern uint   TrackPopupMenu(IntPtr hMenu, uint flags, int x, int y, int reserved, IntPtr hWnd, IntPtr rect);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]         private static extern bool   Shell_NotifyIcon(uint msg, ref NOTIFYICONDATA data);
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]            private static extern IntPtr ExtractAssociatedIcon(IntPtr hInst, StringBuilder path, ref short index);

#pragma warning disable IDE1006 // Naming Styles
        private const uint WM_QUIT          = 0x0012;
        private const uint WM_RBUTTONUP     = 0x0205;
        private const uint WM_LBUTTONDBLCLK = 0x0203;
        private const uint WM_USER          = 0x0400;
        private const uint WM_TRAYICON      = WM_USER + 1;
        private const uint NIM_ADD          = 0;
        private const uint NIM_MODIFY       = 1;
        private const uint NIM_DELETE       = 2;
        private const uint NIF_MESSAGE      = 0x01;
        private const uint NIF_ICON         = 0x02;
        private const uint NIF_TIP          = 0x04;
        private const uint NIF_INFO         = 0x10;
        private const uint NIIF_INFO        = 0x01;
        private const uint MF_STRING        = 0x00;
        private const uint MF_SEPARATOR     = 0x800;
        private const uint TPM_RIGHTBUTTON  = 0x0002;
        private const uint TPM_RETURNCMD   = 0x0100;
        private const uint IDM_SHOW         = 1001;
        private const uint IDM_EXIT         = 1002;
        private const uint IDM_QUICKNOTE    = 1003;

        private const string WndClass = "ElysiumTrayWnd";
#pragma warning restore IDE1006 // Naming Styles

        #endregion

        private Thread          _thread;
        private IntPtr          _hwnd;
        private uint            _threadId;
        private WndProcDelegate _wndProcRef;   // Prevent GC collection
        private Action          _onShow;
        private Action          _onExit;
        private Action          _onQuickNote;

        public void Initialize(Action onShow, Action onExit, Action onQuickNote)
        {
            _onShow = onShow;
            _onExit = onExit;
            _onQuickNote = onQuickNote;

            _thread = new Thread(MessageLoop)
            {
                IsBackground = true,
                Name         = "TrayMessageLoop"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        private void MessageLoop()
        {
            _threadId   = GetCurrentThreadId();
            var hInst   = GetModuleHandle(null);
            _wndProcRef = TrayWndProc;

            var wc = new WNDCLASSEX
            {
                cbSize        = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc   = Marshal.GetFunctionPointerForDelegate(_wndProcRef),
                hInstance     = hInst,
                lpszClassName = WndClass
            };

            RegisterClassEx(ref wc);

            _hwnd = CreateWindowEx(0, WndClass, string.Empty, 0,
                0, 0, 0, 0,
                new IntPtr(-3) /* HWND_MESSAGE */,
                IntPtr.Zero, hInst, IntPtr.Zero);

            AddTrayIcon();

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            RemoveTrayIcon();
            DestroyWindow(_hwnd);
            UnregisterClass(WndClass, hInst);
        }

        private void AddTrayIcon()
        {
            var exe   = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            var path  = new StringBuilder(exe, 260);
            short idx = 0;
            var hIcon = ExtractAssociatedIcon(IntPtr.Zero, path, ref idx);

            var nid = new NOTIFYICONDATA
            {
                cbSize          = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd            = _hwnd,
                uID             = 1,
                uFlags          = NIF_ICON | NIF_MESSAGE | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon           = hIcon,
                szTip           = "MyWorkStation"
            };

            Shell_NotifyIcon(NIM_ADD, ref nid);
        }

        private void RemoveTrayIcon()
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd   = _hwnd,
                uID    = 1
            };
            Shell_NotifyIcon(NIM_DELETE, ref nid);
        }

        private IntPtr TrayWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_TRAYICON)
            {
                var ev = (uint)(lParam.ToInt64() & 0xFFFF);

                if (ev == WM_RBUTTONUP)
                    ShowContextMenu(hWnd);
                else if (ev == WM_LBUTTONDBLCLK)
                    MainThread.BeginInvokeOnMainThread(() => _onShow?.Invoke());
            }

            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private void ShowContextMenu(IntPtr hWnd)
        {
            var menu = CreatePopupMenu();
            AppendMenu(menu, MF_STRING,    IDM_SHOW,      "Mostrar");
            AppendMenu(menu, MF_STRING,    IDM_QUICKNOTE, "📝 Nota rápida");
            AppendMenu(menu, MF_SEPARATOR, 0,              string.Empty);
            AppendMenu(menu, MF_STRING,    IDM_EXIT,      "Salir");

            SetForegroundWindow(hWnd);
            GetCursorPos(out var pt);

            var cmd = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON,
                                     pt.X, pt.Y, 0, hWnd, IntPtr.Zero);
            DestroyMenu(menu);

            if (cmd == IDM_SHOW)
                MainThread.BeginInvokeOnMainThread(() => _onShow?.Invoke());
            else if (cmd == IDM_QUICKNOTE)
                MainThread.BeginInvokeOnMainThread(() => _onQuickNote?.Invoke());
            else if (cmd == IDM_EXIT)
                MainThread.BeginInvokeOnMainThread(() => _onExit?.Invoke());
        }

        public void ShowBalloon(string title, string message)
        {
            if (_hwnd == IntPtr.Zero) return;

            var nid = new NOTIFYICONDATA
            {
                cbSize      = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd        = _hwnd,
                uID         = 1,
                uFlags      = NIF_INFO,
                szInfoTitle = title.Length > 63   ? title[..63]     : title,
                szInfo      = message.Length > 255 ? message[..255]  : message,
                dwInfoFlags = NIIF_INFO
            };

            Shell_NotifyIcon(NIM_MODIFY, ref nid);
        }

        public void Dispose()
        {
            if (_threadId != 0)
                PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
