using System;
using System.Drawing;
using System.Media;
using System.Threading.Tasks;
using System.Windows.Forms;
using HandheldCompanion.Devices;
using HandheldCompanion.Devices.Lenovo;
using HandheldCompanion.Managers;
using HandheldCompanion.Shared;
using HandheldCompanion.Utils;

namespace HandheldCompanion.Views
{
    public class GeneralView : UserControl
    {
        private Label lblStatus;

        // Controller buttons
        private Button btnDS4;
        private Button btnX360;
        private Button btnNative;
        private Button btnDesktop;

        // TDP buttons
        private Button[] tdpButtons;

        // Fan buttons
        private Button btnFanAuto;
        private Button btnFanFull;
        private Button[] fanSpeedButtons;

        public GeneralView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            this.Dock = DockStyle.Fill;
            this.AutoScroll = true;
            this.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);

            // Status Bar at the top
            Panel statusPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = LogicalToDeviceUnits(34),
                Padding = new Padding(LogicalToDeviceUnits(12), LogicalToDeviceUnits(6), LogicalToDeviceUnits(12), 0)
            };

            this.lblStatus = new Label
            {
                Text = "Ready | Legion Go Companion",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                AutoSize = true,
                Dock = DockStyle.Left
            };
            statusPanel.Controls.Add(this.lblStatus);

            // Main Content Layout: Table with 3 compact GroupBoxes
            TableLayoutPanel tableLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(10, 0, 10, 10),
                AutoScroll = true
            };
            tableLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tableLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tableLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // ==========================================
            // 1. Controller GroupBox
            // ==========================================
            GroupBox grpController = new GroupBox
            {
                Text = "Controller Emulation & Layout",
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(8),
                Margin = new Padding(0, 4, 0, 8)
            };

            FlowLayoutPanel flowController = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = true,
                Padding = new Padding(4)
            };

            btnDS4 = CreateCompactButton("DS4", 78, 30, async () =>
            {
                lblStatus.Text = "Switching controller to DS4...";
                PlayDisconnectSound();
                // 1. Cloak native physical controller via HidHide so games ONLY see the virtual DS4
                ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", true);
                ControllerManager.TargetController?.Hide(false);
                SetLegionPassthrough(false);
                // 2. Switch layout to Gamepad
                ManagerFactory.layoutManager.SetLayoutMode(LayoutModes.Gamepad);
                // 3. Connect virtual DualShock 4
                bool modeChanged = await VirtualManager.SetControllerMode(HIDmode.DualShock4Controller);
                await VirtualManager.SetControllerStatus(HIDstatus.Connected);
                PlayConnectSound();
                HighlightControllerButton(btnDS4);
                lblStatus.Text = "Active Controller: DS4 (DualShock 4 / Gyro active; native hidden)";
            });

            btnX360 = CreateCompactButton("x360", 78, 30, async () =>
            {
                lblStatus.Text = "Switching controller to x360...";
                PlayDisconnectSound();
                // 1. Cloak native physical controller via HidHide so games ONLY see the virtual x360
                ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", true);
                ControllerManager.TargetController?.Hide(false);
                SetLegionPassthrough(false);
                // 2. Switch layout to Gamepad
                ManagerFactory.layoutManager.SetLayoutMode(LayoutModes.Gamepad);
                // 3. Connect virtual Xbox 360
                bool modeChanged = await VirtualManager.SetControllerMode(HIDmode.Xbox360Controller);
                await VirtualManager.SetControllerStatus(HIDstatus.Connected);
                PlayConnectSound();
                HighlightControllerButton(btnX360);
                lblStatus.Text = "Active Controller: x360 (Standard XInput; native hidden)";
            });

            btnNative = CreateCompactButton("Native", 78, 30, async () =>
            {
                lblStatus.Text = "Switching controller to Native Passthrough...";
                PlayDisconnectSound();
                // 1. Disconnect any virtual controller
                await VirtualManager.SetControllerMode(HIDmode.NoController);
                await VirtualManager.SetControllerStatus(HIDstatus.Disconnected);
                // 2. Uncloak native controller so games can directly access it
                ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", false);
                ControllerManager.TargetController?.Unhide(false);
                SetLegionPassthrough(true);
                ManagerFactory.layoutManager.SetLayoutMode(LayoutModes.Gamepad);
                PlayConnectSound();
                HighlightControllerButton(btnNative);
                lblStatus.Text = "Active Controller: Native Passthrough (Direct Legion Go hardware exposed)";
            });

            btnDesktop = CreateCompactButton("Desktop", 78, 30, async () =>
            {
                lblStatus.Text = "Enabling Desktop Mode (Stick to Mouse)...";
                PlayConnectSound();
                // 1. Cloak native controller from games so games do NOT see an active controller
                ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", true);
                ControllerManager.TargetController?.Hide(false);
                SetLegionPassthrough(false);
                // 2. Disconnect virtual controller completely so Windows/games detect NO gamepads
                await VirtualManager.SetControllerMode(HIDmode.NoController);
                await VirtualManager.SetControllerStatus(HIDstatus.Disconnected);
                // 3. Switch layout to Desktop (sticks & buttons feed exclusively into mouse cursor & keyboard clicks)
                ManagerFactory.layoutManager.SetLayoutMode(LayoutModes.Desktop);
                HighlightControllerButton(btnDesktop);
                lblStatus.Text = "Active Mode: Desktop (No gamepad exposed; Stick: Mouse, Triggers: Click, Left Stick: Scroll)";
            });

            flowController.Controls.Add(btnDS4);
            flowController.Controls.Add(btnX360);
            flowController.Controls.Add(btnNative);
            flowController.Controls.Add(btnDesktop);
            grpController.Controls.Add(flowController);

            // ==========================================
            // 2. TDP & Power GroupBox
            // ==========================================
            GroupBox grpTdp = new GroupBox
            {
                Text = "TDP Power Limit (Watts)",
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(8),
                Margin = new Padding(0, 4, 0, 8)
            };

            FlowLayoutPanel flowTdp = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = true,
                Padding = new Padding(4)
            };

            int[] tdpValues = new int[] { 5, 8, 10, 15, 20, 25, 30, 35, 40 };
            tdpButtons = new Button[tdpValues.Length];

            for (int i = 0; i < tdpValues.Length; i++)
            {
                int wattage = tdpValues[i];
                Button b = CreateCompactButton(wattage + "W", 56, 30, () => ApplyTdp(wattage));
                tdpButtons[i] = b;
                flowTdp.Controls.Add(b);
            }
            grpTdp.Controls.Add(flowTdp);

            // ==========================================
            // 3. Fan Control GroupBox
            // ==========================================
            GroupBox grpFan = new GroupBox
            {
                Text = "Fan & Thermal Control",
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(8),
                Margin = new Padding(0, 4, 0, 8)
            };

            FlowLayoutPanel flowFan = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = true,
                Padding = new Padding(4)
            };

            btnFanAuto = CreateCompactButton("Auto", 65, 30, () => ApplyFanMode("Auto (Balanced)", 0, false, true, btnFanAuto));
            btnFanFull = CreateCompactButton("100% Full", 75, 30, () => ApplyFanMode("Full Speed (100%)", 100, true, false, btnFanFull));

            flowFan.Controls.Add(btnFanAuto);
            flowFan.Controls.Add(btnFanFull);

            int[] fanSpeeds = new int[] { 30, 50, 70, 85 };
            fanSpeedButtons = new Button[fanSpeeds.Length];
            for (int i = 0; i < fanSpeeds.Length; i++)
            {
                int spd = fanSpeeds[i];
                Button b = null;
                b = CreateCompactButton(spd + "%", 56, 30, () => ApplyFanMode(spd + "% Fixed", spd, false, false, b));
                fanSpeedButtons[i] = b;
                flowFan.Controls.Add(b);
            }
            grpFan.Controls.Add(flowFan);

            // Add groups to table
            tableLayout.Controls.Add(grpController, 0, 0);
            tableLayout.Controls.Add(grpTdp, 0, 1);
            tableLayout.Controls.Add(grpFan, 0, 2);

            this.Controls.Add(tableLayout);
            this.Controls.Add(statusPanel);

            // Initialize active state highlights
            UpdateActiveControllerHighlight();
            HighlightFanButton(btnFanAuto);

            this.ResumeLayout(false);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            this.BeginInvoke(new Action(async () =>
            {
                await ApplyStartupControllerMode();
            }));
        }

        private async Task ApplyStartupControllerMode()
        {
            try
            {
                // Brief delay to allow initial hardware & driver initialization
                await Task.Delay(350);
                bool desktopOnStart = ManagerFactory.settingsManager.GetBoolean("DesktopLayoutOnStart");
                if (desktopOnStart)
                {
                    btnDesktop?.PerformClick();
                }
                else
                {
                    btnX360?.PerformClick();
                }
            }
            catch (Exception ex)
            {
                LogManager.LogError("Failed applying startup controller mode: {0}", ex.Message);
            }
        }

        private Button CreateCompactButton(string text, int width, int height, Action onClick)
        {
            Button btn = new Button
            {
                Text = text,
                Size = LogicalToDeviceUnits(new Size(width, height)),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Margin = new Padding(LogicalToDeviceUnits(3)),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.System
            };
            btn.Click += (s, e) => onClick();
            return btn;
        }

        private Button CreateCompactButton(string text, int width, int height, Func<Task> onClick)
        {
            Button btn = new Button
            {
                Text = text,
                Size = LogicalToDeviceUnits(new Size(width, height)),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Margin = new Padding(LogicalToDeviceUnits(3)),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.System
            };
            btn.Click += async (s, e) =>
            {
                try
                {
                    await onClick();
                }
                catch (Exception ex)
                {
                    LogManager.LogError("Button click failed: {0}", ex.Message);
                    lblStatus.Text = "Error: " + ex.Message;
                    MessageBox.Show(ex.Message, "Action Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            return btn;
        }

        private void UpdateActiveControllerHighlight()
        {
            LayoutModes currentLayoutMode = ManagerFactory.layoutManager.GetCurrentMode();
            if (currentLayoutMode == LayoutModes.Desktop)
            {
                HighlightControllerButton(btnDesktop);
                return;
            }

            if (VirtualManager.HIDmode == HIDmode.DualShock4Controller)
                HighlightControllerButton(btnDS4);
            else if (VirtualManager.HIDmode == HIDmode.Xbox360Controller)
                HighlightControllerButton(btnX360);
            else
                HighlightControllerButton(btnNative);
        }

        private void HighlightControllerButton(Button selected)
        {
            Button[] all = new Button[] { btnDS4, btnX360, btnNative, btnDesktop };
            foreach (var b in all)
            {
                if (b == null) continue;
                bool isSel = (b == selected);
                b.Font = new Font("Segoe UI", 9F, isSel ? FontStyle.Bold : FontStyle.Regular);
            }
        }

        private void HighlightTdpButton(int wattage)
        {
            foreach (var b in tdpButtons)
            {
                if (b == null) continue;
                bool isSel = b.Text == wattage + "W";
                b.Font = new Font("Segoe UI", 9F, isSel ? FontStyle.Bold : FontStyle.Regular);
            }
        }

        private void HighlightFanButton(Button selected)
        {
            if (btnFanAuto != null)
                btnFanAuto.Font = new Font("Segoe UI", 9F, btnFanAuto == selected ? FontStyle.Bold : FontStyle.Regular);
            if (btnFanFull != null)
                btnFanFull.Font = new Font("Segoe UI", 9F, btnFanFull == selected ? FontStyle.Bold : FontStyle.Regular);

            if (fanSpeedButtons != null)
            {
                foreach (var b in fanSpeedButtons)
                {
                    if (b == null) continue;
                    b.Font = new Font("Segoe UI", 9F, b == selected ? FontStyle.Bold : FontStyle.Regular);
                }
            }
        }

        private void ApplyTdp(int tdp)
        {
            try
            {
                LogManager.LogInformation("Applying TDP: {0}W", tdp);

                // 1. Direct RyzenSMU application (PawnIO)
                PerformanceManager.SetTDP(tdp, true);

                // 2. Lenovo WMI native EC power limit & OEM power mode LED colors
                if (IDevice.GetCurrent() is LegionGo lego)
                {
                    int oemMode = (int)LegionGo.LegionMode.Custom;
                    if (tdp <= 10) oemMode = (int)LegionGo.LegionMode.Quiet;
                    else if (tdp == 15) oemMode = (int)LegionGo.LegionMode.Balanced;
                    else if (tdp == 25 || tdp == 30) oemMode = (int)LegionGo.LegionMode.Performance;

                    lego.SetSmartFanMode(oemMode);
                    lego.set_long_limit(tdp);
                    lego.set_short_limit(tdp);
                }

                HighlightTdpButton(tdp);
                lblStatus.Text = string.Format("Active TDP: {0}W (RyzenSMU + Lenovo EC applied)", tdp);
            }
            catch (Exception ex)
            {
                LogManager.LogError("Failed to apply TDP: {0}", ex.Message);
                MessageBox.Show("Error applying TDP: " + ex.Message, "TDP Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ApplyFanMode(string name, int percentage, bool isFullSpeed, bool isAuto, Button clickedBtn)
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

                    HighlightFanButton(clickedBtn);
                    lblStatus.Text = "Active Fan: " + name;
                }
            }
            catch (Exception ex)
            {
                LogManager.LogError("Failed to apply Fan Mode: {0}", ex.Message);
                MessageBox.Show("Error applying Fan mode: " + ex.Message, "Fan Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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
