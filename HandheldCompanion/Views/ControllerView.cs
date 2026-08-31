using System;
using System.Drawing;
using System.Windows.Forms;
using HandheldCompanion.Devices;
using HandheldCompanion.Devices.Lenovo;
using HandheldCompanion.Managers;
using HandheldCompanion.Shared;

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
            this.lblTitle.Size = new Size(450, 35);

            // Status Label
            this.lblStatus.Text = "Active Emulation: DualShock 4 (DS4 + Gyro for Fortnite)";
            this.lblStatus.Font = new Font("Segoe UI", 12F, FontStyle.Regular);
            this.lblStatus.ForeColor = Color.FromArgb(0, 102, 204);
            this.lblStatus.Location = new Point(28, 65);
            this.lblStatus.Size = new Size(600, 25);

            // FlowPanel for touch buttons
            this.flowPanel.Location = new Point(25, 105);
            this.flowPanel.Size = new Size(720, 450);
            this.flowPanel.AutoScroll = true;

            // Button 1: DualShock 4
            AddButton("DUALSHOCK 4\n(Gyro / Fortnite)", () =>
            {
                lblStatus.Text = "Active Emulation: DualShock 4 (Gyro Active)";
                LogManager.LogInformation("Switched to DS4 Emulation");
            }, true);

            // Button 2: Xbox 360
            AddButton("XBOX 360\n(Standard XInput)", () =>
            {
                lblStatus.Text = "Active Emulation: Xbox 360";
                LogManager.LogInformation("Switched to Xbox 360 Emulation");
            }, false);

            // Button 3: Passthrough
            AddButton("PASSTHROUGH\n(Native Controller)", () =>
            {
                if (IDevice.GetCurrent() is LegionGo lego)
                {
                    lego.SetPassthrough(true);
                }
                lblStatus.Text = "Active Emulation: Direct Native Passthrough";
                LogManager.LogInformation("Enabled Controller Passthrough");
            }, false);

            // Button 4: Desktop Mouse Mode
            AddButton("DESKTOP MOUSE\n(Stick as Mouse)", () =>
            {
                lblStatus.Text = "Active Mode: Desktop Mouse Control";
                LogManager.LogInformation("Toggled Desktop Mouse Mode");
            }, false);

            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.flowPanel);

            this.ResumeLayout(false);
        }

        private void AddButton(string text, Action onClick, bool isInitialActive)
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

            btn.Click += (s, e) =>
            {
                onClick();
                HighlightButton(btn);
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
