using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HandheldCompanion.Managers;

namespace HandheldCompanion.Views
{
    /// <summary>
    /// Minimal ModernFlyouts implementation (ported from ModernFlyouts VolumeControl, AudioFlyoutHelper,
    /// CommonGlyphs, and NativeFlyoutHandler) configured for 10x10 volume division without sliders.
    /// </summary>
    public class ModernVolumeFlyout : Window
    {
        // Ported from ModernFlyouts.Utilities.CommonGlyphs (Segoe MDL2 Assets)
        private static class CommonGlyphs
        {
            public const string Mute = "\uE74F";
            public const string Volume0 = "\uE992";
            public const string Volume1 = "\uE993";
            public const string Volume2 = "\uE994";
            public const string Volume3 = "\uE995";
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private const int SW_HIDE = 0;
        private const int SW_MINIMIZE = 6;

        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint EVENT_OBJECT_CREATE = 0x8000;
        private const uint EVENT_OBJECT_SHOW = 0x8002;

        private delegate void WinEventDelegate(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? lclassName, string? windowTitle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc,
            uint idProcess,
            uint idThread,
            uint dwFlags);

        private static ModernVolumeFlyout? _instance;
        private static bool _nativeHookInstalled;
        private static IntPtr _winEventHook = IntPtr.Zero;
        private static WinEventDelegate? _winEventDelegate;

        private readonly TextBlock _volumeShadowGlyph;
        private readonly TextBlock _volumeGlyph;
        private readonly Border[] _segments = new Border[10];
        private readonly TextBlock _textVal;
        private readonly DispatcherTimer _hideTimer;

        private static readonly SolidColorBrush AccentFillBrush = new(Color.FromRgb(0, 120, 215));
        private static readonly SolidColorBrush PartialFillBrush = new(Color.FromRgb(0, 90, 158));
        private static readonly SolidColorBrush EmptyFillBrush = new(Color.FromRgb(52, 58, 70));
        private static readonly SolidColorBrush MutedFillBrush = new(Color.FromRgb(136, 142, 154));
        private static readonly SolidColorBrush ShadowGlyphBrush = new(Color.FromArgb(110, 255, 255, 255));
        private static readonly SolidColorBrush WhiteBrush = new(Color.FromRgb(255, 255, 255));

        static ModernVolumeFlyout()
        {
            AccentFillBrush.Freeze();
            PartialFillBrush.Freeze();
            EmptyFillBrush.Freeze();
            MutedFillBrush.Freeze();
            ShadowGlyphBrush.Freeze();
            WhiteBrush.Freeze();
        }

        public ModernVolumeFlyout()
        {
            // Minimal ModernFlyouts VolumeControl dimensions: 354x48 inside compact dark flyout card
            Title = "ModernFlyoutsVolumeOSD";
            Width = 362;
            Height = 60;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            IsHitTestVisible = false;

            var outerBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(242, 31, 31, 31)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(140, 72, 76, 86)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Height = 48,
                Margin = new Thickness(8, 6, 6, 6),
                SnapsToDevicePixels = true
            };

            // ModernFlyouts Controls/VolumeControl.xaml Grid: 48px | * | 48px
            var grid = new Grid { Height = 48 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });

            var iconGrid = new Grid
            {
                Width = 48,
                Height = 48,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var mdl2Font = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

            _volumeShadowGlyph = new TextBlock
            {
                Text = CommonGlyphs.Volume3,
                FontFamily = mdl2Font,
                FontSize = 16,
                Foreground = ShadowGlyphBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            _volumeGlyph = new TextBlock
            {
                Text = CommonGlyphs.Volume3,
                FontFamily = mdl2Font,
                FontSize = 16,
                Foreground = WhiteBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            iconGrid.Children.Add(_volumeShadowGlyph);
            iconGrid.Children.Add(_volumeGlyph);
            Grid.SetColumn(iconGrid, 0);
            grid.Children.Add(iconGrid);

            // 10x10 division track (replaces interactive slider with 10 discrete 10% blocks)
            var segmentGrid = new UniformGrid
            {
                Columns = 10,
                Rows = 1,
                Height = 6,
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            for (int i = 0; i < 10; i++)
            {
                var seg = new Border
                {
                    Background = EmptyFillBrush,
                    CornerRadius = new CornerRadius(2),
                    Margin = new Thickness(1.5, 0, 1.5, 0),
                    SnapsToDevicePixels = true
                };
                _segments[i] = seg;
                segmentGrid.Children.Add(seg);
            }
            Grid.SetColumn(segmentGrid, 1);
            grid.Children.Add(segmentGrid);

            // ModernFlyouts SubtitleTextBlockStyle textVal ("00" format)
            _textVal = new TextBlock
            {
                Text = "50",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 19,
                FontWeight = FontWeights.SemiBold,
                Foreground = WhiteBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6)
            };
            Grid.SetColumn(_textVal, 2);
            grid.Children.Add(_textVal);

            outerBorder.Child = grid;
            Content = outerBorder;

            _hideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(2000)
            };
            _hideTimer.Tick += (s, e) =>
            {
                _hideTimer.Stop();
                try { Hide(); } catch { }
            };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST);
            }
        }

