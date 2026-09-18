using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using HandheldCompanion.Devices;
using HandheldCompanion.Devices.Lenovo;
using HandheldCompanion.Managers;
using HandheldCompanion.Shared;

namespace HandheldCompanion.Views
{
    public class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            this.AutoScaleMode = AutoScaleMode.Inherit;
            this.Dock = DockStyle.Fill;
            this.AutoScroll = true;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            // Main scrollable content panel
            Panel contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(6, 4, 6, 6)
            };

            // GroupBox 1: Hardware & Power Options
            GroupBox grpSettings = new GroupBox
            {
                Text = "Hardware & Battery Options",
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(6, 4, 6, 6),
                Margin = new Padding(0, 0, 0, 6)
            };

            FlowLayoutPanel flowSettings = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(4)
            };

            CheckBox chkBattery = new CheckBox
            {
                Text = "80% Battery Limit",
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 8.5F, FontStyle.Regular),
                Margin = new Padding(2, 2, 2, 4),
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
            };

            CheckBox chkKeepRunning = new CheckBox
            {
                Text = "Keep running after closing the window (show menu bar / tray icon)",
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 8.5F, FontStyle.Regular),
                Margin = new Padding(2, 2, 2, 4),
                Cursor = Cursors.Hand,
                Checked = ManagerFactory.settingsManager.GetBoolean("CloseMinimises")
            };

            chkKeepRunning.CheckedChanged += (s, e) =>
            {
                ManagerFactory.settingsManager.SetProperty("CloseMinimises", chkKeepRunning.Checked);
            };

            CheckBox chkRunAtStartup = new CheckBox
            {
                Text = "Run at Startup (elevated, no UAC prompt)",
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 8.5F, FontStyle.Regular),
                Margin = new Padding(2, 2, 2, 4),
                Cursor = Cursors.Hand,
                Checked = ManagerFactory.settingsManager.GetBoolean("RunAtStartup")
            };

            chkRunAtStartup.CheckedChanged += (s, e) =>
            {
                ManagerFactory.settingsManager.SetProperty("RunAtStartup", chkRunAtStartup.Checked);
            };

            CheckBox chkStartMinimized = new CheckBox
            {
                Text = "Silent Launch on Startup (start minimized without window popping up)",
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 8.5F, FontStyle.Regular),
                Margin = new Padding(2, 2, 2, 4),
                Cursor = Cursors.Hand,
                Checked = ManagerFactory.settingsManager.GetBoolean("StartMinimized")
            };

            chkStartMinimized.CheckedChanged += (s, e) =>
            {
                ManagerFactory.settingsManager.SetProperty("StartMinimized", chkStartMinimized.Checked);
            };

            CheckBox chkDesktopOnStart = new CheckBox
            {
                Text = "Desktop Layout on Startup (if unchecked, defaults to x360)",
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 8.5F, FontStyle.Regular),
                Margin = new Padding(2, 2, 2, 4),
                Cursor = Cursors.Hand,
                Checked = ManagerFactory.settingsManager.GetBoolean("DesktopLayoutOnStart")
            };

            chkDesktopOnStart.CheckedChanged += (s, e) =>
            {
                ManagerFactory.settingsManager.SetProperty("DesktopLayoutOnStart", chkDesktopOnStart.Checked);
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
                Padding = new Padding(6, 4, 6, 6),
                Margin = new Padding(0, 0, 0, 6)
            };

            FlowLayoutPanel flowUtils = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = true,
                Padding = new Padding(2)
            };

            Button btnLogs = new Button
            {
                Text = "Open Logs",
                Size = new Size(95, 26),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Margin = new Padding(2),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(245, 247, 250),
                ForeColor = Color.FromArgb(30, 35, 45)
            };
            btnLogs.FlatAppearance.BorderColor = Color.FromArgb(210, 215, 222);
            btnLogs.FlatAppearance.BorderSize = 1;
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
                Size = new Size(95, 26),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Margin = new Padding(2),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(245, 247, 250),
                ForeColor = Color.FromArgb(30, 35, 45)
            };
            btnRestart.FlatAppearance.BorderColor = Color.FromArgb(210, 215, 222);
            btnRestart.FlatAppearance.BorderSize = 1;
            btnRestart.Click += (s, e) =>
            {
                Application.Restart();
                Environment.Exit(0);
            };

            flowUtils.Controls.Add(btnLogs);
            flowUtils.Controls.Add(btnRestart);
            grpUtilities.Controls.Add(flowUtils);

            // GroupBox 3: Software Updates
            GroupBox grpUpdates = new GroupBox
            {
                Text = "Software Updates",
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(6, 4, 6, 6),
                Margin = new Padding(0, 0, 0, 6)
            };

            FlowLayoutPanel flowUpdates = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(4)
            };

            Label lblCurrentVersion = new Label
            {
                Text = $"Current Version: v{UpdateService.CurrentVersion.ToString(3)}",
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 8.5F, FontStyle.Bold),
                Margin = new Padding(2, 2, 2, 3)
            };

            Label lblUpdateStatus = new Label
            {
                Text = "Click 'Check for Updates' to search GitHub for newer releases.",
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 8F, FontStyle.Regular),
                ForeColor = Color.DimGray,
                Margin = new Padding(2, 1, 2, 4)
            };

            ProgressBar progressUpdate = new ProgressBar
            {
                Size = new Size(260, 14),
                Visible = false,
                Margin = new Padding(2, 1, 2, 4)
            };

            FlowLayoutPanel flowUpdateButtons = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = true,
                Margin = new Padding(0)
            };

            Button btnCheckUpdates = new Button
            {
                Text = "Check for Updates",
                Size = new Size(125, 26),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Margin = new Padding(2),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.System
            };

            Button btnDownloadInstall = new Button
            {
                Text = "Download & Install",
                Size = new Size(135, 26),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Margin = new Padding(2),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.System,
                Enabled = false,
                Visible = false
            };

            ReleaseInfo? foundRelease = null;

            btnCheckUpdates.Click += async (s, e) =>
            {
                btnCheckUpdates.Enabled = false;
                lblUpdateStatus.ForeColor = Color.Black;
                lblUpdateStatus.Text = "Checking GitHub repository for latest release...";
                progressUpdate.Visible = false;
                btnDownloadInstall.Visible = false;

                try
                {
                    var release = await UpdateService.CheckForUpdateAsync();
                    if (release == null)
                    {
                        lblUpdateStatus.ForeColor = Color.DarkRed;
                        lblUpdateStatus.Text = "Could not check for updates (network or GitHub API error).";
                    }
                    else if (release.IsNewer)
                    {
                        foundRelease = release;
                        lblUpdateStatus.ForeColor = Color.DarkGreen;
                        string assetStr = !string.IsNullOrEmpty(release.AssetName) ? $" ({release.AssetName})" : "";
                        lblUpdateStatus.Text = $"Update available: {release.TagName}{assetStr}! Click 'Download & Install' to proceed.";
                        btnDownloadInstall.Visible = true;
                        btnDownloadInstall.Enabled = true;
                    }
                    else
                    {
                        lblUpdateStatus.ForeColor = Color.DarkSlateGray;
                        lblUpdateStatus.Text = $"You are up to date! Latest release on GitHub is {release.TagName}.";
                    }
                }
                catch (Exception ex)
                {
                    lblUpdateStatus.ForeColor = Color.DarkRed;
                    lblUpdateStatus.Text = $"Update check failed: {ex.Message}";
                }
                finally
                {
                    btnCheckUpdates.Enabled = true;
                }
            };

            btnDownloadInstall.Click += async (s, e) =>
            {
                if (foundRelease == null || string.IsNullOrEmpty(foundRelease.DownloadUrl))
                    return;

                btnCheckUpdates.Enabled = false;
                btnDownloadInstall.Enabled = false;
                progressUpdate.Value = 0;
                progressUpdate.Visible = true;
                lblUpdateStatus.ForeColor = Color.Black;
                lblUpdateStatus.Text = "Starting download...";

                var progress = new Progress<(long downloaded, long total, int percent)>(report =>
                {
                    progressUpdate.Value = Math.Clamp(report.percent, 0, 100);
                    double mbDownloaded = report.downloaded / (1024.0 * 1024.0);
                    double mbTotal = report.total / (1024.0 * 1024.0);
                    lblUpdateStatus.Text = report.total > 0
                        ? $"Downloading: {report.percent}% ({mbDownloaded:F1} MB / {mbTotal:F1} MB)..."
                        : $"Downloading: {mbDownloaded:F1} MB...";
                });

                try
                {
                    string downloadedPath = await UpdateService.DownloadUpdateAsync(foundRelease, progress);
                    lblUpdateStatus.ForeColor = Color.DarkGreen;
                    lblUpdateStatus.Text = "Download complete! Launching installer...";

                    await Task.Delay(1000);
                    UpdateService.LaunchInstallerAndExit(downloadedPath, silent: false);
                }
                catch (Exception ex)
                {
                    lblUpdateStatus.ForeColor = Color.DarkRed;
                    lblUpdateStatus.Text = $"Download failed: {ex.Message}";
                    btnCheckUpdates.Enabled = true;
                    btnDownloadInstall.Enabled = true;
                }
            };

            flowUpdateButtons.Controls.Add(btnCheckUpdates);
            flowUpdateButtons.Controls.Add(btnDownloadInstall);

            flowUpdates.Controls.Add(lblCurrentVersion);
            flowUpdates.Controls.Add(lblUpdateStatus);
            flowUpdates.Controls.Add(progressUpdate);
            flowUpdates.Controls.Add(flowUpdateButtons);
            grpUpdates.Controls.Add(flowUpdates);

            contentPanel.Controls.Add(grpUtilities);
            contentPanel.Controls.Add(grpUpdates);
            contentPanel.Controls.Add(grpSettings);

            this.Controls.Add(contentPanel);

            this.ResumeLayout(false);
        }
    }
}
