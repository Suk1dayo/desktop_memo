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
        public const int WM_GETMINMAXINFO = 0x0024;
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
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;
        public const int VK_ESCAPE = 0x1B;

        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "DesktopMemo";

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

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
        public static extern short GetAsyncKeyState(int vKey);

        public static bool IsVirtualKeyPressed(uint vk)
        {
            return (GetAsyncKeyState((int)vk) & 0x8000) != 0;
        }

        public static bool ParseHotkeyString(string spec, out uint modifiers, out uint vk, out string normalized)
        {
            modifiers = 0;
            vk = 0;
            normalized = "";
            if (string.IsNullOrWhiteSpace(spec)) return false;

            bool ctrl = false;
            bool alt = false;
            bool shift = false;
            bool win = false;
            string mainToken = "";

            string[] parts = spec.Split(new char[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string tok = (parts[i] ?? "").Trim();
                if (tok.Length == 0) continue;
                string lower = tok.ToLowerInvariant();
                if (lower == "ctrl" || lower == "control") ctrl = true;
                else if (lower == "alt") alt = true;
                else if (lower == "shift") shift = true;
                else if (lower == "win" || lower == "windows") win = true;
                else mainToken = tok;
            }

            if (string.IsNullOrEmpty(mainToken)) return false;

            System.Windows.Input.Key wpfKey;
            if (!TryParseHotkeyKeyToken(mainToken, out wpfKey)) return false;
            int rawVk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(wpfKey);
            if (rawVk <= 0) return false;

            bool isFnKey = wpfKey >= System.Windows.Input.Key.F1 && wpfKey <= System.Windows.Input.Key.F12;
            if (!ctrl && !alt && !shift && !win && !isFnKey) return false;

            if (ctrl) modifiers |= MOD_CONTROL;
            if (alt) modifiers |= MOD_ALT;
            if (shift) modifiers |= MOD_SHIFT;
            if (win) modifiers |= MOD_WIN;
            vk = (uint)rawVk;

            var sb = new System.Text.StringBuilder();
            if (ctrl) sb.Append("Ctrl+");
            if (alt) sb.Append("Alt+");
            if (shift) sb.Append("Shift+");
            if (win) sb.Append("Win+");
            sb.Append(FormatKeyToken(wpfKey));
            normalized = sb.ToString();
            return true;
        }

        public static bool FormatHotkeyFromWpf(System.Windows.Input.ModifierKeys mods, System.Windows.Input.Key key, out string normalized)
        {
            normalized = "";
            if (key == System.Windows.Input.Key.None ||
                key == System.Windows.Input.Key.System ||
                key == System.Windows.Input.Key.ImeProcessed ||
                key == System.Windows.Input.Key.DeadCharProcessed ||
                key == System.Windows.Input.Key.LeftCtrl ||
                key == System.Windows.Input.Key.RightCtrl ||
                key == System.Windows.Input.Key.LeftAlt ||
                key == System.Windows.Input.Key.RightAlt ||
                key == System.Windows.Input.Key.LeftShift ||
                key == System.Windows.Input.Key.RightShift ||
                key == System.Windows.Input.Key.LWin ||
                key == System.Windows.Input.Key.RWin)
            {
                return false;
            }

            var parts = new System.Collections.Generic.List<string>();
            if ((mods & System.Windows.Input.ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((mods & System.Windows.Input.ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((mods & System.Windows.Input.ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((mods & System.Windows.Input.ModifierKeys.Windows) != 0) parts.Add("Win");
            parts.Add(FormatKeyToken(key));

            uint dummyMods;
            uint dummyVk;
            return ParseHotkeyString(string.Join("+", parts.ToArray()), out dummyMods, out dummyVk, out normalized);
        }

        private static bool TryParseHotkeyKeyToken(string token, out System.Windows.Input.Key key)
        {
            key = System.Windows.Input.Key.None;
            if (string.IsNullOrWhiteSpace(token)) return false;
            string t = token.Trim();
            if (t.Length == 1 && t[0] >= '0' && t[0] <= '9')
            {
                t = "D" + t;
            }
            else if (t.Length == 1 && ((t[0] >= 'a' && t[0] <= 'z') || (t[0] >= 'A' && t[0] <= 'Z')))
            {
                t = t.ToUpperInvariant();
            }
            if (Enum.TryParse<System.Windows.Input.Key>(t, true, out key))
            {
                if (key != System.Windows.Input.Key.None) return true;
            }
            return false;
        }

        private static string FormatKeyToken(System.Windows.Input.Key key)
        {
            if (key >= System.Windows.Input.Key.D0 && key <= System.Windows.Input.Key.D9)
            {
                return ((char)('0' + (key - System.Windows.Input.Key.D0))).ToString();
            }
            return key.ToString();
        }

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
            return HandleDesktopAndResizeMessages(
                window, hwnd, msg, wParam, lParam,
                pinMode, locked, collapsed, isAdjustingZ, 8.0, ref handled);
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
            double outerMargin,
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
            else if (msg == WM_GETMINMAXINFO)
            {
                try
                {
                    double dpiX = 1.0;
                    double dpiY = 1.0;
                    PresentationSource ps = PresentationSource.FromVisual(window);
                    if (ps != null && ps.CompositionTarget != null)
                    {
                        dpiX = ps.CompositionTarget.TransformToDevice.M11;
                        dpiY = ps.CompositionTarget.TransformToDevice.M22;
                    }
                    MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));
                    if (window.MinWidth > 0)
                    {
                        mmi.ptMinTrackSize.X = Math.Max(mmi.ptMinTrackSize.X, (int)Math.Ceiling(window.MinWidth * dpiX));
                    }
                    if (window.MinHeight > 0)
                    {
                        mmi.ptMinTrackSize.Y = Math.Max(mmi.ptMinTrackSize.Y, (int)Math.Ceiling(window.MinHeight * dpiY));
                    }
                    Marshal.StructureToPtr(mmi, lParam, true);
                }
                catch { }
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
                    if (w <= 0 || h <= 0) return IntPtr.Zero;

                    double edge = outerMargin >= 0 ? outerMargin : 8.0;
                    double leftEdge = edge;
                    double rightEdge = w - edge;
                    double topEdge = edge;
                    double bottomEdge = h - edge;
                    const double outerOut = 4.0;
                    const double innerIn = 6.0;
                    const double corner = 16.0;

                    if (pt.X < leftEdge - outerOut || pt.X > rightEdge + outerOut ||
                        pt.Y < topEdge - outerOut || pt.Y > bottomEdge + outerOut)
                    {
                        return IntPtr.Zero;
                    }

                    bool nearLeft = pt.X <= leftEdge + innerIn;
                    bool nearRight = pt.X >= rightEdge - innerIn;
                    bool nearTop = pt.Y <= topEdge + innerIn;
                    bool nearBottom = pt.Y >= bottomEdge - innerIn;

                    bool cornerLeft = pt.X <= leftEdge + corner;
                    bool cornerRight = pt.X >= rightEdge - corner;
                    bool cornerTop = pt.Y <= topEdge + corner;
                    bool cornerBottom = pt.Y >= bottomEdge - corner;

                    if ((nearTop && cornerLeft) || (nearLeft && cornerTop)) { handled = true; return new IntPtr(HTTOPLEFT); }
                    if ((nearTop && cornerRight) || (nearRight && cornerTop)) { handled = true; return new IntPtr(HTTOPRIGHT); }
                    if ((nearBottom && cornerLeft) || (nearLeft && cornerBottom)) { handled = true; return new IntPtr(HTBOTTOMLEFT); }
                    if ((nearBottom && cornerRight) || (nearRight && cornerBottom)) { handled = true; return new IntPtr(HTBOTTOMRIGHT); }
                    if (nearLeft) { handled = true; return new IntPtr(HTLEFT); }
                    if (nearRight) { handled = true; return new IntPtr(HTRIGHT); }
                    if (nearTop) { handled = true; return new IntPtr(HTTOP); }
                    if (nearBottom) { handled = true; return new IntPtr(HTBOTTOM); }
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

        public static bool CreateDesktopShortcut(string targetExePath, string shortcutName, string description, out string createdPath, out string error)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string fileName = string.IsNullOrWhiteSpace(shortcutName) ? "桌面备忘录.lnk" : shortcutName.Trim();
            if (!fileName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) fileName += ".lnk";
            createdPath = System.IO.Path.Combine(desktop, fileName);
            string workDir = System.IO.Path.GetDirectoryName(targetExePath);
            return CreateShortcutAt(createdPath, targetExePath, workDir, description, out error);
        }

        public static bool CreateShortcutAt(string shortcutFullPath, string targetExePath, string workingDir, string description, out string error)
        {
            error = "";
            object shell = null;
            object shortcut = null;
            try
            {
                string dir = System.IO.Path.GetDirectoryName(shortcutFullPath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    error = "系统未注册 WScript.Shell COM 组件。";
                    return false;
                }

                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember("CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutFullPath });
                Type scType = shortcut.GetType();
                scType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { targetExePath });
                scType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut,
                    new object[] { string.IsNullOrEmpty(workingDir) ? System.IO.Path.GetDirectoryName(targetExePath) : workingDir });
                scType.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { targetExePath + ",0" });
                if (!string.IsNullOrEmpty(description))
                {
                    scType.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { description });
                }
                scType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (shortcut != null) { try { Marshal.FinalReleaseComObject(shortcut); } catch { } }
                if (shell != null) { try { Marshal.FinalReleaseComObject(shell); } catch { } }
            }
        }
    }
}
