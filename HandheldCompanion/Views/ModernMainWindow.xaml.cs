using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using GregsStack.InputSimulatorStandard.Native;
using HandheldCompanion.ADLX;
using HandheldCompanion.Devices;
using HandheldCompanion.Devices.Lenovo;
using HandheldCompanion.GraphicsProcessingUnit;
using HandheldCompanion.Inputs;
using HandheldCompanion.Managers;
using HandheldCompanion.Shared;

namespace HandheldCompanion.Views
{
    public partial class ModernMainWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private const int WM_GETMINMAXINFO = 0x0024;
        private const int WM_DISPLAYCHANGE = 0x007E;
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        [StructLayout(LayoutKind.Sequential)]
        private struct WIN32_POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct WIN32_RECT { public int left; public int top; public int right; public int bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public WIN32_POINT ptReserved;
            public WIN32_POINT ptMaxSize;
            public WIN32_POINT ptMaxPosition;
            public WIN32_POINT ptMinTrackSize;
            public WIN32_POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public WIN32_RECT rcMonitor;
            public WIN32_RECT rcWork;
            public uint dwFlags;
        }

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int SW_RESTORE = 9;

        public static ModernMainWindow? Instance { get; private set; }

        private System.Windows.Forms.NotifyIcon? _trayIcon;
        private System.Windows.Forms.ContextMenuStrip? _trayMenu;
        private DispatcherTimer? _diagnosticsTimer;
        private bool _isExiting = false;

        private EventWaitHandle? _showWindowEvent;
        private EventWaitHandle? _exitAppEvent;
        private Thread? _eventListenerThread;
        private volatile bool _isListening = true;

        private static readonly SolidColorBrush BrushGreen = CreateFrozenBrush(48, 209, 88);
        private static readonly SolidColorBrush BrushRed = CreateFrozenBrush(255, 69, 58);
        private static readonly SolidColorBrush BrushGray = CreateFrozenBrush(158, 164, 176);
        private static readonly SolidColorBrush BrushBlue = CreateFrozenBrush(0, 145, 255);
        private static readonly SolidColorBrush BrushWhite = CreateFrozenBrush(240, 240, 240);
        private static readonly SolidColorBrush BrushPurple = CreateFrozenBrush(175, 82, 222);
        private static readonly SolidColorBrush BrushAdminGreenBg = CreateFrozenBrush(28, 56, 37);
        private static readonly SolidColorBrush BrushAdminGreenBorder = CreateFrozenBrush(46, 107, 62);
        private static readonly SolidColorBrush BrushAdminRedBg = CreateFrozenBrush(56, 28, 28);
        private static readonly SolidColorBrush BrushAdminRedBorder = CreateFrozenBrush(107, 46, 46);
        private static readonly SolidColorBrush BrushProfileCardBg = CreateFrozenBrush(37, 41, 50);
        private static readonly SolidColorBrush BrushProfileCardBorder = CreateFrozenBrush(56, 62, 76);

        private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private int _currentTdp = 15;
        private string _currentFanMode = "Auto";
        private int _currentGpuClockMhz = 0;
        private bool _updatingUi = false;
        private ReleaseInfo? _foundRelease;

        // Controller Remapping dropdown caches
        private static readonly List<(string Name, ButtonFlags Flag)> ControllerTargets = new()
        {
            ("A Button", ButtonFlags.B1),
            ("B Button", ButtonFlags.B2),
            ("X Button", ButtonFlags.B3),
            ("Y Button", ButtonFlags.B4),
            ("Left Bumper (LB)", ButtonFlags.L1),
            ("Right Bumper (RB)", ButtonFlags.R1),
            ("Left Trigger (LT)", ButtonFlags.L2Full),
            ("Right Trigger (RT)", ButtonFlags.R2Full),
            ("Left Stick Click (LS)", ButtonFlags.LeftStickClick),
            ("Right Stick Click (RS)", ButtonFlags.RightStickClick),
            ("D-Pad Up", ButtonFlags.DPadUp),
            ("D-Pad Down", ButtonFlags.DPadDown),
            ("D-Pad Left", ButtonFlags.DPadLeft),
            ("D-Pad Right", ButtonFlags.DPadRight),
            ("View / Back", ButtonFlags.Back),
            ("Menu / Start", ButtonFlags.Start),
            ("Xbox / Guide / PS", ButtonFlags.Special),
            ("Touchpad Click", ButtonFlags.TouchpadClick)
        };

        private static readonly List<(string Name, VirtualKeyCode Key)> KeyboardTargets = new()
        {
            ("Esc", VirtualKeyCode.ESCAPE),
            ("Tab", VirtualKeyCode.TAB),
            ("Enter", VirtualKeyCode.RETURN),
            ("Space", VirtualKeyCode.SPACE),
            ("Backspace", VirtualKeyCode.BACK),
            ("Delete", VirtualKeyCode.DELETE),
            ("Left Shift", VirtualKeyCode.LSHIFT),
            ("Right Shift", VirtualKeyCode.RSHIFT),
            ("Left Ctrl", VirtualKeyCode.LCONTROL),
            ("Right Ctrl", VirtualKeyCode.RCONTROL),
            ("Left Alt", VirtualKeyCode.LMENU),
            ("Right Alt", VirtualKeyCode.RMENU),
            ("Windows Key", VirtualKeyCode.LWIN),
            ("F1", VirtualKeyCode.F1),
            ("F2", VirtualKeyCode.F2),
            ("F3", VirtualKeyCode.F3),
            ("F4", VirtualKeyCode.F4),
            ("F5", VirtualKeyCode.F5),
            ("F6", VirtualKeyCode.F6),
            ("F7", VirtualKeyCode.F7),
            ("F8", VirtualKeyCode.F8),
            ("F9", VirtualKeyCode.F9),
            ("F10", VirtualKeyCode.F10),
            ("F11", VirtualKeyCode.F11),
            ("F12", VirtualKeyCode.F12),
            ("Up Arrow", VirtualKeyCode.UP),
            ("Down Arrow", VirtualKeyCode.DOWN),
            ("Left Arrow", VirtualKeyCode.LEFT),
            ("Right Arrow", VirtualKeyCode.RIGHT),
            ("Volume Mute", VirtualKeyCode.VOLUME_MUTE),
            ("Volume Down", VirtualKeyCode.VOLUME_DOWN),
            ("Volume Up", VirtualKeyCode.VOLUME_UP)
        };

        private readonly Dictionary<ButtonFlags, (ComboBox typeCombo, ComboBox targetCombo, ComboBox triggerCombo, CheckBox chkRepeat, TextBlock txtRate, CheckBox chkHaptic)> _remappingControls = new();

        public ModernMainWindow()
        {
            Instance = this;
            InitializeComponent();

            txtVersionTag.Text = $"v{UpdateService.CurrentVersion.ToString(3)}";
            txtCurrentVerSettings.Text = $"Current Version: v{UpdateService.CurrentVersion.ToString(3)}";

            bool isAdmin = Program.IsAdministrator();
            if (isAdmin)
            {
                txtAdminStatus.Text = "Admin: Elevated";
                badgeAdmin.Background = BrushAdminGreenBg;
                badgeAdmin.BorderBrush = BrushAdminGreenBorder;
                txtAdminStatus.Foreground = BrushGreen;
                btnElevate.Visibility = Visibility.Collapsed;
            }
            else
            {
                txtAdminStatus.Text = "Admin: Standard";
                badgeAdmin.Background = BrushAdminRedBg;
                badgeAdmin.BorderBrush = BrushAdminRedBorder;
                txtAdminStatus.Foreground = BrushRed;
                btnElevate.Visibility = Visibility.Visible;
            }

            SyncWindowChromeState();

            InitializeTrayIcon();
            StartEventListener();
            StartDiagnosticsTimer();

            InitializeGeneralView();
            InitializePerformanceView();
            InitializeControllerView();
            InitializeSettingsView();

            ModernVolumeFlyout.Initialize();

            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            this.Deactivated += (_, _) => { if (this.Topmost) this.Topmost = false; };
            this.Loaded += ModernMainWindow_Loaded;
        }

