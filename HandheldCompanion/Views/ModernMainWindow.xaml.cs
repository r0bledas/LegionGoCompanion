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
using System.Windows.Media;
using System.Windows.Threading;
using GregsStack.InputSimulatorStandard.Native;
using HandheldCompanion.Devices;
using HandheldCompanion.Devices.Lenovo;
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

        private int _currentTdp = 15;
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

        private readonly Dictionary<ButtonFlags, (ComboBox typeCombo, ComboBox targetCombo, CheckBox chkRepeat, TextBlock txtRate)> _remappingControls = new();

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
                badgeAdmin.Background = new SolidColorBrush(Color.FromRgb(28, 56, 37));
                badgeAdmin.BorderBrush = new SolidColorBrush(Color.FromRgb(46, 107, 62));
                txtAdminStatus.Foreground = new SolidColorBrush(Color.FromRgb(48, 209, 88));
                lblBottomAdmin.Text = "Admin: Elevated";
                lblBottomAdmin.Foreground = new SolidColorBrush(Color.FromRgb(48, 209, 88));
                btnElevate.Visibility = Visibility.Collapsed;
            }
            else
            {
                txtAdminStatus.Text = "Admin: Standard";
                badgeAdmin.Background = new SolidColorBrush(Color.FromRgb(56, 28, 28));
                badgeAdmin.BorderBrush = new SolidColorBrush(Color.FromRgb(107, 46, 46));
                txtAdminStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 69, 58));
                lblBottomAdmin.Text = "Admin: Standard";
                lblBottomAdmin.Foreground = new SolidColorBrush(Color.FromRgb(255, 69, 58));
                btnElevate.Visibility = Visibility.Visible;
            }

            InitializeTrayIcon();
            StartEventListener();
            StartDiagnosticsTimer();

            InitializeGeneralView();
            InitializeControllerView();
            InitializeSettingsView();

            this.Loaded += ModernMainWindow_Loaded;
        }

        private async void ModernMainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshAllStates();
            var currentMode = ControllerModeService.GetCurrentMode();
            UpdateActiveControllerHighlight(currentMode);
            await ControllerModeService.ApplyMode(currentMode);
        }

        #region Navigation
        private void NavGeneral_Click(object sender, RoutedEventArgs e)
        {
            SetNavActive(navGeneral);
            panelGeneral.Visibility = Visibility.Visible;
            panelController.Visibility = Visibility.Collapsed;
            panelSettings.Visibility = Visibility.Collapsed;
        }

        private void NavController_Click(object sender, RoutedEventArgs e)
        {
            SetNavActive(navController);
            panelGeneral.Visibility = Visibility.Collapsed;
            panelController.Visibility = Visibility.Visible;
            panelSettings.Visibility = Visibility.Collapsed;
            UpdateGyroStatus();
            UpdateLedControlsFromService();
        }

        private void NavSettings_Click(object sender, RoutedEventArgs e)
        {
            SetNavActive(navSettings);
            panelGeneral.Visibility = Visibility.Collapsed;
            panelController.Visibility = Visibility.Collapsed;
            panelSettings.Visibility = Visibility.Visible;
        }

        private void SetNavActive(Button activeBtn)
        {
            var activeStyle = (Style)FindResource("ActiveTouchButton");
            var normalStyle = (Style)FindResource("TouchButton");

            navGeneral.Style = (navGeneral == activeBtn) ? activeStyle : normalStyle;
            navController.Style = (navController == activeBtn) ? activeStyle : normalStyle;
            navSettings.Style = (navSettings == activeBtn) ? activeStyle : normalStyle;
        }
        #endregion

        #region General View (Controller Mode, TDP, Fan, Resolution)
        private void InitializeGeneralView()
        {
            ControllerModeService.ModeChanged += (mode) =>
            {
                Dispatcher.Invoke(() => UpdateActiveControllerHighlight(mode));
            };

            UpdateActiveControllerHighlight(ControllerModeService.GetCurrentMode());
            int savedTdp = ManagerFactory.settingsManager.GetInt("SelectedTdp");
            if (savedTdp >= 5 && savedTdp <= 35)
                _currentTdp = savedTdp;
            ApplyTdp(_currentTdp);
            UpdateResolutionControls();
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
                        PerformanceManager.SetTDP(targetTdp, true);

                        if (IDevice.GetCurrent() is LegionGo lego)
                        {
                            int oemMode = (int)LegionGo.LegionMode.Custom;
                            if (targetTdp <= 10) oemMode = (int)LegionGo.LegionMode.Quiet;
                            else if (targetTdp == 15) oemMode = (int)LegionGo.LegionMode.Balanced;
                            else if (targetTdp == 25 || targetTdp == 30) oemMode = (int)LegionGo.LegionMode.Performance;

                            lego.SetSmartFanMode(oemMode);
                            lego.set_long_limit(targetTdp);
                            lego.set_short_limit(targetTdp);
                        }

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
            else if (tdp == 25 || tdp == 30)
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

        private void ApplyFanMode(bool isAuto, bool isFullSpeed, int percentage)
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

                    var activeStyle = (Style)FindResource("ActiveTouchButton");
                    var normalStyle = (Style)FindResource("TouchButton");

                    btnFanAuto.Style = isAuto ? activeStyle : normalStyle;
                    btnFanFull.Style = isFullSpeed ? activeStyle : normalStyle;
                    btnFan50.Style = (!isAuto && !isFullSpeed && percentage == 50) ? activeStyle : normalStyle;
                    btnFan75.Style = (!isAuto && !isFullSpeed && percentage == 75) ? activeStyle : normalStyle;
                }
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
                else if (s == "1200") ApplyResolution(1920, 1200);
                else if (s == "1440") ApplyResolution(2560, 1440);
                else if (s == "1600") ApplyResolution(2560, 1600);
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
                    UpdateResolutionControls();
                }
            }
            catch (Exception ex)
            {
                LogManager.LogError("ApplyResolution error: {0}", ex.Message);
            }
        }

        private void UpdateResolutionControls()
        {
            int currentW = 0, currentH = 0;
            var primary = ManagerFactory.multimediaManager?.PrimaryDesktop;
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

            int w = Math.Max(currentW, currentH);
            int h = Math.Min(currentW, currentH);

            txtCurrentRes.Text = $"Current: {w}x{h}";

            var activeStyle = (Style)FindResource("ActiveTouchButton");
            var normalStyle = (Style)FindResource("TouchButton");

            btnRes800.Style = (w == 1280 && h == 800) ? activeStyle : normalStyle;
            btnRes1200.Style = (w == 1920 && h == 1200) ? activeStyle : normalStyle;
            btnRes1440.Style = (w == 2560 && h == 1440) ? activeStyle : normalStyle;
            btnRes1600.Style = (w == 2560 && h == 1600) ? activeStyle : normalStyle;
        }
        #endregion

        #region Controller View (Gyro, Joystick LED, Pointer Mode, Hold Cycle, Remap)
        private void InitializeControllerView()
        {
            // Gyro
            UpdateGyroStatus();

            // LED Service events
            JoystickLedService.Instance.ConfigChanged += () =>
            {
                Dispatcher.Invoke(UpdateLedControlsFromService);
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
                txtGyroStatus.Text = "Gyro Aiming is Active (DualShock 4 motion feed running).";
                txtGyroStatus.Foreground = new SolidColorBrush(Color.FromRgb(48, 209, 88));
            }
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
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
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

                stackTurbo.Children.Add(chkTurbo);
                stackTurbo.Children.Add(btnMinus);
                stackTurbo.Children.Add(borderRate);
                stackTurbo.Children.Add(btnPlus);
                Grid.SetColumn(stackTurbo, 3);

                grid.Children.Add(lblBtn);
                grid.Children.Add(comboType);
                grid.Children.Add(comboTarget);
                grid.Children.Add(stackTurbo);
                border.Child = grid;

                stackRemapRows.Children.Add(border);
                _remappingControls[btn] = (comboType, comboTarget, chkTurbo, txtRate);

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

                chkTurbo.Click += (s, e) => SaveRowMapping(btn);

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
            var (typeCombo, targetCombo, chkRepeat, txtRate) = row;

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
            CustomMappingService.Instance.SetMapping(btn, type, targetBtn, targetKey, targetAction, chkRepeat.IsChecked ?? false, delay);
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

        #region Settings View (Hardware, Utilities, Updates)
        private void InitializeSettingsView()
        {
            chkBatteryLimit.IsChecked = ManagerFactory.settingsManager.GetBoolean("BatteryChargeLimit");
            chkCloseMinimises.IsChecked = ManagerFactory.settingsManager.GetBoolean("CloseMinimises");
            chkRunAtStartup.IsChecked = ManagerFactory.settingsManager.GetBoolean("RunAtStartup");
            chkStartMinimized.IsChecked = ManagerFactory.settingsManager.GetBoolean("StartMinimized");
            chkDesktopOnStart.IsChecked = ManagerFactory.settingsManager.GetBoolean("DesktopLayoutOnStart");
        }

        private void ChkBatteryLimit_Click(object sender, RoutedEventArgs e)
        {
            bool val = chkBatteryLimit.IsChecked ?? false;
            ManagerFactory.settingsManager.SetProperty("BatteryChargeLimit", val);
            if (IDevice.GetCurrent() is LegionGo lego)
            {
                lego.SetBatteryChargeLimit(val);
            }
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
                bool vigemOk = File.Exists(Path.Combine(Environment.SystemDirectory, "drivers", "ViGEmBus.sys")) || VirtualManager.IsInitialized;
                bool hidHideOk = File.Exists(Path.Combine(Environment.SystemDirectory, "drivers", "HidHide.sys"));
                bool controllerOk = ControllerManager.HasTargetController;

                txtVigemStatus.Text = vigemOk ? "ViGEm: OK" : "ViGEm: Missing";
                txtVigemStatus.Foreground = vigemOk ? new SolidColorBrush(Color.FromRgb(48, 209, 88)) : new SolidColorBrush(Color.FromRgb(255, 69, 58));

                txtHidHideStatus.Text = hidHideOk ? "HidHide: OK" : "HidHide: Missing";
                txtHidHideStatus.Foreground = hidHideOk ? new SolidColorBrush(Color.FromRgb(48, 209, 88)) : new SolidColorBrush(Color.FromRgb(255, 69, 58));

                lblBottomHealth.Text = $"ViGEm: {(vigemOk ? "OK" : "Missing")}  |  HidHide: {(hidHideOk ? "OK" : "Missing")}  |  Controller: {(controllerOk ? "OK" : "Disconnected")}";
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
            if (this.WindowState == WindowState.Minimized)
            {
                this.WindowState = WindowState.Normal;
            }

            if (!this.IsVisible)
            {
                this.Show();
            }

            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            ShowWindow(helper.Handle, SW_RESTORE);
            this.Activate();
            this.Focus();
            try { SetForegroundWindow(helper.Handle); } catch { }
        }

        public static void ToggleOrShowWindow()
        {
            if (Instance == null) return;
            Instance.Dispatcher.Invoke(() =>
            {
                if (Instance.IsVisible && Instance.WindowState != WindowState.Minimized && Instance.IsActive)
                {
                    Instance.Hide();
                }
                else
                {
                    Instance.RestoreFromTray();
                }
            });
        }

        public static void ShowWindowDirect()
        {
            if (Instance == null) return;
            Instance.Dispatcher.Invoke(() => Instance.RestoreFromTray());
        }

        public static void HideWindowDirect()
        {
            if (Instance == null) return;
            Instance.Dispatcher.Invoke(() => Instance.Hide());
        }

        public void ExitApplication(bool restart = false)
        {
            _isExiting = true;
            _isListening = false;
            _diagnosticsTimer?.Stop();

            try { _showWindowEvent?.Set(); _showWindowEvent?.Dispose(); } catch { }
            try { _exitAppEvent?.Set(); _exitAppEvent?.Dispose(); } catch { }
            try { Targets.Viiper.UsbipCli.DetachAll(); } catch { }
            try { Program.SingleInstanceMutex?.ReleaseMutex(); Program.SingleInstanceMutex?.Dispose(); } catch { }

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }

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
        }
        #endregion
    }
}
