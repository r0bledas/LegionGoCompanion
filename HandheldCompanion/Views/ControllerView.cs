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

        public ControllerView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            this.AutoScaleMode = AutoScaleMode.Inherit;
            this.Dock = DockStyle.Fill;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            // Main scrollable content panel
            Panel mainScrollPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(10, 8, 10, 8)
            };

            // ==========================================
            // 1. Gyro Aiming GroupBox
            // ==========================================
            GroupBox grpGyro = new GroupBox
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

            btnGyroOn = CreateCompactButton("On", 50, 26, () =>
            {
                ControllerManager.GyroAimingEnabled = true;
                UpdateGyroStatus();
            });

            btnGyroOff = CreateCompactButton("Off", 50, 26, () =>
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
            // 2. Button Remapping GroupBox
            // ==========================================
            GroupBox grpRemap = new GroupBox
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
            tableRemap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
            tableRemap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22F));
            tableRemap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26F));
            tableRemap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24F));

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
                Size = new Size(80, 26),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(245, 247, 250),
                ForeColor = Color.FromArgb(40, 45, 55)
            };
            btnReset.FlatAppearance.BorderColor = Color.FromArgb(210, 215, 222);
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
                    int selIndex = 0;
                    for (int i = 0; i < ControllerTargets.Count; i++)
                    {
                        var item = ControllerTargets[i];
                        comboTarget.Items.Add(item.Name);
                        if (item.Flag == selectedBtn)
                            selIndex = i;
                    }
                    comboTarget.SelectedIndex = selIndex;
                    break;

                case MappingType.Keyboard:
                    comboTarget.Enabled = true;
                    int keyIndex = 0;
                    for (int i = 0; i < KeyboardTargets.Count; i++)
                    {
                        var item = KeyboardTargets[i];
                        comboTarget.Items.Add(item.Name);
                        if (item.Key == selectedKey)
                            keyIndex = i;
                    }
                    comboTarget.SelectedIndex = keyIndex;
                    break;

                case MappingType.Action:
                    comboTarget.Enabled = true;
                    int actIndex = 0;
                    for (int i = 0; i < CustomMappingService.AvailableActions.Count; i++)
                    {
                        var item = CustomMappingService.AvailableActions[i];
                        comboTarget.Items.Add(item.Name);
                        if (item.Action == selectedAction)
                            actIndex = i;
                    }
                    comboTarget.SelectedIndex = actIndex;
                    break;
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
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (this.Visible)
            {
                UpdateGyroStatus();
            }
        }

        private Button CreateCompactButton(string text, int width, int height, Action onClick)
        {
            Button btn = new Button
            {
                Text = text,
                Size = new Size(width, height),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
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
            if (btn == null) return;
            btn.Font = new Font("Segoe UI", 8.5F, isActive ? FontStyle.Bold : FontStyle.Regular);
            btn.BackColor = isActive ? Color.FromArgb(0, 120, 215) : Color.FromArgb(245, 247, 250);
            btn.ForeColor = isActive ? Color.White : Color.FromArgb(30, 35, 45);
            btn.FlatAppearance.BorderColor = isActive ? Color.FromArgb(0, 95, 175) : Color.FromArgb(210, 215, 222);
        }
    }
}
