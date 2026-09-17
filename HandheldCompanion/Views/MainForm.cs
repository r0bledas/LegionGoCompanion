using System;
using System.Drawing;
using System.Windows.Forms;
using HandheldCompanion.Managers;

namespace HandheldCompanion.Views
{
    public class MainForm : Form
    {
        private TabControl mainTabControl;
        private TabPage tabGeneral;
        private TabPage tabSettings;

        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private bool isExiting = false;

        public MainForm()
        {
            InitializeComponent();
            InitializeTrayIcon();
        }

        private void InitializeComponent()
        {
            this.mainTabControl = new TabControl();
            this.tabGeneral = new TabPage();
            this.tabSettings = new TabPage();

            this.SuspendLayout();

            // Form properties & High-DPI Scaling for Legion Go 2K display
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            this.Text = "Legion Go Companion";
            this.Size = new Size(700, 520);
            this.MinimumSize = new Size(580, 420);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);

            // TabControl
            this.mainTabControl.Dock = DockStyle.Fill;
            this.mainTabControl.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            this.mainTabControl.Padding = new Point(14, 4);

            // Tab 1: General (Controller, TDP, Fans consolidated)
            this.tabGeneral.Text = "General";
            this.tabGeneral.Padding = new Padding(4);
            this.tabGeneral.UseVisualStyleBackColor = true;
            GeneralView generalView = new GeneralView { Dock = DockStyle.Fill };
            this.tabGeneral.Controls.Add(generalView);

            // Tab 2: Settings (Battery cap, Logs, Restart)
            this.tabSettings.Text = "Settings";
            this.tabSettings.Padding = new Padding(4);
            this.tabSettings.UseVisualStyleBackColor = true;
            SettingsView settingsView = new SettingsView { Dock = DockStyle.Fill };
            this.tabSettings.Controls.Add(settingsView);

            // Add tabs
            this.mainTabControl.Controls.Add(this.tabGeneral);
            this.mainTabControl.Controls.Add(this.tabSettings);

            this.Controls.Add(this.mainTabControl);

            this.ResumeLayout(false);
        }

        private void InitializeTrayIcon()
        {
            this.trayMenu = new ContextMenuStrip();

            ToolStripMenuItem itemOpen = new ToolStripMenuItem("Open Legion Go Companion");
            itemOpen.Font = new Font(itemOpen.Font, FontStyle.Bold);
            itemOpen.Click += (s, e) => RestoreFromTray();

            ToolStripMenuItem itemRestart = new ToolStripMenuItem("Restart");
            itemRestart.Click += (s, e) =>
            {
                Application.Restart();
                Environment.Exit(0);
            };

            ToolStripMenuItem itemExit = new ToolStripMenuItem("Exit");
            itemExit.Click += (s, e) => ExitApplication();

            this.trayMenu.Items.Add(itemOpen);
            this.trayMenu.Items.Add(new ToolStripSeparator());
            this.trayMenu.Items.Add(itemRestart);
            this.trayMenu.Items.Add(itemExit);

            this.trayIcon = new NotifyIcon
            {
                Text = "Legion Go Companion",
                ContextMenuStrip = this.trayMenu
            };

            try
            {
                Icon appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
                this.Icon = appIcon;
                this.trayIcon.Icon = appIcon;
            }
            catch
            {
                this.Icon = SystemIcons.Application;
                this.trayIcon.Icon = SystemIcons.Application;
            }

            this.trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    RestoreFromTray();
                }
            };
            this.trayIcon.DoubleClick += (s, e) => RestoreFromTray();

            // Set initial visibility based on CloseMinimises setting
            UpdateTrayIconVisibility();

            // Listen for setting changes
            ManagerFactory.settingsManager.SettingValueChanged += (name, value, temp, init) =>
            {
                if (name == "CloseMinimises")
                {
                    if (this.IsHandleCreated && !this.IsDisposed)
                    {
                        this.BeginInvoke((MethodInvoker)(() => UpdateTrayIconVisibility()));
                    }
                }
            };
        }

        private void UpdateTrayIconVisibility()
        {
            bool keepRunning = ManagerFactory.settingsManager.GetBoolean("CloseMinimises");
            this.trayIcon.Visible = keepRunning;
        }

        private bool allowVisible = false;

        protected override void SetVisibleCore(bool value)
        {
            if (!allowVisible)
            {
                bool startMinimized = ManagerFactory.settingsManager.GetBoolean("StartMinimized");
                if (startMinimized)
                {
                    value = false;
                    if (!this.IsHandleCreated) CreateHandle();
                }
                else
                {
                    allowVisible = true;
                }
            }
            base.SetVisibleCore(value);
        }

        protected override void WndProc(ref Message m)
        {
            if (Program.WM_SHOWME != 0 && (uint)m.Msg == Program.WM_SHOWME)
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(() => RestoreFromTray()));
                }
                else
                {
                    RestoreFromTray();
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            bool startMinimized = ManagerFactory.settingsManager.GetBoolean("StartMinimized");
            if (startMinimized)
            {
                this.trayIcon.Visible = true;
            }
            else
            {
                RestoreFromTray();
            }
        }

        public void RestoreFromTray()
        {
            allowVisible = true;
            this.Show();
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.WindowState = FormWindowState.Normal;
            }
            this.Visible = true;
            this.Activate();
            this.BringToFront();
        }

        public void ExitApplication()
        {
            isExiting = true;
            if (this.trayIcon != null)
            {
                this.trayIcon.Visible = false;
                this.trayIcon.Dispose();
            }
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!isExiting && e.CloseReason == CloseReason.UserClosing)
            {
                bool keepRunning = ManagerFactory.settingsManager.GetBoolean("CloseMinimises");
                if (keepRunning)
                {
                    e.Cancel = true;
                    this.Hide();
                    if (this.trayIcon != null)
                    {
                        this.trayIcon.Visible = true;
                        this.trayIcon.ShowBalloonTip(1200, "Legion Go Companion", "Running in background (system tray)", ToolTipIcon.Info);
                    }
                    return;
                }
            }

            if (this.trayIcon != null)
            {
                this.trayIcon.Visible = false;
                this.trayIcon.Dispose();
            }

            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.trayIcon?.Dispose();
                this.trayMenu?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