        /// <summary>
        /// Pre-creates and warms the flyout HWND/glyph layout on launch and installs the
        /// NativeFlyoutHandler WinEvent hook so Windows' stock OSD never flashes on the first key press.
        /// </summary>
        public static void Initialize()
        {
            var app = Application.Current;
            if (app == null)
                return;

            app.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    InstallNativeFlyoutHook();
                    HideNativeWindowsFlyout();

                    if (_instance == null)
                    {
                        _instance = new ModernVolumeFlyout();
                        var mm = ManagerFactory.multimediaManager;
                        if (mm != null)
                        {
                            _instance.UpdateVolume(mm.GetVolume(), mm.IsMuted());
                        }

                        // Pre-warm HWND, composition layer, and font cache silently off-screen
                        double prevOpacity = _instance.Opacity;
                        _instance.Opacity = 0;
                        _instance.Left = -32000;
                        _instance.Top = -32000;
                        _instance.Show();
                        _instance.Hide();
                        _instance.Opacity = prevOpacity;
                        _instance.PositionTopLeft();
                    }
                }
                catch { }
            });
        }

        private static void InstallNativeFlyoutHook()
        {
            if (_nativeHookInstalled && _winEventHook != IntPtr.Zero)
                return;

            try
            {
                uint shellPid = 0;
                IntPtr shellHwnd = GetShellWindow();
                if (shellHwnd != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(shellHwnd, out int pid);
                    if (pid > 0) shellPid = (uint)pid;
                }

                if (shellPid == 0)
                    return;

                _winEventDelegate ??= NativeWinEventProc;
                _winEventHook = SetWinEventHook(
                    EVENT_OBJECT_CREATE,
                    EVENT_OBJECT_SHOW,
                    IntPtr.Zero,
                    _winEventDelegate,
                    shellPid,
                    0,
                    WINEVENT_OUTOFCONTEXT);

                if (_winEventHook != IntPtr.Zero)
                {
                    _nativeHookInstalled = true;
                }
            }
            catch { }
        }

        private static void NativeWinEventProc(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hWnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime)
        {
            if (idObject != 0 || idChild != 0 || hWnd == IntPtr.Zero)
                return;

            if (eventType != EVENT_OBJECT_CREATE && eventType != EVENT_OBJECT_SHOW)
                return;

            var mm = ManagerFactory.multimediaManager;
            if (mm == null || !mm.ModernFlyoutEnabled)
                return;

            try
            {
                var sb = new StringBuilder(64);
                if (GetClassName(hWnd, sb, sb.Capacity) <= 0)
                    return;

                string cls = sb.ToString();
                if (cls == "NativeHWNDHost")
                {
                    IntPtr child = FindWindowEx(hWnd, IntPtr.Zero, "DirectUIHWND", null);
                    if (child != IntPtr.Zero)
                    {
                        ShowWindowAsync(child, SW_MINIMIZE);
                        ShowWindowAsync(hWnd, SW_HIDE);
                    }
                }
                else if (cls == "XamlExplorerHostIslandWindow")
                {
                    IntPtr child = FindWindowEx(hWnd, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", "DesktopWindowXamlSource");
                    if (child != IntPtr.Zero)
                    {
                        ShowWindowAsync(child, SW_MINIMIZE);
                        ShowWindowAsync(hWnd, SW_HIDE);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Ported from ModernFlyouts.Core.Interop.NativeFlyoutHandler.HideNativeFlyout().
        /// Hides Windows' native volume OSD host window if present.
        /// </summary>
        public static void HideNativeWindowsFlyout()
        {
            try
            {
                HideNativeHost("NativeHWNDHost", "DirectUIHWND", "");
                HideNativeHost("XamlExplorerHostIslandWindow", "Windows.UI.Composition.DesktopWindowContentBridge", "DesktopWindowXamlSource");
            }
            catch { }
        }

        private static void HideNativeHost(string outerClass, string innerClass, string innerName)
        {
            IntPtr hWndHost = IntPtr.Zero;
            while ((hWndHost = FindWindowEx(IntPtr.Zero, hWndHost, outerClass, "")) != IntPtr.Zero)
            {
                IntPtr hWndDUI = FindWindowEx(hWndHost, IntPtr.Zero, innerClass, string.IsNullOrEmpty(innerName) ? null : innerName);
                if (hWndDUI != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(hWndHost, out int pid);
                    try
                    {
                        if (string.Equals(Process.GetProcessById(pid).ProcessName, "explorer", StringComparison.OrdinalIgnoreCase))
                        {
                            ShowWindowAsync(hWndDUI, SW_MINIMIZE);
                            ShowWindowAsync(hWndHost, SW_HIDE);
                        }
                    }
                    catch { }
                }
            }
        }

        private void PositionTopLeft()
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left;
            Top = workArea.Top + 16;
        }

        /// <summary>
        /// Ported from ModernFlyouts.AudioFlyoutHelper.UpdateVolume() & UpdateVolumeGlyph().
        /// </summary>
        private void UpdateVolume(double volume, bool isMuted)
        {
            int vol = Math.Clamp((int)Math.Round(volume), 0, 100);
            _textVal.Text = vol.ToString("00");

            if (!isMuted)
            {
                _volumeShadowGlyph.Visibility = Visibility.Visible;
                if (vol >= 66)
                    _volumeGlyph.Text = CommonGlyphs.Volume3;
                else if (vol < 1)
                    _volumeGlyph.Text = CommonGlyphs.Volume0;
                else if (vol < 33)
                    _volumeGlyph.Text = CommonGlyphs.Volume1;
                else
                    _volumeGlyph.Text = CommonGlyphs.Volume2;

                _volumeGlyph.Foreground = WhiteBrush;
                _textVal.Foreground = WhiteBrush;
            }
            else
            {
                _volumeShadowGlyph.Visibility = Visibility.Collapsed;
                _volumeGlyph.Text = CommonGlyphs.Mute;
                _volumeGlyph.Foreground = MutedFillBrush;
                _textVal.Foreground = MutedFillBrush;
            }

            for (int i = 0; i < 10; i++)
            {
                int segThreshold = (i + 1) * 10;
                int segPrev = i * 10;

                if (vol >= segThreshold)
                {
                    _segments[i].Background = isMuted ? MutedFillBrush : AccentFillBrush;
                }
                else if (vol > segPrev)
                {
                    _segments[i].Background = isMuted ? MutedFillBrush : PartialFillBrush;
                }
                else
                {
                    _segments[i].Background = EmptyFillBrush;
                }
            }
        }

        public static void ShowCurrentVolume()
        {
            var app = Application.Current;
            if (app == null)
                return;

            app.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    InstallNativeFlyoutHook();
                    HideNativeWindowsFlyout();

                    var mm = ManagerFactory.multimediaManager;
                    if (mm == null || !mm.ModernFlyoutEnabled)
                        return;

                    double vol = mm.GetVolume();
                    bool muted = mm.IsMuted();

                    _instance ??= new ModernVolumeFlyout();
                    _instance.PositionTopLeft();
                    _instance.UpdateVolume(vol, muted);

                    if (!_instance.IsVisible)
                    {
                        _instance.Show();
                    }

                    var hwnd = new WindowInteropHelper(_instance).Handle;
                    if (hwnd != IntPtr.Zero)
                    {
                        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                    }

                    _instance._hideTimer.Stop();
                    _instance._hideTimer.Start();
                }
                catch { }
            });
        }
    }
}
