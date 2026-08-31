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

            this.BackColor = Color.White;
            this.ForeColor = Color.Black;
            this.Dock = DockStyle.Fill;
            this.AutoScroll = true;

            // Title
            this.lblTitle.Text = "Controller & Gyro Emulation";
            this.lblTitle.Font = new Font("Segoe UI", 18F, FontStyle.Bold);
            this.lblTitle.Location = new Point(25, 20);
            this.lblTitle.Size = new Size(500, 35);

            // Status Label
            this.lblStatus.Text = "Active Mode: " + (VirtualManager.HIDmode != HIDmode.NoController ? VirtualManager.HIDmode.ToString() : "Passthrough / Native");
            this.lblStatus.Font = new Font("Segoe UI", 12F, FontStyle.Regular);
            this.lblStatus.ForeColor = Color.FromArgb(0, 102, 204);
            this.lblStatus.Location = new Point(28, 65);
            this.lblStatus.Size = new Size(600, 25);

            // FlowPanel for touch buttons
            this.flowPanel.Location = new Point(25, 105);
            this.flowPanel.Size = new Size(720, 450);
            this.flowPanel.AutoScroll = true;

            // Button 1: DualShock 4
            AddButton("DUALSHOCK 4\n(Gyro / Fortnite)", async () =>
            {
                lblStatus.Text = "Switching to DualShock 4...";
                PlayDisconnectSound();
                await VirtualManager.SetControllerMode(HIDmode.DualShock4Controller);
                await VirtualManager.SetControllerStatus(HIDstatus.Connected);
                PlayConnectSound();
                lblStatus.Text = "Active Emulation: DualShock 4 (Gyro Active)";
            }, VirtualManager.HIDmode == HIDmode.DualShock4Controller);

            // Button 2: Xbox 360
            AddButton("XBOX 360\n(Standard XInput)", async () =>
            {
                lblStatus.Text = "Switching to Xbox 360...";
                PlayDisconnectSound();
                await VirtualManager.SetControllerMode(HIDmode.Xbox360Controller);
                await VirtualManager.SetControllerStatus(HIDstatus.Connected);
                PlayConnectSound();
                lblStatus.Text = "Active Emulation: Xbox 360 (XInput)";
            }, VirtualManager.HIDmode == HIDmode.Xbox360Controller);

            // Button 3: Passthrough
            AddButton("PASSTHROUGH\n(Native Controller)", async () =>
            {
                lblStatus.Text = "Switching to Native Passthrough...";
                PlayDisconnectSound();
                await VirtualManager.SetControllerMode(HIDmode.NoController);
                await VirtualManager.SetControllerStatus(HIDstatus.Disconnected);
                if (IDevice.GetCurrent() is LegionGo lego)
                {
                    lego.SetPassthrough(true);
                }
                PlayConnectSound();
                lblStatus.Text = "Active Mode: Direct Native Passthrough";
            }, VirtualManager.HIDmode == HIDmode.NoController);

            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.flowPanel);

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

        private void AddButton(string text, Func<Task> onClick, bool isInitialActive)
        {
            Button btn = new Button
            {
                Text = text,
                Size = new Size(210, 100),
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = isInitialActive ? Color.FromArgb(0, 102, 204) : Color.FromArgb(245, 245, 245),
                ForeColor = isInitialActive ? Color.White : Color.Black,
                Margin = new Padding(10),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 2;
            btn.FlatAppearance.BorderColor = isInitialActive ? Color.FromArgb(0, 80, 180) : Color.FromArgb(210, 210, 210);

            btn.Click += async (s, e) =>
            {
                HighlightButton(btn);
                await onClick();
            };

            this.flowPanel.Controls.Add(btn);
        }

        private void HighlightButton(Button activeBtn)
        {
            foreach (Control ctrl in flowPanel.Controls)
            {
                if (ctrl is Button b)
                {
                    b.BackColor = Color.FromArgb(245, 245, 245);
                    b.ForeColor = Color.Black;
                    b.FlatAppearance.BorderColor = Color.FromArgb(210, 210, 210);
                }
            }
            activeBtn.BackColor = Color.FromArgb(0, 102, 204);
            activeBtn.ForeColor = Color.White;
            activeBtn.FlatAppearance.BorderColor = Color.FromArgb(0, 80, 180);
        }
    }
}
