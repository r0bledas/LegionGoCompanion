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
        private Label lblStatus;
        private FlowLayoutPanel flowPanel;

        public TdpPowerView()
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
            this.lblTitle.Text = "TDP & Power Limit Presets";
            this.lblTitle.Font = new Font(this.Font.FontFamily, 11F, FontStyle.Bold);
            this.lblTitle.Location = new Point(15, 15);
            this.lblTitle.AutoSize = true;

            // Status Label
            this.lblStatus.Text = "Active TDP: Select a preset below";
            this.lblStatus.Location = new Point(16, 42);
            this.lblStatus.AutoSize = true;

            // GroupBox for Presets
            GroupBox grpPresets = new GroupBox
            {
                Text = "Power Presets (Watts)",
                Location = new Point(15, 75),
                Size = new Size(780, 220),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            // FlowPanel for buttons
            this.flowPanel.Location = new Point(15, 25);
            this.flowPanel.Size = new Size(750, 180);
            this.flowPanel.AutoScroll = true;

            int[] presets = new int[] { 5, 10, 15, 20, 25, 30, 35, 40 };

            foreach (int tdp in presets)
            {
                Button btn = new Button
                {
                    Text = tdp.ToString() + " W",
                    Size = new Size(110, 48),
                    Font = new Font(this.Font.FontFamily, 11F, FontStyle.Bold),
                    Margin = new Padding(8),
                    Cursor = Cursors.Hand
                };

                int currentTdp = tdp;
                btn.Click += (s, e) => ApplyTdp(currentTdp, btn);
                this.flowPanel.Controls.Add(btn);
            }

            grpPresets.Controls.Add(this.flowPanel);

            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(grpPresets);

            this.ResumeLayout(false);
        }

        private void ApplyTdp(int tdp, Button clickedBtn)
        {
            try
            {
                LogManager.LogInformation("Applying TDP Preset: " + tdp + "W");

                // 1. Direct RyzenSMU application (PawnIO)
                PerformanceManager.SetTDP(tdp, true);

                // 2. Lenovo WMI native EC power limit & OEM power mode LED colors
                if (IDevice.GetCurrent() is LegionGo lego)
                {
                    // Lenovo Legion Go OEM Power Button LED Mapping:
                    // Quiet (Blue) = 8W - 10W (0x01)
                    // Balanced (White) = 15W (0x02)
                    // Performance (Red) = 25W - 30W (0x03)
                    // Custom (Purple) = Any other custom wattage (0xFF)
                    int oemMode = (int)LegionGo.LegionMode.Custom;
                    if (tdp <= 10) oemMode = (int)LegionGo.LegionMode.Quiet;
                    else if (tdp == 15) oemMode = (int)LegionGo.LegionMode.Balanced;
                    else if (tdp == 25 || tdp == 30) oemMode = (int)LegionGo.LegionMode.Performance;

                    lego.SetSmartFanMode(oemMode);
                    lego.set_long_limit(tdp);
                    lego.set_short_limit(tdp);
                }

                // Update UI button highlights
                foreach (Control ctrl in flowPanel.Controls)
                {
                    if (ctrl is Button b)
                    {
                        b.BackColor = Color.FromArgb(245, 245, 245);
                        b.ForeColor = Color.Black;
                        b.FlatAppearance.BorderColor = Color.FromArgb(210, 210, 210);
                    }
                }

                clickedBtn.BackColor = Color.FromArgb(0, 102, 204);
                clickedBtn.ForeColor = Color.White;
                clickedBtn.FlatAppearance.BorderColor = Color.FromArgb(0, 80, 180);
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
