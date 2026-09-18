using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace HandheldCompanion.Views
{
    public static class TrayCleaner
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string? windowTitle);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const uint WM_MOUSEMOVE = 0x0200;

        /// <summary>
        /// Sweeps the Windows notification area toolbars with WM_MOUSEMOVE.
        /// When Explorer detects mouse movement over a tray icon whose owning process has died,
        /// it automatically deletes the dead/phantom button immediately.
        /// </summary>
        public static void Clean()
        {
            try
            {
                // 1. Primary taskbar notification area
                IntPtr hTray = FindWindow("Shell_TrayWnd", null);
                if (hTray != IntPtr.Zero)
                {
                    IntPtr hNotify = FindWindowEx(hTray, IntPtr.Zero, "TrayNotifyWnd", null);
                    if (hNotify != IntPtr.Zero)
                    {
                        IntPtr hPager = FindWindowEx(hNotify, IntPtr.Zero, "SysPager", null);
                        IntPtr hToolbar = FindWindowEx(hPager != IntPtr.Zero ? hPager : hNotify, IntPtr.Zero, "ToolbarWindow32", null);
                        if (hToolbar != IntPtr.Zero)
                        {
                            SweepToolbar(hToolbar);
                        }
                    }
                }

                // 2. Windows 11 / 10 Overflow tray window ("^" popup)
                IntPtr hOverflow = FindWindow("NotifyIconOverflowWindow", null);
                if (hOverflow != IntPtr.Zero)
                {
                    IntPtr hOverflowToolbar = FindWindowEx(hOverflow, IntPtr.Zero, "ToolbarWindow32", null);
                    if (hOverflowToolbar != IntPtr.Zero)
                    {
                        SweepToolbar(hOverflowToolbar);
                    }
                }
            }
            catch { }
        }

        private static void SweepToolbar(IntPtr hToolbar)
        {
            if (GetClientRect(hToolbar, out RECT rect))
            {
                for (int x = 0; x < rect.Right; x += 10)
                {
                    for (int y = 0; y < rect.Bottom; y += 10)
                    {
                        IntPtr lParam = (IntPtr)((y << 16) | (x & 0xffff));
                        SendMessage(hToolbar, WM_MOUSEMOVE, IntPtr.Zero, lParam);
                    }
                }
            }
        }
    }
}
