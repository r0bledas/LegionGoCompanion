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

        private Panel topHeaderPanel;
        private Label lblAdminStatus;
        private Button btnRestartAdmin;
        private Label lblSystemHealth;
        private System.Windows.Forms.Timer diagnosticsTimer;

        private TabControl mainTabControl;
        private TabPage tabGeneral;
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
            this.topHeaderPanel = new Panel();
            this.lblAdminStatus = new Label();
            this.btnRestartAdmin = new Button();
            this.lblSystemHealth = new Label();

            this.mainTabControl = new TabControl();
            this.tabGeneral = new TabPage();
            this.tabSettings = new TabPage();

            this.SuspendLayout();

            // Form properties & High-DPI Scaling for Legion Go 2K display
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            this.Text = "Legion Go Companion";
            this.Size = new Size(720, 560);
            this.MinimumSize = new Size(600, 460);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);

            // ==========================================
            // Top Header Panel: Admin Indicator & System Status
            // ==========================================
            this.topHeaderPanel.Dock = DockStyle.Top;
            this.topHeaderPanel.Height = LogicalToDeviceUnits(44);
            this.topHeaderPanel.BackColor = Color.FromArgb(245, 247, 250);
            this.topHeaderPanel.Padding = new Padding(LogicalToDeviceUnits(12), LogicalToDeviceUnits(8), LogicalToDeviceUnits(12), LogicalToDeviceUnits(8));
            this.topHeaderPanel.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(220, 224, 230), 1);
                e.Graphics.DrawLine(pen, 0, topHeaderPanel.Height - 1, topHeaderPanel.Width, topHeaderPanel.Height - 1);
            };

            bool isAdmin = Program.IsAdministrator();

            // 1. Admin status label
            this.lblAdminStatus.AutoSize = true;
            this.lblAdminStatus.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            this.lblAdminStatus.Location = new Point(LogicalToDeviceUnits(10), LogicalToDeviceUnits(11));
            if (isAdmin)
            {
                this.lblAdminStatus.Text = "🛡️ Admin: YES (Elevated)";
                this.lblAdminStatus.ForeColor = Color.FromArgb(34, 139, 34);
            }
            else
            {
                this.lblAdminStatus.Text = "⚠️ Admin: NO (Features Limited)";
                this.lblAdminStatus.ForeColor = Color.FromArgb(211, 47, 47);
            }
            this.topHeaderPanel.Controls.Add(this.lblAdminStatus);

            // 2. Restart as Admin button (only if not running elevated)
            this.btnRestartAdmin.Text = "🛡️ Restart as Admin";
            this.btnRestartAdmin.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            this.btnRestartAdmin.Size = new Size(LogicalToDeviceUnits(150), LogicalToDeviceUnits(28));
            this.btnRestartAdmin.Location = new Point(LogicalToDeviceUnits(250), LogicalToDeviceUnits(8));
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
            this.topHeaderPanel.Controls.Add(this.btnRestartAdmin);

            // 3. System Diagnostics Health label
            this.lblSystemHealth.AutoSize = true;
            this.lblSystemHealth.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            this.lblSystemHealth.ForeColor = Color.FromArgb(70, 75, 85);
            this.lblSystemHealth.Dock = DockStyle.Right;
            this.lblSystemHealth.TextAlign = ContentAlignment.MiddleRight;
            this.lblSystemHealth.Text = "Checking system health...";
            this.topHeaderPanel.Controls.Add(this.lblSystemHealth);

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
            this.Controls.Add(this.topHeaderPanel);

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

                string vigemStr = vigemOk ? "● ViGEm: OK" : "❌ ViGEm: N/A";
                string hidHideStr = hidHideOk ? "● HidHide: OK" : "⚠️ HidHide: N/A";
                string controllerStr = controllerOk ? "● Controller: Connected" : "⚠️ Controller: Disconnected";

                this.lblSystemHealth.Text = $"{vigemStr}  |  {hidHideStr}  |  {controllerStr}";
            }
            catch { }
        }

        private void StartShowWindowListener()
        {
            try
            {
                showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowWindowEventName);
                showWindowThread = new Thread(() =>
                {
                    while (isListeningForShow)
                    {
                        try
                        {
                            if (showWindowEvent != null && showWindowEvent.WaitOne(1000))
                            {
                                if (isListeningForShow && this.IsHandleCreated && !this.IsDisposed)
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
            this.trayIcon.Visible = true;
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
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.WindowState = FormWindowState.Normal;
            }
            this.Show();
            this.Visible = true;
            this.BringToFront();
            this.Activate();
            try
            {
                SetForegroundWindow(this.Handle);
            }
            catch { }
        }

        public void ExitApplication()
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

            ExitApplication();
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
