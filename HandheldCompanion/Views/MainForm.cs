using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using HandheldCompanion.Managers;
using HandheldCompanion.Shared;

namespace HandheldCompanion.Views
{
    public class MainForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private Panel bottomStatusPanel;
        private Label lblAdminStatus;
        private Button btnRestartAdmin;
        private Label lblSystemHealth;
        private System.Windows.Forms.Timer diagnosticsTimer;

        private TabControl mainTabControl;
        private TabPage tabGeneral;
        private TabPage tabController;
        private TabPage tabSettings;

        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private bool isExiting = false;

        private EventWaitHandle? showWindowEvent;
        private Thread? showWindowThread;
        private volatile bool isListeningForShow = true;

        public MainForm()
        {
            InitializeComponent();
            InitializeTrayIcon();
            StartShowWindowListener();
            StartDiagnosticsTimer();
        }

        private void InitializeComponent()
        {
            this.bottomStatusPanel = new Panel();
            this.lblAdminStatus = new Label();
            this.btnRestartAdmin = new Button();
            this.lblSystemHealth = new Label();

            this.mainTabControl = new TabControl();
            this.tabGeneral = new TabPage();
            this.tabController = new TabPage();
            this.tabSettings = new TabPage();

            this.SuspendLayout();

            // Form properties & High-DPI Scaling for Legion Go 2K display
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            this.Text = "Legion Go Companion";
            this.Size = new Size(540, 390);
            this.MinimumSize = new Size(420, 280);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            // ==========================================
            // Bottom Status Panel: Minimal, Clean, No Emojis
            // ==========================================
            this.bottomStatusPanel.Dock = DockStyle.Bottom;
            this.bottomStatusPanel.Height = 24;
            this.bottomStatusPanel.BackColor = Color.FromArgb(245, 246, 248);
            this.bottomStatusPanel.Padding = new Padding(8, 0, 8, 0);
            this.bottomStatusPanel.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(225, 228, 232), 1);
                e.Graphics.DrawLine(pen, 0, 0, bottomStatusPanel.Width, 0);
            };

            bool isAdmin = Program.IsAdministrator();

            // 1. Admin status label (No emojis)
            this.lblAdminStatus.AutoSize = true;
            this.lblAdminStatus.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            this.lblAdminStatus.Location = new Point(8, 4);
            if (isAdmin)
            {
                this.lblAdminStatus.Text = "Admin: Elevated";
                this.lblAdminStatus.ForeColor = Color.FromArgb(34, 139, 34);
            }
            else
            {
                this.lblAdminStatus.Text = "Admin: No";
                this.lblAdminStatus.ForeColor = Color.FromArgb(200, 30, 30);
            }
            this.bottomStatusPanel.Controls.Add(this.lblAdminStatus);

            // 2. Restart as Admin button (only if not running elevated)
            this.btnRestartAdmin.Text = "Restart as Admin";
            this.btnRestartAdmin.Font = new Font("Segoe UI", 8F, FontStyle.Regular);
            this.btnRestartAdmin.Size = new Size(100, 20);
            this.btnRestartAdmin.Location = new Point(85, 2);
            this.btnRestartAdmin.FlatStyle = FlatStyle.Flat;
            this.btnRestartAdmin.FlatAppearance.BorderSize = 0;
            this.btnRestartAdmin.BackColor = Color.FromArgb(0, 120, 215);
            this.btnRestartAdmin.ForeColor = Color.White;
            this.btnRestartAdmin.Cursor = Cursors.Hand;
            this.btnRestartAdmin.Visible = !isAdmin;
            this.btnRestartAdmin.Click += (s, e) =>
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = Application.ExecutablePath,
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    Process.Start(psi);
                    this.ExitApplication();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not start elevated process: " + ex.Message, "Restart as Admin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            this.bottomStatusPanel.Controls.Add(this.btnRestartAdmin);

            // 3. System Diagnostics Health label (Clean text, no emojis)
            this.lblSystemHealth.AutoSize = true;
            this.lblSystemHealth.Font = new Font("Segoe UI", 8F, FontStyle.Regular);
            this.lblSystemHealth.ForeColor = Color.FromArgb(80, 85, 95);
            this.lblSystemHealth.Dock = DockStyle.Right;
            this.lblSystemHealth.TextAlign = ContentAlignment.MiddleRight;
            this.lblSystemHealth.Padding = new Padding(0, 4, 4, 0);
            this.lblSystemHealth.Text = "Checking health...";
            this.bottomStatusPanel.Controls.Add(this.lblSystemHealth);

            // TabControl
            this.mainTabControl.Dock = DockStyle.Fill;
            this.mainTabControl.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            this.mainTabControl.Padding = new Point(12, 3);

            // Tab 1: General (Controller, TDP, Fans consolidated)
            this.tabGeneral.Text = "General";
            this.tabGeneral.Padding = new Padding(2);
            this.tabGeneral.UseVisualStyleBackColor = true;
            GeneralView generalView = new GeneralView { Dock = DockStyle.Fill };
            this.tabGeneral.Controls.Add(generalView);

            // Tab 2: Controller (Emulation & Button Remapping)
            this.tabController.Text = "Controller";
            this.tabController.Padding = new Padding(2);
            this.tabController.UseVisualStyleBackColor = true;
            ControllerView controllerView = new ControllerView { Dock = DockStyle.Fill };
            this.tabController.Controls.Add(controllerView);

            // Tab 3: Settings (Battery cap, Logs, Restart)
            this.tabSettings.Text = "Settings";
            this.tabSettings.Padding = new Padding(2);
            this.tabSettings.UseVisualStyleBackColor = true;
            SettingsView settingsView = new SettingsView { Dock = DockStyle.Fill };
            this.tabSettings.Controls.Add(settingsView);

            // Add tabs
            this.mainTabControl.Controls.Add(this.tabGeneral);
            this.mainTabControl.Controls.Add(this.tabController);
            this.mainTabControl.Controls.Add(this.tabSettings);

            this.Controls.Add(this.bottomStatusPanel);
            this.Controls.Add(this.mainTabControl);
            this.mainTabControl.BringToFront();

            this.Resize += (s, e) => UpdateDiagnostics();

            this.ResumeLayout(false);
        }

        private void StartDiagnosticsTimer()
        {
            diagnosticsTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            diagnosticsTimer.Tick += (s, e) => UpdateDiagnostics();
            diagnosticsTimer.Start();
            UpdateDiagnostics();
        }

        private void UpdateDiagnostics()
        {
            try
            {
                bool vigemOk = File.Exists(Path.Combine(Environment.SystemDirectory, "drivers", "ViGEmBus.sys")) || VirtualManager.IsInitialized;
                bool hidHideOk = File.Exists(Path.Combine(Environment.SystemDirectory, "drivers", "HidHide.sys"));
                bool controllerOk = ControllerManager.HasTargetController;

                string vigemStr = vigemOk ? "ViGEm: OK" : "ViGEm: Missing";
                string hidHideStr = hidHideOk ? "HidHide: OK" : "HidHide: Missing";
                string controllerStr = controllerOk ? "Controller: OK" : "Controller: Disconnected";

                this.lblSystemHealth.Text = $"{vigemStr}  |  {hidHideStr}  |  {controllerStr}";
            }
            catch { }
        }

        private void StartShowWindowListener()
        {
            try
            {
                try
                {
                    var security = new System.Security.AccessControl.EventWaitHandleSecurity();
                    security.AddAccessRule(new System.Security.AccessControl.EventWaitHandleAccessRule(
                        new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null),
                        System.Security.AccessControl.EventWaitHandleRights.FullControl,
                        System.Security.AccessControl.AccessControlType.Allow));

                    showWindowEvent = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, Program.ShowWindowEventName, out _, security);
                }
                catch
                {
                    showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowWindowEventName);
                }

                showWindowThread = new Thread(() =>
                {
                    while (isListeningForShow)
                    {
                        try
                        {
                            if (showWindowEvent != null && showWindowEvent.WaitOne(1000))
                            {
                                if (isListeningForShow && !this.IsDisposed)
                                {
                                    this.BeginInvoke(new Action(() => RestoreFromTray()));
                                }
                            }
                        }
                        catch { }
                    }
                })
                {
                    IsBackground = true,
                    Priority = ThreadPriority.BelowNormal
                };
                showWindowThread.Start();
            }
            catch (Exception ex)
            {
                LogManager.LogError("Failed to start ShowWindowListener: {0}", ex.Message);
            }
        }

        private void InitializeTrayIcon()
        {
            this.trayMenu = new ContextMenuStrip();

            ToolStripMenuItem itemOpen = new ToolStripMenuItem("Open Legion Go Companion");
            itemOpen.Font = new Font(itemOpen.Font, FontStyle.Bold);
            itemOpen.Click += (s, e) => RestoreFromTray();

            ToolStripMenuItem itemRestart = new ToolStripMenuItem("Restart");
            itemRestart.Click += (s, e) => ExitApplication(restart: true);

            ToolStripMenuItem itemExit = new ToolStripMenuItem("Exit");
            itemExit.Click += (s, e) => ExitApplication(restart: false);

            this.trayMenu.Items.Add(itemOpen);
            this.trayMenu.Items.Add(new ToolStripSeparator());
            this.trayMenu.Items.Add(itemRestart);
            this.trayMenu.Items.Add(itemExit);

            this.trayIcon = new NotifyIcon
            {
                Text = "Legion Go Companion",
                ContextMenuStrip = this.trayMenu
            };

            Icon? appIcon = null;
            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "icon.ico");
                if (File.Exists(icoPath))
                {
                    appIcon = new Icon(icoPath);
                }
            }
            catch { }

            if (appIcon == null)
            {
                try
                {
                    appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }
                catch { }
            }

            appIcon ??= SystemIcons.Application;

            this.Icon = appIcon;
            this.ShowInTaskbar = true;
            this.trayIcon.Icon = appIcon;

            this.trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    RestoreFromTray();
                }
            };
            this.trayIcon.DoubleClick += (s, e) => RestoreFromTray();
            this.trayIcon.Visible = true;
        }

        private static bool IsSilentStartupArg()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i].Trim().ToLowerInvariant();
                if (a == "--minimized" || a == "--silent" || a == "-minimized" || a == "-silent" || a == "/minimized" || a == "/silent")
                    return true;
            }
            return false;
        }

        private bool allowVisible = false;

        protected override void SetVisibleCore(bool value)
        {
            if (!allowVisible)
            {
                // Only suppress initial window visibility if launched with an explicit silent/minimized startup argument (e.g. from logon trigger)
                if (IsSilentStartupArg())
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
            if (this.trayIcon != null)
            {
                this.trayIcon.Visible = true;
            }
            if (!IsSilentStartupArg())
            {
                RestoreFromTray();
            }
        }

        public void RestoreFromTray()
        {
            allowVisible = true;
            if (!this.IsHandleCreated)
            {
                this.CreateHandle();
            }
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.WindowState = FormWindowState.Normal;
            }
            this.Show();
            this.Visible = true;
            if (this.trayIcon != null)
            {
                this.trayIcon.Visible = true;
            }
            this.BringToFront();
            this.Activate();
            try
            {
                SetForegroundWindow(this.Handle);
            }
            catch { }
        }

        public void ExitApplication(bool restart = false)
        {
            isExiting = true;
            isListeningForShow = false;
            diagnosticsTimer?.Stop();
            try { showWindowEvent?.Set(); showWindowEvent?.Dispose(); } catch { }

            if (this.trayIcon != null)
            {
                this.trayIcon.Visible = false;
                this.trayIcon.Dispose();
            }

            TrayCleaner.Clean();

            if (restart)
            {
                Application.Restart();
            }
            else
            {
                Application.Exit();
            }
            Environment.Exit(0);
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

            ExitApplication(restart: false);
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                isListeningForShow = false;
                diagnosticsTimer?.Dispose();
                try { showWindowEvent?.Set(); showWindowEvent?.Dispose(); } catch { }
                this.trayIcon?.Dispose();
                this.trayMenu?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
