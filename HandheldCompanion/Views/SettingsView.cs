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
        private Label lblStatus;

        public SettingsView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            this.Dock = DockStyle.Fill;
            this.AutoScroll = true;
            this.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);

            // Status Bar at the top
            Panel statusPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = LogicalToDeviceUnits(34),
                Padding = new Padding(LogicalToDeviceUnits(12), LogicalToDeviceUnits(6), LogicalToDeviceUnits(12), 0)
            };

            this.lblStatus = new Label
            {
                Text = "Hardware & Companion Settings",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                AutoSize = true,
                Dock = DockStyle.Left
            };
            statusPanel.Controls.Add(this.lblStatus);

            TableLayoutPanel tableLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(10, 0, 10, 10),
                AutoScroll = true
            };
            tableLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tableLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // GroupBox 1: Hardware & Power Options
            GroupBox grpSettings = new GroupBox
            {
                Text = "Hardware & Battery Options",
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(8),
                Margin = new Padding(0, 4, 0, 8)
            };

            FlowLayoutPanel flowSettings = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(8)
            };

            CheckBox chkBattery = new CheckBox
            {
                Text = "80% Battery Limit",
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 9F, FontStyle.Regular),
                Margin = new Padding(4, 4, 4, 8),
                Cursor = Cursors.Hand,
                Checked = ManagerFactory.settingsManager.GetBoolean("BatteryChargeLimit")
            };

            chkBattery.CheckedChanged += (s, e) =>
            {
                ManagerFactory.settingsManager.SetProperty("BatteryChargeLimit", chkBattery.Checked);
                if (IDevice.GetCurrent() is LegionGo lego)
                {
                    lego.SetBatteryChargeLimit(chkBattery.Checked);
                }
                lblStatus.Text = chkBattery.Checked ? "80% Battery Limit: Enabled" : "80% Battery Limit: Disabled (100% Full Charge)";
            };

            CheckBox chkKeepRunning = new CheckBox
            {
                Text = "Keep running after closing the window (show menu bar / tray icon)",
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 9F, FontStyle.Regular),
                Margin = new Padding(4, 4, 4, 8),
                Cursor = Cursors.Hand,
                Checked = ManagerFactory.settingsManager.GetBoolean("CloseMinimises")
            };

            chkKeepRunning.CheckedChanged += (s, e) =>
            {
                ManagerFactory.settingsManager.SetProperty("CloseMinimises", chkKeepRunning.Checked);
                lblStatus.Text = chkKeepRunning.Checked
                    ? "Background mode active: Window will minimize to menu bar / tray icon on close."
                    : "Background mode disabled: Application will fully exit when window is closed.";
            };

            CheckBox chkRunAtStartup = new CheckBox
            {
                Text = "Run at Startup (elevated, no UAC prompt)",
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 9F, FontStyle.Regular),
                Margin = new Padding(4, 4, 4, 8),
                Cursor = Cursors.Hand,
                Checked = ManagerFactory.settingsManager.GetBoolean("RunAtStartup")
            };

            chkRunAtStartup.CheckedChanged += (s, e) =>
            {
                ManagerFactory.settingsManager.SetProperty("RunAtStartup", chkRunAtStartup.Checked);
                lblStatus.Text = chkRunAtStartup.Checked
                    ? "Startup enabled: App will launch automatically at login with highest privileges."
                    : "Startup disabled: App will not launch at login.";
            };

            CheckBox chkStartMinimized = new CheckBox
            {
                Text = "Silent Launch on Startup (start minimized without window popping up)",
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 9F, FontStyle.Regular),
                Margin = new Padding(4, 4, 4, 8),
                Cursor = Cursors.Hand,
                Checked = ManagerFactory.settingsManager.GetBoolean("StartMinimized")
            };

            chkStartMinimized.CheckedChanged += (s, e) =>
            {
                ManagerFactory.settingsManager.SetProperty("StartMinimized", chkStartMinimized.Checked);
                lblStatus.Text = chkStartMinimized.Checked
                    ? "Silent launch enabled: Window will not pop up on startup (runs silently in tray/background)."
                    : "Silent launch disabled: Window will pop up on screen on startup.";
            };

            CheckBox chkDesktopOnStart = new CheckBox
            {
                Text = "Desktop Layout on Startup (if unchecked, defaults to x360)",
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 9F, FontStyle.Regular),
                Margin = new Padding(4, 4, 4, 8),
                Cursor = Cursors.Hand,
                Checked = ManagerFactory.settingsManager.GetBoolean("DesktopLayoutOnStart")
            };

            chkDesktopOnStart.CheckedChanged += (s, e) =>
            {
                ManagerFactory.settingsManager.SetProperty("DesktopLayoutOnStart", chkDesktopOnStart.Checked);
                lblStatus.Text = chkDesktopOnStart.Checked
                    ? "Startup mode: Desktop (sticks → mouse, no gamepad exposed)"
                    : "Startup mode: x360 (Xbox 360 virtual controller)";
            };

            flowSettings.Controls.Add(chkBattery);
            flowSettings.Controls.Add(chkKeepRunning);
            flowSettings.Controls.Add(chkRunAtStartup);
            flowSettings.Controls.Add(chkStartMinimized);
            flowSettings.Controls.Add(chkDesktopOnStart);
            grpSettings.Controls.Add(flowSettings);

            // GroupBox 2: Utilities & Diagnostics
            GroupBox grpUtilities = new GroupBox
            {
                Text = "Utilities & Diagnostics",
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(8),
                Margin = new Padding(0, 4, 0, 8)
            };

            FlowLayoutPanel flowUtils = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = true,
                Padding = new Padding(4)
            };

            Button btnLogs = new Button
            {
                Text = "Open Logs",
                Size = LogicalToDeviceUnits(new Size(115, 32)),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Margin = new Padding(LogicalToDeviceUnits(3)),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.System
            };
            btnLogs.Click += (s, e) =>
            {
                string logPath = Environment.GetEnvironmentVariable("LOG_PATH") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HandheldCompanion", "logs");
                if (Directory.Exists(logPath))
                {
                    Process.Start("explorer.exe", logPath);
                }
            };

            Button btnRestart = new Button
            {
                Text = "Restart App",
                Size = LogicalToDeviceUnits(new Size(115, 32)),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Margin = new Padding(LogicalToDeviceUnits(3)),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.System
            };
            btnRestart.Click += (s, e) =>
            {
                Application.Restart();
                Environment.Exit(0);
            };

            flowUtils.Controls.Add(btnLogs);
            flowUtils.Controls.Add(btnRestart);
            grpUtilities.Controls.Add(flowUtils);

            tableLayout.Controls.Add(grpSettings, 0, 0);
            tableLayout.Controls.Add(grpUtilities, 0, 1);

            this.Controls.Add(tableLayout);
            this.Controls.Add(statusPanel);

            this.ResumeLayout(false);
        }
    }
}
