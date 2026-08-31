using System;
using System.Drawing;
using System.Windows.Forms;
using HandheldCompanion.Devices;
using HandheldCompanion.Devices.Lenovo;
using HandheldCompanion.Managers;
using HandheldCompanion.Shared;

namespace HandheldCompanion.Views
{
    public class TdpPowerView : UserControl
    {
        private Label lblTitle;
        private FlowLayoutPanel flowPanel;
        private Label lblStatus;

        public TdpPowerView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.lblTitle = new Label();
            this.flowPanel = new FlowLayoutPanel();
            this.lblStatus = new Label();

            this.SuspendLayout();

            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.White;
            this.Dock = DockStyle.Fill;
            this.AutoScroll = true;

            // Title
            this.lblTitle.Text = "TDP Presets (Touch-Optimized)";
            this.lblTitle.Font = new Font("Segoe UI", 18F, FontStyle.Bold);
            this.lblTitle.Location = new Point(25, 20);
            this.lblTitle.AutoSize = true;

            // Status Label
            this.lblStatus.Text = "Active TDP: Select a preset below";
            this.lblStatus.Font = new Font("Segoe UI", 12F, FontStyle.Italic);
            this.lblStatus.ForeColor = Color.FromArgb(0, 122, 204);
            this.lblStatus.Location = new Point(30, 65);
            this.lblStatus.AutoSize = true;

            // FlowPanel for big touch buttons
            this.flowPanel.Location = new Point(25, 110);
            this.flowPanel.Size = new Size(700, 350);
            this.flowPanel.AutoScroll = true;

            int[] presets = new int[] { 5, 10, 15, 20, 25, 30, 35, 40 };

            foreach (int tdp in presets)
            {
                Button btn = new Button
                {
                    Text = tdp.ToString() + " W",
                    Size = new Size(150, 90),
                    Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(45, 45, 48),
                    ForeColor = Color.White,
                    Margin = new Padding(10),
                    Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderSize = 2;
                btn.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);

                int currentTdp = tdp;
                btn.Click += (s, e) => ApplyTdp(currentTdp, btn);
                this.flowPanel.Controls.Add(btn);
            }

            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.flowPanel);

            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private void ApplyTdp(int tdp, Button clickedBtn)
        {
            try
            {
                LogManager.LogInformation("Applying TDP Preset: " + tdp + "W");

                // 1. Direct RyzenSMU application (PawnIO)
                PerformanceManager.SetTDP(tdp, true);

                // 2. Lenovo WMI native EC power limit call (triggers power button LED to purple custom mode)
                if (IDevice.GetCurrent() is LegionGo lego)
                {
                    lego.SetSmartFanMode((int)LegionGo.LegionMode.Custom);
                    lego.set_long_limit(tdp);
                    lego.set_short_limit(tdp);
                }

                // Update UI button highlights
                foreach (Control ctrl in flowPanel.Controls)
                {
                    if (ctrl is Button b)
                    {
                        b.BackColor = Color.FromArgb(45, 45, 48);
                        b.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
                    }
                }

                clickedBtn.BackColor = Color.FromArgb(0, 122, 204);
                clickedBtn.FlatAppearance.BorderColor = Color.White;
                lblStatus.Text = "Active TDP: " + tdp + " W (Applied via RyzenSMU & Lenovo EC)";
            }
            catch (Exception ex)
            {
                LogManager.LogError("Failed to apply TDP: " + ex.Message);
                MessageBox.Show("Error applying TDP: " + ex.Message, "TDP Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
