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

            this.Dock = DockStyle.Fill;
            this.AutoScroll = true;

            // Title
            this.lblTitle.Text = "Fan Speed & Thermal Control";
            this.lblTitle.Font = new Font(this.Font.FontFamily, 11F, FontStyle.Bold);
            this.lblTitle.Location = new Point(15, 15);
            this.lblTitle.AutoSize = true;

            // Status Label
            this.lblStatus.Text = "Active Mode: Auto / Balanced";
            this.lblStatus.Location = new Point(16, 42);
            this.lblStatus.AutoSize = true;

            // GroupBox for Fan Modes
            GroupBox grpFan = new GroupBox
            {
                Text = "Fan Modes & Presets",
                Location = new Point(15, 75),
                Size = new Size(780, 220),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            // FlowPanel for buttons
            this.flowPanel.Location = new Point(15, 25);
            this.flowPanel.Size = new Size(750, 180);
            this.flowPanel.AutoScroll = true;

            // Mode 1: Auto (Balanced)
            AddFanButton("Auto / Balanced", () => ApplyFanMode("Auto / Balanced", 0, false, true));

            // Mode 2: Full Speed (100%)
            AddFanButton("Full Speed (100%)", () => ApplyFanMode("Full Speed (100%)", 100, true, false));

            // Speed Presets
            int[] fanSpeeds = new int[] { 30, 50, 70, 85 };
            foreach (int speed in fanSpeeds)
            {
                int s = speed;
                AddFanButton(s.ToString() + "% Speed", () => ApplyFanMode(s.ToString() + "% Custom", s, false, false));
            }

            grpFan.Controls.Add(this.flowPanel);

            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(grpFan);

            this.ResumeLayout(false);
        }

        private void AddFanButton(string text, Action onClick)
        {
            Button btn = new Button
            {
                Text = text,
                Size = new Size(130, 48),
                Font = new Font(this.Font.FontFamily, 10F, FontStyle.Regular),
                Margin = new Padding(8),
                Cursor = Cursors.Hand
            };

            btn.Click += (s, e) =>
            {
                onClick();
            };

            this.flowPanel.Controls.Add(btn);
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