        private WindowState _lastNonMinimizedState = WindowState.Maximized;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                HandleGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            else if (msg == WM_DISPLAYCHANGE)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    if (!_isExiting)
                    {
                        ReapplyMaximizedBounds();
                        SyncWindowChromeState();
                    }
                }));
            }
            return IntPtr.Zero;
        }

        private void HandleGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(monitor, ref mi))
                {
                    var rcWork = mi.rcWork;
                    var rcMonitor = mi.rcMonitor;
                    mmi.ptMaxPosition.x = Math.Abs(rcWork.left - rcMonitor.left);
                    mmi.ptMaxPosition.y = Math.Abs(rcWork.top - rcMonitor.top);
                    mmi.ptMaxSize.x = Math.Abs(rcWork.right - rcWork.left);
                    mmi.ptMaxSize.y = Math.Abs(rcWork.bottom - rcWork.top);
                }
            }

            var source = PresentationSource.FromVisual(this);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
            mmi.ptMinTrackSize.x = (int)Math.Ceiling(MinWidth * dpiX);
            mmi.ptMinTrackSize.y = (int)Math.Ceiling(MinHeight * dpiY);

            Marshal.StructureToPtr(mmi, lParam, true);
        }

        private void ReapplyMaximizedBounds()
        {
            if (WindowState != WindowState.Maximized)
                return;

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
                return;

            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitor, ref mi))
            {
                int w = Math.Abs(mi.rcWork.right - mi.rcWork.left);
                int h = Math.Abs(mi.rcWork.bottom - mi.rcWork.top);
                SetWindowPos(hwnd, IntPtr.Zero, mi.rcWork.left, mi.rcWork.top, w, h,
                    SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
        }

        private void SyncWindowChromeState()
        {
            if (rootGrid == null || windowFrame == null || btnWinMaxRestore == null)
                return;

            if (WindowState != WindowState.Minimized)
            {
                _lastNonMinimizedState = WindowState;
            }

            bool isMax = WindowState == WindowState.Maximized;
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
            rootGrid.Margin = new Thickness(0);
            windowFrame.BorderThickness = isMax ? new Thickness(0) : new Thickness(1);
            btnWinMaxRestore.Content = isMax ? "\uE923" : "\uE922";
            btnWinMaxRestore.ToolTip = isMax ? "Restore" : "Maximize";
        }

        private async void ModernMainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ReapplyMaximizedBounds();
            SyncWindowChromeState();
            RefreshAllStates();
            var currentMode = ControllerModeService.GetCurrentMode();
            UpdateActiveControllerHighlight(currentMode);
            if (ControllerManager.IsInitialized &&
                ManagerFactory.layoutManager != null &&
                ManagerFactory.layoutManager.Status.HasFlag(ManagerStatus.Initialized))
            {
                await ControllerModeService.ApplyMode(currentMode);
            }
        }

        private void BtnWinMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnWinMaxRestore_Click(object sender, RoutedEventArgs e)
        {
            WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        }

        private void BtnWinClose_Click(object sender, RoutedEventArgs e)
        {
            // Defer Close until after the current touch/mouse input frame finishes unwinding
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Close));
        }

        private void Window_StateChanged(object? sender, EventArgs e)
        {
            if (_isExiting)
                return;

            SyncWindowChromeState();

            if (WindowState == WindowState.Minimized)
            {
                _diagnosticsTimer?.Stop();
            }
            else if (IsVisible)
            {
                if (_diagnosticsTimer != null && !_diagnosticsTimer.IsEnabled)
                {
                    _diagnosticsTimer.Start();
                    UpdateDiagnostics();
                }
            }
        }

        private void ScrollViewer_ManipulationBoundaryFeedback(object? sender, ManipulationBoundaryFeedbackEventArgs e)
        {
            e.Handled = true;
        }

        private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (_isExiting)
                return;

            var d = Dispatcher;
            if (d == null || d.HasShutdownStarted || d.HasShutdownFinished)
                return;

            d.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (!_isExiting)
                {
                    ReapplyMaximizedBounds();
                    SyncWindowChromeState();
                    UpdateResolutionControls();
                }
            }));
        }

        #region Navigation
        private void NavGeneral_Click(object sender, RoutedEventArgs e)
        {
            SetNavActive(navGeneral);
            panelGeneral.Visibility = Visibility.Visible;
            panelGraphics.Visibility = Visibility.Collapsed;
            panelPerformance.Visibility = Visibility.Collapsed;
            panelController.Visibility = Visibility.Collapsed;
            panelTuning.Visibility = Visibility.Collapsed;
            panelSettings.Visibility = Visibility.Collapsed;
        }

        private void NavGraphics_Click(object sender, RoutedEventArgs e)
        {
            SetNavActive(navGraphics);
            panelGeneral.Visibility = Visibility.Collapsed;
            panelGraphics.Visibility = Visibility.Visible;
            panelPerformance.Visibility = Visibility.Collapsed;
            panelController.Visibility = Visibility.Collapsed;
            panelTuning.Visibility = Visibility.Collapsed;
            panelSettings.Visibility = Visibility.Collapsed;
            UpdateResolutionControls();
            RefreshPerformanceControls();
            if (!_adlxBusy)
                RefreshAdlxControlsAsync();
        }

        private void NavPerformance_Click(object sender, RoutedEventArgs e)
        {
            SetNavActive(navPerformance);
            panelGeneral.Visibility = Visibility.Collapsed;
            panelGraphics.Visibility = Visibility.Collapsed;
            panelPerformance.Visibility = Visibility.Visible;
            panelController.Visibility = Visibility.Collapsed;
            panelTuning.Visibility = Visibility.Collapsed;
            panelSettings.Visibility = Visibility.Collapsed;
            RefreshPerformanceControls();
        }

        private void NavController_Click(object sender, RoutedEventArgs e)
        {
            SetNavActive(navController);
            panelGeneral.Visibility = Visibility.Collapsed;
            panelGraphics.Visibility = Visibility.Collapsed;
            panelPerformance.Visibility = Visibility.Collapsed;
            panelController.Visibility = Visibility.Visible;
            panelTuning.Visibility = Visibility.Collapsed;
            panelSettings.Visibility = Visibility.Collapsed;
            UpdateGyroStatus();
            UpdateLedControlsFromService();
        }

        private void NavTuning_Click(object sender, RoutedEventArgs e)
        {
            SetNavActive(navTuning);
            panelGeneral.Visibility = Visibility.Collapsed;
            panelGraphics.Visibility = Visibility.Collapsed;
            panelPerformance.Visibility = Visibility.Collapsed;
            panelController.Visibility = Visibility.Collapsed;
            panelTuning.Visibility = Visibility.Visible;
            panelSettings.Visibility = Visibility.Collapsed;
            RefreshTuningControlsAsync();
        }

        private void NavSettings_Click(object sender, RoutedEventArgs e)
        {
            SetNavActive(navSettings);
            panelGeneral.Visibility = Visibility.Collapsed;
            panelGraphics.Visibility = Visibility.Collapsed;
            panelPerformance.Visibility = Visibility.Collapsed;
            panelController.Visibility = Visibility.Collapsed;
            panelTuning.Visibility = Visibility.Collapsed;
            panelSettings.Visibility = Visibility.Visible;
            RefreshBatteryLimitStatusAsync();
        }

        private void SetNavActive(Button activeBtn)
        {
            TouchHoverHelper.ClearActiveHover();
            var activeStyle = (Style)FindResource("ActiveTouchButton");
            var normalStyle = (Style)FindResource("TouchButton");

            navGeneral.Style = (navGeneral == activeBtn) ? activeStyle : normalStyle;
            navGraphics.Style = (navGraphics == activeBtn) ? activeStyle : normalStyle;
            navPerformance.Style = (navPerformance == activeBtn) ? activeStyle : normalStyle;
            navController.Style = (navController == activeBtn) ? activeStyle : normalStyle;
            navTuning.Style = (navTuning == activeBtn) ? activeStyle : normalStyle;
            navSettings.Style = (navSettings == activeBtn) ? activeStyle : normalStyle;

            TouchHoverHelper.SetIsHovered(navGeneral, false);
            TouchHoverHelper.SetIsHovered(navGraphics, false);
            TouchHoverHelper.SetIsHovered(navPerformance, false);
            TouchHoverHelper.SetIsHovered(navController, false);
            TouchHoverHelper.SetIsHovered(navTuning, false);
            TouchHoverHelper.SetIsHovered(navSettings, false);
        }
        #endregion

        #region General View (Controller Mode, TDP, Fan, Resolution)
        private void InitializeGeneralView()
        {
            ControllerModeService.ModeChanged += (mode) =>
            {
                Dispatcher.BeginInvoke(() => UpdateActiveControllerHighlight(mode));
            };

            if (ManagerFactory.multimediaManager != null)
            {
                ManagerFactory.multimediaManager.VolumeNotification += (vol) =>
                {
                    Dispatcher.BeginInvoke(UpdateVolumeControls);
                };
            }

            UpdateActiveControllerHighlight(ControllerModeService.GetCurrentMode());
            int savedTdp = ManagerFactory.settingsManager.GetInt("SelectedTdp");
            if (savedTdp >= 5 && savedTdp <= 35)
                _currentTdp = savedTdp;
            ApplyTdp(_currentTdp);
            UpdateResolutionControls();
            UpdateVolumeControls();

            // Re-sync TDP, Fan state, and Battery Charge Limit once low-level drivers (PawnIO / WMI / DeviceManager) finish initializing
            PerformanceManager.Initialized += (_, _) =>
            {
                if (!_isExiting)
                    Dispatcher.BeginInvoke(() => ApplyTdp(_currentTdp));
            };

            // Real-time Legion L + Y hardware thermal mode shortcut listener (LENOVO_GAMEZONE_THERMAL_MODE_EVENT)
            LegionGo.HardwareThermalModeChanged += (oemMode) =>
            {
                if (_isExiting) return;
                Dispatcher.BeginInvoke(() =>
                {
                    int mappedTdp = oemMode switch
                    {
                        (int)LegionGo.LegionMode.Quiet => 10,
                        (int)LegionGo.LegionMode.Balanced => 15,
                        (int)LegionGo.LegionMode.Performance => 25,
                        _ => _currentTdp
                    };
                    _currentTdp = mappedTdp;
                    ManagerFactory.settingsManager.SetProperty("SelectedTdp", mappedTdp);
                    UpdateTdpUi(mappedTdp);
                });
            };

            if (!ManagerFactory.settingsManager.HasSetting("ThermalGuard90"))
                ManagerFactory.settingsManager.SetProperty("ThermalGuard90", true);
            bool thermalGuardOn = ManagerFactory.settingsManager.GetBoolean("ThermalGuard90");
            chkThermalGuard90.IsChecked = thermalGuardOn;
            chkThermalGuard90Settings.IsChecked = thermalGuardOn;
            StartThermalGuardMonitor();
            SyncHardwareOnStartupAsync();
        }

        private System.Threading.Timer? _thermalGuardTimer;
        private volatile bool _thermalGuardTriggered = false;
        private string _fanModeBeforeThermalGuard = "Auto";

        private void StartThermalGuardMonitor()
        {
            if (_thermalGuardTimer != null) return;
            _thermalGuardTimer = new System.Threading.Timer(_ => PollThermalAndFanTelemetry(), null, 2500, 4000);
        }

        private void PollThermalAndFanTelemetry()
        {
            if (_isExiting) return;
            try
            {
                int cpuTemp = 0;
                int gpuTemp = 0;
                int fanRpm = 0;

                if (IDevice.GetCurrent() is LegionGo lego)
                {
                    cpuTemp = lego.GetCpuTemperature();
                    gpuTemp = lego.GetGpuTemperature();
                    fanRpm = lego.GetCpuFanSpeed();
                }
                else if (ADLX.ADLXBackend.IsInitialized)
                {
                    gpuTemp = (int)Math.Round(ADLX.ADLXBackend.GetGPUTemperature(0));
                    fanRpm = ADLX.ADLXBackend.GetGPUFanSpeed(0);
                }

                int maxTemp = Math.Max(cpuTemp, gpuTemp);
                bool guardEnabled = ManagerFactory.settingsManager.GetBoolean("ThermalGuard90");

                if (IDevice.GetCurrent() is LegionGo legoGuard)
                {
                    if (guardEnabled && maxTemp >= 90 && !_thermalGuardTriggered)
                    {
                        _thermalGuardTriggered = true;
                        _fanModeBeforeThermalGuard = _currentFanMode;
                        legoGuard.SetFanFullSpeed(true);
                        Dispatcher.BeginInvoke(() =>
                        {
                            if (!_isExiting)
                                UpdateFanUi(isAuto: false, isFullSpeed: true, percentage: 100);
                        });
                    }
                    else if (_thermalGuardTriggered && (!guardEnabled || (maxTemp > 0 && maxTemp <= 82)))
                    {
                        _thermalGuardTriggered = false;
                        string restore = _fanModeBeforeThermalGuard;
                        Dispatcher.BeginInvoke(() =>
                        {
                            if (_isExiting) return;
                            if (restore == "100") ApplyFanMode(false, true, 100);
                            else if (restore == "Silent" || restore == "Cool") ApplyFanCurvePreset(restore);
                            else if (int.TryParse(restore, out int p) && p > 0) ApplyFanMode(false, false, p);
                            else ApplyFanMode(true, false, 0);
                        });
                    }
                }

                Dispatcher.BeginInvoke(() =>
                {
                    if (_isExiting) return;
                    if (!IsVisible || WindowState == WindowState.Minimized || panelGeneral.Visibility != Visibility.Visible)
                        return;

                    string tempStr = cpuTemp > 0
                        ? (gpuTemp > 0 ? $"CPU: {cpuTemp}°C  |  GPU: {gpuTemp}°C" : $"APU Temp: {cpuTemp}°C")
                        : (gpuTemp > 0 ? $"GPU Temp: {gpuTemp}°C" : "Temp: --");
                    string rpmStr = fanRpm > 0 ? $"  |  Fan: {fanRpm} RPM" : "";
                    string guardTag = _thermalGuardTriggered ? "  [90°C GUARD ACTIVE: 100%]" : "";
                    txtFanLiveTelemetry.Text = tempStr + rpmStr + guardTag;
                    txtFanLiveTelemetry.Foreground = (maxTemp >= 90 || _thermalGuardTriggered) ? BrushRed : BrushGreen;
                });
            }
            catch { }
        }

        private void ChkThermalGuard90_Click(object sender, RoutedEventArgs e)
        {
            bool enabled = (sender as CheckBox)?.IsChecked ?? false;
            if (!enabled)
            {
                var result = MessageBox.Show(
                    "Disabling the 90°C Thermal Guard can allow the APU to overheat under load and cause an unexpected thermal shutdown.\n\nAre you sure you want to turn it off?",
                    "Disable 90°C Thermal Guard?",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (result != MessageBoxResult.Yes)
                {
                    enabled = true;
                }
            }

            chkThermalGuard90.IsChecked = enabled;
            chkThermalGuard90Settings.IsChecked = enabled;
            ManagerFactory.settingsManager.SetProperty("ThermalGuard90", enabled);
            Task.Run(PollThermalAndFanTelemetry);
        }

        private void SyncHardwareOnStartupAsync()
        {
            Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 24; i++)
                    {
                        if (_isExiting) return;
                        bool layoutReady = ManagerFactory.layoutManager != null &&
                                           ManagerFactory.layoutManager.Status.HasFlag(ManagerStatus.Initialized);
                        if (PerformanceManager.IsInitialized &&
                            IDevice.GetCurrent() is LegionGo &&
                            ControllerManager.IsInitialized &&
                            layoutReady)
                            break;
                        await Task.Delay(500);
                    }

                    if (_isExiting) return;

                    // 0. Ensure the saved Controller Mode (Desktop / DS4 / X360 / Native) is actively applied
                    var savedMode = ControllerModeService.GetCurrentMode();
                    if (ManagerFactory.layoutManager != null)
                    {
                        await ControllerModeService.ApplyMode(savedMode);
                        Dispatcher.BeginInvoke(() =>
                        {
                            if (!_isExiting) UpdateActiveControllerHighlight(savedMode);
                        });
                    }

                    // 1. Re-apply saved TDP so SMU + Lenovo OEM Power Mode & Power LED sync accurately
                    Dispatcher.BeginInvoke(() => ApplyTdp(_currentTdp));

                    // 2. Restore saved CPU Boost after TDP/power profile sync (only if explicitly saved)
                    if (ManagerFactory.settingsManager.HasSetting("CpuBoostEnabled"))
                    {
                        bool savedBoost = ManagerFactory.settingsManager.GetBoolean("CpuBoostEnabled");
                        await Task.Delay(300);
                        if (_isExiting) return;
                        PerformanceManager.SetCpuBoostEnabled(savedBoost);
                        Dispatcher.BeginInvoke(() =>
                        {
                            if (_isExiting) return;
                            var activeStyle = (Style)FindResource("ActiveTouchButton");
                            var normalStyle = (Style)FindResource("TouchButton");
                            btnCpuBoostOn.Style = savedBoost ? activeStyle : normalStyle;
                            btnCpuBoostOff.Style = !savedBoost ? activeStyle : normalStyle;
                        });
                    }

                    // 3. Restore saved Fan Curve / Mode (Silent, Cool, 100%, percentage, or Auto)
                    if (IDevice.GetCurrent() is LegionGo lego)
                    {
                        bool hwFullSpeed = lego.GetFanFullSpeed();
                        bool hasSavedFan = ManagerFactory.settingsManager.HasSetting("FanMode");
                        string savedFan = hasSavedFan ? (ManagerFactory.settingsManager.GetString("FanMode") ?? "Auto") : string.Empty;

                        Dispatcher.BeginInvoke(() =>
                        {
                            if (_isExiting) return;
                            if (savedFan == "100")
                            {
                                ApplyFanMode(isAuto: false, isFullSpeed: true, percentage: 100);
                            }
                            else if (savedFan == "Silent" || savedFan == "Cool")
                            {
                                ApplyFanCurvePreset(savedFan);
                            }
                            else if (int.TryParse(savedFan, out int pct) && pct > 0)
                            {
                                ApplyFanMode(isAuto: false, isFullSpeed: false, percentage: pct);
                            }
                            else if (hwFullSpeed && !hasSavedFan)
                            {
                                UpdateFanUi(isAuto: false, isFullSpeed: true, percentage: 100);
                            }
                            else
                            {
                                ApplyFanMode(isAuto: true, isFullSpeed: false, percentage: 0);
                            }
                        });
                    }

                    // 4. Restore saved Trackpad Enabled state (default ON when unset so trackpad is never stuck disabled)
                    bool savedTp = ManagerFactory.settingsManager.HasSetting("TrackpadEnabled")
                        ? ManagerFactory.settingsManager.GetBoolean("TrackpadEnabled")
                        : true;
                    _trackpadEnabled = savedTp;
                    try
                    {
                        bool wantPassthrough = !savedTp || savedMode == ControllerTargetMode.Native || savedMode == ControllerTargetMode.Pointer;
                        if (IDevice.GetCurrent() is LegionGo legoTp)
                            legoTp.SetPassthrough(wantPassthrough);
                        Devices.Lenovo.SapientiaUsb.Init();
                        Devices.Lenovo.SapientiaUsb.SetTouchPadStatus(savedTp ? 1 : 0);
                        Devices.Lenovo.SapientiaUsb.SetTrackpadStatus(4, savedTp ? 1 : 0);
                    }
                    catch { }
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (!_isExiting) UpdateTuningUi();
                    });

                    // 5. Sync real hardware Battery Charge Limit & Auto-Rotation
                    RefreshBatteryLimitStatusAsync();
                    AutoRotationService.Instance.InitializeFromSettings();
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (!_isExiting)
                        {
                            chkAutoRotation.IsChecked = AutoRotationService.Instance.IsEnabled;
                            UpdateGyroStatus();
                        }
                    });
                }
                catch { }
            });
        }

        private void UpdateActiveControllerHighlight(ControllerTargetMode mode)
        {
            var activeStyle = (Style)FindResource("ActiveTouchButton");
            var normalStyle = (Style)FindResource("TouchButton");

            btnModeDesktop.Style = (mode == ControllerTargetMode.Desktop) ? activeStyle : normalStyle;
            btnModeX360.Style = (mode == ControllerTargetMode.X360) ? activeStyle : normalStyle;
            btnModeDS4.Style = (mode == ControllerTargetMode.DS4) ? activeStyle : normalStyle;
            btnModeNative.Style = (mode == ControllerTargetMode.Native) ? activeStyle : normalStyle;

            txtModeHint.Text = mode switch
            {
                ControllerTargetMode.Desktop => "Active Mode: Desktop (Right Pad / Stick acts as mouse, clicks enabled)",
                ControllerTargetMode.X360 => "Active Mode: Xbox 360 (Standard XInput for broad compatibility)",
                ControllerTargetMode.DS4 => "Active Mode: DualShock 4 (Direct motion feed for Gyro Aiming)",
                ControllerTargetMode.Native => "Active Mode: Native Direct (Raw Legion Go hardware passthrough)",
                _ => $"Active Mode: {mode}"
            };

            UpdateGyroStatus();
        }

        private async void BtnModeDesktop_Click(object sender, RoutedEventArgs e) => await ControllerModeService.ApplyMode(ControllerTargetMode.Desktop);
        private async void BtnModeX360_Click(object sender, RoutedEventArgs e) => await ControllerModeService.ApplyMode(ControllerTargetMode.X360);
        private async void BtnModeDS4_Click(object sender, RoutedEventArgs e) => await ControllerModeService.ApplyMode(ControllerTargetMode.DS4);
        private async void BtnModeNative_Click(object sender, RoutedEventArgs e) => await ControllerModeService.ApplyMode(ControllerTargetMode.Native);

        private void BtnTdpPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string s && int.TryParse(s, out int tdp))
            {
                ApplyTdp(tdp);
            }
        }

        private void BtnTdpMinus_Click(object sender, RoutedEventArgs e) => ApplyTdp(Math.Max(5, _currentTdp - 1));
        private void BtnTdpPlus_Click(object sender, RoutedEventArgs e) => ApplyTdp(Math.Min(35, _currentTdp + 1));

        private void ApplyTdp(int tdp)
        {
            _currentTdp = Math.Clamp(tdp, 5, 35);
            int targetTdp = _currentTdp;
            try
            {
                ManagerFactory.settingsManager.SetProperty("SelectedTdp", targetTdp);
                UpdateTdpUi(targetTdp);

                Task.Run(() =>
                {
                    try
                    {
                        if (IDevice.GetCurrent() is LegionGo lego)
                        {
                            int oemMode = (int)LegionGo.LegionMode.Custom;
                            if (targetTdp <= 10) oemMode = (int)LegionGo.LegionMode.Quiet;
                            else if (targetTdp == 15) oemMode = (int)LegionGo.LegionMode.Balanced;
                            else if (targetTdp == 25) oemMode = (int)LegionGo.LegionMode.Performance;

                            lego.SetSmartFanMode(oemMode);
                            lego.set_long_limit(targetTdp);
                            lego.set_short_limit(targetTdp);
                        }

                        PerformanceManager.SetTDP(targetTdp, true);
                        JoystickLedService.Instance.ApplyToHardware();
                    }
                    catch (Exception ex)
                    {
                        LogManager.LogError("Failed to apply TDP in background: {0}", ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                LogManager.LogError("Failed to apply TDP: {0}", ex.Message);
            }
        }

        private void UpdateTdpUi(int tdp)
        {
            txtTdpCurrent.Text = $"{tdp}W";

            var activeStyle = (Style)FindResource("ActiveTouchButton");
            var normalStyle = (Style)FindResource("TouchButton");

            btnTdp10.Style = (tdp == 10) ? activeStyle : normalStyle;
            btnTdp15.Style = (tdp == 15) ? activeStyle : normalStyle;
            btnTdp20.Style = (tdp == 20) ? activeStyle : normalStyle;
            btnTdp25.Style = (tdp == 25) ? activeStyle : normalStyle;
            btnTdp30.Style = (tdp == 30) ? activeStyle : normalStyle;
            btnTdp35.Style = (tdp == 35) ? activeStyle : normalStyle;

            if (tdp <= 10)
            {
                txtTdpModeDesc.Text = "Quiet Mode (Blue Power LED)";
                txtTdpModeDesc.Foreground = new SolidColorBrush(Color.FromRgb(0, 145, 255));
            }
            else if (tdp == 15)
            {
                txtTdpModeDesc.Text = "Balanced Mode (White Power LED)";
                txtTdpModeDesc.Foreground = new SolidColorBrush(Color.FromRgb(240, 240, 240));
            }
            else if (tdp == 25)
            {
                txtTdpModeDesc.Text = "Performance Mode (Red Power LED)";
                txtTdpModeDesc.Foreground = new SolidColorBrush(Color.FromRgb(255, 59, 48));
            }
            else
            {
                txtTdpModeDesc.Text = "Custom Power Mode (Purple Power LED)";
                txtTdpModeDesc.Foreground = new SolidColorBrush(Color.FromRgb(175, 82, 222));
            }
        }

        private void BtnFanAuto_Click(object sender, RoutedEventArgs e) => ApplyFanMode(isAuto: true, isFullSpeed: false, percentage: 0);
        private void BtnFanFull_Click(object sender, RoutedEventArgs e) => ApplyFanMode(isAuto: false, isFullSpeed: true, percentage: 100);
        private void BtnFanPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string s && int.TryParse(s, out int pct))
            {
                ApplyFanMode(isAuto: false, isFullSpeed: false, percentage: pct);
            }
        }

        private void BtnFanCurve_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string curveName)
            {
                ApplyFanCurvePreset(curveName);
            }
        }

        private void ApplyFanCurvePreset(string curveName)
        {
            try
            {
                var activeStyle = (Style)FindResource("ActiveTouchButton");
                var normalStyle = (Style)FindResource("TouchButton");

                btnFanAuto.Style = normalStyle;
                btnFanFull.Style = normalStyle;
                btnFan50.Style = normalStyle;
                btnFan70.Style = normalStyle;
                btnFanSilent.Style = (curveName == "Silent") ? activeStyle : normalStyle;
                btnFanCool.Style = (curveName == "Cool") ? activeStyle : normalStyle;
                _currentFanMode = curveName;
                ManagerFactory.settingsManager.SetProperty("FanMode", _currentFanMode);

                Task.Run(() =>
                {
                    try
                    {
                        if (IDevice.GetCurrent() is LegionGo lego)
                        {
                            lego.SetFanFullSpeed(false);
                            ushort[] table = curveName == "Silent"
                                ? new ushort[] { 30, 35, 40, 45, 52, 60, 70, 82, 100, 100 }
                                : new ushort[] { 50, 58, 65, 72, 80, 88, 95, 100, 100, 100 };
                            lego.SetFanTable(new FanTable(table));
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.LogError("Failed to apply Fan curve '{0}': {1}", curveName, ex.Message);
                    }
                });
            }
            catch { }
        }

        private void UpdateFanUi(bool isAuto, bool isFullSpeed, int percentage)
        {
            var activeStyle = (Style)FindResource("ActiveTouchButton");
            var normalStyle = (Style)FindResource("TouchButton");

            btnFanAuto.Style = isAuto ? activeStyle : normalStyle;
            btnFanFull.Style = isFullSpeed ? activeStyle : normalStyle;
            btnFan50.Style = (!isAuto && !isFullSpeed && percentage == 50) ? activeStyle : normalStyle;
            btnFan70.Style = (!isAuto && !isFullSpeed && percentage == 70) ? activeStyle : normalStyle;
            if (btnFanSilent != null) btnFanSilent.Style = normalStyle;
            if (btnFanCool != null) btnFanCool.Style = normalStyle;
            _currentFanMode = isAuto ? "Auto" : (isFullSpeed ? "100" : percentage.ToString());
        }

        private void ApplyFanMode(bool isAuto, bool isFullSpeed, int percentage)
        {
            try
            {
                UpdateFanUi(isAuto, isFullSpeed, percentage);
                ManagerFactory.settingsManager.SetProperty("FanMode", _currentFanMode);

                Task.Run(() =>
                {
                    try
                    {
                        if (IDevice.GetCurrent() is LegionGo lego)
                        {
                            if (isFullSpeed)
                            {
                                lego.SetFanFullSpeed(true);
                            }
                            else if (isAuto)
                            {
                                lego.SetFanFullSpeed(false);
                                lego.SetSmartFanMode((int)LegionGo.LegionMode.Balanced);
                            }
                            else
                            {
                                lego.SetFanFullSpeed(false);
                                ushort clampedSpeed = (ushort)Math.Clamp(percentage, 0, 100);
                                lego.SetFanTable(new FanTable(new ushort[] { clampedSpeed, clampedSpeed, clampedSpeed, clampedSpeed, clampedSpeed, clampedSpeed, clampedSpeed, clampedSpeed, clampedSpeed, clampedSpeed }));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.LogError("Failed to apply Fan mode in background: {0}", ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                LogManager.LogError("Failed to apply Fan mode: {0}", ex.Message);
            }
        }

        private void BtnRes_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string s)
            {
                if (s == "800") ApplyResolution(1280, 800);
                else if (s == "1000") ApplyResolution(1600, 1000);
                else if (s == "1200") ApplyResolution(1920, 1200);
                else if (s == "1600") ApplyResolution(2560, 1600);
            }
        }

        private void BtnRefreshRate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string s && int.TryParse(s, out int hz))
            {
                try
                {
                    var mm = ManagerFactory.multimediaManager;
                    var dev = mm?.PrimaryDesktop?.screen?.DeviceName ?? System.Windows.Forms.Screen.PrimaryScreen?.DeviceName;
                    int w = 0, h = 0;
                    if (!string.IsNullOrEmpty(dev))
                    {
                        var curDm = MultimediaManager.GetDisplay(dev);
                        w = Math.Max(curDm.dmPelsWidth, curDm.dmPelsHeight);
                        h = Math.Min(curDm.dmPelsWidth, curDm.dmPelsHeight);
                    }
                    if (w > 0 && h > 0 && mm != null)
                    {
                        bool ok = mm.SetResolution(w, h, hz);
                        if (ok)
                        {
                            UpdateResolutionControls(w, h, hz);
                            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => UpdateResolutionControls()));
                        }
                        else
                        {
                            UpdateResolutionControls();
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogManager.LogError("BtnRefreshRate_Click error: {0}", ex.Message);
                }
            }
        }

        private void ApplyResolution(int width, int height)
        {
            try
            {
                int freq = ManagerFactory.multimediaManager?.PrimaryDesktop?.GetCurrentFrequency() ?? 0;
                bool ok = ManagerFactory.multimediaManager?.SetResolution(width, height, freq) ?? false;
                if (ok)
                {
                    // Immediately reflect the committed resolution in the UI, then re-verify live OS settings
                    UpdateResolutionControls(width, height, freq > 0 ? freq : (int?)null);
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => UpdateResolutionControls()));
                }
                else
                {
                    UpdateResolutionControls();
                    txtCurrentRes.Text += $"  ({width}x{height} unavailable in display driver)";
                }
            }
            catch (Exception ex)
            {
                LogManager.LogError("ApplyResolution error: {0}", ex.Message);
            }
        }

        private void UpdateResolutionControls(int? expectedW = null, int? expectedH = null, int? expectedHz = null)
        {
            int currentW = 0, currentH = 0, currentHz = expectedHz ?? 0;
            if (expectedW.HasValue && expectedH.HasValue)
            {
                currentW = expectedW.Value;
                currentH = expectedH.Value;
            }

            var primary = ManagerFactory.multimediaManager?.PrimaryDesktop;
            string? deviceName = primary?.screen?.DeviceName ?? System.Windows.Forms.Screen.PrimaryScreen?.DeviceName;
            if (!string.IsNullOrEmpty(deviceName))
            {
                var liveDm = MultimediaManager.GetDisplay(deviceName);
                if (currentW == 0 && liveDm.dmPelsWidth > 0 && liveDm.dmPelsHeight > 0)
                {
                    currentW = liveDm.dmPelsWidth;
                    currentH = liveDm.dmPelsHeight;
                }
                if (currentHz == 0 && liveDm.dmDisplayFrequency > 0)
                {
                    currentHz = liveDm.dmDisplayFrequency;
                }
            }

            if (currentW == 0 || currentH == 0)
            {
                var current = primary?.GetResolution();
                if (current != null)
                {
                    currentW = current.Width;
                    currentH = current.Height;
                }
                else
                {
                    var scr = System.Windows.Forms.Screen.PrimaryScreen;
                    if (scr != null)
                    {
                        currentW = scr.Bounds.Width;
                        currentH = scr.Bounds.Height;
                    }
                }
            }

            int w = Math.Max(currentW, currentH);
            int h = Math.Min(currentW, currentH);

            txtCurrentRes.Text = currentHz > 0 ? $"Current: {w}x{h} @ {currentHz}Hz" : $"Current: {w}x{h}";

            var activeStyle = (Style)FindResource("ActiveTouchButton");
            var normalStyle = (Style)FindResource("TouchButton");

            btnRes800.Style = (w == 1280 && h == 800) ? activeStyle : normalStyle;
            btnRes1000.Style = (w == 1600 && h == 1000) ? activeStyle : normalStyle;
            btnRes1200.Style = (w == 1920 && h == 1200) ? activeStyle : normalStyle;
            btnRes1600.Style = (w == 2560 && h == 1600) ? activeStyle : normalStyle;

            btnHz60.Style = (currentHz >= 58 && currentHz <= 62) ? activeStyle : normalStyle;
            btnHz144.Style = (currentHz >= 140 && currentHz <= 148) ? activeStyle : normalStyle;
        }

        #region Performance View (CPU Boost, EPP, GPU Clock, AMD ADLX, Per-Game Profiles)
        private void InitializePerformanceView()
        {
            GameProfileService.Instance.ActiveProfileChanged += (activeName) =>
            {
                if (_isExiting) return;
                Dispatcher.BeginInvoke(() =>
                {
                    if (_isExiting) return;
                    txtActiveGameProfile.Text = string.IsNullOrEmpty(activeName)
                        ? "Active Profile: None (Desktop)"
                        : $"Active Profile: {activeName}";
                    UpdateResolutionControls();
                    RefreshPerformanceControls();
                });
            };

            RefreshRunningAppsCombo();
            RebuildGameProfilesList();
            RefreshPerformanceControls();
        }

        private void RefreshPerformanceControls()
        {
            try
            {
                var activeStyle = (Style)FindResource("ActiveTouchButton");
                var normalStyle = (Style)FindResource("TouchButton");

                // CPU Boost
                bool boostOn = PerformanceManager.GetCpuBoostEnabled();
                btnCpuBoostOn.Style = boostOn ? activeStyle : normalStyle;
                btnCpuBoostOff.Style = !boostOn ? activeStyle : normalStyle;

                // EPP
                uint epp = PerformanceManager.GetEppValue();
                btnEpp80.Style = (epp >= 70) ? activeStyle : normalStyle;
                btnEpp50.Style = (epp >= 35 && epp < 70) ? activeStyle : normalStyle;
                btnEpp20.Style = (epp >= 10 && epp < 35) ? activeStyle : normalStyle;
                btnEpp0.Style = (epp < 10) ? activeStyle : normalStyle;

                // GPU Clock
                _currentGpuClockMhz = PerformanceManager.GetManualGpuClock();
                UpdateGpuClockVisuals();

                // Per-Game Profiles status
                bool profilesOn = GameProfileService.Instance.Enabled;
                btnProfilesOn.Style = profilesOn ? activeStyle : normalStyle;
                btnProfilesOff.Style = !profilesOn ? activeStyle : normalStyle;
                string? activeName = GameProfileService.Instance.ActiveProfileName;
                txtActiveGameProfile.Text = string.IsNullOrEmpty(activeName)
                    ? "Active Profile: None (Desktop)"
                    : $"Active Profile: {activeName}";

                // AMD ADLX (queried on background thread so UI never stalls)
                RefreshAdlxControlsAsync();
            }
            catch { }
        }

        private void UpdateGpuClockVisuals()
        {
            if (_currentGpuClockMhz >= 10000) _currentGpuClockMhz = 0;

            var activeStyle = (Style)FindResource("ActiveTouchButton");
            var normalStyle = (Style)FindResource("TouchButton");

            btnGpuAuto.Style = (_currentGpuClockMhz <= 0) ? activeStyle : normalStyle;
            btnGpu800.Style = (_currentGpuClockMhz == 800) ? activeStyle : normalStyle;
            btnGpu1200.Style = (_currentGpuClockMhz == 1200) ? activeStyle : normalStyle;
            btnGpu1600.Style = (_currentGpuClockMhz == 1600) ? activeStyle : normalStyle;
            btnGpu2000.Style = (_currentGpuClockMhz == 2000) ? activeStyle : normalStyle;
            btnGpu2400.Style = (_currentGpuClockMhz == 2400) ? activeStyle : normalStyle;
            btnGpu2700.Style = (_currentGpuClockMhz == 2700) ? activeStyle : normalStyle;
            txtGpuClockCurrent.Text = _currentGpuClockMhz <= 0 ? "Auto" : $"{_currentGpuClockMhz} MHz";
        }

        private void BtnCpuBoost_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string s)
            {
                bool enable = s == "1";
                var activeStyle = (Style)FindResource("ActiveTouchButton");
                var normalStyle = (Style)FindResource("TouchButton");
                btnCpuBoostOn.Style = enable ? activeStyle : normalStyle;
                btnCpuBoostOff.Style = !enable ? activeStyle : normalStyle;
                ManagerFactory.settingsManager.SetProperty("CpuBoostEnabled", enable);
                Task.Run(() => PerformanceManager.SetCpuBoostEnabled(enable));
            }
        }

        private void BtnEpp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string s && uint.TryParse(s, out uint epp))
            {
                var activeStyle = (Style)FindResource("ActiveTouchButton");
                var normalStyle = (Style)FindResource("TouchButton");
                btnEpp80.Style = (epp == 80) ? activeStyle : normalStyle;
                btnEpp50.Style = (epp == 50) ? activeStyle : normalStyle;
                btnEpp20.Style = (epp == 20) ? activeStyle : normalStyle;
                btnEpp0.Style = (epp == 0) ? activeStyle : normalStyle;
                Task.Run(() => PerformanceManager.SetEppValue(epp));
            }
        }

        private void BtnGpuClockPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string s && int.TryParse(s, out int mhz))
            {
                ApplyGpuClock(mhz);
            }
        }

        private void BtnGpuClockMinus_Click(object sender, RoutedEventArgs e)
        {
            int next = _currentGpuClockMhz <= 400 ? 0 : _currentGpuClockMhz - 100;
            ApplyGpuClock(next);
        }

        private void BtnGpuClockPlus_Click(object sender, RoutedEventArgs e)
        {
            int next = _currentGpuClockMhz <= 0 ? 1200 : Math.Min(3000, _currentGpuClockMhz + 100);
            ApplyGpuClock(next);
        }

        private void ApplyGpuClock(int mhz)
        {
            _currentGpuClockMhz = mhz <= 0 ? 0 : Math.Clamp(mhz, 400, 3000);
            UpdateGpuClockVisuals();
            int target = _currentGpuClockMhz;
            Task.Run(() => PerformanceManager.SetManualGpuClock(target));
        }

        private void RefreshAdlxControlsAsync()
        {
            if (!ADLXBackend.IsInitialized || GPUManager.GetCurrent() is not AMDGPU amdGpu || !amdGpu.IsInitialized)
            {
                btnAdlxInteger.IsEnabled = false;
                btnAdlxGpuScale.IsEnabled = false;
                btnAdlxFsr.IsEnabled = false;
                btnAdlxAfmf.IsEnabled = false;
                btnAdlxAntiLag.IsEnabled = false;
                btnAdlxSharpen.IsEnabled = false;
                btnAdlxVivid.IsEnabled = false;
                btnScalePreserve.IsEnabled = false;
                btnScaleFull.IsEnabled = false;
                btnScaleCenter.IsEnabled = false;
                txtAdlxStatus.Text = "ADLX: Initializing AMD Radeon driver...";
                txtAdlxStatus.Foreground = BrushGray;
                return;
            }

            Task.Run(() =>
            {
                if (!ADLXBackend.IsInitialized || GPUManager.GetCurrent() is not AMDGPU gpu || !gpu.IsInitialized)
                    return;

                bool ready = false;
                bool supInt = false, valInt = false;
                bool supGpuScale = false, valGpuScale = false;
                bool supScaleMode = false;
                int valScaleMode = -1;
                bool supRsr = false, valRsr = false;
                bool supAfmf = false, valAfmf = false;
                bool supAntiLag = false, valAntiLag = false;
                bool supSharp = false, valSharp = false;
                bool supVivid = false, valVivid = false;

                try
                {
                    supInt = gpu.HasIntegerScalingSupport();
                    if (supInt) valInt = gpu.GetIntegerScaling();

                    supGpuScale = gpu.HasGPUScalingSupport();
                    if (supGpuScale) valGpuScale = gpu.GetGPUScaling();

                    supScaleMode = gpu.HasScalingModeSupport();
                    if (supScaleMode) valScaleMode = gpu.GetScalingMode();

                    if (ManagerFactory.multimediaManager is not null)
                    {
                        ManagerFactory.multimediaManager.PreferredIntegerScaling = valInt;
                        if (valScaleMode == 1 && ManagerFactory.multimediaManager.PreferredScalingMode != 1)
                            valScaleMode = ManagerFactory.multimediaManager.PreferredScalingMode;
                    }

                    supRsr = gpu.HasRSRSupport();
                    if (supRsr) valRsr = gpu.GetRSR();

                    supAfmf = gpu.HasAFMFSupport();
                    if (supAfmf) valAfmf = gpu.GetAFMF();

                    valAntiLag = gpu.GetAntiLag();
                    supAntiLag = true;

                    supSharp = gpu.GetImageSharpeningSharpness() >= 0;
                    if (supSharp) valSharp = gpu.GetImageSharpening();

                    supVivid = gpu.HasVividColorSupport();
                    if (supVivid) valVivid = gpu.GetVividColors();

                    bool chillOn = gpu.GetChill();
                    int chillFps = chillOn ? gpu.GetChillMaxFPS() : 0;
                    _currentChillFps = chillOn && chillFps >= 30 ? chillFps : 0;

                    ready = supInt || supGpuScale || supScaleMode || supRsr || supAfmf || supAntiLag || supSharp || supVivid;
                }
                catch { }

                if (_isExiting) return;
                Dispatcher.BeginInvoke(() =>
                {
                    if (_isExiting) return;
                    var activeStyle = (Style)FindResource("ActiveTouchButton");
                    var normalStyle = (Style)FindResource("TouchButton");

                    UpdateChillUi(_currentChillFps);

                    btnAdlxInteger.IsEnabled = supInt;
                    btnAdlxInteger.Content = valInt ? "Integer Scaling: On" : "Integer Scaling: Off";
                    btnAdlxInteger.Style = valInt ? activeStyle : normalStyle;

                    btnAdlxGpuScale.IsEnabled = supGpuScale;
                    btnAdlxGpuScale.Content = valGpuScale ? "GPU Scaling: On" : "GPU Scaling: Off";
                    btnAdlxGpuScale.Style = valGpuScale ? activeStyle : normalStyle;

                    // Scaling Mode only applies when GPU Scaling is ON and Integer Scaling is OFF
                    bool scaleModeActive = supScaleMode && valGpuScale && !valInt;
                    btnScalePreserve.IsEnabled = scaleModeActive;
                    btnScaleFull.IsEnabled = scaleModeActive;
                    btnScaleCenter.IsEnabled = scaleModeActive;
                    btnScalePreserve.Style = (scaleModeActive && valScaleMode == 0) ? activeStyle : normalStyle;
                    btnScaleFull.Style = (scaleModeActive && valScaleMode == 1) ? activeStyle : normalStyle;
                    btnScaleCenter.Style = (scaleModeActive && valScaleMode == 2) ? activeStyle : normalStyle;

                    btnAdlxFsr.IsEnabled = supRsr;
                    btnAdlxFsr.Content = valRsr ? "RSR / FSR: On" : "RSR / FSR: Off";
                    btnAdlxFsr.Style = valRsr ? activeStyle : normalStyle;

                    btnAdlxAfmf.IsEnabled = supAfmf;
                    btnAdlxAfmf.Content = valAfmf ? "Frame Gen (AFMF): On" : "Frame Gen (AFMF): Off";
                    btnAdlxAfmf.Style = valAfmf ? activeStyle : normalStyle;

                    btnAdlxAntiLag.IsEnabled = supAntiLag;
                    btnAdlxAntiLag.Content = valAntiLag ? "Anti-Lag: On" : "Anti-Lag: Off";
                    btnAdlxAntiLag.Style = valAntiLag ? activeStyle : normalStyle;

                    btnAdlxSharpen.IsEnabled = supSharp;
                    btnAdlxSharpen.Content = valSharp ? "Sharpening: On" : "Sharpening: Off";
                    btnAdlxSharpen.Style = valSharp ? activeStyle : normalStyle;

                    btnAdlxVivid.IsEnabled = supVivid;
                    btnAdlxVivid.Content = valVivid ? "Vivid Colors: On" : "Vivid Colors: Off";
                    btnAdlxVivid.Style = valVivid ? activeStyle : normalStyle;

                    if (ready)
                    {
                        string intDetail = valInt ? "ON (2x Pixel Doubling @ 800p)" : "OFF";
                        txtAdlxStatus.Text = $"Driver Active  |  GPU Scale: {(valGpuScale ? "ON" : "OFF")}  |  Integer: {intDetail}  |  AFMF: {(valAfmf ? "ON" : "OFF")}  |  Anti-Lag: {(valAntiLag ? "ON" : "OFF")}  |  Vivid: {(valVivid ? "ON" : "OFF")}";
                    }
                    else
                    {
                        txtAdlxStatus.Text = "ADLX: Waiting for AMD Radeon driver (or unsupported on current display)";
                    }
                    txtAdlxStatus.Foreground = ready ? BrushGreen : BrushGray;
                });
            });
        }

        private int _currentChillFps = 0;

        private void UpdateChillUi(int fps)
        {
            var activeStyle = (Style)FindResource("ActiveTouchButton");
            var normalStyle = (Style)FindResource("TouchButton");

            btnChillOff.Style = (fps <= 0) ? activeStyle : normalStyle;
            btnChill30.Style = (fps == 30) ? activeStyle : normalStyle;
            btnChill40.Style = (fps == 40) ? activeStyle : normalStyle;
            btnChill45.Style = (fps == 45) ? activeStyle : normalStyle;
            btnChill60.Style = (fps == 60) ? activeStyle : normalStyle;
            btnChill72.Style = (fps == 72) ? activeStyle : normalStyle;
            btnChill90.Style = (fps == 90) ? activeStyle : normalStyle;
            btnChill120.Style = (fps == 120) ? activeStyle : normalStyle;
            btnChill144.Style = (fps == 144) ? activeStyle : normalStyle;
            txtChillCurrent.Text = fps <= 0 ? "Off" : $"{fps} FPS";
        }

        private void ApplyChillCap(int fps)
        {
            _currentChillFps = fps <= 0 ? 0 : Math.Clamp(fps, 30, 300);
            UpdateChillUi(_currentChillFps);
            int target = _currentChillFps;
            GPUManager.ManualGraphicsOverride = true;
            Task.Run(() =>
            {
                try
                {
                    if (GPUManager.GetCurrent() is AMDGPU gpu && gpu.IsInitialized)
                    {
                        gpu.ApplyChillFpsCap(target);
                        Thread.Sleep(120);
                        if (!_isExiting) Dispatcher.BeginInvoke(RefreshAdlxControlsAsync);
                    }
                }
                catch (Exception ex)
                {
                    LogManager.LogError("ApplyChillCap failed: {0}", ex.Message);
                }
            });
        }

        private void BtnChillPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string s && int.TryParse(s, out int fps))
                ApplyChillCap(fps);
        }

        private void BtnChillMinus_Click(object sender, RoutedEventArgs e)
        {
            int next = _currentChillFps <= 30 ? 0 : _currentChillFps - 5;
            ApplyChillCap(next);
        }

        private void BtnChillPlus_Click(object sender, RoutedEventArgs e)
        {
            int next = _currentChillFps <= 0 ? 60 : Math.Min(240, _currentChillFps + 5);
            ApplyChillCap(next);
        }

        private volatile bool _adlxBusy = false;

        private void BtnScalingMode_Click(object sender, RoutedEventArgs e)
        {
            if (_adlxBusy)
                return;
            if (!ADLXBackend.IsInitialized || GPUManager.GetCurrent() is not AMDGPU gpu || !gpu.IsInitialized)
                return;
            if (sender is not Button b || b.Tag is not string s || !int.TryParse(s, out int mode))
                return;

            GPUManager.ManualGraphicsOverride = true;
            _adlxBusy = true;
            Task.Run(() =>
            {
                try
                {
                    var profile = ManagerFactory.profileManager?.GetCurrent();
                    if (gpu.HasIntegerScalingSupport() && gpu.GetIntegerScaling())
                    {
                        gpu.SetIntegerScaling(false);
                        if (profile is not null) profile.IntegerScalingEnabled = false;
                        Thread.Sleep(200);
                    }
                    if (gpu.HasGPUScalingSupport() && !gpu.GetGPUScaling())
                    {
                        gpu.SetGPUScaling(true);
                        if (profile is not null) profile.GPUScaling = true;
                        Thread.Sleep(200);
                    }
                    gpu.SetScalingMode(mode);
                    if (profile is not null) profile.ScalingMode = mode;
                    ManagerFactory.multimediaManager?.CommitDisplayScalingMode(false, mode);
                    Thread.Sleep(350);
                }
                catch (Exception ex)
                {
                    LogManager.LogError("ADLX SetScalingMode({0}) failed: {1}", mode, ex.Message);
                }
                finally
                {
                    _adlxBusy = false;
                }
                RefreshAdlxControlsAsync();
            });
        }

        private void BtnAdlxToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_adlxBusy)
                return;
            if (!ADLXBackend.IsInitialized || GPUManager.GetCurrent() is not AMDGPU gpu || !gpu.IsInitialized)
                return;
            if (sender is not Button b || b.Tag is not string feature)
                return;

            GPUManager.ManualGraphicsOverride = true;
            _adlxBusy = true;
            Task.Run(() =>
            {
                try
                {
                    var profile = ManagerFactory.profileManager?.GetCurrent();
                    switch (feature)
                    {
                        case "IntegerScaling":
                            bool curInt = gpu.GetIntegerScaling();
                            if (!curInt)
                            {
                                // Turning ON: disable mutually-exclusive RSR and ensure GPU Scaling is ON first
                                if (gpu.HasRSRSupport() && gpu.GetRSR())
                                {
                                    gpu.SetRSR(false);
                                    if (profile is not null) profile.RSREnabled = false;
                                    Thread.Sleep(150);
                                }
                                if (gpu.HasGPUScalingSupport() && !gpu.GetGPUScaling())
                                {
                                    gpu.SetGPUScaling(true);
                                    if (profile is not null) profile.GPUScaling = true;
                                    Thread.Sleep(250);
                                }
                                if (profile is not null) profile.IntegerScalingEnabled = true;
                                gpu.SetIntegerScaling(true);
                                ManagerFactory.multimediaManager?.CommitDisplayScalingMode(true, ManagerFactory.multimediaManager?.PreferredScalingMode ?? 0);
                            }
                            else
                            {
                                if (profile is not null) profile.IntegerScalingEnabled = false;
                                gpu.SetIntegerScaling(false);
                                ManagerFactory.multimediaManager?.CommitDisplayScalingMode(false, ManagerFactory.multimediaManager?.PreferredScalingMode ?? 0);
                            }
                            break;
                        case "GPUScaling":
                            bool curGpu = gpu.GetGPUScaling();
                            if (curGpu)
                            {
                                // Turning OFF: must disable Integer Scaling first or AMD rejects disabling GPU Scaling
                                if (gpu.HasIntegerScalingSupport() && gpu.GetIntegerScaling())
                                {
                                    if (profile is not null) profile.IntegerScalingEnabled = false;
                                    gpu.SetIntegerScaling(false);
                                    Thread.Sleep(200);
                                }
                                if (profile is not null) profile.GPUScaling = false;
                                gpu.SetGPUScaling(false);
                            }
                            else
                            {
                                if (profile is not null) profile.GPUScaling = true;
                                gpu.SetGPUScaling(true);
                            }
                            break;
                        case "FSR":
                            bool curRsr = gpu.GetRSR();
                            if (!curRsr && gpu.HasIntegerScalingSupport() && gpu.GetIntegerScaling())
                            {
                                if (profile is not null) profile.IntegerScalingEnabled = false;
                                gpu.SetIntegerScaling(false);
                                Thread.Sleep(200);
                            }
                            if (profile is not null) profile.RSREnabled = !curRsr;
                            gpu.SetRSR(!curRsr);
                            break;
                        case "AFMF":
                            bool nextAfmf = !gpu.GetAFMF();
                            if (profile is not null) profile.AFMFEnabled = nextAfmf;
                            gpu.SetAFMF(nextAfmf);
                            break;
                        case "AntiLag":
                            bool nextAntiLag = !gpu.GetAntiLag();
                            gpu.SetAntiLag(nextAntiLag);
                            break;
                        case "Sharpen":
                            bool nextSharp = !gpu.GetImageSharpening();
                            if (profile is not null) profile.RISEnabled = nextSharp;
                            gpu.SetImageSharpening(nextSharp);
                            break;
                        case "Vivid":
                            bool nextVivid = !gpu.GetVividColors();
                            gpu.SetVividColors(nextVivid);
                            break;
                    }
                    Thread.Sleep(feature == "Vivid" || feature == "AFMF" || feature == "AntiLag" || feature == "Sharpen" ? 120 : 350);
                }
                catch (Exception ex)
                {
                    LogManager.LogError("ADLX toggle '{0}' failed: {1}", feature, ex.Message);
                }
                finally
                {
                    _adlxBusy = false;
                }

                RefreshAdlxControlsAsync();
            });
        }

        private void BtnProfilesToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string s)
            {
                bool enable = s == "1";
                GameProfileService.Instance.Enabled = enable;
                var activeStyle = (Style)FindResource("ActiveTouchButton");
                var normalStyle = (Style)FindResource("TouchButton");
                btnProfilesOn.Style = enable ? activeStyle : normalStyle;
                btnProfilesOff.Style = !enable ? activeStyle : normalStyle;
            }
        }

        private void BtnRefreshApps_Click(object sender, RoutedEventArgs e)
        {
            RefreshRunningAppsCombo();
        }

        private void RefreshRunningAppsCombo()
        {
            string prev = cmbRunningApps.Text;
            cmbRunningApps.Items.Clear();
            foreach (var proc in GameProfileService.GetRunningWindowedProcesses())
            {
                cmbRunningApps.Items.Add(proc);
            }
            if (!string.IsNullOrWhiteSpace(prev))
                cmbRunningApps.Text = prev;
            else if (cmbRunningApps.Items.Count > 0)
                cmbRunningApps.SelectedIndex = 0;
        }

        private void BtnSaveGameProfile_Click(object sender, RoutedEventArgs e)
        {
            string procName = (cmbRunningApps.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(procName))
                return;

            int curResH = 0, curHz = 0;
            try
            {
                var dev = ManagerFactory.multimediaManager?.PrimaryDesktop?.screen?.DeviceName ?? System.Windows.Forms.Screen.PrimaryScreen?.DeviceName;
                if (!string.IsNullOrEmpty(dev))
                {
                    var dm = MultimediaManager.GetDisplay(dev);
                    curResH = Math.Min(dm.dmPelsWidth, dm.dmPelsHeight);
                    curHz = dm.dmDisplayFrequency;
                }
            }
            catch { }

            var profile = new GamePerformanceProfile
            {
                ProcessName = procName,
                TdpWatts = _currentTdp,
                FanMode = _currentFanMode,
                ResolutionHeight = curResH,
                RefreshRate = curHz,
                CpuBoost = PerformanceManager.GetCpuBoostEnabled(),
                Epp = (int)PerformanceManager.GetEppValue(),
                GpuClockMhz = _currentGpuClockMhz
            };

            GameProfileService.Instance.AddOrUpdateProfile(profile);
            RebuildGameProfilesList();
        }

        private void RebuildGameProfilesList()
        {
            panelGameProfilesList.Children.Clear();
            var profiles = GameProfileService.Instance.GetProfiles();

            if (profiles.Count == 0)
            {
                panelGameProfilesList.Children.Add(new TextBlock
                {
                    Text = "No saved game profiles yet. Configure TDP/Fan/Res/Boost/GPU Clock, pick a running game above, and tap 'Save Current Settings for App'.",
                    FontSize = 11.5,
                    Foreground = BrushGray,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(2, 2, 0, 0)
                });
                return;
            }

            var normalStyle = (Style)FindResource("TouchButton");
            foreach (var p in profiles)
            {
                var row = new Border
                {
                    Background = BrushProfileCardBg,
                    BorderBrush = BrushProfileCardBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 6, 8, 6),
                    Margin = new Thickness(0, 0, 0, 5)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                textStack.Children.Add(new TextBlock
                {
                    Text = p.ProcessName,
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = BrushWhite
                });
                textStack.Children.Add(new TextBlock
                {
                    Text = p.Summary,
                    FontSize = 11,
                    Foreground = BrushGray,
                    Margin = new Thickness(0, 1, 0, 0)
                });

                var btnDel = new Button
                {
                    Content = "Delete",
                    Height = 28,
                    Padding = new Thickness(10, 2, 10, 2),
                    FontSize = 11.5,
                    Style = normalStyle,
                    Tag = p.ProcessName
                };
                btnDel.Click += (s, _) =>
                {
                    if (s is Button btn && btn.Tag is string name)
                    {
                        GameProfileService.Instance.RemoveProfile(name);
                        RebuildGameProfilesList();
                    }
                };

                Grid.SetColumn(textStack, 0);
                Grid.SetColumn(btnDel, 1);
                grid.Children.Add(textStack);
                grid.Children.Add(btnDel);
                row.Child = grid;
                panelGameProfilesList.Children.Add(row);
            }
        }
        #endregion

        private void UpdateVolumeControls()
        {
            var mm = ManagerFactory.multimediaManager;
            if (mm == null) return;

            var activeStyle = (Style)FindResource("ActiveTouchButton");
            var normalStyle = (Style)FindResource("TouchButton");

            bool flyoutOn = mm.ModernFlyoutEnabled;
            btnFlyoutOn.Style = flyoutOn ? activeStyle : normalStyle;
            btnFlyoutOff.Style = !flyoutOn ? activeStyle : normalStyle;

            int step = mm.VolumeStep;
            btnVolStep10.Style = (step == 10) ? activeStyle : normalStyle;
            btnVolStep5.Style = (step == 5) ? activeStyle : normalStyle;
            btnVolStep2.Style = (step == 2) ? activeStyle : normalStyle;

            int vol = (int)Math.Round(mm.GetVolume());
            bool muted = mm.IsMuted();
            txtVolCurrent.Text = muted ? "MUTE" : $"{vol}%";
            btnVolMute.Style = muted ? activeStyle : normalStyle;
            btnVolMute.Content = muted ? "Unmute" : "Mute";
        }

        private void BtnFlyoutOn_Click(object sender, RoutedEventArgs e)
        {
            if (ManagerFactory.multimediaManager != null)
            {
                ManagerFactory.multimediaManager.ModernFlyoutEnabled = true;
                UpdateVolumeControls();
                ModernVolumeFlyout.ShowCurrentVolume();
            }
        }

        private void BtnFlyoutOff_Click(object sender, RoutedEventArgs e)
        {
            if (ManagerFactory.multimediaManager != null)
            {
                ManagerFactory.multimediaManager.ModernFlyoutEnabled = false;
                UpdateVolumeControls();
            }
        }

        private void BtnVolStep_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string s && int.TryParse(s, out int step) && ManagerFactory.multimediaManager != null)
            {
                ManagerFactory.multimediaManager.VolumeStep = step;
                UpdateVolumeControls();
                ModernVolumeFlyout.ShowCurrentVolume();
            }
        }

        private void BtnVolDown_Click(object sender, RoutedEventArgs e)
        {
            if (ManagerFactory.multimediaManager != null)
            {
                ManagerFactory.multimediaManager.DecreaseVolume();
                UpdateVolumeControls();
                ModernVolumeFlyout.ShowCurrentVolume();
            }
        }

        private void BtnVolUp_Click(object sender, RoutedEventArgs e)
        {
            if (ManagerFactory.multimediaManager != null)
            {
                ManagerFactory.multimediaManager.IncreaseVolume();
                UpdateVolumeControls();
                ModernVolumeFlyout.ShowCurrentVolume();
            }
        }

        private void BtnVolMute_Click(object sender, RoutedEventArgs e)
        {
            if (ManagerFactory.multimediaManager != null)
            {
                ManagerFactory.multimediaManager.ToggleMute();
                UpdateVolumeControls();
                ModernVolumeFlyout.ShowCurrentVolume();
            }
        }
        #endregion

        #region Controller View (Gyro, Haptics, Joystick LED, Pointer Mode, Hold Cycle, Remap)
        private int _currentVibrationStrength = 100;

        private void InitializeControllerView()
        {
            // Gyro
            UpdateGyroStatus();

            // Haptics / Vibration Strength
            string? savedVib = ManagerFactory.settingsManager.GetString("VibrationStrength");
            _currentVibrationStrength = int.TryParse(savedVib, out int v) ? Math.Clamp(v, 0, 100) : 100;
            UpdateVibrationUi(_currentVibrationStrength);

            // LED Service events
            JoystickLedService.Instance.ConfigChanged += () =>
            {
                Dispatcher.BeginInvoke(UpdateLedControlsFromService);
            };
            UpdateLedControlsFromService();

            // Pointer Mode initial values
            var ptr = PointerModeService.Instance;
            chkPointerDetachedOnly.IsChecked = ptr.DetachedOnly;
            chkPointerRightClickRB.IsChecked = ptr.RightClickOnRB;
            chkPointerInvertX.IsChecked = ptr.InvertX;
            chkPointerInvertY.IsChecked = ptr.InvertY;
            cmbPointerBtn.SelectedIndex = Math.Clamp((int)ptr.ActivationButton, 0, cmbPointerBtn.Items.Count - 1);
            cmbPointerType.SelectedIndex = Math.Clamp((int)ptr.ActivationType, 0, cmbPointerType.Items.Count - 1);
            txtPointerSens.Text = $"{ptr.Sensitivity:F1}x";

            // Hold to Cycle initial values
            var cycle = CustomMappingService.Instance;
            chkHoldCycleEnabled.IsChecked = cycle.HoldCycleEnabled;
            txtHoldTime.Text = $"{cycle.HoldCycleDurationMs}ms";
            chkCycleDesktop.IsChecked = cycle.CycleDesktop;
            chkCycleX360.IsChecked = cycle.CycleX360;
            chkCycleDS4.IsChecked = cycle.CycleDS4;
            chkCycleNative.IsChecked = cycle.CycleNative;
            chkCyclePointer.IsChecked = cycle.CyclePointer;

            cmbTriggerBtn.Items.Clear();
            cmbTriggerBtn.Items.Add(new ComboBoxItem { Content = "(None / Disabled)" });
            foreach (var b in CustomMappingService.RemappableButtons)
            {
                cmbTriggerBtn.Items.Add(new ComboBoxItem { Content = CustomMappingService.GetButtonDisplayName(b) });
            }
            int trigIdx = Array.IndexOf(CustomMappingService.RemappableButtons, cycle.HoldCycleButton);
            cmbTriggerBtn.SelectedIndex = trigIdx >= 0 ? trigIdx + 1 : 0;
            UpdateCycleSummary();

            int ds4ShareMode = Math.Clamp(ManagerFactory.settingsManager.GetInt("Ds4ShareMode"), 0, 2);
            Targets.DualShock4Target.Ds4ShareMode = ds4ShareMode;
            _updatingUi = true;
            try { comboDs4ShareMode.SelectedIndex = ds4ShareMode; }
            finally { _updatingUi = false; }

            // Build Remapping rows
            BuildRemappingRows();
        }

        private void UpdateGyroStatus()
        {
            bool enabled = ControllerManager.GyroAimingEnabled;
            var activeStyle = (Style)FindResource("ActiveTouchButton");
            var normalStyle = (Style)FindResource("TouchButton");

            btnGyroOn.Style = enabled ? activeStyle : normalStyle;
            btnGyroOff.Style = !enabled ? activeStyle : normalStyle;

            var gyroSrc = ControllerManager.GyroSource;
            btnGyroSrcLeft.Style = (gyroSrc == ControllerManager.GyroSourceMode.Left) ? activeStyle : normalStyle;
            btnGyroSrcTablet.Style = (gyroSrc == ControllerManager.GyroSourceMode.Tablet) ? activeStyle : normalStyle;
            btnGyroSrcRight.Style = (gyroSrc == ControllerManager.GyroSourceMode.Right) ? activeStyle : normalStyle;

            bool isDesktop = ManagerFactory.layoutManager?.GetCurrentMode() == LayoutModes.Desktop;
            bool isDS4 = ControllerModeService.GetCurrentMode() == ControllerTargetMode.DS4;

            if (!enabled)
            {
                txtGyroStatus.Text = "Gyro Aiming is Off.";
                txtGyroStatus.Foreground = new SolidColorBrush(Color.FromRgb(158, 164, 176));
            }
            else if (isDesktop)
            {
                txtGyroStatus.Text = "Gyro Aiming is On (Disabled in Desktop mode).";
                txtGyroStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 159, 10));
            }
            else if (!isDS4)
            {
                txtGyroStatus.Text = "Gyro Aiming is On (Disabled: only active when DS4 mode is selected).";
                txtGyroStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 159, 10));
            }
            else
            {
                string srcLabel = gyroSrc switch
                {
                    ControllerManager.GyroSourceMode.Left => "Left Controller",
                    ControllerManager.GyroSourceMode.Tablet => "Tablet IMU",
                    _ => "Right Controller"
                };
                txtGyroStatus.Text = $"Gyro Aiming is Active ({srcLabel} -> DualShock 4).";
                txtGyroStatus.Foreground = new SolidColorBrush(Color.FromRgb(48, 209, 88));
            }
        }

        private void UpdateVibrationUi(int pct)
        {
            var activeStyle = (Style)FindResource("ActiveTouchButton");
            var normalStyle = (Style)FindResource("TouchButton");

            btnVib0.Style = (pct == 0) ? activeStyle : normalStyle;
            btnVib30.Style = (pct == 30) ? activeStyle : normalStyle;
            btnVib60.Style = (pct == 60) ? activeStyle : normalStyle;
            btnVib100.Style = (pct == 100) ? activeStyle : normalStyle;
            txtVibCurrent.Text = $"{pct}%";
        }

        private void ApplyVibrationStrength(int pct, bool pulse = false)
        {
            _currentVibrationStrength = Math.Clamp(pct, 0, 100);
            UpdateVibrationUi(_currentVibrationStrength);
            ManagerFactory.settingsManager.SetProperty("VibrationStrength", _currentVibrationStrength.ToString());
            int targetPct = _currentVibrationStrength;
            Task.Run(() =>
            {
                ControllerManager.SetHardwareVibrationEnabled(targetPct > 0);
                if (pulse && targetPct > 0)
                {
                    byte s = (byte)Math.Clamp(targetPct * 255 / 100, 30, 255);
                    ControllerManager.TestRumble(s, s, 180);
                }
            });
        }

        private void BtnVibPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string s && int.TryParse(s, out int pct))
                ApplyVibrationStrength(pct, pulse: true);
        }

        private void BtnVibMinus_Click(object sender, RoutedEventArgs e) => ApplyVibrationStrength(_currentVibrationStrength - 10, pulse: true);
        private void BtnVibPlus_Click(object sender, RoutedEventArgs e) => ApplyVibrationStrength(_currentVibrationStrength + 10, pulse: true);
        private void BtnTestRumble_Click(object sender, RoutedEventArgs e)
        {
            int targetPct = _currentVibrationStrength;
            Task.Run(() =>
            {
                byte s = targetPct > 0 ? (byte)Math.Clamp(targetPct * 255 / 100, 30, 255) : (byte)160;
                ControllerManager.TestRumble(s, s, 300);
            });
        }

        private void BtnGyroOn_Click(object sender, RoutedEventArgs e)
        {
            ControllerManager.GyroAimingEnabled = true;
            UpdateGyroStatus();
        }

        private void BtnGyroOff_Click(object sender, RoutedEventArgs e)
        {
            ControllerManager.GyroAimingEnabled = false;
            UpdateGyroStatus();
        }

        private void BtnGyroSource_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string tag)
            {
                var mode = tag switch
                {
                    "Left" => ControllerManager.GyroSourceMode.Left,
                    "Tablet" => ControllerManager.GyroSourceMode.Tablet,
                    _ => ControllerManager.GyroSourceMode.Right
                };
                ControllerManager.SetGyroSource(mode);
                UpdateGyroStatus();
            }
        }

        // Joystick LED Lighting
        private void UpdateLedControlsFromService()
        {
            var svc = JoystickLedService.Instance;
            var activeStyle = (Style)FindResource("ActiveTouchButton");
            var normalStyle = (Style)FindResource("TouchButton");

            btnLedOn.Style = svc.Enabled ? activeStyle : normalStyle;
            btnLedOff.Style = !svc.Enabled ? activeStyle : normalStyle;

            btnLedSolid.Style = (svc.Effect == LegionGo.RgbMode.Solid) ? activeStyle : normalStyle;
            btnLedPulse.Style = (svc.Effect == LegionGo.RgbMode.Pulse) ? activeStyle : normalStyle;
            btnLedDynamic.Style = (svc.Effect == LegionGo.RgbMode.Dynamic) ? activeStyle : normalStyle;
            btnLedSpiral.Style = (svc.Effect == LegionGo.RgbMode.Spiral) ? activeStyle : normalStyle;

            txtLedBrightness.Text = $"{svc.Brightness}%";
            txtLedSpeed.Text = $"{svc.Speed}%";
            btnSpeedMinus.IsEnabled = svc.Effect != LegionGo.RgbMode.Solid;
            btnSpeedPlus.IsEnabled = svc.Effect != LegionGo.RgbMode.Solid;

            swatchLeft.Background = new SolidColorBrush(Color.FromRgb(svc.LeftR, svc.LeftG, svc.LeftB));
            swatchRight.Background = new SolidColorBrush(Color.FromRgb(svc.RightR, svc.RightG, svc.RightB));
            chkSyncLR.IsChecked = svc.SyncLeftRight;

            swatchRight.Visibility = svc.SyncLeftRight ? Visibility.Collapsed : Visibility.Visible;
            btnCustomRight.Visibility = svc.SyncLeftRight ? Visibility.Collapsed : Visibility.Visible;

            if (!svc.Enabled)
            {
                txtLedStatus.Text = "Joystick LEDs are Off.";
                txtLedStatus.Foreground = new SolidColorBrush(Color.FromRgb(158, 164, 176));
            }
            else
            {
                string effName = JoystickLedService.GetEffectDisplayName(svc.Effect);
                string desc = svc.Effect == LegionGo.RgbMode.Dynamic || svc.Effect == LegionGo.RgbMode.Spiral
                    ? $"Brightness {svc.Brightness}%  |  Speed {svc.Speed}%"
                    : svc.SyncLeftRight
                        ? $"RGB({svc.LeftR}, {svc.LeftG}, {svc.LeftB})  |  Brightness {svc.Brightness}%"
                        : $"L: RGB({svc.LeftR},{svc.LeftG},{svc.LeftB}) / R: RGB({svc.RightR},{svc.RightG},{svc.RightB})  |  Brightness {svc.Brightness}%";
                txtLedStatus.Text = $"Active: {effName}  |  {desc}";
                txtLedStatus.Foreground = new SolidColorBrush(Color.FromRgb(48, 209, 88));
            }
        }

        private void BtnLedOn_Click(object sender, RoutedEventArgs e) => JoystickLedService.Instance.SetEnabled(true);
        private void BtnLedOff_Click(object sender, RoutedEventArgs e) => JoystickLedService.Instance.SetEnabled(false);

        private void BtnLedEffect_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string s && int.TryParse(s, out int eff))
            {
                JoystickLedService.Instance.SetEffect((LegionGo.RgbMode)eff);
            }
        }

        private void BtnBrightMinus_Click(object sender, RoutedEventArgs e) => JoystickLedService.Instance.SetBrightness(Math.Max(0, JoystickLedService.Instance.Brightness - 10));
        private void BtnBrightPlus_Click(object sender, RoutedEventArgs e) => JoystickLedService.Instance.SetBrightness(Math.Min(100, JoystickLedService.Instance.Brightness + 10));

        private void BtnSpeedMinus_Click(object sender, RoutedEventArgs e) => JoystickLedService.Instance.SetSpeed(Math.Max(0, JoystickLedService.Instance.Speed - 10));
        private void BtnSpeedPlus_Click(object sender, RoutedEventArgs e) => JoystickLedService.Instance.SetSpeed(Math.Min(100, JoystickLedService.Instance.Speed + 10));

        private void BtnColorPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string hex)
            {
                var mediaColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                var drawColor = System.Drawing.Color.FromArgb(mediaColor.R, mediaColor.G, mediaColor.B);
                JoystickLedService.Instance.SetLeftColor(drawColor);
            }
        }

        private void BtnCustomLeft_Click(object sender, RoutedEventArgs e) => PickColor(isRight: false);
        private void BtnCustomRight_Click(object sender, RoutedEventArgs e) => PickColor(isRight: true);

        private void PickColor(bool isRight)
        {
            var svc = JoystickLedService.Instance;
            using var dlg = new System.Windows.Forms.ColorDialog
            {
                AllowFullOpen = true,
                FullOpen = true,
                Color = isRight ? svc.RightColor : svc.LeftColor
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (isRight) svc.SetRightColor(dlg.Color);
                else svc.SetLeftColor(dlg.Color);
            }
        }

        private void ChkSyncLR_Click(object sender, RoutedEventArgs e)
        {
            JoystickLedService.Instance.SetSyncLeftRight(chkSyncLR.IsChecked ?? true);
        }

        // Pointer Mode
        private void PointerOption_Changed(object sender, RoutedEventArgs e)
        {
            var ptr = PointerModeService.Instance;
            ptr.DetachedOnly = chkPointerDetachedOnly.IsChecked ?? true;
            ptr.RightClickOnRB = chkPointerRightClickRB.IsChecked ?? true;
            ptr.InvertX = chkPointerInvertX.IsChecked ?? false;
            ptr.InvertY = chkPointerInvertY.IsChecked ?? false;
            ptr.SaveConfig();
        }

        private void CmbPointerBtn_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbPointerBtn.SelectedIndex >= 0)
            {
                PointerModeService.Instance.ActivationButton = (PointerActivationButton)cmbPointerBtn.SelectedIndex;
                PointerModeService.Instance.SaveConfig();
            }
        }

        private void CmbPointerType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbPointerType.SelectedIndex >= 0)
            {
                PointerModeService.Instance.ActivationType = (PointerActivationType)cmbPointerType.SelectedIndex;
                PointerModeService.Instance.SaveConfig();
            }
        }

        private void BtnPointerSensMinus_Click(object sender, RoutedEventArgs e)
        {
            var ptr = PointerModeService.Instance;
            ptr.Sensitivity = (float)Math.Round(Math.Max(0.2f, ptr.Sensitivity - 0.1f), 1);
            txtPointerSens.Text = $"{ptr.Sensitivity:F1}x";
            ptr.SaveConfig();
        }

        private void BtnPointerSensPlus_Click(object sender, RoutedEventArgs e)
        {
            var ptr = PointerModeService.Instance;
            ptr.Sensitivity = (float)Math.Round(Math.Min(5.0f, ptr.Sensitivity + 0.1f), 1);
            txtPointerSens.Text = $"{ptr.Sensitivity:F1}x";
            ptr.SaveConfig();
        }

        // Hold to Cycle
        private void HoldCycleConfig_Changed(object sender, RoutedEventArgs e)
        {
            SaveHoldCycleConfig();
            UpdateCycleSummary();
        }

        private void BtnHoldMinus_Click(object sender, RoutedEventArgs e)
        {
            var svc = CustomMappingService.Instance;
            svc.HoldCycleDurationMs = Math.Max(200, svc.HoldCycleDurationMs - 50);
            txtHoldTime.Text = $"{svc.HoldCycleDurationMs}ms";
            SaveHoldCycleConfig();
        }

        private void BtnHoldPlus_Click(object sender, RoutedEventArgs e)
        {
            var svc = CustomMappingService.Instance;
            svc.HoldCycleDurationMs = Math.Min(2000, svc.HoldCycleDurationMs + 50);
            txtHoldTime.Text = $"{svc.HoldCycleDurationMs}ms";
            SaveHoldCycleConfig();
        }

        private void SaveHoldCycleConfig()
        {
            ButtonFlags selectedBtn = ButtonFlags.None;
            if (cmbTriggerBtn.SelectedIndex > 0 && cmbTriggerBtn.SelectedIndex - 1 < CustomMappingService.RemappableButtons.Length)
            {
                selectedBtn = CustomMappingService.RemappableButtons[cmbTriggerBtn.SelectedIndex - 1];
            }

            CustomMappingService.Instance.SetHoldCycleConfig(
                chkHoldCycleEnabled.IsChecked ?? false,
                selectedBtn,
                CustomMappingService.Instance.HoldCycleDurationMs,
                chkCycleDesktop.IsChecked ?? true,
                chkCycleX360.IsChecked ?? true,
                chkCycleDS4.IsChecked ?? false,
                chkCycleNative.IsChecked ?? false,
                chkCyclePointer.IsChecked ?? false
            );
        }

        private void UpdateCycleSummary()
        {
            var modes = new List<string>();
            if (chkCycleDesktop.IsChecked ?? false) modes.Add("Desktop");
            if (chkCycleDS4.IsChecked ?? false) modes.Add("DS4");
            if (chkCycleX360.IsChecked ?? false) modes.Add("x360");
            if (chkCycleNative.IsChecked ?? false) modes.Add("Native");
            if (chkCyclePointer.IsChecked ?? false) modes.Add("Pointer");

            if (modes.Count >= 2)
            {
                txtCycleSummary.Text = "Cycle sequence: " + string.Join(" -> ", modes) + " -> " + modes[0];
                txtCycleSummary.Foreground = new SolidColorBrush(Color.FromRgb(0, 144, 255));
            }
            else
            {
                txtCycleSummary.Text = "Select at least 2 modes to cycle between.";
                txtCycleSummary.Foreground = new SolidColorBrush(Color.FromRgb(255, 69, 58));
            }
        }

        private void ComboDs4ShareMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingUi) return;
            int mode = Math.Clamp(comboDs4ShareMode.SelectedIndex, 0, 2);
            Targets.DualShock4Target.Ds4ShareMode = mode;
            ManagerFactory.settingsManager.SetProperty("Ds4ShareMode", mode);
        }

        // Remapping Grid
        private void BuildRemappingRows()
        {
            stackRemapRows.Children.Clear();
            _remappingControls.Clear();

            foreach (var btn in CustomMappingService.RemappableButtons)
            {
                var item = CustomMappingService.Instance.GetMapping(btn);

                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(28, 31, 38)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(45, 50, 62)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 0, 6)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(165) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(115) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var lblBtn = new TextBlock
                {
                    Text = CustomMappingService.GetButtonDisplayName(btn),
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(226, 230, 238)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(lblBtn, 0);

                var comboType = new ComboBox { Margin = new Thickness(3, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                comboType.Items.Add(new ComboBoxItem { Content = "None / Default" });
                comboType.Items.Add(new ComboBoxItem { Content = "Controller Button" });
                comboType.Items.Add(new ComboBoxItem { Content = "Keyboard Key" });
                comboType.Items.Add(new ComboBoxItem { Content = "Action" });
                comboType.SelectedIndex = (int)item.TargetType;
                Grid.SetColumn(comboType, 1);

                var comboTarget = new ComboBox { Margin = new Thickness(3, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(comboTarget, 2);

                var comboTrigger = new ComboBox { Margin = new Thickness(3, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "When remapped output triggers: Default (v1.6.0), On Down (Press), or On Up (Release)" };
                comboTrigger.Items.Add(new ComboBoxItem { Content = "Default" });
                comboTrigger.Items.Add(new ComboBoxItem { Content = "On Down" });
                comboTrigger.Items.Add(new ComboBoxItem { Content = "On Up" });
                comboTrigger.SelectedIndex = (int)item.TriggerMode;
                Grid.SetColumn(comboTrigger, 3);

                var stackTurbo = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var chkTurbo = new CheckBox { Content = "Repeat", IsChecked = item.HasTurbo, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
                var btnMinus = new Button { Content = "-", Width = 26, Height = 26, Style = (Style)FindResource("StepButton") };
                var borderRate = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(37, 41, 50)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(56, 62, 76)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    MinWidth = 42,
                    Height = 26,
                    Margin = new Thickness(2, 0, 2, 0)
                };
                var txtRate = new TextBlock
                {
                    Text = $"{item.TurboDelayMs}ms",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                borderRate.Child = txtRate;
                var btnPlus = new Button { Content = "+", Width = 26, Height = 26, Style = (Style)FindResource("StepButton") };
                var chkHaptic = new CheckBox
                {
                    Content = "Haptic",
                    IsChecked = item.HapticEnabled,
                    Foreground = Brushes.LightGray,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0),
                    ToolTip = "Vibrate dynamically matching the Trigger mode (On Down / On Up)"
                };

                stackTurbo.Children.Add(chkTurbo);
                stackTurbo.Children.Add(btnMinus);
                stackTurbo.Children.Add(borderRate);
                stackTurbo.Children.Add(btnPlus);
                stackTurbo.Children.Add(chkHaptic);
                Grid.SetColumn(stackTurbo, 4);

                grid.Children.Add(lblBtn);
                grid.Children.Add(comboType);
                grid.Children.Add(comboTarget);
                grid.Children.Add(comboTrigger);
                grid.Children.Add(stackTurbo);
                border.Child = grid;

                stackRemapRows.Children.Add(border);
                _remappingControls[btn] = (comboType, comboTarget, comboTrigger, chkTurbo, txtRate, chkHaptic);

                PopulateTargetDropdown(comboTarget, item.TargetType, item.TargetButton, item.TargetKey, item.TargetAction);

                comboType.SelectionChanged += (s, e) =>
                {
                    if (_updatingUi) return;
                    var newType = (MappingType)comboType.SelectedIndex;
                    PopulateTargetDropdown(comboTarget, newType, ButtonFlags.None, VirtualKeyCode.NONAME, CustomActionType.None);
                    SaveRowMapping(btn);
                };

                comboTarget.SelectionChanged += (s, e) =>
                {
                    if (_updatingUi) return;
                    SaveRowMapping(btn);
                };

                comboTrigger.SelectionChanged += (s, e) =>
                {
                    if (_updatingUi) return;
                    SaveRowMapping(btn);
                };

                chkTurbo.Click += (s, e) => SaveRowMapping(btn);
                chkHaptic.Click += (s, e) => { if (!_updatingUi) SaveRowMapping(btn); };

                btnMinus.Click += (s, e) =>
                {
                    var cur = CustomMappingService.Instance.GetMapping(btn);
                    int newRate = Math.Max(10, cur.TurboDelayMs - 10);
                    txtRate.Text = $"{newRate}ms";
                    SaveRowMapping(btn, newRate);
                };

                btnPlus.Click += (s, e) =>
                {
                    var cur = CustomMappingService.Instance.GetMapping(btn);
                    int newRate = Math.Min(1000, cur.TurboDelayMs + 10);
                    txtRate.Text = $"{newRate}ms";
                    SaveRowMapping(btn, newRate);
                };
            }
        }

        private void PopulateTargetDropdown(ComboBox comboTarget, MappingType type, ButtonFlags selectedBtn, VirtualKeyCode selectedKey, CustomActionType selectedAction)
        {
            comboTarget.Items.Clear();

            switch (type)
            {
                case MappingType.None:
                    comboTarget.Items.Add(new ComboBoxItem { Content = "(Default)" });
                    comboTarget.SelectedIndex = 0;
                    comboTarget.IsEnabled = false;
                    break;

                case MappingType.Controller:
                    comboTarget.IsEnabled = true;
                    int selIndex = 0;
                    for (int i = 0; i < ControllerTargets.Count; i++)
                    {
                        comboTarget.Items.Add(new ComboBoxItem { Content = ControllerTargets[i].Name });
                        if (ControllerTargets[i].Flag == selectedBtn) selIndex = i;
                    }
                    comboTarget.SelectedIndex = selIndex;
                    break;

                case MappingType.Keyboard:
                    comboTarget.IsEnabled = true;
                    int keyIdx = 0;
                    for (int i = 0; i < KeyboardTargets.Count; i++)
                    {
                        comboTarget.Items.Add(new ComboBoxItem { Content = KeyboardTargets[i].Name });
                        if (KeyboardTargets[i].Key == selectedKey) keyIdx = i;
                    }
                    comboTarget.SelectedIndex = keyIdx;
                    break;

                case MappingType.Action:
                    comboTarget.IsEnabled = true;
                    var actions = CustomMappingService.AvailableActions;
                    int actIdx = 0;
                    for (int i = 0; i < actions.Count; i++)
                    {
                        comboTarget.Items.Add(new ComboBoxItem { Content = actions[i].Name });
                        if (actions[i].Action == selectedAction) actIdx = i;
                    }
                    comboTarget.SelectedIndex = actIdx;
                    break;
            }
        }

        private void SaveRowMapping(ButtonFlags btn, int? overrideDelay = null)
        {
            if (!_remappingControls.TryGetValue(btn, out var row)) return;
            var (typeCombo, targetCombo, triggerCombo, chkRepeat, txtRate, chkHaptic) = row;

            var type = (MappingType)typeCombo.SelectedIndex;
            ButtonFlags targetBtn = ButtonFlags.None;
            VirtualKeyCode targetKey = VirtualKeyCode.NONAME;
            CustomActionType targetAction = CustomActionType.None;

            if (type == MappingType.Controller && targetCombo.SelectedIndex >= 0 && targetCombo.SelectedIndex < ControllerTargets.Count)
            {
                targetBtn = ControllerTargets[targetCombo.SelectedIndex].Flag;
            }
            else if (type == MappingType.Keyboard && targetCombo.SelectedIndex >= 0 && targetCombo.SelectedIndex < KeyboardTargets.Count)
            {
                targetKey = KeyboardTargets[targetCombo.SelectedIndex].Key;
            }
            else if (type == MappingType.Action && targetCombo.SelectedIndex >= 0 && targetCombo.SelectedIndex < CustomMappingService.AvailableActions.Count)
            {
                targetAction = CustomMappingService.AvailableActions[targetCombo.SelectedIndex].Action;
            }

            int delay = overrideDelay ?? int.Parse(txtRate.Text.Replace("ms", ""));
            var triggerMode = triggerCombo.SelectedIndex >= 0 ? (RemapTriggerMode)triggerCombo.SelectedIndex : RemapTriggerMode.Default;
            CustomMappingService.Instance.SetMapping(btn, type, targetBtn, targetKey, targetAction, chkRepeat.IsChecked ?? false, delay, triggerMode, chkHaptic.IsChecked ?? false);
        }

        private void BtnResetRemap_Click(object sender, RoutedEventArgs e)
        {
            var res = System.Windows.MessageBox.Show(
                "Reset all custom back and Legion button remappings to default?",
                "Reset Remappings",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                CustomMappingService.Instance.ResetAll();
                _updatingUi = true;
                try { BuildRemappingRows(); }
                finally { _updatingUi = false; }
            }
        }
        #endregion

        #region Deadzones & Firmware Tuning View
        private int _lStickDz = 4;
        private int _rStickDz = 4;
        private int _stickCurve = 1;
        private int _trigStartDz = 5;
        private int _trigMargin = 5;
        private bool _trackpadEnabled = true;
        private bool _trackpadVibEnabled = true;
        private int _legionLrSwapMode = 1;
        private int _mouseDpi = 800;
        private int _autoSleepMins = 60;

        private void RefreshTuningControlsAsync()
        {
            Task.Run(() =>
            {
                try
                {
                    Devices.Lenovo.SapientiaUsb.Init();
                    int lDz = Math.Clamp(Devices.Lenovo.SapientiaUsb.GetStickCustomDeadzone(3), 0, 50);
                    int rDz = Math.Clamp(Devices.Lenovo.SapientiaUsb.GetStickCustomDeadzone(4), 0, 50);
                    int curve = Devices.Lenovo.SapientiaUsb.GetStickPresetCurve(3);
                    if (curve < 1 || curve > 4) curve = 1;

                    var trig = Devices.Lenovo.SapientiaUsb.GetTriggerDeadzoneAndMargin(4);
                    int tDz = Math.Clamp(trig.Deadzone, 0, 80);
                    int tMar = Math.Clamp(trig.Margin, 0, 80);

                    int tpState = Devices.Lenovo.SapientiaUsb.GetTouchPadStatus();
                    int tpVib = Devices.Lenovo.SapientiaUsb.GetTouchpadVibrateState(4);
                    int lrSwap = Devices.Lenovo.SapientiaUsb.GetLegionLRAndMenuViewMode(3);
                    int dpi = Devices.Lenovo.SapientiaUsb.GetMouseCurrentDPI(4);
                    int sleep = Devices.Lenovo.SapientiaUsb.GetAutoSleepTime(3);

                    _lStickDz = lDz;
                    _rStickDz = rDz;
                    _stickCurve = curve;
                    _trigStartDz = tDz;
                    _trigMargin = tMar;
                    _trackpadEnabled = tpState != 0 || !(IDevice.GetCurrent() is LegionGo lg && lg.ToString().Length < 0);
                    _trackpadVibEnabled = tpVib != 0;
                    _legionLrSwapMode = lrSwap == 2 ? 2 : 1;
                    if (dpi >= 400 && dpi <= 3200) _mouseDpi = dpi;
                    if (sleep >= 0 && sleep <= 120) _autoSleepMins = sleep;

                    if (_isExiting) return;
                    Dispatcher.BeginInvoke(UpdateTuningUi);
                }
                catch { }
            });
        }

        private void UpdateTuningUi()
        {
            var activeStyle = (Style)FindResource("ActiveTouchButton");
            var normalStyle = (Style)FindResource("TouchButton");

            // Left Stick Deadzone
            btnLStickDz0.Style = (_lStickDz == 0) ? activeStyle : normalStyle;
            btnLStickDz2.Style = (_lStickDz == 2) ? activeStyle : normalStyle;
            btnLStickDz4.Style = (_lStickDz == 4) ? activeStyle : normalStyle;
            btnLStickDz8.Style = (_lStickDz == 8) ? activeStyle : normalStyle;
            btnLStickDz12.Style = (_lStickDz == 12) ? activeStyle : normalStyle;
            txtLStickDz.Text = $"{_lStickDz}%";

            // Right Stick Deadzone
            btnRStickDz0.Style = (_rStickDz == 0) ? activeStyle : normalStyle;
            btnRStickDz2.Style = (_rStickDz == 2) ? activeStyle : normalStyle;
            btnRStickDz4.Style = (_rStickDz == 4) ? activeStyle : normalStyle;
            btnRStickDz8.Style = (_rStickDz == 8) ? activeStyle : normalStyle;
            btnRStickDz12.Style = (_rStickDz == 12) ? activeStyle : normalStyle;
            txtRStickDz.Text = $"{_rStickDz}%";

            // Stick Curve
            btnCurve1.Style = (_stickCurve == 1) ? activeStyle : normalStyle;
            btnCurve2.Style = (_stickCurve == 2) ? activeStyle : normalStyle;
            btnCurve3.Style = (_stickCurve == 3) ? activeStyle : normalStyle;
            btnCurve4.Style = (_stickCurve == 4) ? activeStyle : normalStyle;

            // Triggers
            int fullPullPct = Math.Clamp(100 - _trigMargin, 20, 100);
            txtTrigStartDz.Text = $"{_trigStartDz}%";
            txtTrigFullPull.Text = $"{fullPullPct}%";
            btnTrigHair.Style = (_trigStartDz == 0 && _trigMargin == 65) ? activeStyle : normalStyle;
            btnTrigFast.Style = (_trigStartDz == 2 && _trigMargin == 35) ? activeStyle : normalStyle;
            btnTrigDefault.Style = (_trigStartDz == 5 && _trigMargin == 5) ? activeStyle : normalStyle;
            btnTrigFull.Style = (_trigStartDz == 0 && _trigMargin == 0) ? activeStyle : normalStyle;

            // Trackpad & Button Swap
            btnTrackpadToggle.Content = _trackpadEnabled ? "Trackpad: Enabled" : "Trackpad: Disabled";
            btnTrackpadToggle.Style = _trackpadEnabled ? activeStyle : normalStyle;
            chkKeepTrackpadOnExit.IsChecked = ManagerFactory.settingsManager.GetBoolean("KeepTrackpadOnExit");

            btnTrackpadVibToggle.Content = _trackpadVibEnabled ? "Trackpad Haptics: On" : "Trackpad Haptics: Off";
            btnTrackpadVibToggle.Style = _trackpadVibEnabled ? activeStyle : normalStyle;

            bool swapped = _legionLrSwapMode == 2;
            btnSwapLegionButtons.Content = swapped ? "Legion L/R Swap: Swapped" : "Legion L/R Swap: Normal";
            btnSwapLegionButtons.Style = swapped ? activeStyle : normalStyle;

            // FPS Mouse DPI
            btnDpi500.Style = (_mouseDpi == 500) ? activeStyle : normalStyle;
            btnDpi800.Style = (_mouseDpi == 800) ? activeStyle : normalStyle;
            btnDpi1200.Style = (_mouseDpi == 1200) ? activeStyle : normalStyle;
            btnDpi1600.Style = (_mouseDpi == 1600) ? activeStyle : normalStyle;
            btnDpi1800.Style = (_mouseDpi == 1800) ? activeStyle : normalStyle;

            // Auto-Sleep
            btnSleep10.Style = (_autoSleepMins >= 8 && _autoSleepMins <= 12) ? activeStyle : normalStyle;
            btnSleep15.Style = (_autoSleepMins >= 13 && _autoSleepMins <= 20) ? activeStyle : normalStyle;
            btnSleep30.Style = (_autoSleepMins >= 25 && _autoSleepMins <= 35) ? activeStyle : normalStyle;
            btnSleep60.Style = (_autoSleepMins >= 50) ? activeStyle : normalStyle;
            btnSleep0.Style = (_autoSleepMins == 0) ? activeStyle : normalStyle;
        }

        private void ApplyLeftStickDeadzone(int dz)
        {
            _lStickDz = Math.Clamp(dz, 0, 40);
            UpdateTuningUi();
            int target = _lStickDz;
            Task.Run(() =>
            {
                try
                {
                    Devices.Lenovo.SapientiaUsb.Init();
                    Devices.Lenovo.SapientiaUsb.SetStickCustomDeadzone(3, target);
                }
                catch { }
            });
        }

        private void ApplyRightStickDeadzone(int dz)
        {
            _rStickDz = Math.Clamp(dz, 0, 40);
            UpdateTuningUi();
            int target = _rStickDz;
            Task.Run(() =>
            {
                try
                {
                    Devices.Lenovo.SapientiaUsb.Init();
                    Devices.Lenovo.SapientiaUsb.SetStickCustomDeadzone(4, target);
                }
                catch { }
            });
        }

        private void BtnLStickDzPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string s && int.TryParse(s, out int dz))
                ApplyLeftStickDeadzone(dz);
        }
        private void BtnLStickDzMinus_Click(object sender, RoutedEventArgs e) => ApplyLeftStickDeadzone(_lStickDz - 1);
        private void BtnLStickDzPlus_Click(object sender, RoutedEventArgs e) => ApplyLeftStickDeadzone(_lStickDz + 1);

        private void BtnRStickDzPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string s && int.TryParse(s, out int dz))
                ApplyRightStickDeadzone(dz);
        }
        private void BtnRStickDzMinus_Click(object sender, RoutedEventArgs e) => ApplyRightStickDeadzone(_rStickDz - 1);
        private void BtnRStickDzPlus_Click(object sender, RoutedEventArgs e) => ApplyRightStickDeadzone(_rStickDz + 1);

        private void BtnStickCurve_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string s && int.TryParse(s, out int preset))
            {
                _stickCurve = Math.Clamp(preset, 1, 4);
                UpdateTuningUi();
                int target = _stickCurve;
                Task.Run(() =>
                {
                    try
                    {
                        Devices.Lenovo.SapientiaUsb.Init();
                        Devices.Lenovo.SapientiaUsb.SetStickPresetCurve(3, target);
                        Devices.Lenovo.SapientiaUsb.SetStickPresetCurve(4, target);
                    }
                    catch { }
                });
            }
        }

        private void ApplyTriggerDeadzones(int startDz, int margin)
        {
            _trigStartDz = Math.Clamp(startDz, 0, 60);
            _trigMargin = Math.Clamp(margin, 0, 75);
            if (_trigStartDz + _trigMargin > 80)
                _trigMargin = 80 - _trigStartDz;
            UpdateTuningUi();

            var cfg = new Devices.Lenovo.SapientiaUsb.LegionTriggerDeadzone
            {
                Deadzone = _trigStartDz,
                Margin = _trigMargin
            };
            Task.Run(() =>
            {
                try
                {
                    Devices.Lenovo.SapientiaUsb.Init();
                    Devices.Lenovo.SapientiaUsb.SetTriggerDeadzoneAndMargin(3, cfg);
                    Devices.Lenovo.SapientiaUsb.SetTriggerDeadzoneAndMargin(4, cfg);
                }
                catch { }
            });
        }

        private void BtnTriggerPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string s)
            {
                var parts = s.Split(',');
                if (parts.Length == 2 && int.TryParse(parts[0], out int dz) && int.TryParse(parts[1], out int mar))
                    ApplyTriggerDeadzones(dz, mar);
            }
        }

        private void BtnTrigDzMinus_Click(object sender, RoutedEventArgs e) => ApplyTriggerDeadzones(_trigStartDz - 1, _trigMargin);
        private void BtnTrigDzPlus_Click(object sender, RoutedEventArgs e) => ApplyTriggerDeadzones(_trigStartDz + 1, _trigMargin);
        private void BtnTrigMaxMinus_Click(object sender, RoutedEventArgs e) => ApplyTriggerDeadzones(_trigStartDz, _trigMargin + 5);
        private void BtnTrigMaxPlus_Click(object sender, RoutedEventArgs e) => ApplyTriggerDeadzones(_trigStartDz, _trigMargin - 5);

        private void BtnTrackpadToggle_Click(object sender, RoutedEventArgs e)
        {
            _trackpadEnabled = !_trackpadEnabled;
            UpdateTuningUi();
            bool en = _trackpadEnabled;
            ManagerFactory.settingsManager.SetProperty("TrackpadEnabled", en);
            Task.Run(() =>
            {
                try
                {
                    if (IDevice.GetCurrent() is LegionGo lego)
                        lego.SetPassthrough(!en);
                    Devices.Lenovo.SapientiaUsb.Init();
                    Devices.Lenovo.SapientiaUsb.SetTouchPadStatus(en ? 1 : 0);
                    Devices.Lenovo.SapientiaUsb.SetTrackpadStatus(4, en ? 1 : 0);
                }
                catch { }
            });
        }

        private void BtnTrackpadVibToggle_Click(object sender, RoutedEventArgs e)
        {
            _trackpadVibEnabled = !_trackpadVibEnabled;
            UpdateTuningUi();
            int state = _trackpadVibEnabled ? 1 : 0;
            Task.Run(() =>
            {
                try
                {
                    Devices.Lenovo.SapientiaUsb.Init();
                    Devices.Lenovo.SapientiaUsb.SetTouchpadVibrateState(4, state);
                }
                catch { }
            });
        }

        private void ChkKeepTrackpadOnExit_Click(object sender, RoutedEventArgs e)
        {
            bool enabled = (sender as CheckBox)?.IsChecked ?? false;
            ManagerFactory.settingsManager.SetProperty("KeepTrackpadOnExit", enabled);
            chkKeepTrackpadOnExit.IsChecked = enabled;
            chkKeepTrackpadOnExitSettings.IsChecked = enabled;
        }

        private void BtnSwapLegionButtons_Click(object sender, RoutedEventArgs e)
        {
            _legionLrSwapMode = (_legionLrSwapMode == 2) ? 1 : 2;
            UpdateTuningUi();
            int mode = _legionLrSwapMode;
            Task.Run(() =>
            {
                try
                {
                    Devices.Lenovo.SapientiaUsb.Init();
                    Devices.Lenovo.SapientiaUsb.SetLegionLRAndMenuViewMode(3, mode);
                    Devices.Lenovo.SapientiaUsb.SetLegionLRAndMenuViewMode(4, mode);
                }
                catch { }
            });
        }

        private void BtnMouseDpi_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string s && int.TryParse(s, out int dpi))
            {
                _mouseDpi = dpi;
                UpdateTuningUi();
                Task.Run(() =>
                {
                    try
                    {
                        Devices.Lenovo.SapientiaUsb.Init();
                        Devices.Lenovo.SapientiaUsb.SetMouseDPI(4, dpi);
                    }
                    catch { }
                });
            }
        }

        private void BtnAutoSleep_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string s && int.TryParse(s, out int mins))
            {
                _autoSleepMins = mins;
                UpdateTuningUi();
                Task.Run(() =>
                {
                    try
                    {
                        Devices.Lenovo.SapientiaUsb.Init();
                        Devices.Lenovo.SapientiaUsb.SetAutoSleepTime(3, mins);
                        Devices.Lenovo.SapientiaUsb.SetAutoSleepTime(4, mins);
                    }
                    catch { }
                });
            }
        }

        private async void BtnCalibrateSticks_Click(object sender, RoutedEventArgs e)
        {
            btnCalSticks.IsEnabled = false;
            txtCalibrationStatus.Text = "Calibrating Sticks (5s): Rotate both thumbsticks in full circles 3 times, then release...";
            txtCalibrationStatus.Foreground = BrushGreen;
            await Task.Run(() =>
            {
                try
                {
                    Devices.Lenovo.SapientiaUsb.Init();
                    Devices.Lenovo.SapientiaUsb.StartStickCalibration(3);
                    Devices.Lenovo.SapientiaUsb.StartStickCalibration(4);
                    Thread.Sleep(5000);
                    Devices.Lenovo.SapientiaUsb.StopStickCalibration(3);
                    Devices.Lenovo.SapientiaUsb.StopStickCalibration(4);
                }
                catch { }
            });
            txtCalibrationStatus.Text = "Thumbstick calibration complete!";
            btnCalSticks.IsEnabled = true;
        }

        private async void BtnCalibrateTriggers_Click(object sender, RoutedEventArgs e)
        {
            btnCalTriggers.IsEnabled = false;
            txtCalibrationStatus.Text = "Calibrating Triggers (5s): Press LT and RT all the way down 3 times, then release...";
            txtCalibrationStatus.Foreground = BrushGreen;
            await Task.Run(() =>
            {
                try
                {
                    Devices.Lenovo.SapientiaUsb.Init();
                    Devices.Lenovo.SapientiaUsb.StartTriggerCalibration(3);
                    Devices.Lenovo.SapientiaUsb.StartTriggerCalibration(4);
                    Thread.Sleep(5000);
                    Devices.Lenovo.SapientiaUsb.StopTriggerCalibration(3);
                    Devices.Lenovo.SapientiaUsb.StopTriggerCalibration(4);
                }
                catch { }
            });
            txtCalibrationStatus.Text = "Trigger calibration complete!";
            btnCalTriggers.IsEnabled = true;
        }

        private async void BtnCalibrateGyro_Click(object sender, RoutedEventArgs e)
        {
            btnCalGyro.IsEnabled = false;
            txtCalibrationStatus.Text = "Calibrating Gyro (3s): Keep the device completely still on a flat surface...";
            txtCalibrationStatus.Foreground = BrushGreen;
            await Task.Run(() =>
            {
                try
                {
                    Devices.Lenovo.SapientiaUsb.Init();
                    Devices.Lenovo.SapientiaUsb.StartGyroCalibration(3);
                    Devices.Lenovo.SapientiaUsb.StartGyroCalibration(4);
                    Thread.Sleep(3000);
                    Devices.Lenovo.SapientiaUsb.StopGyroCalibration(3);
                    Devices.Lenovo.SapientiaUsb.StopGyroCalibration(4);
                }
                catch { }
            });
            txtCalibrationStatus.Text = "Gyroscope calibration complete!";
            btnCalGyro.IsEnabled = true;
        }
        #endregion

        #region Settings View (Hardware, Utilities, Updates)
        private void InitializeSettingsView()
        {
            chkBatteryLimit.IsChecked = ManagerFactory.settingsManager.GetBoolean("BatteryChargeLimit");
            chkCloseMinimises.IsChecked = ManagerFactory.settingsManager.GetBoolean("CloseMinimises");
            chkKeepTrackpadOnExitSettings.IsChecked = ManagerFactory.settingsManager.GetBoolean("KeepTrackpadOnExit");
            chkAutoRotation.IsChecked = ManagerFactory.settingsManager.GetBoolean("AutoRotationEnabled");
            chkRunAtStartup.IsChecked = ManagerFactory.settingsManager.GetBoolean("RunAtStartup");
            chkStartMinimized.IsChecked = ManagerFactory.settingsManager.GetBoolean("StartMinimized");
            chkDesktopOnStart.IsChecked = ManagerFactory.settingsManager.GetBoolean("DesktopLayoutOnStart");
        }

        private void ChkPowerLight_Click(object sender, RoutedEventArgs e)
        {
            bool enabled = chkPowerLight.IsChecked ?? true;
            Task.Run(() =>
            {
                try
                {
                    if (IDevice.GetCurrent() is LegionGo lego)
                        lego.SetPowerLightStatus(enabled);
                }
                catch { }
            });
        }

        private void ChkAutoRotation_Click(object sender, RoutedEventArgs e)
        {
            bool enabled = chkAutoRotation.IsChecked ?? false;
            AutoRotationService.Instance.SetEnabled(enabled);
        }

        private void BtnOpenRealtek_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = @"shell:AppsFolder\RealtekSemiconductorCorp.RealtekAudioControl_dt26b99r8h8gj!App",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LogManager.LogError("Failed to open Realtek Audio Control: {0}", ex.Message);
            }
        }

        private void ChkBatteryLimit_Click(object sender, RoutedEventArgs e)
        {
            bool val = chkBatteryLimit.IsChecked ?? false;
            ManagerFactory.settingsManager.SetProperty("BatteryChargeLimit", val);
            txtBatteryLimitStatus.Text = val ? "Hardware Charge Limit: Applying 80% cap..." : "Hardware Charge Limit: Disabling (100% full charge)...";
            txtBatteryLimitStatus.Foreground = BrushGray;
            Task.Run(() =>
            {
                try
                {
                    if (IDevice.GetCurrent() is LegionGo lego)
                    {
                        lego.SetBatteryChargeLimit(val);
                    }
                }
                catch { }
                RefreshBatteryLimitStatusAsync();
            });
        }

        private void RefreshBatteryLimitStatusAsync()
        {
            Task.Run(() =>
            {
                try
                {
                    if (IDevice.GetCurrent() is LegionGo lego)
                    {
                        bool active = lego.GetBatteryChargeLimit();
                        bool pwrLight = lego.GetPowerLightStatus();
                        Dispatcher.BeginInvoke(() =>
                        {
                            if (_isExiting) return;
                            chkBatteryLimit.IsChecked = active;
                            if (chkPowerLight != null) chkPowerLight.IsChecked = pwrLight;
                            txtBatteryLimitStatus.Text = active
                                ? "Hardware Status: Active (Charging capped at 80%)"
                                : "Hardware Status: Inactive (Charges to 100%)";
                            txtBatteryLimitStatus.Foreground = active ? BrushGreen : BrushGray;
                        });
                    }
                }
                catch { }
            });
        }

        private void ChkCloseMinimises_Click(object sender, RoutedEventArgs e)
        {
            ManagerFactory.settingsManager.SetProperty("CloseMinimises", chkCloseMinimises.IsChecked ?? false);
        }

        private void ChkRunAtStartup_Click(object sender, RoutedEventArgs e)
        {
            ManagerFactory.settingsManager.SetProperty("RunAtStartup", chkRunAtStartup.IsChecked ?? false);
        }

        private void ChkStartMinimized_Click(object sender, RoutedEventArgs e)
        {
            ManagerFactory.settingsManager.SetProperty("StartMinimized", chkStartMinimized.IsChecked ?? false);
        }

        private void ChkDesktopOnStart_Click(object sender, RoutedEventArgs e)
        {
            ManagerFactory.settingsManager.SetProperty("DesktopLayoutOnStart", chkDesktopOnStart.IsChecked ?? false);
        }

        private void BtnOpenLogs_Click(object sender, RoutedEventArgs e)
        {
            string logPath = Environment.GetEnvironmentVariable("LOG_PATH") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HandheldCompanion", "logs");
            if (Directory.Exists(logPath))
            {
                Process.Start("explorer.exe", logPath);
            }
        }

        private void BtnRestartApp_Click(object sender, RoutedEventArgs e)
        {
            ExitApplication(restart: true);
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            btnCheckUpdates.IsEnabled = false;
            txtUpdateStatus.Text = "Checking GitHub Releases...";

            try
            {
                _foundRelease = await UpdateService.CheckForUpdateAsync();
                if (_foundRelease != null && _foundRelease.IsNewer)
                {
                    txtUpdateStatus.Text = $"Update available: {_foundRelease.Name} ({_foundRelease.TagName})";
                    txtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(48, 209, 88));
                    btnInstallUpdate.Visibility = Visibility.Visible;
                    btnInstallUpdate.IsEnabled = true;
                }
                else
                {
                    txtUpdateStatus.Text = "You are on the latest version.";
                    txtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(158, 164, 176));
                    btnInstallUpdate.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                txtUpdateStatus.Text = "Check failed: " + ex.Message;
                txtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 69, 58));
            }
            finally
            {
                btnCheckUpdates.IsEnabled = true;
            }
        }

        private async void BtnInstallUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_foundRelease == null) return;

            btnCheckUpdates.IsEnabled = false;
            btnInstallUpdate.IsEnabled = false;
            progressUpdate.Visibility = Visibility.Visible;
            progressUpdate.Value = 0;
            txtUpdateStatus.Text = "Downloading installer...";

            var progress = new Progress<(long downloaded, long total, int percent)>(p =>
            {
                progressUpdate.Value = p.percent;
                txtUpdateStatus.Text = $"Downloading update: {p.percent}% ({p.downloaded / (1024 * 1024)}MB / {p.total / (1024 * 1024)}MB)";
            });

            try
            {
                string installer = await UpdateService.DownloadUpdateAsync(_foundRelease, progress);
                txtUpdateStatus.Text = "Download complete. Launching installer...";
                await Task.Delay(500);
                UpdateService.LaunchInstallerAndExit(installer);
            }
            catch (Exception ex)
            {
                txtUpdateStatus.Text = "Download failed: " + ex.Message;
                txtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 69, 58));
                btnCheckUpdates.IsEnabled = true;
                btnInstallUpdate.IsEnabled = true;
            }
        }
        #endregion

        #region Status Bar & Diagnostics
        private void StartDiagnosticsTimer()
        {
            _diagnosticsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _diagnosticsTimer.Tick += (s, e) => UpdateDiagnostics();
            _diagnosticsTimer.Start();
            UpdateDiagnostics();
        }

        private void UpdateDiagnostics()
        {
            try
            {
                bool viiperOk = VirtualManager.IsInitialized;
                bool vigemSys = File.Exists(Path.Combine(Environment.SystemDirectory, "drivers", "ViGEmBus.sys"));
                bool hidHideOk = File.Exists(Path.Combine(Environment.SystemDirectory, "drivers", "HidHide.sys"));

                if (viiperOk)
                {
                    txtVigemStatus.Text = "VIIPER: OK";
                    txtVigemStatus.Foreground = BrushGreen;
                }
                else if (vigemSys)
                {
                    txtVigemStatus.Text = "ViGEm: OK";
                    txtVigemStatus.Foreground = BrushGreen;
                }
                else
                {
                    txtVigemStatus.Text = "Virtual Pad: Off";
                    txtVigemStatus.Foreground = BrushRed;
                }

                txtHidHideStatus.Text = hidHideOk ? "HidHide: OK" : "HidHide: Missing";
                txtHidHideStatus.Foreground = hidHideOk ? BrushGreen : BrushRed;
            }
            catch { }
        }

        private void BtnElevate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Process.GetCurrentProcess().MainModule?.FileName ?? "LegionGoCompanion.exe",
                    UseShellExecute = true,
                    Verb = "runas"
                });
                ExitApplication(restart: false);
            }
            catch { }
        }
        #endregion

        #region Tray Icon & IPC Lifecycle
        private void InitializeTrayIcon()
        {
            _trayMenu = new System.Windows.Forms.ContextMenuStrip();

            var itemOpen = new System.Windows.Forms.ToolStripMenuItem("Open Legion Go Companion");
            itemOpen.Font = new System.Drawing.Font(itemOpen.Font, System.Drawing.FontStyle.Bold);
            itemOpen.Click += (s, e) => RestoreFromTray();

            var itemRestart = new System.Windows.Forms.ToolStripMenuItem("Restart");
            itemRestart.Click += (s, e) => ExitApplication(restart: true);

            var itemExit = new System.Windows.Forms.ToolStripMenuItem("Exit");
            itemExit.Click += (s, e) => ExitApplication(restart: false);

            _trayMenu.Items.Add(itemOpen);
            _trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            _trayMenu.Items.Add(itemRestart);
            _trayMenu.Items.Add(itemExit);

            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Text = "Legion Go Companion",
                ContextMenuStrip = _trayMenu
            };

            System.Drawing.Icon? appIcon = null;
            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "icon.ico");
                if (File.Exists(icoPath))
                {
                    appIcon = new System.Drawing.Icon(icoPath);
                }
                if (appIcon == null)
                {
                    string? exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                    {
                        appIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                    }
                }
            }
            catch { }

            appIcon ??= System.Drawing.SystemIcons.Application;
            _trayIcon.Icon = appIcon;
            _trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    RestoreFromTray();
                }
            };
            _trayIcon.DoubleClick += (s, e) => RestoreFromTray();
            _trayIcon.Visible = true;
        }

        private void StartEventListener()
        {
            try
            {
                _showWindowEvent = Program.EnsureShowWindowEventCreated();
                _exitAppEvent = Program.EnsureExitEventCreated();

                _eventListenerThread = new Thread(() =>
                {
                    var handles = new List<WaitHandle>();
                    if (_showWindowEvent != null) handles.Add(_showWindowEvent);
                    if (_exitAppEvent != null) handles.Add(_exitAppEvent);
                    var waitArray = handles.ToArray();

                    while (_isListening && waitArray.Length > 0)
                    {
                        try
                        {
                            int sig = WaitHandle.WaitAny(waitArray, 1000);
                            if (!_isListening) break;

                            if (sig >= 0 && sig < waitArray.Length)
                            {
                                var signaled = waitArray[sig];
                                if (signaled == _exitAppEvent)
                                {
                                    Dispatcher.Invoke(() => ExitApplication(restart: false));
                                    break;
                                }
                                else if (signaled == _showWindowEvent)
                                {
                                    Dispatcher.Invoke(RestoreFromTray);
                                }
                            }
                        }
                        catch { }
                    }
                })
                {
                    IsBackground = true,
                    Priority = ThreadPriority.BelowNormal
                };
                _eventListenerThread.Start();
            }
            catch { }
        }

        public void RestoreFromTray()
        {
            if (_isExiting) return;

            if (this.WindowState == WindowState.Minimized)
            {
                this.WindowState = _lastNonMinimizedState;
            }

            if (!this.IsVisible)
            {
                this.Show();
            }

            SyncWindowChromeState();

            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;
            if (hwnd != IntPtr.Zero)
            {
                if (_lastNonMinimizedState != WindowState.Maximized)
                {
                    ShowWindow(hwnd, SW_RESTORE);
                }

                // Pulse HWND_TOPMOST briefly to rise above fullscreen/borderless games, then immediately restore HWND_NOTOPMOST
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

                try
                {
                    IntPtr fg = GetForegroundWindow();
                    uint fgThread = fg != IntPtr.Zero ? GetWindowThreadProcessId(fg, out _) : 0;
                    uint curThread = GetCurrentThreadId();
                    if (fgThread != 0 && fgThread != curThread)
                    {
                        AttachThreadInput(curThread, fgThread, true);
                        BringWindowToTop(hwnd);
                        SetForegroundWindow(hwnd);
                        AttachThreadInput(curThread, fgThread, false);
                    }
                    else
                    {
                        BringWindowToTop(hwnd);
                        SetForegroundWindow(hwnd);
                    }
                }
                catch
                {
                    try { SetForegroundWindow(hwnd); } catch { }
                }

                SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }

            this.Topmost = false;
            this.Activate();
            this.Focus();

            if (_diagnosticsTimer != null && !_diagnosticsTimer.IsEnabled)
            {
                _diagnosticsTimer.Start();
                UpdateDiagnostics();
            }
        }

        public static void ToggleOrShowWindow()
        {
            var inst = Instance;
            if (inst == null || inst._isExiting) return;
            var d = inst.Dispatcher;
            if (d == null || d.HasShutdownStarted || d.HasShutdownFinished) return;
            try
            {
                d.BeginInvoke(() =>
                {
                    if (inst._isExiting) return;
                    if (inst.IsVisible && inst.WindowState != WindowState.Minimized && inst.IsActive)
                    {
                        inst._diagnosticsTimer?.Stop();
                        inst.Hide();
                    }
                    else
                    {
                        inst.RestoreFromTray();
                    }
                });
            }
            catch { }
        }

        public static void ShowWindowDirect()
        {
            var inst = Instance;
            if (inst == null || inst._isExiting) return;
            var d = inst.Dispatcher;
            if (d == null || d.HasShutdownStarted || d.HasShutdownFinished) return;
            try
            {
                d.BeginInvoke(() =>
                {
                    if (!inst._isExiting) inst.RestoreFromTray();
                });
            }
            catch { }
        }

        public static void HideWindowDirect()
        {
            var inst = Instance;
            if (inst == null || inst._isExiting) return;
            var d = inst.Dispatcher;
            if (d == null || d.HasShutdownStarted || d.HasShutdownFinished) return;
            try
            {
                d.BeginInvoke(() =>
                {
                    if (!inst._isExiting)
                    {
                        inst._diagnosticsTimer?.Stop();
                        inst.Hide();
                    }
                });
            }
            catch { }
        }

        private int _exitStarted = 0;

        public void ExitApplication(bool restart = false)
        {
            if (Interlocked.Exchange(ref _exitStarted, 1) != 0)
                return;

            _isExiting = true;
            _isListening = false;
            try { SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged; } catch { }

            if (Dispatcher != null && !Dispatcher.CheckAccess())
            {
                try { Dispatcher.BeginInvoke(() => FinishExitApplication(restart)); } catch { Environment.Exit(0); }
                return;
            }

            FinishExitApplication(restart);
        }

        private void FinishExitApplication(bool restart)
        {
            try { _diagnosticsTimer?.Stop(); } catch { }
            try { _thermalGuardTimer?.Dispose(); _thermalGuardTimer = null; } catch { }
            try { this.Hide(); } catch { }
            try { Program.SingleInstanceMutex?.ReleaseMutex(); Program.SingleInstanceMutex?.Dispose(); } catch { }
            try { _showWindowEvent?.Set(); _showWindowEvent?.Dispose(); } catch { }
            try { _exitAppEvent?.Set(); _exitAppEvent?.Dispose(); } catch { }

            if (_trayIcon != null)
            {
                try
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }
                catch { }
            }

            try
            {
                Task.Run(() => { try { Targets.Viiper.UsbipCli.DetachAll(); } catch { } }).Wait(800);
            }
            catch { }

            try
            {
                // Uncloak HidHide and unhide registered physical devices so Windows immediately regains access to controller & trackpad
                HidHide.SetCloaking(false);
                foreach (string instanceId in HidHide.GetRegisteredDevices())
                {
                    if (!string.IsNullOrWhiteSpace(instanceId))
                        HidHide.UnhidePath(instanceId);
                }
            }
            catch { }

            try
            {
                if (ManagerFactory.settingsManager.GetBoolean("KeepTrackpadOnExit"))
                {
                    if (IDevice.GetCurrent() is LegionGo legoDevice)
                        legoDevice.SetPassthrough(false);
                    Devices.Lenovo.SapientiaUsb.Init();
                    Devices.Lenovo.SapientiaUsb.SetTouchPadStatus(1);
                    Devices.Lenovo.SapientiaUsb.SetTrackpadStatus(4, 1);
                }
            }
            catch { }

            try
            {
                // Restore default hardware touchpad passthrough & cleanly release SapientiaUsb/HID handles
                IDevice.GetCurrent()?.Close();
            }
            catch { }

            TrayCleaner.Clean();

            if (restart)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Process.GetCurrentProcess().MainModule?.FileName ?? "LegionGoCompanion.exe",
                        Arguments = "--restart",
                        UseShellExecute = true
                    });
                }
                catch { }
            }

            Environment.Exit(0);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExiting)
            {
                bool keepRunning = ManagerFactory.settingsManager.GetBoolean("CloseMinimises");
                if (keepRunning)
                {
                    e.Cancel = true;
                    _diagnosticsTimer?.Stop();
                    this.Hide();
                    if (_trayIcon != null)
                    {
                        _trayIcon.Visible = true;
                        _trayIcon.ShowBalloonTip(1000, "Legion Go Companion", "Running in background (system tray)", System.Windows.Forms.ToolTipIcon.Info);
                    }
                    return;
                }
            }

            ExitApplication(restart: false);
        }

        private void RefreshAllStates()
        {
            UpdateGyroStatus();
            UpdateLedControlsFromService();
            UpdateResolutionControls();
            UpdateVolumeControls();
        }
        #endregion
    }
}
