using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace DesktopMemo
{
    public static class Win32Native
    {
        public const int GWL_EXSTYLE = -20;
        public const int GWLP_HWNDPARENT = -8;

        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_APPWINDOW = 0x00040000;

        public const int WM_SYSCOMMAND = 0x0112;
        public const int SC_MINIMIZE = 0xF020;
        public const int WM_WINDOWPOSCHANGING = 0x0046;
        public const int WM_NCHITTEST = 0x0084;
        public const int WM_HOTKEY = 0x0312;

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_FRAMECHANGED = 0x0020;

        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        public const int HTLEFT = 10;
        public const int HTRIGHT = 11;
        public const int HTTOP = 12;
        public const int HTTOPLEFT = 13;
        public const int HTTOPRIGHT = 14;
        public const int HTBOTTOM = 15;
        public const int HTBOTTOMLEFT = 16;
        public const int HTBOTTOMRIGHT = 17;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_NOREPEAT = 0x4000;

        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "DesktopMemo";

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }

        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiFlag);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDPIAware();

        public static void EnablePerMonitorDpiAwareness()
        {
            try
            {
                if (!SetProcessDpiAwarenessContext(new IntPtr(-4)))
                {
                    SetProcessDPIAware();
                }
            }
            catch
            {
                try { SetProcessDPIAware(); } catch { }
            }
        }

        public static IntPtr GetWindowLongPtrSafe(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8) return GetWindowLongPtr64(hWnd, nIndex);
            return GetWindowLong32(hWnd, nIndex);
        }

        public static IntPtr SetWindowLongPtrSafe(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            if (IntPtr.Size == 8) return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
            return SetWindowLong32(hWnd, nIndex, dwNewLong);
        }

        public static IntPtr FindDesktopOwnerWindow()
        {
            try
            {
                IntPtr progman = FindWindow("Progman", null);
                if (progman != IntPtr.Zero)
                {
                    IntPtr shellView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (shellView != IntPtr.Zero)
                    {
                        return progman;
                    }
                }

                IntPtr workerOwner = IntPtr.Zero;
                EnumWindows(delegate(IntPtr topHandle, IntPtr lParam)
                {
                    IntPtr shell = FindWindowEx(topHandle, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (shell != IntPtr.Zero)
                    {
                        workerOwner = topHandle;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);

                if (workerOwner != IntPtr.Zero) return workerOwner;
                return progman;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        public static void ApplyToolWindowStyle(IntPtr hwnd, bool hideFromTaskbar)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                long exStyle = GetWindowLongPtrSafe(hwnd, GWL_EXSTYLE).ToInt64();
                if (hideFromTaskbar)
                {
                    exStyle |= WS_EX_TOOLWINDOW;
                    exStyle &= ~((long)WS_EX_APPWINDOW);
                }
                else
                {
                    exStyle &= ~((long)WS_EX_TOOLWINDOW);
                    exStyle |= WS_EX_APPWINDOW;
                }
                SetWindowLongPtrSafe(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
            catch { }
        }

        public static void ApplyPinMode(Window window, string pinMode, ref bool isAdjustingZ)
        {
            if (window == null) return;
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            isAdjustingZ = true;
            try
            {
                if (pinMode == "topmost")
                {
                    ApplyToolWindowStyle(hwnd, true);
                    SetWindowLongPtrSafe(hwnd, GWLP_HWNDPARENT, IntPtr.Zero);
                    window.Topmost = true;
                    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
                else if (pinMode == "normal")
                {
                    ApplyToolWindowStyle(hwnd, false);
                    SetWindowLongPtrSafe(hwnd, GWLP_HWNDPARENT, IntPtr.Zero);
                    window.Topmost = false;
                    SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
                else
                {
                    ApplyToolWindowStyle(hwnd, true);
                    window.Topmost = false;
                    IntPtr desktopOwner = FindDesktopOwnerWindow();
                    if (desktopOwner != IntPtr.Zero && IsWindow(desktopOwner))
                    {
                        SetWindowLongPtrSafe(hwnd, GWLP_HWNDPARENT, desktopOwner);
                    }
                    else
                    {
                        SetWindowLongPtrSafe(hwnd, GWLP_HWNDPARENT, IntPtr.Zero);
                    }
                    SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            }
            catch { }
            finally
            {
                isAdjustingZ = false;
            }
        }

        public static IntPtr HandleDesktopAndResizeMessages(
            Window window,
            IntPtr hwnd,
            int msg,
            IntPtr wParam,
            IntPtr lParam,
            string pinMode,
            bool locked,
            bool collapsed,
            bool isAdjustingZ,
            ref bool handled)
        {
            if (msg == WM_SYSCOMMAND)
            {
                int cmd = wParam.ToInt32() & 0xFFF0;
                if (cmd == SC_MINIMIZE && pinMode == "desktop")
                {
                    handled = true;
                    return IntPtr.Zero;
                }
            }
            else if (msg == WM_WINDOWPOSCHANGING)
            {
                try
                {
                    if (pinMode == "desktop" && !isAdjustingZ)
                    {
                        WINDOWPOS wp = (WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(WINDOWPOS));
                        if ((wp.flags & SWP_NOZORDER) == 0)
                        {
                            wp.hwndInsertAfter = HWND_BOTTOM;
                            Marshal.StructureToPtr(wp, lParam, true);
                        }
                    }
                }
                catch { }
            }
            else if (msg == WM_NCHITTEST && !locked && !collapsed)
            {
                try
                {
                    if (PresentationSource.FromVisual(window) == null) return IntPtr.Zero;
                    int screenX = unchecked((short)(lParam.ToInt64() & 0xFFFF));
                    int screenY = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
                    Point pt = window.PointFromScreen(new Point(screenX, screenY));
                    double w = window.ActualWidth;
                    double h = window.ActualHeight;
                    const double grip = 8.0;

                    bool left = pt.X >= 0 && pt.X <= grip;
                    bool right = pt.X <= w && pt.X >= w - grip;
                    bool top = pt.Y >= 0 && pt.Y <= grip;
                    bool bottom = pt.Y <= h && pt.Y >= h - grip;

                    if (top && left) { handled = true; return new IntPtr(HTTOPLEFT); }
                    if (top && right) { handled = true; return new IntPtr(HTTOPRIGHT); }
                    if (bottom && left) { handled = true; return new IntPtr(HTBOTTOMLEFT); }
                    if (bottom && right) { handled = true; return new IntPtr(HTBOTTOMRIGHT); }
                    if (left) { handled = true; return new IntPtr(HTLEFT); }
                    if (right) { handled = true; return new IntPtr(HTRIGHT); }
                    if (top) { handled = true; return new IntPtr(HTTOP); }
                    if (bottom) { handled = true; return new IntPtr(HTBOTTOM); }
                }
                catch
                {
                    return IntPtr.Zero;
                }
            }

            return IntPtr.Zero;
        }

        public static void ClampWindowToScreens(ref double left, ref double top, double width, double height)
        {
            double vLeft = SystemParameters.VirtualScreenLeft;
            double vTop = SystemParameters.VirtualScreenTop;
            double vWidth = SystemParameters.VirtualScreenWidth;
            double vHeight = SystemParameters.VirtualScreenHeight;

            if (vWidth <= 0 || vHeight <= 0) return;

            double maxLeft = vLeft + vWidth - Math.Min(width, vWidth);
            double maxTop = vTop + vHeight - Math.Min(height, vHeight);

            if (double.IsNaN(left) || left < vLeft - 40 || left > maxLeft + 40)
            {
                left = Math.Max(vLeft + 24, Math.Min(left, maxLeft - 24));
                if (left < vLeft) left = vLeft + 40;
            }
            if (double.IsNaN(top) || top < vTop - 20 || top > maxTop + 40)
            {
                top = Math.Max(vTop + 24, Math.Min(top, maxTop - 24));
                if (top < vTop) top = vTop + 40;
            }
        }

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return false;
                    object val = key.GetValue(RunValueName);
                    return val != null && !string.IsNullOrWhiteSpace(val.ToString());
                }
            }
            catch
            {
                return false;
            }
        }

        public static string GetAutoStartCommand()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return "";
                    object val = key.GetValue(RunValueName);
                    return val != null ? val.ToString() : "";
                }
            }
            catch
            {
                return "";
            }
        }

        public static bool SetAutoStart(bool enabled, string exePath, out string error)
        {
            error = "";
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null)
                    {
                        using (RegistryKey created = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                        {
                            if (created == null)
                            {
                                error = "无法打开 Windows 启动项注册表键值。";
                                return false;
                            }
                            if (enabled)
                            {
                                created.SetValue(RunValueName, "\"" + exePath + "\" --autostart");
                            }
                        }
                        return true;
                    }

                    if (enabled)
                    {
                        key.SetValue(RunValueName, "\"" + exePath + "\" --autostart");
                    }
                    else
                    {
                        if (key.GetValue(RunValueName) != null)
                        {
                            key.DeleteValue(RunValueName, false);
                        }
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static string CyclePinMode(string current)
        {
            if (current == "desktop") return "topmost";
            if (current == "topmost") return "normal";
            return "desktop";
        }

        public static string GetPinModeLabel(string pinMode)
        {
            if (pinMode == "topmost") return "置顶悬浮";
            if (pinMode == "normal") return "普通窗口";
            return "贴在桌面";
        }
    }
}
