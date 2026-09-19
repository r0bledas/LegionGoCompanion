using System;
using System.Drawing;
using System.Linq;
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

        // Display Resolution presets
        private static readonly (string Label, int Width, int Height)[] ResolutionPresets = new[]
        {
            ("720p", 1280, 720),
            ("800p", 1280, 800),
            ("900p", 1600, 900),
            ("1000p", 1600, 1000),
            ("1080p", 1920, 1080),
            ("1200p", 1920, 1200),
            ("1440p", 2560, 1440),
            ("1600p", 2560, 1600)
        };
        private Button[]? resolutionButtons;
        private Label? lblResStatus;

        public GeneralView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            this.AutoScaleMode = AutoScaleMode.Inherit;
            this.Dock = DockStyle.Fill;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            // Main scrollable content panel (compact margins)
            Panel contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(8, 4, 8, 4)
            };

            // ==========================================
            // 1. Controller GroupBox
            // ==========================================
            GroupBox grpController = new GroupBox
            {
                Text = "Controller Mode",
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Padding = new Padding(6, 4, 6, 6),
                Margin = new Padding(0, 0, 0, 4)
            };

            FlowLayoutPanel flowController = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Padding = new Padding(1)
            };

            btnDS4 = CreateCompactButton("DS4", 52, 26, async () =>
            {
                PlayDisconnectSound();
                ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", true);
                ControllerManager.TargetController?.Hide(false);
                SetLegionPassthrough(false);
                ManagerFactory.layoutManager.SetLayoutMode(LayoutModes.Gamepad);
                bool modeChanged = await VirtualManager.SetControllerMode(HIDmode.DualShock4Controller);
                await VirtualManager.SetControllerStatus(HIDstatus.Connected);
                PlayConnectSound();
                HighlightControllerButton(btnDS4);
            });

            btnX360 = CreateCompactButton("x360", 52, 26, async () =>
            {
                PlayDisconnectSound();
                ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", true);
                ControllerManager.TargetController?.Hide(false);
                SetLegionPassthrough(false);
                ManagerFactory.layoutManager.SetLayoutMode(LayoutModes.Gamepad);
                bool modeChanged = await VirtualManager.SetControllerMode(HIDmode.Xbox360Controller);
                await VirtualManager.SetControllerStatus(HIDstatus.Connected);
                PlayConnectSound();
                HighlightControllerButton(btnX360);
            });

            btnNative = CreateCompactButton("Native", 58, 26, async () =>
            {
                PlayDisconnectSound();
                await VirtualManager.SetControllerMode(HIDmode.NoController);
                await VirtualManager.SetControllerStatus(HIDstatus.Disconnected);
                ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", false);
                ControllerManager.TargetController?.Unhide(false);
                SetLegionPassthrough(true);
                ManagerFactory.layoutManager.SetLayoutMode(LayoutModes.Gamepad);
                PlayConnectSound();
                HighlightControllerButton(btnNative);
            });

            btnDesktop = CreateCompactButton("Desktop", 62, 26, async () =>
            {
                PlayConnectSound();
                ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", true);
                ControllerManager.TargetController?.Hide(false);
                SetLegionPassthrough(false);
                await VirtualManager.SetControllerMode(HIDmode.NoController);
                await VirtualManager.SetControllerStatus(HIDstatus.Disconnected);
                ManagerFactory.layoutManager.SetLayoutMode(LayoutModes.Desktop);
                HighlightControllerButton(btnDesktop);
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
                Text = "TDP Limit (Watts)",
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Padding = new Padding(6, 4, 6, 6),
                Margin = new Padding(0, 0, 0, 4)
            };

            FlowLayoutPanel flowTdp = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Padding = new Padding(1)
            };

            int[] tdpValues = new int[] { 5, 8, 10, 15, 20, 25, 30, 35, 40 };
            tdpButtons = new Button[tdpValues.Length];

            for (int i = 0; i < tdpValues.Length; i++)
            {
                int wattage = tdpValues[i];
                Button b = CreateCompactButton(wattage + "W", 38, 24, () => ApplyTdp(wattage));
                tdpButtons[i] = b;
                flowTdp.Controls.Add(b);
            }
            grpTdp.Controls.Add(flowTdp);

            // ==========================================
            // 3. Fan Control GroupBox
            // ==========================================
            GroupBox grpFan = new GroupBox
            {
                Text = "Fan Control",
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Padding = new Padding(6, 4, 6, 6),
                Margin = new Padding(0, 0, 0, 4)
            };

            FlowLayoutPanel flowFan = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Padding = new Padding(1)
            };

            btnFanAuto = CreateCompactButton("Auto", 46, 24, () => ApplyFanMode("Auto (Balanced)", 0, false, true, btnFanAuto));
            btnFanFull = CreateCompactButton("100%", 52, 24, () => ApplyFanMode("Full Speed (100%)", 100, true, false, btnFanFull));

            flowFan.Controls.Add(btnFanAuto);
            flowFan.Controls.Add(btnFanFull);

            int[] fanSpeeds = new int[] { 30, 50, 70, 85 };
            fanSpeedButtons = new Button[fanSpeeds.Length];
            for (int i = 0; i < fanSpeeds.Length; i++)
            {
                int spd = fanSpeeds[i];
                Button b = null;
                b = CreateCompactButton(spd + "%", 38, 24, () => ApplyFanMode(spd + "% Fixed", spd, false, false, b));
                fanSpeedButtons[i] = b;
                flowFan.Controls.Add(b);
            }
            grpFan.Controls.Add(flowFan);

            // ==========================================
            // 4. Display Resolution GroupBox (Internal Legion Go Display)
            // ==========================================
            GroupBox grpResolution = new GroupBox
            {
                Text = "Display Resolution",
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Padding = new Padding(6, 4, 6, 6),
                Margin = new Padding(0, 0, 0, 4)
            };

            FlowLayoutPanel flowResolution = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Padding = new Padding(1)
            };

            resolutionButtons = new Button[ResolutionPresets.Length];
            for (int i = 0; i < ResolutionPresets.Length; i++)
            {
                var preset = ResolutionPresets[i];
                int w = preset.Width;
                int h = preset.Height;
                int btnWidth = preset.Label.Length <= 4 ? 46 : 52;
                Button b = CreateCompactButton(preset.Label, btnWidth, 24, () => ApplyResolution(w, h));
                resolutionButtons[i] = b;
                flowResolution.Controls.Add(b);
            }
            if (resolutionButtons.Length > 0)
                flowResolution.SetFlowBreak(resolutionButtons[resolutionButtons.Length - 1], true);

            lblResStatus = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                ForeColor = Color.FromArgb(90, 95, 105),
                Padding = new Padding(4, 4, 2, 2)
            };

            flowResolution.Controls.Add(lblResStatus);
            grpResolution.Controls.Add(flowResolution);

            // Add groups to content panel (Dock.Top docks in reverse order of addition)
            contentPanel.Controls.Add(grpResolution);
            contentPanel.Controls.Add(grpFan);
            contentPanel.Controls.Add(grpTdp);
            contentPanel.Controls.Add(grpController);

            this.Controls.Add(contentPanel);

            // Initialize active state highlights
            UpdateActiveControllerHighlight();
            HighlightFanButton(btnFanAuto);
            UpdateResolutionControls();

            if (ManagerFactory.multimediaManager != null)
            {
                ManagerFactory.multimediaManager.DisplaySettingsChanged += (screen, res) =>
                {
                    if (this.IsHandleCreated && !this.IsDisposed)
                        this.BeginInvoke(new Action(UpdateResolutionControls));
                };
                ManagerFactory.multimediaManager.PrimaryScreenChanged += (screen) =>
                {
                    if (this.IsHandleCreated && !this.IsDisposed)
                        this.BeginInvoke(new Action(UpdateResolutionControls));
                };
            }

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
                Size = new Size(width, height),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Margin = new Padding(2),
                Padding = Padding.Empty,
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(245, 247, 250),
                ForeColor = Color.FromArgb(30, 35, 45)
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(210, 215, 222);
            btn.FlatAppearance.BorderSize = 1;
            btn.Click += (s, e) => onClick();
            return btn;
        }

        private Button CreateCompactButton(string text, int width, int height, Func<Task> onClick)
        {
            Button btn = new Button
            {
                Text = text,
                Size = new Size(width, height),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Margin = new Padding(2),
                Padding = Padding.Empty,
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(245, 247, 250),
                ForeColor = Color.FromArgb(30, 35, 45)
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(210, 215, 222);
            btn.FlatAppearance.BorderSize = 1;
            btn.Click += async (s, e) =>
            {
                try
                {
                    await onClick();
                }
                catch (Exception ex)
                {
                    LogManager.LogError("Button click failed: {0}", ex.Message);
                    MessageBox.Show(ex.Message, "Action Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            return btn;
        }

        private void SetButtonSelected(Button? b, bool isSel)
        {
            if (b == null) return;
            b.Font = new Font("Segoe UI", 8.5F, isSel ? FontStyle.Bold : FontStyle.Regular);
            b.BackColor = isSel ? Color.FromArgb(0, 120, 215) : Color.FromArgb(245, 247, 250);
            b.ForeColor = isSel ? Color.White : Color.FromArgb(30, 35, 45);
            b.FlatAppearance.BorderColor = isSel ? Color.FromArgb(0, 95, 175) : Color.FromArgb(210, 215, 222);
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
                SetButtonSelected(b, b == selected);
            }
        }

        private void HighlightTdpButton(int wattage)
        {
            if (tdpButtons == null) return;
            foreach (var b in tdpButtons)
            {
                SetButtonSelected(b, b != null && b.Text == wattage + "W");
            }
        }

        private void HighlightFanButton(Button selected)
        {
            SetButtonSelected(btnFanAuto, btnFanAuto == selected);
            SetButtonSelected(btnFanFull, btnFanFull == selected);

            if (fanSpeedButtons != null)
            {
                foreach (var b in fanSpeedButtons)
                {
                    SetButtonSelected(b, b == selected);
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

        private void UpdateResolutionControls()
        {
            var primary = ManagerFactory.multimediaManager?.PrimaryDesktop;
            bool isInternal = primary != null && primary.IsInternal;

            if (resolutionButtons != null)
            {
                foreach (var b in resolutionButtons)
                {
                    if (b != null) b.Enabled = isInternal;
                }
            }

            if (lblResStatus == null) return;

            if (!isInternal)
            {
                lblResStatus.Text = "Disabled (External Display Active)";
                lblResStatus.ForeColor = Color.FromArgb(180, 50, 50);
                if (resolutionButtons != null)
                {
                    foreach (var b in resolutionButtons)
                    {
                        if (b != null) SetButtonSelected(b, false);
                    }
                }
                return;
            }

            var current = primary?.GetResolution();
            if (current != null)
            {
                lblResStatus.Text = $"Current: {current.Width}x{current.Height}";
                lblResStatus.ForeColor = Color.FromArgb(60, 65, 75);

                HighlightResolutionButton(current.Width, current.Height);
            }
        }

        private void HighlightResolutionButton(int width, int height)
        {
            if (resolutionButtons == null) return;
            int w = Math.Max(width, height);
            int h = Math.Min(width, height);

            for (int i = 0; i < ResolutionPresets.Length; i++)
            {
                var preset = ResolutionPresets[i];
                bool isMatch = (preset.Width == w && preset.Height == h) || (preset.Width == h && preset.Height == w);
                SetButtonSelected(resolutionButtons[i], isMatch);
            }
        }

        private void ApplyResolution(int width, int height)
        {
            try
            {
                var primary = ManagerFactory.multimediaManager?.PrimaryDesktop;
                if (primary == null || !primary.IsInternal)
                {
                    MessageBox.Show("Resolution quick adjustment is only available on the internal Legion Go display.",
                        "Display Resolution", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                int freq = primary.GetCurrentFrequency();
                bool success = ManagerFactory.multimediaManager.SetResolution(width, height, freq);
                if (success)
                {
                    HighlightResolutionButton(width, height);
                    if (lblResStatus != null)
                        lblResStatus.Text = $"Current: {width}x{height}";
                }
                else
                {
                    MessageBox.Show($"Unable to switch resolution to {width}x{height}.",
                        "Resolution Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                LogManager.LogError("ApplyResolution error: {0}", ex.Message);
            }
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
