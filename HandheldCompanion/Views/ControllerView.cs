using System;
using System.Collections.Generic;
using System.Drawing;
using System.Media;
using System.Threading.Tasks;
using System.Windows.Forms;
using GregsStack.InputSimulatorStandard.Native;
using HandheldCompanion.Devices;
using HandheldCompanion.Devices.Lenovo;
using HandheldCompanion.Inputs;
using HandheldCompanion.Managers;
using HandheldCompanion.Shared;
using HandheldCompanion.Utils;

namespace HandheldCompanion.Views
{
    public class ControllerView : UserControl
    {
        private Label? lblGyroStatus;
        private Button? btnGyroOn;
        private Button? btnGyroOff;

        // GroupBoxes for scaling
        private GroupBox? grpGyro;
        private GroupBox? grpLed;
        private GroupBox? grpPointer;
        private GroupBox? grpHoldCycle;
        private GroupBox? grpRemap;

        // Joystick LED controls
        private Button? btnLedOn;
        private Button? btnLedOff;
        private ComboBox? cmbLedEffect;
        private NumericUpDown? numLedBrightness;
        private NumericUpDown? numLedSpeed;
        private Panel? pnlLedSwatchLeft;
        private Panel? pnlLedSwatchRight;
        private CheckBox? chkLedSyncLR;
        private Button? btnLedCustomLeft;
        private Button? btnLedCustomRight;
        private Label? lblLedStatus;
        private bool _updatingLedUi = false;

        private static readonly (string Name, Color Color)[] LedColorPresets = new[]
        {
            ("Blue", Color.FromArgb(0, 120, 215)),
            ("Cyan", Color.FromArgb(0, 229, 255)),
            ("Red", Color.FromArgb(255, 30, 30)),
            ("Green", Color.FromArgb(0, 230, 118)),
            ("Purple", Color.FromArgb(170, 0, 255)),
            ("Orange", Color.FromArgb(255, 145, 0)),
            ("Yellow", Color.FromArgb(255, 214, 0)),
            ("White", Color.FromArgb(255, 255, 255))
        };

        // Pointer Mode controls
        private CheckBox? chkPointerDetachedOnly;
        private CheckBox? chkPointerRightClickRB;
        private ComboBox? cmbPointerButton;
        private ComboBox? cmbPointerType;
        private NumericUpDown? numPointerSens;
        private CheckBox? chkPointerInvertX;
        private CheckBox? chkPointerInvertY;
        private Label? lblPointerStatus;

        // Hold to Cycle controls
        private CheckBox? chkHoldCycleEnabled;
        private ComboBox? cmbTriggerBtn;
        private NumericUpDown? numHoldTime;
        private CheckBox? chkCycleDesktop;
        private CheckBox? chkCycleX360;
        private CheckBox? chkCycleDS4;
        private CheckBox? chkCycleNative;
        private CheckBox? chkCyclePointer;
        private Label? lblCycleSummary;

        // Controls references for live updates and reset
        private readonly Dictionary<ButtonFlags, (ComboBox typeCombo, ComboBox targetCombo, CheckBox chkRepeat, NumericUpDown numRate)> _mappingControls = new();

        // Controller buttons for dropdown
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

        // Pre-cached dropdown names for zero-lag population
        private static readonly string[] ControllerTargetNames = ControllerTargets.ConvertAll(t => t.Name).ToArray();

        // Keyboard keys for dropdown
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
            ("Page Up", VirtualKeyCode.PRIOR),
            ("Page Down", VirtualKeyCode.NEXT),
            ("Home", VirtualKeyCode.HOME),
            ("End", VirtualKeyCode.END),
            ("Volume Mute", VirtualKeyCode.VOLUME_MUTE),
            ("Volume Down", VirtualKeyCode.VOLUME_DOWN),
            ("Volume Up", VirtualKeyCode.VOLUME_UP),
            ("Play / Pause", VirtualKeyCode.MEDIA_PLAY_PAUSE),
            ("A", VirtualKeyCode.VK_A),
            ("B", VirtualKeyCode.VK_B),
            ("C", VirtualKeyCode.VK_C),
            ("D", VirtualKeyCode.VK_D),
            ("E", VirtualKeyCode.VK_E),
            ("F", VirtualKeyCode.VK_F),
            ("G", VirtualKeyCode.VK_G),
            ("H", VirtualKeyCode.VK_H),
            ("I", VirtualKeyCode.VK_I),
            ("J", VirtualKeyCode.VK_J),
            ("K", VirtualKeyCode.VK_K),
            ("L", VirtualKeyCode.VK_L),
            ("M", VirtualKeyCode.VK_M),
            ("N", VirtualKeyCode.VK_N),
            ("O", VirtualKeyCode.VK_O),
            ("P", VirtualKeyCode.VK_P),
            ("Q", VirtualKeyCode.VK_Q),
            ("R", VirtualKeyCode.VK_R),
            ("S", VirtualKeyCode.VK_S),
            ("T", VirtualKeyCode.VK_T),
            ("U", VirtualKeyCode.VK_U),
            ("V", VirtualKeyCode.VK_V),
            ("W", VirtualKeyCode.VK_W),
            ("X", VirtualKeyCode.VK_X),
            ("Y", VirtualKeyCode.VK_Y),
            ("Z", VirtualKeyCode.VK_Z),
            ("0", VirtualKeyCode.VK_0),
            ("1", VirtualKeyCode.VK_1),
            ("2", VirtualKeyCode.VK_2),
            ("3", VirtualKeyCode.VK_3),
            ("4", VirtualKeyCode.VK_4),
            ("5", VirtualKeyCode.VK_5),
            ("6", VirtualKeyCode.VK_6),
            ("7", VirtualKeyCode.VK_7),
            ("8", VirtualKeyCode.VK_8),
            ("9", VirtualKeyCode.VK_9)
        };

        // Pre-cached keyboard names for zero-lag population
        private static readonly string[] KeyboardTargetNames = KeyboardTargets.ConvertAll(t => t.Name).ToArray();

        private struct ManagedButtonSpec
        {
            public Button Button;
            public int BaseWidth;
            public int BaseHeight;
            public float BaseFontSize;
        }
        private readonly System.Collections.Generic.List<ManagedButtonSpec> _managedButtons = new();

