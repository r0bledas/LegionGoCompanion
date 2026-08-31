using System;
using System.Drawing;
using System.Windows.Forms;
using HandheldCompanion.Devices;
using HandheldCompanion.Devices.Lenovo;
using HandheldCompanion.Managers;
using HandheldCompanion.Shared;

namespace HandheldCompanion.Views
{
    public class FanControlView : UserControl
    {
        private Label lblTitle;
        private Label lblStatus;
        private FlowLayoutPanel flowPanel;

        public FanControlView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.lblTitle = new Label();
            this.lblStatus = new Label();
            this.flowPanel = new FlowLayoutPanel();

            this.SuspendLayout();

            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.White;
            this.Dock = DockStyle.Fill;
            this.AutoScroll = true;

            // Title
            this.lblTitle.Text = "Fan Control (Touch-Optimized)";
            this.lblTitle.Font = new Font("Segoe UI", 18F, FontStyle.Bold);
            this.lblTitle.Location = new Point(25, 20);
            this.lblTitle.AutoSize = true;

            // Status Label
            this.lblStatus.Text = "Active Mode: Select a fan preset below";
            this.lblStatus.Font = new Font("Segoe UI", 12F, FontStyle.Italic);
            this.lblStatus.ForeColor = Color.FromArgb(0, 122, 204);
            this.lblStatus.Location = new Point(30, 65);
            this.lblStatus.AutoSize = true;

            // FlowPanel for big touch buttons
            this.flowPanel.Location = new Point(25, 110);
            this.flowPanel.Size = new Size(700, 350);
            this.flowPanel.AutoScroll = true;

            // Mode 1: Auto (Balanced)
            AddFanButton("AUTO / BALANCED", () => ApplyFanMode("Auto", 0, false, true));

            // Mode 2: Full Speed (100%)
            AddFanButton("FULL SPEED (100%)", () => ApplyFanMode("Full Speed", 100, true, false));

            // Speed Presets
            int[] fanSpeeds = new int[] { 30, 50, 70, 85 };
            foreach (int speed in fanSpeeds)
            {
                int s = speed;
                AddFanButton(s.ToString() + "% SPEED", () => ApplyFanMode(s.ToString() + "% Custom", s, false, false));
            }

            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.flowPanel);

            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private void AddFanButton(string text, Action onClick)
        {
            Button btn = new Button
            {
                Text = text,
                Size = new Size(200, 90),
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White,
                Margin = new Padding(10),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 2;
            btn.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);

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
                    b.BackColor = Color.FromArgb(45, 45, 48);
                    b.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
                }
            }
            activeBtn.BackColor = Color.FromArgb(0, 122, 204);
            activeBtn.FlatAppearance.BorderColor = Color.White;
        }

        private void ApplyFanMode(string name, int percentage, bool isFullSpeed, bool isAuto)
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

                    lblStatus.Text = "Active Mode: " + name;
                }
            }
            catch (Exception ex)
            {
                LogManager.LogError("Failed to apply Fan Mode: " + ex.Message);
                MessageBox.Show("Error applying Fan mode: " + ex.Message, "Fan Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
