using System;
using System.Drawing;
using System.Windows.Forms;

namespace HandheldCompanion.Views
{
    public class MainForm : Form
    {
        private Panel sidebarPanel;
        private Panel contentPanel;
        private Button btnPower;
        private Button btnFans;
        private Button btnController;
        private Button btnSettings;
        private Label lblTitle;

        public MainForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.sidebarPanel = new Panel();
            this.contentPanel = new Panel();
            this.btnPower = new Button();
            this.btnFans = new Button();
            this.btnController = new Button();
            this.btnSettings = new Button();
            this.lblTitle = new Label();

            this.SuspendLayout();

            // Form
            this.Text = "Legion Go Companion";
            this.Size = new Size(1000, 650);
            this.BackColor = Color.White;
            this.ForeColor = Color.Black;
            this.Font = new Font("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Point);
            this.StartPosition = FormStartPosition.CenterScreen;

            // Sidebar
            this.sidebarPanel.Dock = DockStyle.Left;
            this.sidebarPanel.Width = 220;
            this.sidebarPanel.BackColor = Color.FromArgb(240, 240, 240);
            
            this.lblTitle.Text = "LEGION GO";
            this.lblTitle.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
            this.lblTitle.Dock = DockStyle.Top;
            this.lblTitle.Height = 70;
            this.lblTitle.TextAlign = ContentAlignment.MiddleCenter;
            this.lblTitle.ForeColor = Color.FromArgb(0, 102, 204);

            this.sidebarPanel.Controls.Add(this.btnSettings);
            this.sidebarPanel.Controls.Add(this.btnController);
            this.sidebarPanel.Controls.Add(this.btnFans);
            this.sidebarPanel.Controls.Add(this.btnPower);
            this.sidebarPanel.Controls.Add(this.lblTitle);

            // Buttons Helper
            void StyleButton(Button btn, string text)
            {
                btn.Text = text;
                btn.Dock = DockStyle.Top;
                btn.Height = 65;
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderSize = 0;
                btn.TextAlign = ContentAlignment.MiddleLeft;
                btn.Padding = new Padding(20, 0, 0, 0);
                btn.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
                btn.ForeColor = Color.FromArgb(40, 40, 40);
                btn.BackColor = Color.FromArgb(240, 240, 240);
                btn.Cursor = Cursors.Hand;
            }

            StyleButton(this.btnPower, "TDP & Power");
            StyleButton(this.btnFans, "Fan Control");
            StyleButton(this.btnController, "Controller");
            StyleButton(this.btnSettings, "Settings");

            // Content Panel
            this.contentPanel.Dock = DockStyle.Fill;
            this.contentPanel.BackColor = Color.White;
            this.contentPanel.Padding = new Padding(20);

            // Navigation actions
            this.btnPower.Click += (s, e) => ShowView(new TdpPowerView());
            this.btnFans.Click += (s, e) => ShowView(new FanControlView());
            this.btnController.Click += (s, e) => ShowView(new ControllerView());
            this.btnSettings.Click += (s, e) => ShowView(new SettingsView());

            this.Controls.Add(this.contentPanel);
            this.Controls.Add(this.sidebarPanel);

            ShowView(new TdpPowerView());

            this.ResumeLayout(false);
        }

        private void ShowView(UserControl view)
        {
            this.contentPanel.Controls.Clear();
            view.Dock = DockStyle.Fill;
            this.contentPanel.Controls.Add(view);
        }
    }
}
