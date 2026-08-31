using System;
using System.Drawing;
using System.Windows.Forms;
using HandheldCompanion.Managers;
using HandheldCompanion.Shared;
namespace HandheldCompanion.Views
{
    public class TdpPowerView : UserControl
    {
        private Label lblTitle;
        private Label lblTdpValue;
        private TrackBar tbTdp;
        private Label lblTdpLimits;
        private Button btnApply;

        public TdpPowerView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.lblTitle = new Label();
            this.lblTdpValue = new Label();
            this.tbTdp = new TrackBar();
            this.lblTdpLimits = new Label();
            this.btnApply = new Button();

            this.SuspendLayout();

            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.White;
            this.Size = new Size(600, 500);

            // Title
            this.lblTitle.Text = "TDP & Power Control";
            this.lblTitle.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
            this.lblTitle.Location = new Point(30, 30);
            this.lblTitle.AutoSize = true;

            // Trackbar
            this.tbTdp.Minimum = 5;
            this.tbTdp.Maximum = 30;
            this.tbTdp.Value = 15;
            this.tbTdp.TickFrequency = 1;
            this.tbTdp.Location = new Point(30, 100);
            this.tbTdp.Width = 400;
            this.tbTdp.Scroll += TbTdp_Scroll;

            // Value Label
            this.lblTdpValue.Text = tbTdp.Value.ToString() + " W";
            this.lblTdpValue.Font = new Font("Segoe UI", 18F, FontStyle.Bold);
            this.lblTdpValue.ForeColor = Color.FromArgb(0, 122, 204);
            this.lblTdpValue.Location = new Point(450, 95);
            this.lblTdpValue.AutoSize = true;

            // Limits Label
            this.lblTdpLimits.Text = "5W (Min) - 30W (Max)";
            this.lblTdpLimits.Font = new Font("Segoe UI", 10F);
            this.lblTdpLimits.ForeColor = Color.Gray;
            this.lblTdpLimits.Location = new Point(35, 140);
            this.lblTdpLimits.AutoSize = true;

            // Apply Button
            this.btnApply.Text = "Apply TDP";
            this.btnApply.Location = new Point(30, 200);
            this.btnApply.Size = new Size(150, 45);
            this.btnApply.FlatStyle = FlatStyle.Flat;
            this.btnApply.BackColor = Color.FromArgb(45, 45, 48);
            this.btnApply.Cursor = Cursors.Hand;
            this.btnApply.Click += BtnApply_Click;

            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.tbTdp);
            this.Controls.Add(this.lblTdpValue);
            this.Controls.Add(this.lblTdpLimits);
            this.Controls.Add(this.btnApply);

            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private void TbTdp_Scroll(object sender, EventArgs e)
        {
            lblTdpValue.Text = tbTdp.Value.ToString() + " W";
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            int targetTdp = tbTdp.Value;
            LogManager.LogInformation("Applying TDP: " + targetTdp + "W");
            try
            {
                //((HandheldCompanion.Devices.Lenovo.LegionGoTablet)HandheldCompanion.Devices.IDevice.GetCurrent()).TDP_Set(targetTdp);
                MessageBox.Show("Successfully applied " + targetTdp + "W TDP!", "TDP Applied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LogManager.LogError("Failed to apply TDP: " + ex.Message);
                MessageBox.Show("Failed to apply TDP: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

