using System;
using System.Drawing;
using System.Windows.Forms;
using HandheldCompanion.Devices;
using HandheldCompanion.Managers;
using HandheldCompanion.Shared;
namespace HandheldCompanion.Views
{
    public class FanControlView : UserControl
    {
        private Label lblTitle;
        private CheckBox chkSmartFan;
        private CheckBox chkFullSpeed;
        private Label lblFanSpeed;
        private TrackBar tbFanSpeed;
        private Button btnApply;

        public FanControlView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.lblTitle = new Label();
            this.chkSmartFan = new CheckBox();
            this.chkFullSpeed = new CheckBox();
            this.lblFanSpeed = new Label();
            this.tbFanSpeed = new TrackBar();
            this.btnApply = new Button();

            this.SuspendLayout();

            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.White;
            this.Size = new Size(600, 500);

            // Title
            this.lblTitle.Text = "Fan Control";
            this.lblTitle.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
            this.lblTitle.Location = new Point(30, 30);
            this.lblTitle.AutoSize = true;

            // Smart Fan Toggle
            this.chkSmartFan.Text = "Smart Fan Mode (Auto)";
            this.chkSmartFan.Font = new Font("Segoe UI", 12F);
            this.chkSmartFan.Location = new Point(35, 90);
            this.chkSmartFan.AutoSize = true;
            this.chkSmartFan.Checked = true;

            // Full Speed Toggle
            this.chkFullSpeed.Text = "Full Speed (100%)";
            this.chkFullSpeed.Font = new Font("Segoe UI", 12F);
            this.chkFullSpeed.Location = new Point(35, 130);
            this.chkFullSpeed.AutoSize = true;

            // Manual Speed Trackbar
            this.tbFanSpeed.Minimum = 0;
            this.tbFanSpeed.Maximum = 100;
            this.tbFanSpeed.Value = 50;
            this.tbFanSpeed.TickFrequency = 10;
            this.tbFanSpeed.Location = new Point(30, 200);
            this.tbFanSpeed.Width = 400;
            this.tbFanSpeed.Scroll += TbFanSpeed_Scroll;

            // Fan Speed Label
            this.lblFanSpeed.Text = tbFanSpeed.Value.ToString() + " %";
            this.lblFanSpeed.Font = new Font("Segoe UI", 18F, FontStyle.Bold);
            this.lblFanSpeed.ForeColor = Color.FromArgb(0, 122, 204);
            this.lblFanSpeed.Location = new Point(450, 195);
            this.lblFanSpeed.AutoSize = true;

            // Apply Button
            this.btnApply.Text = "Apply Curve";
            this.btnApply.Location = new Point(30, 270);
            this.btnApply.Size = new Size(150, 45);
            this.btnApply.FlatStyle = FlatStyle.Flat;
            this.btnApply.BackColor = Color.FromArgb(45, 45, 48);
            this.btnApply.Cursor = Cursors.Hand;
            this.btnApply.Click += BtnApply_Click;

            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.chkSmartFan);
            this.Controls.Add(this.chkFullSpeed);
            this.Controls.Add(this.tbFanSpeed);
            this.Controls.Add(this.lblFanSpeed);
            this.Controls.Add(this.btnApply);

            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private void TbFanSpeed_Scroll(object sender, EventArgs e)
        {
            lblFanSpeed.Text = tbFanSpeed.Value.ToString() + " %";
            chkSmartFan.Checked = false;
            chkFullSpeed.Checked = tbFanSpeed.Value == 100;
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            int targetFan = tbFanSpeed.Value;
            bool auto = chkSmartFan.Checked;
            LogManager.LogInformation("Applying Fan: " + (auto ? "Auto" : targetFan + "%"));
            
            try
            {
                if (IDevice.GetCurrent() is HandheldCompanion.Devices.Lenovo.LegionGo lego)
                {
                    if (chkFullSpeed.Checked)
                    {
                        lego.SetFanFullSpeed(true);
                    }
                    else if (auto)
                    {
                        lego.SetFanFullSpeed(false);
                        lego.SetSmartFanMode(2); // Balanced mode default
                    }
                    else
                    {
                        lego.SetFanFullSpeed(false);
                        ushort clampedSpeed = (ushort)Math.Clamp(targetFan, 0, 100);
                        lego.SetFanTable(new HandheldCompanion.Devices.Lenovo.FanTable(new ushort[] { clampedSpeed, clampedSpeed, clampedSpeed, clampedSpeed, clampedSpeed, clampedSpeed, clampedSpeed, clampedSpeed, clampedSpeed, clampedSpeed }));
                    }

                    MessageBox.Show("Fan setting applied via Lenovo WMI!", "Fan Applied", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("Device is not recognized as a Legion Go.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                LogManager.LogError("Failed to apply Fan: " + ex.Message);
                MessageBox.Show("Failed to apply Fan: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
