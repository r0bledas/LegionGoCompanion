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
        private Label lblStatus;
        private Button btnDS4;
        private Button btnX360;
        private Button btnNative;

        // ComboBox references for live updates and reset
        private readonly Dictionary<ButtonFlags, (ComboBox typeCombo, ComboBox targetCombo)> _mappingControls = new();

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
            // 1. Controller Emulation Mode GroupBox
            // ==========================================
            GroupBox grpEmulation = new GroupBox
            {
                Text = "Emulation Target & Gyro Aiming",
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Padding = new Padding(8, 6, 8, 8),
                Margin = new Padding(0, 0, 0, 6)
            };

            FlowLayoutPanel flowEmulation = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Padding = new Padding(2)
            };

            btnDS4 = CreateCompactButton("DualShock 4 (Gyro Active)", 160, 28, async () =>
            {
                PlayDisconnectSound();
                ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", true);
                ControllerManager.TargetController?.Hide(false);
                SetLegionPassthrough(false);
                ManagerFactory.layoutManager.SetLayoutMode(LayoutModes.Gamepad);
                bool modeChanged = await VirtualManager.SetControllerMode(HIDmode.DualShock4Controller);
                await VirtualManager.SetControllerStatus(HIDstatus.Connected);
                PlayConnectSound();
                UpdateActiveHighlight();
                lblStatus.Text = "Active: DualShock 4 (IMU Gyro Enabled for Fortnite / Aiming)";
            });

            btnX360 = CreateCompactButton("Xbox 360 (Standard XInput)", 160, 28, async () =>
            {
                PlayDisconnectSound();
                ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", true);
                ControllerManager.TargetController?.Hide(false);
                SetLegionPassthrough(false);
                ManagerFactory.layoutManager.SetLayoutMode(LayoutModes.Gamepad);
                bool modeChanged = await VirtualManager.SetControllerMode(HIDmode.Xbox360Controller);
                await VirtualManager.SetControllerStatus(HIDstatus.Connected);
                PlayConnectSound();
                UpdateActiveHighlight();
                lblStatus.Text = "Active: Xbox 360 (Standard XInput Controller)";
            });

            btnNative = CreateCompactButton("Native Passthrough", 140, 28, async () =>
            {
                PlayDisconnectSound();
                await VirtualManager.SetControllerMode(HIDmode.NoController);
                await VirtualManager.SetControllerStatus(HIDstatus.Disconnected);
                ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", false);
                ControllerManager.TargetController?.Unhide(false);
                SetLegionPassthrough(true);
                ManagerFactory.layoutManager.SetLayoutMode(LayoutModes.Gamepad);
                PlayConnectSound();
                UpdateActiveHighlight();
                lblStatus.Text = "Active: Direct Hardware Passthrough (Uncloaked)";
            });

            flowEmulation.Controls.Add(btnDS4);
            flowEmulation.Controls.Add(btnX360);
            flowEmulation.Controls.Add(btnNative);
            grpEmulation.Controls.Add(flowEmulation);

            this.lblStatus = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(70, 75, 85),
                Padding = new Padding(4, 4, 4, 4),
                Text = "Controller status: " + (VirtualManager.HIDmode != HIDmode.NoController ? VirtualManager.HIDmode.ToString() : "Passthrough")
            };
            grpEmulation.Controls.Add(this.lblStatus);

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
                ColumnCount = 3,
                Padding = new Padding(4)
            };
            tableRemap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            tableRemap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
            tableRemap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));

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

                ComboBox comboTarget = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                    Dock = DockStyle.Fill,
                    Height = 26,
                    Margin = new Padding(2, 3, 2, 3)
                };

                // Populate and bind
                SetupButtonRow(btn, comboType, comboTarget);

                tableRemap.Controls.Add(lblBtn, 0, row);
                tableRemap.Controls.Add(comboType, 1, row);
                tableRemap.Controls.Add(comboTarget, 2, row);

                _mappingControls[btn] = (comboType, comboTarget);
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
                Text = "Reset All Remappings",
                Size = new Size(150, 26),
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
            mainScrollPanel.Controls.Add(grpEmulation);

            this.Controls.Add(mainScrollPanel);

            UpdateActiveHighlight();

            this.ResumeLayout(false);
        }

        private void SetupButtonRow(ButtonFlags btn, ComboBox comboType, ComboBox comboTarget)
        {
            var current = CustomMappingService.Instance.GetMapping(btn);

            comboType.SelectedIndex = (int)current.TargetType;
            PopulateTargetDropdown(comboTarget, current.TargetType, current.TargetButton, current.TargetKey);

            comboType.SelectedIndexChanged += (s, e) =>
            {
                MappingType newType = (MappingType)comboType.SelectedIndex;
                PopulateTargetDropdown(comboTarget, newType, ButtonFlags.None, VirtualKeyCode.NONAME);
                SaveRowMapping(btn, comboType, comboTarget);
            };

            comboTarget.SelectedIndexChanged += (s, e) =>
            {
                SaveRowMapping(btn, comboType, comboTarget);
            };
        }

        private void PopulateTargetDropdown(ComboBox comboTarget, MappingType type, ButtonFlags selectedBtn, VirtualKeyCode selectedKey)
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
            }
        }

        private void SaveRowMapping(ButtonFlags btn, ComboBox comboType, ComboBox comboTarget)
        {
            MappingType type = (MappingType)comboType.SelectedIndex;
            ButtonFlags targetBtn = ButtonFlags.None;
            VirtualKeyCode targetKey = VirtualKeyCode.NONAME;

            if (type == MappingType.Controller && comboTarget.SelectedIndex >= 0 && comboTarget.SelectedIndex < ControllerTargets.Count)
            {
                targetBtn = ControllerTargets[comboTarget.SelectedIndex].Flag;
            }
            else if (type == MappingType.Keyboard && comboTarget.SelectedIndex >= 0 && comboTarget.SelectedIndex < KeyboardTargets.Count)
            {
                targetKey = KeyboardTargets[comboTarget.SelectedIndex].Key;
            }

            CustomMappingService.Instance.SetMapping(btn, type, targetBtn, targetKey);
        }

        private void RefreshAllRows()
        {
            foreach (var kv in _mappingControls)
            {
                var btn = kv.Key;
                var (comboType, comboTarget) = kv.Value;
                var item = CustomMappingService.Instance.GetMapping(btn);

                comboType.SelectedIndex = (int)item.TargetType;
                PopulateTargetDropdown(comboTarget, item.TargetType, item.TargetButton, item.TargetKey);
            }
        }

        private Button CreateCompactButton(string text, int width, int height, Func<Task> onClick)
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
            btn.Click += async (s, e) =>
            {
                try
                {
                    await onClick();
                }
                catch (Exception ex)
                {
                    LogManager.LogError("Controller action failed: {0}", ex.Message);
                    MessageBox.Show(ex.Message, "Controller Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            return btn;
        }

        private void UpdateActiveHighlight()
        {
            SetButtonActive(btnDS4, VirtualManager.HIDmode == HIDmode.DualShock4Controller);
            SetButtonActive(btnX360, VirtualManager.HIDmode == HIDmode.Xbox360Controller);
            SetButtonActive(btnNative, VirtualManager.HIDmode == HIDmode.NoController);
        }

        private static void SetButtonActive(Button? btn, bool isActive)
        {
            if (btn == null) return;
            btn.Font = new Font("Segoe UI", 8.5F, isActive ? FontStyle.Bold : FontStyle.Regular);
            btn.BackColor = isActive ? Color.FromArgb(0, 120, 215) : Color.FromArgb(245, 247, 250);
            btn.ForeColor = isActive ? Color.White : Color.FromArgb(30, 35, 45);
            btn.FlatAppearance.BorderColor = isActive ? Color.FromArgb(0, 95, 175) : Color.FromArgb(210, 215, 222);
        }

        private static void SetLegionPassthrough(bool enabled)
        {
            if (IDevice.GetCurrent() is LegionGo lego)
                lego.SetPassthrough(enabled);
        }

        private void PlayDisconnectSound()
        {
            try { SystemSounds.Asterisk.Play(); } catch { }
        }

        private void PlayConnectSound()
        {
            try { SystemSounds.Exclamation.Play(); } catch { }
        }
    }
}
