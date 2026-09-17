using System;
using System.Drawing;
using System.Media;
using System.Threading.Tasks;
using System.Windows.Forms;
using HandheldCompanion.Devices;
using HandheldCompanion.Devices.Lenovo;
using HandheldCompanion.Helpers;
using HandheldCompanion.Managers;
using HandheldCompanion.Shared;
using HandheldCompanion.Utils;

namespace HandheldCompanion.Views
{
    public class ControllerView : UserControl
    {
        private Label lblTitle;
        private Label lblStatus;
        private FlowLayoutPanel flowPanel;

        public ControllerView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.lblTitle = new Label();
            this.lblStatus = new Label();
            this.flowPanel = new FlowLayoutPanel();

            this.SuspendLayout();

            this.Dock = DockStyle.Fill;
            this.AutoScroll = true;

            // Title
            this.lblTitle.Text = "Controller & Gyro Emulation";
            this.lblTitle.Font = new Font(this.Font.FontFamily, 11F, FontStyle.Bold);
            this.lblTitle.Location = new Point(15, 15);
            this.lblTitle.AutoSize = true;

            // Status Label
            this.lblStatus.Text = "Active Mode: " + (VirtualManager.HIDmode != HIDmode.NoController ? VirtualManager.HIDmode.ToString() : "Passthrough / Native");
            this.lblStatus.Location = new Point(16, 42);
            this.lblStatus.AutoSize = true;

            // GroupBox for Emulation Modes
            GroupBox grpController = new GroupBox
            {
                Text = "Emulation Targets",
                Location = new Point(15, 75),
                Size = new Size(780, 220),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            // FlowPanel for buttons
            this.flowPanel.Location = new Point(15, 25);
            this.flowPanel.Size = new Size(750, 180);
            this.flowPanel.AutoScroll = true;

            // Button 1: DualShock 4
            AddButton("DualShock 4 (Gyro / Fortnite)", async () =>
            {
                lblStatus.Text = "Switching to DualShock 4...";
                PlayDisconnectSound();
                SetLegionPassthrough(false);
                bool modeChanged = await VirtualManager.SetControllerMode(HIDmode.DualShock4Controller);
                bool connected = modeChanged && await VirtualManager.SetControllerStatus(HIDstatus.Connected);
                if (!connected)
                    throw new InvalidOperationException("DualShock 4 emulation could not be connected. Make sure VIIPER is enabled and running.");
                PlayConnectSound();
                lblStatus.Text = "Active Emulation: DualShock 4 (Gyro Active)";
            }, VirtualManager.HIDmode == HIDmode.DualShock4Controller);

            // Button 2: Xbox 360
            AddButton("Xbox 360 (Standard XInput)", async () =>
            {
                lblStatus.Text = "Switching to Xbox 360...";
                PlayDisconnectSound();
                SetLegionPassthrough(false);
                bool modeChanged = await VirtualManager.SetControllerMode(HIDmode.Xbox360Controller);
                bool connected = modeChanged && await VirtualManager.SetControllerStatus(HIDstatus.Connected);
                if (!connected)
                    throw new InvalidOperationException("Xbox 360 emulation could not be connected. Make sure VIIPER is enabled and running.");
                PlayConnectSound();
                lblStatus.Text = "Active Emulation: Xbox 360 (XInput)";
            }, VirtualManager.HIDmode == HIDmode.Xbox360Controller);

            // Button 3: Passthrough
            AddButton("Passthrough (Native Controller)", async () =>
            {
                lblStatus.Text = "Switching to Native Passthrough...";
                PlayDisconnectSound();
                bool modeChanged = await VirtualManager.SetControllerMode(HIDmode.NoController);
                bool disconnected = modeChanged && await VirtualManager.SetControllerStatus(HIDstatus.Disconnected);
                if (!disconnected)
                    throw new InvalidOperationException("The virtual controller could not be disconnected.");
                SetLegionPassthrough(true);
                PlayConnectSound();
                lblStatus.Text = "Active Mode: Direct Native Passthrough";
            }, VirtualManager.HIDmode == HIDmode.NoController);

            grpController.Controls.Add(this.flowPanel);

            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(grpController);

            this.ResumeLayout(false);
        }

        private void PlayDisconnectSound()
        {
            try { SystemSounds.Asterisk.Play(); } catch { }
        }

        private void PlayConnectSound()
        {
            try { SystemSounds.Exclamation.Play(); } catch { }
        }

        private static void SetLegionPassthrough(bool enabled)
        {
            if (IDevice.GetCurrent() is LegionGo lego)
                lego.SetPassthrough(enabled);
        }

        private void AddButton(string text, Func<Task> onClick, bool isInitialActive)
        {
            Button btn = new Button
            {
                Text = text,
                Size = new Size(220, 50),
                Font = new Font(this.Font.FontFamily, 10F, FontStyle.Regular),
                Margin = new Padding(8),
                Cursor = Cursors.Hand
            };

            btn.Click += async (s, e) =>
            {
                try
                {
                    await onClick();
                }
                catch (Exception ex)
                {
                    LogManager.LogError("Controller mode switch failed: {0}", ex.Message);
                    lblStatus.Text = "Controller switch failed: " + ex.Message;
                    MessageBox.Show(ex.Message, "Controller Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            this.flowPanel.Controls.Add(btn);
        }
    }
}
