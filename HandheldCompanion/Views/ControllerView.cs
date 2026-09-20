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
        private GroupBox? grpHoldCycle;
        private GroupBox? grpRemap;

        // Hold to Cycle controls
        private CheckBox? chkHoldCycleEnabled;
        private ComboBox? cmbTriggerBtn;
        private NumericUpDown? numHoldTime;
        private CheckBox? chkCycleDesktop;
        private CheckBox? chkCycleX360;
        private CheckBox? chkCycleDS4;
        private CheckBox? chkCycleNative;
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
                Dock = DockStyle.Top,
                AutoSize = true,
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                ForeColor = Color.FromArgb(70, 75, 85),
                Padding = new Padding(4, 4, 4, 4)
            };
            grpGyro.Controls.Add(lblGyroStatus);

            // ==========================================
            // 2. Hold to Cycle Controller Modes GroupBox
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

            flowModes.Controls.Add(lblIncluded);
            flowModes.Controls.Add(chkCycleDesktop);
            flowModes.Controls.Add(chkCycleX360);
            flowModes.Controls.Add(chkCycleDS4);
            flowModes.Controls.Add(chkCycleNative);

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
            mainScrollPanel.Controls.Add(grpGyro);

            this.Controls.Add(mainScrollPanel);

            UpdateGyroStatus();

            this.ResumeLayout(false);
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
                            (chkCycleNative.Checked ? 1 : 0);
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
                chkCycleNative.Checked
            );
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (this.Visible)
            {
                UpdateGyroStatus();
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

        public void RefreshControlSizes()
        {
            if (this.IsDisposed) return;

            float scale = GeneralView.GetUiScaleFactor();

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
                if (grpHoldCycle != null) grpHoldCycle.Font = new Font("Segoe UI", headerFontSize, FontStyle.Bold);
                if (grpRemap != null) grpRemap.Font = new Font("Segoe UI", headerFontSize, FontStyle.Bold);

                float bodyFontSize = 8.5f * (1.0f + (scale - 1.0f) * 0.50f);
                float smallFontSize = 8.0f * (1.0f + (scale - 1.0f) * 0.50f);

                if (lblGyroStatus != null) lblGyroStatus.Font = new Font("Segoe UI", smallFontSize, FontStyle.Regular);

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
                if (c is Label lbl && c.Name != "lblCycleSummary" && c.Name != "lblGyroStatus")
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
