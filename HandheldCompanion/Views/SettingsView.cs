using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using HandheldCompanion.Devices;
using HandheldCompanion.Devices.Lenovo;
using HandheldCompanion.Managers;
using HandheldCompanion.Shared;

namespace HandheldCompanion.Views
{
    public class SettingsView : UserControl
    {
        private Label lblTitle;
        private Label lblStatus;
        private FlowLayoutPanel flowPanel;

        public SettingsView()
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
            this.lblTitle.Text = "System & Legion Go Settings";
            this.lblTitle.Font = new Font("Segoe UI", 18F, FontStyle.Bold);
            this.lblTitle.Location = new Point(25, 20);
            this.lblTitle.Size = new Size(500, 35);

            // Status Label
            this.lblStatus.Text = "Lenovo Legion Go (Model 83E1) - Anti-Bloat Edition";
            this.lblStatus.Font = new Font("Segoe UI", 12F, FontStyle.Regular);
            this.lblStatus.ForeColor = Color.FromArgb(0, 102, 204);
            this.lblStatus.Location = new Point(28, 65);
            this.lblStatus.Size = new Size(600, 25);

            // FlowPanel for touch buttons
            this.flowPanel.Location = new Point(25, 105);
            this.flowPanel.Size = new Size(720, 450);
            this.flowPanel.AutoScroll = true;

            // Button 1: Battery Charge Limit (80%)
            AddToggleButton("BATTERY BYPASS\n(Limit to 80%)", (enabled) =>
            {
                if (IDevice.GetCurrent() is LegionGo lego)
                {
                    lego.SetBatteryChargeLimit(enabled);
                }
                lblStatus.Text = "Battery Limit: " + (enabled ? "Enabled (80% Cap)" : "Disabled (100% Charge)");
            });

            // Button 2: Open Logs
            AddAction("OPEN LOGS\nFOLDER", () =>
            {
                string logPath = Environment.GetEnvironmentVariable("LOG_PATH") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HandheldCompanion", "logs");
                if (System.IO.Directory.Exists(logPath))
                {
                    Process.Start("explorer.exe", logPath);
                }
            });

            // Button 3: Restart App
            AddAction("RESTART\nCOMPANION", () =>
            {
                Application.Restart();
                Environment.Exit(0);
            });

            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.flowPanel);

            this.ResumeLayout(false);
        }

        private void AddToggleButton(string text, Action<bool> onToggle)
        {
            bool state = false;
            Button btn = new Button
            {
                Text = text,
                Size = new Size(210, 100),
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(245, 245, 245),
                ForeColor = Color.Black,
                Margin = new Padding(10),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 2;
            btn.FlatAppearance.BorderColor = Color.FromArgb(210, 210, 210);

            btn.Click += (s, e) =>
            {
                state = !state;
                onToggle(state);
                btn.BackColor = state ? Color.FromArgb(0, 102, 204) : Color.FromArgb(245, 245, 245);
                btn.ForeColor = state ? Color.White : Color.Black;
                btn.FlatAppearance.BorderColor = state ? Color.FromArgb(0, 80, 180) : Color.FromArgb(210, 210, 210);
            };

            this.flowPanel.Controls.Add(btn);
        }

        private void AddAction(string text, Action onClick)
        {
            Button btn = new Button
            {
                Text = text,
                Size = new Size(210, 100),
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(245, 245, 245),
                ForeColor = Color.Black,
                Margin = new Padding(10),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 2;
            btn.FlatAppearance.BorderColor = Color.FromArgb(210, 210, 210);

            btn.Click += (s, e) => onClick();

            this.flowPanel.Controls.Add(btn);
        }
    }
}