        public ControllerView()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            InitializeComponent();
        }

        private static void EnableDoubleBuffering(Control control)
        {
            try
            {
                typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.SetValue(control, true, null);
            }
            catch { }
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            this.AutoScaleMode = AutoScaleMode.None;
            this.Dock = DockStyle.Fill;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            // Main scrollable content panel
            Panel mainScrollPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(10, 8, 10, 8)
            };
            EnableDoubleBuffering(mainScrollPanel);

            // ==========================================
            // 1. Gyro Aiming GroupBox
            // ==========================================
            grpGyro = new GroupBox
            {
                Text = "Gyro Aiming",
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Padding = new Padding(8, 6, 8, 8),
                Margin = new Padding(0, 0, 0, 6)
            };

            FlowLayoutPanel flowGyro = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Padding = new Padding(2)
            };

            btnGyroOn = CreateCompactButton("On", 64, 32, 9.0F, () =>
            {
                ControllerManager.GyroAimingEnabled = true;
                UpdateGyroStatus();
            });

            btnGyroOff = CreateCompactButton("Off", 64, 32, 9.0F, () =>
            {
                ControllerManager.GyroAimingEnabled = false;
                UpdateGyroStatus();
            });

            flowGyro.Controls.Add(btnGyroOn);
            flowGyro.Controls.Add(btnGyroOff);
            grpGyro.Controls.Add(flowGyro);

            lblGyroStatus = new Label
            {
                Name = "lblGyroStatus",
                Dock = DockStyle.Top,
                AutoSize = true,
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                ForeColor = Color.FromArgb(70, 75, 85),
                Padding = new Padding(4, 4, 4, 4)
            };
            grpGyro.Controls.Add(lblGyroStatus);

            // ==========================================
            // 1B. Joystick LED Lighting GroupBox
            // ==========================================
            grpLed = new GroupBox
            {
                Text = "Joystick LED Lighting (RGB Rings)",
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Padding = new Padding(8, 6, 8, 8),
                Margin = new Padding(0, 0, 0, 6)
            };

            FlowLayoutPanel flowLedTop = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Padding = new Padding(2)
            };

            btnLedOn = CreateCompactButton("On", 56, 30, 9.0F, () =>
            {
                JoystickLedService.Instance.SetEnabled(true);
                UpdateLedControlsFromService();
            });

            btnLedOff = CreateCompactButton("Off", 56, 30, 9.0F, () =>
            {
                JoystickLedService.Instance.SetEnabled(false);
                UpdateLedControlsFromService();
            });

            Label lblLedEffect = new Label
            {
                Text = "Effect:",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(40, 45, 55),
                Margin = new Padding(6, 7, 3, 3)
            };

            cmbLedEffect = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Width = 145,
                Margin = new Padding(2, 4, 8, 3)
            };
            cmbLedEffect.Items.AddRange(new object[]
            {
                "Solid Color",
                "Pulse (Breathing)",
                "Dynamic (Rainbow)",
                "Spiral (Wheel)"
            });
            cmbLedEffect.SelectedIndexChanged += (s, e) =>
            {
                if (_updatingLedUi) return;
                LegionGo.RgbMode mode = (cmbLedEffect.SelectedIndex + 1) switch
                {
                    1 => LegionGo.RgbMode.Solid,
                    2 => LegionGo.RgbMode.Pulse,
                    3 => LegionGo.RgbMode.Dynamic,
                    4 => LegionGo.RgbMode.Spiral,
                    _ => LegionGo.RgbMode.Solid
                };
                JoystickLedService.Instance.SetEffect(mode);
                UpdateLedControlsFromService();
            };

            Label lblLedBright = new Label
            {
                Text = "Brightness:",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(40, 45, 55),
                Margin = new Padding(4, 7, 3, 3)
            };

            numLedBrightness = new NumericUpDown
            {
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Width = 54,
                Minimum = 0,
                Maximum = 100,
                Increment = 10,
                Value = Math.Clamp(JoystickLedService.Instance.Brightness, 0, 100),
                Margin = new Padding(2, 4, 8, 3)
            };
            numLedBrightness.ValueChanged += (s, e) =>
            {
                if (_updatingLedUi) return;
                JoystickLedService.Instance.SetBrightness((int)numLedBrightness.Value);
                UpdateLedControlsFromService();
            };

            Label lblLedSpeed = new Label
            {
                Text = "Speed:",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(40, 45, 55),
                Margin = new Padding(4, 7, 3, 3)
            };

            numLedSpeed = new NumericUpDown
            {
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Width = 54,
                Minimum = 0,
                Maximum = 100,
                Increment = 10,
                Value = Math.Clamp(JoystickLedService.Instance.Speed, 0, 100),
                Margin = new Padding(2, 4, 4, 3)
            };
            numLedSpeed.ValueChanged += (s, e) =>
            {
                if (_updatingLedUi) return;
                JoystickLedService.Instance.SetSpeed((int)numLedSpeed.Value);
                UpdateLedControlsFromService();
            };

            flowLedTop.Controls.Add(btnLedOn);
            flowLedTop.Controls.Add(btnLedOff);
            flowLedTop.Controls.Add(lblLedEffect);
            flowLedTop.Controls.Add(cmbLedEffect);
            flowLedTop.Controls.Add(lblLedBright);
            flowLedTop.Controls.Add(numLedBrightness);
            flowLedTop.Controls.Add(lblLedSpeed);
            flowLedTop.Controls.Add(numLedSpeed);

            FlowLayoutPanel flowLedColors = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Padding = new Padding(2)
            };

            Label lblPresets = new Label
            {
                Text = "Presets:",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(40, 45, 55),
                Margin = new Padding(3, 7, 4, 3)
            };
            flowLedColors.Controls.Add(lblPresets);

            foreach (var preset in LedColorPresets)
            {
                Color c = preset.Color;
                Button btnColor = CreateCompactButton(preset.Name, 58, 28, 8.2F, () =>
                {
                    JoystickLedService.Instance.SetLeftColor(c);
                    UpdateLedControlsFromService();
                });
                flowLedColors.Controls.Add(btnColor);
            }

            FlowLayoutPanel flowLedCustom = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Padding = new Padding(2)
            };

            pnlLedSwatchLeft = new Panel
            {
                Size = new Size(24, 24),
                BackColor = JoystickLedService.Instance.LeftColor,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(4, 5, 4, 3),
                Cursor = Cursors.Hand
            };
            pnlLedSwatchLeft.Click += (s, e) => PickCustomLedColor(isRight: false);

            btnLedCustomLeft = CreateCompactButton("Pick Color...", 94, 28, 8.5F, () => PickCustomLedColor(isRight: false));

            chkLedSyncLR = new CheckBox
            {
                Text = "Sync Left & Right Joysticks",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Checked = JoystickLedService.Instance.SyncLeftRight,
                Margin = new Padding(10, 6, 8, 3)
            };
            chkLedSyncLR.CheckedChanged += (s, e) =>
            {
                if (_updatingLedUi) return;
                JoystickLedService.Instance.SetSyncLeftRight(chkLedSyncLR.Checked);
                UpdateLedControlsFromService();
            };

            pnlLedSwatchRight = new Panel
            {
                Size = new Size(24, 24),
                BackColor = JoystickLedService.Instance.RightColor,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(4, 5, 4, 3),
                Cursor = Cursors.Hand
            };
            pnlLedSwatchRight.Click += (s, e) => PickCustomLedColor(isRight: true);

            btnLedCustomRight = CreateCompactButton("Right Color...", 94, 28, 8.5F, () => PickCustomLedColor(isRight: true));

            flowLedCustom.Controls.Add(pnlLedSwatchLeft);
            flowLedCustom.Controls.Add(btnLedCustomLeft);
            flowLedCustom.Controls.Add(chkLedSyncLR);
            flowLedCustom.Controls.Add(pnlLedSwatchRight);
            flowLedCustom.Controls.Add(btnLedCustomRight);

            lblLedStatus = new Label
            {
                Name = "lblLedStatus",
                Dock = DockStyle.Top,
                AutoSize = true,
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                ForeColor = Color.FromArgb(0, 120, 195),
                Padding = new Padding(4, 2, 4, 2)
            };

            grpLed.Controls.Add(lblLedStatus);
            grpLed.Controls.Add(flowLedCustom);
            grpLed.Controls.Add(flowLedColors);
            grpLed.Controls.Add(flowLedTop);

            JoystickLedService.Instance.ConfigChanged += () =>
            {
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    this.BeginInvoke(new Action(UpdateLedControlsFromService));
                }
            };

            // ==========================================
            // 2. Pointer Mode (Right Controller Gyro Air Mouse) GroupBox
            // ==========================================
            grpPointer = new GroupBox
            {
                Text = "Pointer Mode (Right Controller Gyro Air Mouse)",
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Padding = new Padding(8, 6, 8, 8),
                Margin = new Padding(0, 0, 0, 6)
            };

            lblPointerStatus = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Font = new Font("Segoe UI", 7.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(80, 85, 95),
                Text = "Pointer Mode uses the detached Right Controller as an air mouse. RT is primary click (Left Click), and RB is secondary click (Right Click).",
                Padding = new Padding(4, 2, 4, 4)
            };

            FlowLayoutPanel flowPointerOptions = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Padding = new Padding(2)
            };

            chkPointerDetachedOnly = new CheckBox
            {
                Text = "Only active when detached",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Checked = PointerModeService.Instance.DetachedOnly,
                Margin = new Padding(3, 4, 12, 3)
            };
            chkPointerDetachedOnly.CheckedChanged += (s, e) =>
            {
                PointerModeService.Instance.DetachedOnly = chkPointerDetachedOnly.Checked;
                PointerModeService.Instance.SaveConfig();
            };

            chkPointerRightClickRB = new CheckBox
            {
                Text = "Right Click on RB",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Checked = PointerModeService.Instance.RightClickOnRB,
                Margin = new Padding(3, 4, 12, 3)
            };
            chkPointerRightClickRB.CheckedChanged += (s, e) =>
            {
                PointerModeService.Instance.RightClickOnRB = chkPointerRightClickRB.Checked;
                PointerModeService.Instance.SaveConfig();
            };

            flowPointerOptions.Controls.Add(chkPointerDetachedOnly);
            flowPointerOptions.Controls.Add(chkPointerRightClickRB);

            FlowLayoutPanel flowPointerSettings = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Padding = new Padding(2)
            };

            Label lblActivationBtn = new Label
            {
                Text = "Activation:",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(40, 45, 55),
                Margin = new Padding(3, 5, 4, 3)
            };

            cmbPointerButton = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Width = 155,
                Margin = new Padding(3, 3, 10, 3)
            };
            cmbPointerButton.Items.AddRange(new object[]
            {
                "M1 (Upper Back)",
                "M2 (Lower Back)",
                "M3 (Side/Bottom)",
                "Right Stick Click (RS)",
                "Right Bumper (RB)",
                "Y3 (Lower Right Back)",
                "Always Active"
            });
            cmbPointerButton.SelectedIndex = Math.Clamp((int)PointerModeService.Instance.ActivationButton, 0, cmbPointerButton.Items.Count - 1);
            cmbPointerButton.SelectedIndexChanged += (s, e) =>
            {
                PointerModeService.Instance.ActivationButton = (PointerActivationButton)cmbPointerButton.SelectedIndex;
                PointerModeService.Instance.SaveConfig();
            };

            Label lblActivationType = new Label
            {
                Text = "Type:",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(40, 45, 55),
                Margin = new Padding(3, 5, 4, 3)
            };

            cmbPointerType = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Width = 110,
                Margin = new Padding(3, 3, 10, 3)
            };
            cmbPointerType.Items.AddRange(new object[]
            {
                "Hold to Aim",
                "Toggle On/Off",
                "Always Active"
            });
            cmbPointerType.SelectedIndex = Math.Clamp((int)PointerModeService.Instance.ActivationType, 0, cmbPointerType.Items.Count - 1);
            cmbPointerType.SelectedIndexChanged += (s, e) =>
            {
                PointerModeService.Instance.ActivationType = (PointerActivationType)cmbPointerType.SelectedIndex;
                PointerModeService.Instance.SaveConfig();
            };

            Label lblSens = new Label
            {
                Text = "Speed:",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(40, 45, 55),
                Margin = new Padding(3, 5, 4, 3)
            };

            numPointerSens = new NumericUpDown
            {
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Width = 55,
                DecimalPlaces = 1,
                Minimum = 0.2M,
                Maximum = 5.0M,
                Increment = 0.1M,
                Value = (decimal)Math.Clamp(PointerModeService.Instance.Sensitivity, 0.2f, 5.0f),
                Margin = new Padding(3, 3, 10, 3)
            };
            numPointerSens.ValueChanged += (s, e) =>
            {
                PointerModeService.Instance.Sensitivity = (float)numPointerSens.Value;
                PointerModeService.Instance.SaveConfig();
            };

            chkPointerInvertX = new CheckBox
            {
                Text = "Invert X",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Checked = PointerModeService.Instance.InvertX,
                Margin = new Padding(3, 4, 8, 3)
            };
            chkPointerInvertX.CheckedChanged += (s, e) =>
            {
                PointerModeService.Instance.InvertX = chkPointerInvertX.Checked;
                PointerModeService.Instance.SaveConfig();
            };

            chkPointerInvertY = new CheckBox
            {
                Text = "Invert Y",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Checked = PointerModeService.Instance.InvertY,
                Margin = new Padding(3, 4, 8, 3)
            };
            chkPointerInvertY.CheckedChanged += (s, e) =>
            {
                PointerModeService.Instance.InvertY = chkPointerInvertY.Checked;
                PointerModeService.Instance.SaveConfig();
            };

            flowPointerSettings.Controls.Add(lblActivationBtn);
            flowPointerSettings.Controls.Add(cmbPointerButton);
            flowPointerSettings.Controls.Add(lblActivationType);
            flowPointerSettings.Controls.Add(cmbPointerType);
            flowPointerSettings.Controls.Add(lblSens);
            flowPointerSettings.Controls.Add(numPointerSens);
            flowPointerSettings.Controls.Add(chkPointerInvertX);
            flowPointerSettings.Controls.Add(chkPointerInvertY);

            grpPointer.Controls.Add(flowPointerSettings);
            grpPointer.Controls.Add(flowPointerOptions);
            grpPointer.Controls.Add(lblPointerStatus);

            // ==========================================
            // 3. Hold to Cycle Controller Modes GroupBox
            // ==========================================
            grpHoldCycle = new GroupBox
            {
                Text = "Hold to Cycle Controller Modes",
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Padding = new Padding(8, 6, 8, 8),
                Margin = new Padding(0, 0, 0, 6)
            };

            FlowLayoutPanel flowTop = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Padding = new Padding(2)
            };

            chkHoldCycleEnabled = new CheckBox
            {
                Text = "Enable Hold to Cycle",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Margin = new Padding(3, 4, 12, 3)
            };

            Label lblTrigger = new Label
            {
                Text = "Trigger Button:",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(40, 45, 55),
                Margin = new Padding(0, 6, 3, 3)
            };

            cmbTriggerBtn = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Width = 160,
                Margin = new Padding(0, 2, 12, 2)
            };
            cmbTriggerBtn.Items.Add("(None / Disabled)");
            foreach (var b in CustomMappingService.RemappableButtons)
            {
                cmbTriggerBtn.Items.Add(CustomMappingService.GetButtonDisplayName(b));
            }

            Label lblHoldTime = new Label
            {
                Text = "Hold Time:",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(40, 45, 55),
                Margin = new Padding(0, 6, 3, 3)
            };

            numHoldTime = new NumericUpDown
            {
                Minimum = 200,
                Maximum = 2000,
                Value = 500,
                Increment = 50,
                Width = 55,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Margin = new Padding(0, 2, 2, 2)
            };

            Label lblMsHold = new Label
            {
                Text = "ms",
                AutoSize = true,
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                ForeColor = Color.FromArgb(90, 95, 105),
                Margin = new Padding(0, 6, 3, 3)
            };

            flowTop.Controls.Add(chkHoldCycleEnabled);
            flowTop.Controls.Add(lblTrigger);
            flowTop.Controls.Add(cmbTriggerBtn);
            flowTop.Controls.Add(lblHoldTime);
            flowTop.Controls.Add(numHoldTime);
            flowTop.Controls.Add(lblMsHold);

            FlowLayoutPanel flowModes = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Padding = new Padding(2)
            };

            Label lblIncluded = new Label
            {
                Text = "Cycle through:",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(40, 45, 55),
                Margin = new Padding(3, 5, 8, 3)
            };

            chkCycleDesktop = new CheckBox
            {
                Text = "Desktop",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Margin = new Padding(3, 4, 10, 3)
            };

            chkCycleX360 = new CheckBox
            {
                Text = "Xbox 360 (x360)",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Margin = new Padding(3, 4, 10, 3)
            };

            chkCycleDS4 = new CheckBox
            {
                Text = "DualShock 4 (DS4)",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Margin = new Padding(3, 4, 10, 3)
            };

            chkCycleNative = new CheckBox
            {
                Text = "Native",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Margin = new Padding(3, 4, 10, 3)
            };

            chkCyclePointer = new CheckBox
            {
                Text = "Pointer",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Margin = new Padding(3, 4, 10, 3)
            };

            flowModes.Controls.Add(lblIncluded);
            flowModes.Controls.Add(chkCycleDesktop);
            flowModes.Controls.Add(chkCycleX360);
            flowModes.Controls.Add(chkCycleDS4);
            flowModes.Controls.Add(chkCycleNative);
            flowModes.Controls.Add(chkCyclePointer);

            lblCycleSummary = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 100, 180),
                Padding = new Padding(5, 4, 5, 2)
            };

            Label lblCycleNote = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Font = new Font("Segoe UI", 7.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 105, 115),
                Text = "Hold the trigger button for the set duration to cycle modes. A quick tap (< hold time) performs the button's normal action.",
                Padding = new Padding(5, 2, 5, 4)
            };

            InitializeHoldCycleControls();

            grpHoldCycle.Controls.Add(lblCycleNote);
            grpHoldCycle.Controls.Add(lblCycleSummary);
            grpHoldCycle.Controls.Add(flowModes);
            grpHoldCycle.Controls.Add(flowTop);

            // ==========================================
            // 3. Button Remapping GroupBox
            // ==========================================
            grpRemap = new GroupBox
            {
                Text = "Button Remapping (Legion & Back Buttons)",
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Padding = new Padding(8, 6, 8, 8),
                Margin = new Padding(0, 0, 0, 6)
            };

            TableLayoutPanel tableRemap = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 4,
                Padding = new Padding(4)
            };
            EnableDoubleBuffering(tableRemap);
            tableRemap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
            tableRemap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22F));
            tableRemap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26F));
            tableRemap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24F));

            tableRemap.SuspendLayout();
            try
            {
                int row = 0;
                foreach (var btn in CustomMappingService.RemappableButtons)
                {
                    tableRemap.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                    Label lblBtn = new Label
                    {
                        Text = CustomMappingService.GetButtonDisplayName(btn),
                        Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                        ForeColor = Color.FromArgb(30, 35, 45),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        Height = 28,
                        Margin = new Padding(2, 3, 2, 3)
                    };

                    ComboBox comboType = new ComboBox
                    {
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                        Dock = DockStyle.Fill,
                        Height = 26,
                        Margin = new Padding(2, 3, 2, 3)
                    };
                    comboType.Items.Add("None / Default");
                    comboType.Items.Add("Controller Button");
                    comboType.Items.Add("Keyboard Key");
                    comboType.Items.Add("Action");

                    ComboBox comboTarget = new ComboBox
                    {
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                        Dock = DockStyle.Fill,
                        Height = 26,
                        Margin = new Padding(2, 3, 2, 3)
                    };

                    FlowLayoutPanel flowRepeat = new FlowLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        AutoSize = true,
                        WrapContents = false,
                        Margin = new Padding(0),
                        Padding = new Padding(0, 1, 0, 0)
                    };

                    CheckBox chkRepeat = new CheckBox
                    {
                        Text = "Repeat",
                        AutoSize = true,
                        Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                        Margin = new Padding(2, 4, 1, 2)
                    };

                    NumericUpDown numRate = new NumericUpDown
                    {
                        Minimum = 10,
                        Maximum = 1000,
                        Value = 50,
                        Increment = 10,
                        Width = 46,
                        Height = 22,
                        Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                        Margin = new Padding(1, 2, 1, 2)
                    };

                    Label lblMs = new Label
                    {
                        Text = "ms",
                        AutoSize = true,
                        Font = new Font("Segoe UI", 7.5F, FontStyle.Regular),
                        ForeColor = Color.FromArgb(90, 95, 105),
                        Margin = new Padding(0, 5, 0, 0)
                    };

                    flowRepeat.Controls.Add(chkRepeat);
                    flowRepeat.Controls.Add(numRate);
                    flowRepeat.Controls.Add(lblMs);

                    // Populate and bind
                    SetupButtonRow(btn, comboType, comboTarget, chkRepeat, numRate);

                    tableRemap.Controls.Add(lblBtn, 0, row);
                    tableRemap.Controls.Add(comboType, 1, row);
                    tableRemap.Controls.Add(comboTarget, 2, row);
                    tableRemap.Controls.Add(flowRepeat, 3, row);

                    _mappingControls[btn] = (comboType, comboTarget, chkRepeat, numRate);
                    row++;
                }
            }
            finally
            {
                tableRemap.ResumeLayout(false);
            }

            // Reset Button row
            FlowLayoutPanel flowReset = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(2, 6, 2, 2)
            };

            Button btnReset = new Button
            {
                Text = "Reset All",
                Size = new Size(95, 32),
                Font = new Font("Segoe UI", 9.0F, FontStyle.Regular),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(245, 247, 250),
                ForeColor = Color.FromArgb(40, 45, 55)
            };
            btnReset.FlatAppearance.BorderColor = Color.FromArgb(210, 215, 222);
            _managedButtons.Add(new ManagedButtonSpec { Button = btnReset, BaseWidth = 95, BaseHeight = 32, BaseFontSize = 9.0F });
            btnReset.Click += (s, e) =>
            {
                var confirm = MessageBox.Show(
                    "Reset all custom back and Legion button remappings to default?",
                    "Reset Remappings",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (confirm == DialogResult.Yes)
                {
                    CustomMappingService.Instance.ResetAll();
                    RefreshAllRows();
                }
            };
            flowReset.Controls.Add(btnReset);

            grpRemap.Controls.Add(flowReset);
            grpRemap.Controls.Add(tableRemap);

            // Add groups to main scroll panel (dock top in reverse order)
            mainScrollPanel.Controls.Add(grpRemap);
            mainScrollPanel.Controls.Add(grpHoldCycle);
            if (grpPointer != null) mainScrollPanel.Controls.Add(grpPointer);
            if (grpLed != null) mainScrollPanel.Controls.Add(grpLed);
            mainScrollPanel.Controls.Add(grpGyro);

            this.Controls.Add(mainScrollPanel);

            UpdateGyroStatus();
            UpdateLedControlsFromService();

            this.ResumeLayout(false);
        }

        private void PickCustomLedColor(bool isRight)
        {
            var svc = JoystickLedService.Instance;
            using var dlg = new ColorDialog
            {
                AllowFullOpen = true,
                FullOpen = true,
                Color = isRight ? svc.RightColor : svc.LeftColor
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                if (isRight)
                    svc.SetRightColor(dlg.Color);
                else
                    svc.SetLeftColor(dlg.Color);
                UpdateLedControlsFromService();
            }
        }

        private void UpdateLedControlsFromService()
        {
            if (this.IsDisposed) return;
            _updatingLedUi = true;
            try
            {
                var svc = JoystickLedService.Instance;
                SetButtonActive(btnLedOn, svc.Enabled);
                SetButtonActive(btnLedOff, !svc.Enabled);

                if (cmbLedEffect != null)
                {
                    int idx = Math.Clamp((int)svc.Effect - 1, 0, cmbLedEffect.Items.Count - 1);
                    if (cmbLedEffect.SelectedIndex != idx)
                        cmbLedEffect.SelectedIndex = idx;
                }

                if (numLedBrightness != null)
                {
                    decimal b = Math.Clamp(svc.Brightness, 0, 100);
                    if (numLedBrightness.Value != b)
                        numLedBrightness.Value = b;
                }

                if (numLedSpeed != null)
                {
                    decimal sp = Math.Clamp(svc.Speed, 0, 100);
                    if (numLedSpeed.Value != sp)
                        numLedSpeed.Value = sp;
                    numLedSpeed.Enabled = svc.Effect != LegionGo.RgbMode.Solid;
                }

                if (chkLedSyncLR != null && chkLedSyncLR.Checked != svc.SyncLeftRight)
                {
                    chkLedSyncLR.Checked = svc.SyncLeftRight;
                }

                if (pnlLedSwatchLeft != null)
                {
                    pnlLedSwatchLeft.BackColor = svc.LeftColor;
                }

                if (pnlLedSwatchRight != null)
                {
                    pnlLedSwatchRight.BackColor = svc.RightColor;
                    pnlLedSwatchRight.Visible = !svc.SyncLeftRight;
                }

                if (btnLedCustomRight != null)
                {
                    btnLedCustomRight.Visible = !svc.SyncLeftRight;
                }

                if (lblLedStatus != null)
                {
                    if (!svc.Enabled)
                    {
                        lblLedStatus.Text = "Joystick LEDs are Off.";
                        lblLedStatus.ForeColor = Color.FromArgb(100, 105, 115);
                    }
                    else
                    {
                        string effName = JoystickLedService.GetEffectDisplayName(svc.Effect);
                        string colorDesc = svc.Effect == LegionGo.RgbMode.Dynamic || svc.Effect == LegionGo.RgbMode.Spiral
                            ? $"Brightness {svc.Brightness}%  |  Speed {svc.Speed}%"
                            : svc.SyncLeftRight
                                ? $"RGB({svc.LeftR}, {svc.LeftG}, {svc.LeftB})  |  Brightness {svc.Brightness}%"
                                : $"L: RGB({svc.LeftR},{svc.LeftG},{svc.LeftB}) / R: RGB({svc.RightR},{svc.RightG},{svc.RightB})  |  Brightness {svc.Brightness}%";
                        lblLedStatus.Text = $"Active: {effName}  |  {colorDesc}";
                        lblLedStatus.ForeColor = Color.FromArgb(0, 130, 60);
                    }
                }
            }
            finally
            {
                _updatingLedUi = false;
            }
        }

        private void SetupButtonRow(ButtonFlags btn, ComboBox comboType, ComboBox comboTarget, CheckBox chkRepeat, NumericUpDown numRate)
        {
            var current = CustomMappingService.Instance.GetMapping(btn);

            comboType.SelectedIndex = (int)current.TargetType;
            PopulateTargetDropdown(comboTarget, current.TargetType, current.TargetButton, current.TargetKey, current.TargetAction);

            chkRepeat.Checked = current.HasTurbo;
            numRate.Value = Math.Clamp(current.TurboDelayMs, 10, 1000);

            bool allowRepeat = (current.TargetType == MappingType.Controller || current.TargetType == MappingType.Keyboard);
            chkRepeat.Enabled = allowRepeat;
            numRate.Enabled = allowRepeat && chkRepeat.Checked;

            comboType.SelectedIndexChanged += (s, e) =>
            {
                MappingType newType = (MappingType)comboType.SelectedIndex;
                PopulateTargetDropdown(comboTarget, newType, ButtonFlags.None, VirtualKeyCode.NONAME, CustomActionType.None);
                bool canRepeat = (newType == MappingType.Controller || newType == MappingType.Keyboard);
                chkRepeat.Enabled = canRepeat;
                numRate.Enabled = canRepeat && chkRepeat.Checked;
                SaveRowMapping(btn, comboType, comboTarget, chkRepeat, numRate);
            };

            comboTarget.SelectedIndexChanged += (s, e) =>
            {
                SaveRowMapping(btn, comboType, comboTarget, chkRepeat, numRate);
            };

            chkRepeat.CheckedChanged += (s, e) =>
            {
                numRate.Enabled = chkRepeat.Checked && chkRepeat.Enabled;
                SaveRowMapping(btn, comboType, comboTarget, chkRepeat, numRate);
            };

            numRate.ValueChanged += (s, e) =>
            {
                SaveRowMapping(btn, comboType, comboTarget, chkRepeat, numRate);
            };
        }

        private void PopulateTargetDropdown(ComboBox comboTarget, MappingType type, ButtonFlags selectedBtn, VirtualKeyCode selectedKey, CustomActionType selectedAction)
        {
            comboTarget.BeginUpdate();
            try
            {
                comboTarget.Items.Clear();

                switch (type)
                {
                    case MappingType.None:
                        comboTarget.Items.Add("(Default)");
                        comboTarget.SelectedIndex = 0;
                        comboTarget.Enabled = false;
                        break;

                    case MappingType.Controller:
                        comboTarget.Enabled = true;
                        comboTarget.Items.AddRange(ControllerTargetNames);
                        int selIndex = ControllerTargets.FindIndex(item => item.Flag == selectedBtn);
                        comboTarget.SelectedIndex = selIndex >= 0 ? selIndex : 0;
                        break;

                    case MappingType.Keyboard:
                        comboTarget.Enabled = true;
                        comboTarget.Items.AddRange(KeyboardTargetNames);
                        int keyIndex = KeyboardTargets.FindIndex(item => item.Key == selectedKey);
                        comboTarget.SelectedIndex = keyIndex >= 0 ? keyIndex : 0;
                        break;

                    case MappingType.Action:
                        comboTarget.Enabled = true;
                        var actions = CustomMappingService.AvailableActions;
                        string[] actionNames = new string[actions.Count];
                        int actIndex = 0;
                        for (int i = 0; i < actions.Count; i++)
                        {
                            actionNames[i] = actions[i].Name;
                            if (actions[i].Action == selectedAction)
                                actIndex = i;
                        }
                        comboTarget.Items.AddRange(actionNames);
                        comboTarget.SelectedIndex = actIndex;
                        break;
                }
            }
            finally
            {
                comboTarget.EndUpdate();
            }
        }

        private void SaveRowMapping(ButtonFlags btn, ComboBox comboType, ComboBox comboTarget, CheckBox chkRepeat, NumericUpDown numRate)
        {
            MappingType type = (MappingType)comboType.SelectedIndex;
            ButtonFlags targetBtn = ButtonFlags.None;
            VirtualKeyCode targetKey = VirtualKeyCode.NONAME;
            CustomActionType targetAction = CustomActionType.None;

            if (type == MappingType.Controller && comboTarget.SelectedIndex >= 0 && comboTarget.SelectedIndex < ControllerTargets.Count)
            {
                targetBtn = ControllerTargets[comboTarget.SelectedIndex].Flag;
            }
            else if (type == MappingType.Keyboard && comboTarget.SelectedIndex >= 0 && comboTarget.SelectedIndex < KeyboardTargets.Count)
            {
                targetKey = KeyboardTargets[comboTarget.SelectedIndex].Key;
            }
            else if (type == MappingType.Action && comboTarget.SelectedIndex >= 0 && comboTarget.SelectedIndex < CustomMappingService.AvailableActions.Count)
            {
                targetAction = CustomMappingService.AvailableActions[comboTarget.SelectedIndex].Action;
            }

            bool hasTurbo = chkRepeat.Checked && chkRepeat.Enabled;
            int turboDelay = (int)numRate.Value;

            CustomMappingService.Instance.SetMapping(btn, type, targetBtn, targetKey, targetAction, hasTurbo, turboDelay);
        }

        private void RefreshAllRows()
        {
            foreach (var kv in _mappingControls)
            {
                var btn = kv.Key;
                var (comboType, comboTarget, chkRepeat, numRate) = kv.Value;
                var item = CustomMappingService.Instance.GetMapping(btn);

                comboType.SelectedIndex = (int)item.TargetType;
                PopulateTargetDropdown(comboTarget, item.TargetType, item.TargetButton, item.TargetKey, item.TargetAction);

                chkRepeat.Checked = item.HasTurbo;
                numRate.Value = Math.Clamp(item.TurboDelayMs, 10, 1000);

                bool allowRepeat = (item.TargetType == MappingType.Controller || item.TargetType == MappingType.Keyboard);
                chkRepeat.Enabled = allowRepeat;
                numRate.Enabled = allowRepeat && chkRepeat.Checked;
            }

            // Refresh hold cycle controls
            var svc = CustomMappingService.Instance;
            if (chkHoldCycleEnabled != null) chkHoldCycleEnabled.Checked = svc.HoldCycleEnabled;
            if (numHoldTime != null) numHoldTime.Value = Math.Clamp(svc.HoldCycleDurationMs, 200, 2000);
            if (chkCycleDesktop != null) chkCycleDesktop.Checked = svc.CycleDesktop;
            if (chkCycleX360 != null) chkCycleX360.Checked = svc.CycleX360;
            if (chkCycleDS4 != null) chkCycleDS4.Checked = svc.CycleDS4;
            if (chkCycleNative != null) chkCycleNative.Checked = svc.CycleNative;
            if (cmbTriggerBtn != null)
            {
                int idx = Array.IndexOf(CustomMappingService.RemappableButtons, svc.HoldCycleButton);
                cmbTriggerBtn.SelectedIndex = idx >= 0 ? idx + 1 : 0;
            }
            UpdateHoldCycleControlStates();
            UpdateCycleSummary();
        }

        private void InitializeHoldCycleControls()
        {
            if (chkHoldCycleEnabled == null || cmbTriggerBtn == null || numHoldTime == null ||
                chkCycleDesktop == null || chkCycleX360 == null || chkCycleDS4 == null || chkCycleNative == null)
                return;

            var svc = CustomMappingService.Instance;

            chkHoldCycleEnabled.Checked = svc.HoldCycleEnabled;
            numHoldTime.Value = Math.Clamp(svc.HoldCycleDurationMs, 200, 2000);
            chkCycleDesktop.Checked = svc.CycleDesktop;
            chkCycleX360.Checked = svc.CycleX360;
            chkCycleDS4.Checked = svc.CycleDS4;
            chkCycleNative.Checked = svc.CycleNative;
            if (chkCyclePointer != null) chkCyclePointer.Checked = svc.CyclePointer;

            if (svc.HoldCycleButton == ButtonFlags.None)
            {
                cmbTriggerBtn.SelectedIndex = 0;
            }
            else
            {
                int idx = Array.IndexOf(CustomMappingService.RemappableButtons, svc.HoldCycleButton);
                cmbTriggerBtn.SelectedIndex = idx >= 0 ? idx + 1 : 0;
            }

            UpdateHoldCycleControlStates();
            UpdateCycleSummary();

            chkHoldCycleEnabled.CheckedChanged += (s, e) =>
            {
                UpdateHoldCycleControlStates();
                SaveHoldCycleConfig();
            };

            cmbTriggerBtn.SelectedIndexChanged += (s, e) => SaveHoldCycleConfig();
            numHoldTime.ValueChanged += (s, e) => SaveHoldCycleConfig();

            Action<CheckBox> onModeCheckChanged = (cb) =>
            {
                int count = (chkCycleDesktop.Checked ? 1 : 0) +
                            (chkCycleX360.Checked ? 1 : 0) +
                            (chkCycleDS4.Checked ? 1 : 0) +
                            (chkCycleNative.Checked ? 1 : 0) +
                            (chkCyclePointer?.Checked == true ? 1 : 0);
                if (count < 2)
                {
                    cb.Checked = true;
                    MessageBox.Show("Please keep at least 2 modes selected for cycling.", "Cycle Controller Modes", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                UpdateCycleSummary();
                SaveHoldCycleConfig();
            };

            chkCycleDesktop.CheckedChanged += (s, e) => onModeCheckChanged(chkCycleDesktop);
            chkCycleX360.CheckedChanged += (s, e) => onModeCheckChanged(chkCycleX360);
            chkCycleDS4.CheckedChanged += (s, e) => onModeCheckChanged(chkCycleDS4);
            chkCycleNative.CheckedChanged += (s, e) => onModeCheckChanged(chkCycleNative);
            if (chkCyclePointer != null) chkCyclePointer.CheckedChanged += (s, e) => onModeCheckChanged(chkCyclePointer);
        }

        private void UpdateHoldCycleControlStates()
        {
            if (chkHoldCycleEnabled == null) return;
            bool enabled = chkHoldCycleEnabled.Checked;
            if (cmbTriggerBtn != null) cmbTriggerBtn.Enabled = enabled;
            if (numHoldTime != null) numHoldTime.Enabled = enabled;
            if (chkCycleDesktop != null) chkCycleDesktop.Enabled = enabled;
            if (chkCycleX360 != null) chkCycleX360.Enabled = enabled;
            if (chkCycleDS4 != null) chkCycleDS4.Enabled = enabled;
            if (chkCycleNative != null) chkCycleNative.Enabled = enabled;
            if (chkCyclePointer != null) chkCyclePointer.Enabled = enabled;
        }

        private void UpdateCycleSummary()
        {
            if (lblCycleSummary == null || chkCycleDesktop == null || chkCycleX360 == null || chkCycleDS4 == null || chkCycleNative == null)
                return;

            var modes = new List<string>();
            if (chkCycleDesktop.Checked) modes.Add("Desktop");
            if (chkCycleDS4.Checked) modes.Add("DS4");
            if (chkCycleX360.Checked) modes.Add("x360");
            if (chkCycleNative.Checked) modes.Add("Native");
            if (chkCyclePointer?.Checked == true) modes.Add("Pointer");

            if (modes.Count >= 2)
            {
                lblCycleSummary.Text = "Cycle sequence: " + string.Join(" -> ", modes) + " -> " + modes[0];
                lblCycleSummary.ForeColor = Color.FromArgb(0, 100, 180);
            }
            else
            {
                lblCycleSummary.Text = "Select at least 2 modes to cycle between.";
                lblCycleSummary.ForeColor = Color.FromArgb(180, 50, 50);
            }
        }

        private void SaveHoldCycleConfig()
        {
            if (chkHoldCycleEnabled == null || cmbTriggerBtn == null || numHoldTime == null ||
                chkCycleDesktop == null || chkCycleX360 == null || chkCycleDS4 == null || chkCycleNative == null)
                return;

            ButtonFlags selectedBtn = ButtonFlags.None;
            if (cmbTriggerBtn.SelectedIndex > 0 && cmbTriggerBtn.SelectedIndex - 1 < CustomMappingService.RemappableButtons.Length)
            {
                selectedBtn = CustomMappingService.RemappableButtons[cmbTriggerBtn.SelectedIndex - 1];
            }

            CustomMappingService.Instance.SetHoldCycleConfig(
                chkHoldCycleEnabled.Checked,
                selectedBtn,
                (int)numHoldTime.Value,
                chkCycleDesktop.Checked,
                chkCycleX360.Checked,
                chkCycleDS4.Checked,
                chkCycleNative.Checked,
                chkCyclePointer?.Checked ?? false
            );
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (this.Visible)
            {
                UpdateGyroStatus();
                UpdateLedControlsFromService();
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RefreshControlSizes();
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            RefreshControlSizes();
        }

        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            base.ScaleControl(factor, specified);
            RefreshControlSizes();
        }

        private float _lastAppliedScale = -1f;

        public void RefreshControlSizes()
        {
            if (this.IsDisposed) return;

            this.DoubleBuffered = true;
            float scale = GeneralView.GetUiScaleFactor();
            if (Math.Abs(scale - _lastAppliedScale) < 0.001f)
                return;

            _lastAppliedScale = scale;

            this.SuspendLayout();
            try
            {
                foreach (var spec in _managedButtons)
                {
                    if (spec.Button == null || spec.Button.IsDisposed) continue;

                    int scaledW = (int)Math.Round(spec.BaseWidth * scale);
                    int scaledH = (int)Math.Round(spec.BaseHeight * scale);

                    spec.Button.Size = new Size(scaledW, scaledH);
                    bool isBold = spec.Button.Font?.Bold ?? false;
                    float scaledFontSize = spec.BaseFontSize * (1.0f + (scale - 1.0f) * 0.60f);
                    spec.Button.Font = new Font("Segoe UI", scaledFontSize, isBold ? FontStyle.Bold : FontStyle.Regular);
                }

                // Scale GroupBox headers
                float headerFontSize = 8.5f * (1.0f + (scale - 1.0f) * 0.50f);
                if (grpGyro != null) grpGyro.Font = new Font("Segoe UI", headerFontSize, FontStyle.Bold);
                if (grpLed != null) grpLed.Font = new Font("Segoe UI", headerFontSize, FontStyle.Bold);
                if (grpPointer != null) grpPointer.Font = new Font("Segoe UI", headerFontSize, FontStyle.Bold);
                if (grpHoldCycle != null) grpHoldCycle.Font = new Font("Segoe UI", headerFontSize, FontStyle.Bold);
                if (grpRemap != null) grpRemap.Font = new Font("Segoe UI", headerFontSize, FontStyle.Bold);

                float bodyFontSize = 8.5f * (1.0f + (scale - 1.0f) * 0.50f);
                float smallFontSize = 8.0f * (1.0f + (scale - 1.0f) * 0.50f);

                if (lblGyroStatus != null) lblGyroStatus.Font = new Font("Segoe UI", smallFontSize, FontStyle.Regular);
                if (lblLedStatus != null) lblLedStatus.Font = new Font("Segoe UI", smallFontSize, FontStyle.Regular);

                // Scale Joystick LED controls
                if (cmbLedEffect != null)
                {
                    cmbLedEffect.Font = new Font("Segoe UI", bodyFontSize, FontStyle.Regular);
                    cmbLedEffect.Width = (int)Math.Round(145 * scale);
                }
                if (numLedBrightness != null)
                {
                    numLedBrightness.Font = new Font("Segoe UI", bodyFontSize, FontStyle.Regular);
                    numLedBrightness.Width = (int)Math.Round(54 * scale);
                }
                if (numLedSpeed != null)
                {
                    numLedSpeed.Font = new Font("Segoe UI", bodyFontSize, FontStyle.Regular);
                    numLedSpeed.Width = (int)Math.Round(54 * scale);
                }
                if (chkLedSyncLR != null) chkLedSyncLR.Font = new Font("Segoe UI", bodyFontSize, FontStyle.Regular);
                if (pnlLedSwatchLeft != null) pnlLedSwatchLeft.Size = new Size((int)Math.Round(24 * scale), (int)Math.Round(24 * scale));
                if (pnlLedSwatchRight != null) pnlLedSwatchRight.Size = new Size((int)Math.Round(24 * scale), (int)Math.Round(24 * scale));

                // Scale Pointer Mode controls
                if (chkPointerDetachedOnly != null) chkPointerDetachedOnly.Font = new Font("Segoe UI", bodyFontSize, FontStyle.Regular);
                if (chkPointerRightClickRB != null) chkPointerRightClickRB.Font = new Font("Segoe UI", bodyFontSize, FontStyle.Regular);
                if (chkPointerInvertX != null) chkPointerInvertX.Font = new Font("Segoe UI", bodyFontSize, FontStyle.Regular);
                if (chkPointerInvertY != null) chkPointerInvertY.Font = new Font("Segoe UI", bodyFontSize, FontStyle.Regular);
                if (cmbPointerButton != null)
                {
                    cmbPointerButton.Font = new Font("Segoe UI", bodyFontSize, FontStyle.Regular);
                    cmbPointerButton.Width = (int)Math.Round(155 * scale);
                }
                if (cmbPointerType != null)
                {
                    cmbPointerType.Font = new Font("Segoe UI", bodyFontSize, FontStyle.Regular);
                    cmbPointerType.Width = (int)Math.Round(110 * scale);
                }
                if (numPointerSens != null)
                {
                    numPointerSens.Font = new Font("Segoe UI", bodyFontSize, FontStyle.Regular);
                    numPointerSens.Width = (int)Math.Round(55 * scale);
                }
                if (lblPointerStatus != null) lblPointerStatus.Font = new Font("Segoe UI", smallFontSize, FontStyle.Regular);

                // Scale Hold to Cycle controls
                if (chkHoldCycleEnabled != null) chkHoldCycleEnabled.Font = new Font("Segoe UI", bodyFontSize, FontStyle.Regular);
                if (cmbTriggerBtn != null)
                {
                    cmbTriggerBtn.Font = new Font("Segoe UI", bodyFontSize, FontStyle.Regular);
                    cmbTriggerBtn.Width = (int)Math.Round(160 * scale);
                }
                if (numHoldTime != null)
                {
                    numHoldTime.Font = new Font("Segoe UI", bodyFontSize, FontStyle.Regular);
                    numHoldTime.Width = (int)Math.Round(55 * scale);
                }
                if (chkCycleDesktop != null) chkCycleDesktop.Font = new Font("Segoe UI", bodyFontSize, FontStyle.Regular);
                if (chkCycleX360 != null) chkCycleX360.Font = new Font("Segoe UI", bodyFontSize, FontStyle.Regular);
                if (chkCycleDS4 != null) chkCycleDS4.Font = new Font("Segoe UI", bodyFontSize, FontStyle.Regular);
                if (chkCycleNative != null) chkCycleNative.Font = new Font("Segoe UI", bodyFontSize, FontStyle.Regular);
                if (chkCyclePointer != null) chkCyclePointer.Font = new Font("Segoe UI", bodyFontSize, FontStyle.Regular);
                if (lblCycleSummary != null) lblCycleSummary.Font = new Font("Segoe UI", smallFontSize, FontStyle.Bold);

                // Scale Remapping dropdowns and steppers
                foreach (var (typeCombo, targetCombo, chkRepeat, numRate) in _mappingControls.Values)
                {
                    if (typeCombo != null) typeCombo.Font = new Font("Segoe UI", bodyFontSize, FontStyle.Regular);
                    if (targetCombo != null) targetCombo.Font = new Font("Segoe UI", bodyFontSize, FontStyle.Regular);
                    if (chkRepeat != null) chkRepeat.Font = new Font("Segoe UI", smallFontSize, FontStyle.Regular);
                    if (numRate != null)
                    {
                        numRate.Font = new Font("Segoe UI", smallFontSize, FontStyle.Regular);
                        numRate.Width = (int)Math.Round(46 * scale);
                    }
                }

                ScaleChildLabels(grpLed, scale);
                ScaleChildLabels(grpPointer, scale);
                ScaleChildLabels(grpHoldCycle, scale);
                ScaleChildLabels(grpRemap, scale);
            }
            finally
            {
                this.ResumeLayout(true);
            }
        }

        private static void ScaleChildLabels(Control? parent, float scale)
        {
            if (parent == null) return;
            float bodyFontSize = 8.5f * (1.0f + (scale - 1.0f) * 0.50f);
            foreach (Control c in parent.Controls)
            {
                if (c is Label lbl && c.Name != "lblCycleSummary" && c.Name != "lblGyroStatus" && c.Name != "lblLedStatus")
                {
                    lbl.Font = new Font("Segoe UI", bodyFontSize, FontStyle.Regular);
                }
                else if (c.HasChildren)
                {
                    ScaleChildLabels(c, scale);
                }
            }
        }

        private Button CreateCompactButton(string text, int width, int height, float fontSize, Action onClick)
        {
            Button btn = new Button
            {
                Text = text,
                Size = new Size(width, height),
                Font = new Font("Segoe UI", fontSize, FontStyle.Regular),
                Margin = new Padding(3),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(245, 247, 250),
                ForeColor = Color.FromArgb(30, 35, 45)
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(210, 215, 222);
            btn.Click += (s, e) =>
            {
                try
                {
                    onClick();
                }
                catch (Exception ex)
                {
                    LogManager.LogError("Controller action failed: {0}", ex.Message);
                    MessageBox.Show(ex.Message, "Controller Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            _managedButtons.Add(new ManagedButtonSpec { Button = btn, BaseWidth = width, BaseHeight = height, BaseFontSize = fontSize });
            return btn;
        }

        private void UpdateGyroStatus()
        {
            if (lblGyroStatus == null) return;
            bool enabled = ControllerManager.GyroAimingEnabled;
            SetButtonActive(btnGyroOn, enabled);
            SetButtonActive(btnGyroOff, !enabled);

            bool isDesktop = ManagerFactory.layoutManager?.GetCurrentMode() == LayoutModes.Desktop;
            bool isDS4 = VirtualManager.HIDmode == HIDmode.DualShock4Controller;

            if (!enabled)
            {
                lblGyroStatus.Text = "Gyro Aiming is Off.";
                lblGyroStatus.ForeColor = Color.FromArgb(100, 105, 115);
            }
            else if (isDesktop)
            {
                lblGyroStatus.Text = "Gyro Aiming is On (Disabled in Desktop mode).";
                lblGyroStatus.ForeColor = Color.FromArgb(180, 100, 20);
            }
            else if (!isDS4)
            {
                lblGyroStatus.Text = "Gyro Aiming is On (Disabled: only works when DS4 is active).";
                lblGyroStatus.ForeColor = Color.FromArgb(180, 100, 20);
            }
            else
            {
                lblGyroStatus.Text = "Gyro Aiming is Active (DualShock 4 motion enabled).";
                lblGyroStatus.ForeColor = Color.FromArgb(0, 130, 60);
            }
        }

        private static void SetButtonActive(Button? btn, bool isActive)
        {
            if (btn == null || btn.IsDisposed) return;
            float currentSize = btn.Font?.Size > 0 ? btn.Font.Size : 9.0F;
            btn.Font = new Font(btn.Font?.FontFamily ?? new FontFamily("Segoe UI"), currentSize, isActive ? FontStyle.Bold : FontStyle.Regular);
            btn.BackColor = isActive ? Color.FromArgb(0, 120, 215) : Color.FromArgb(245, 247, 250);
            btn.ForeColor = isActive ? Color.White : Color.FromArgb(30, 35, 45);
            btn.FlatAppearance.BorderColor = isActive ? Color.FromArgb(0, 95, 175) : Color.FromArgb(210, 215, 222);
        }
    }
}
