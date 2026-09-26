using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using SharpDX.DirectInput;

namespace LegionDiagnostics
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                var hidHide = new Nefarius.Drivers.HidHide.HidHideControlService();
                string exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(exe))
                    hidHide.AddApplicationPath(exe);
            }
            catch { }

            Application.Run(new DiagnosticForm());
        }
    }

    public class DiagnosticForm : Form
    {
        private DirectInput? directInput;
        private Joystick? jsRight;
        private Joystick? jsLeft;
        private controller_hidapi.net.LegionController? rawController;

        private byte[] rawHidData = new byte[64];
        private readonly object rawLock = new();
        private long hidPacketCount = 0;
        private DateTime lastHidTime = DateTime.UtcNow;
        private double hidHz = 0;

        // UI Controls
        private Label lblHeader = null!;
        private Label lblRightStatus = null!;
        private Label lblLeftStatus = null!;
        private Label lblStickValues = null!;
        private Label lblButtonStates = null!;
        private Label lblHidStatus = null!;
        private Label lblGyroValues = null!;
        private Label lblMouseStatus = null!;
        private Panel pnlCrosshair = null!;
        private TextBox txtLog = null!;
        private Button btnSaveLog = null!;
        private System.Windows.Forms.Timer pollTimer = null!;

        private int mappedX = 0;
        private int mappedY = 0;
        private Point lastMousePos;
        private readonly StringBuilder logBuffer = new();
        private readonly List<string> recentEvents = new();

        public DiagnosticForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            Text = "Legion Go Companion - Diagnostics & Inspector";
            Size = new Size(1100, 800);
            MinimumSize = new Size(950, 650);
            BackColor = Color.FromArgb(20, 20, 28);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            StartPosition = FormStartPosition.CenterScreen;

            InitializeComponents();
            InitDevices();

            pollTimer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60 Hz UI refresh
            pollTimer.Tick += PollTimer_Tick;
            pollTimer.Start();

            LogEvent("Diagnostics application started. Looking for Legion Go controllers...");
        }

        private void InitializeComponents()
        {
            var rootLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1,
                Padding = new Padding(12),
                BackColor = Color.FromArgb(20, 20, 28)
            };
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F)); // Header
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 65F));  // Main cards
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 35F));  // Log & buttons
            Controls.Add(rootLayout);

            // 1. Header
            lblHeader = new Label
            {
                Text = "🎮 Legion Go Controller & Sensor Inspector (Standalone Diagnostic)",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(0, 210, 106),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            rootLayout.Controls.Add(lblHeader, 0, 0);

            // 2. Middle Panels (Split into Left = DirectInput / Stick, Right = Raw HID / Gyro)
            var middleSplit = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            middleSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            middleSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            rootLayout.Controls.Add(middleSplit, 0, 1);

            // Left Card: DirectInput & Stick
            var leftCard = CreateCardPanel("DirectInput / Thumbstick & Buttons");
            middleSplit.Controls.Add(leftCard, 0, 0);

            lblRightStatus = CreateInfoLabel("Right Gamepad (COL02): Searching...");
            lblLeftStatus = CreateInfoLabel("Left Gamepad (COL01): Searching...");
            lblStickValues = CreateInfoLabel("Stick Raw: X=0, Y=0 | Mapped: X=0, Y=0");
            lblButtonStates = CreateInfoLabel("Buttons: None");

            pnlCrosshair = new Panel
            {
                Size = new Size(160, 160),
                BackColor = Color.FromArgb(14, 14, 20),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(8, 4, 8, 8)
            };
            pnlCrosshair.Paint += PnlCrosshair_Paint;

            leftCard.Controls.Add(lblRightStatus);
            leftCard.Controls.Add(lblLeftStatus);
            leftCard.Controls.Add(lblStickValues);
            leftCard.Controls.Add(pnlCrosshair);
            leftCard.Controls.Add(lblButtonStates);

            // Right Card: Raw HID & Gyro
            var rightCard = CreateCardPanel("Raw HID & IMU Gyro Telemetry");
            middleSplit.Controls.Add(rightCard, 1, 0);

            lblHidStatus = CreateInfoLabel("Raw HID Report (MI_00): Searching...");
            lblGyroValues = CreateInfoLabel("Right Gyro: Waiting for HID data...");
            lblMouseStatus = CreateInfoLabel("Mouse Cursor: X=0, Y=0");

            rightCard.Controls.Add(lblHidStatus);
            rightCard.Controls.Add(lblGyroValues);
            rightCard.Controls.Add(lblMouseStatus);

            // 3. Bottom Panel: Event Log & Export
            var bottomCard = CreateCardPanel("Live Event Log & Diagnostic Summary");
            rootLayout.Controls.Add(bottomCard, 0, 2);

            txtLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(12, 12, 18),
                ForeColor = Color.FromArgb(200, 220, 240),
                Font = new Font("Consolas", 9.5F, FontStyle.Regular, GraphicsUnit.Point)
            };

            var bottomBtnBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 4, 0, 0)
            };

            btnSaveLog = new Button
            {
                Text = "💾 Save Full Diagnostic Log to Downloads",
                AutoSize = true,
                Height = 32,
                BackColor = Color.FromArgb(0, 180, 216),
                ForeColor = Color.Black,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnSaveLog.FlatAppearance.BorderSize = 0;
            btnSaveLog.Click += (s, e) => SaveDiagnosticLog();
            bottomBtnBar.Controls.Add(btnSaveLog);

            bottomCard.Controls.Add(txtLog);
            bottomCard.Controls.Add(bottomBtnBar);
        }

        private FlowLayoutPanel CreateCardPanel(string title)
        {
            var card = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.FromArgb(28, 28, 40),
                Padding = new Padding(12),
                Margin = new Padding(4)
            };

            var lblTitle = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 11.5F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(0, 180, 216),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 8)
            };
            card.Controls.Add(lblTitle);
            return card;
        }

        private Label CreateInfoLabel(string initialText)
        {
            return new Label
            {
                Text = initialText,
                AutoSize = true,
                ForeColor = Color.FromArgb(230, 230, 240),
                Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
                Margin = new Padding(0, 3, 0, 4)
            };
        }

        private void InitDevices()
        {
            try
            {
                directInput = new DirectInput();
                var devices = directInput.GetDevices(DeviceClass.All, DeviceEnumerationFlags.AllDevices);

                foreach (var dev in devices)
                {
                    try
                    {
                        var js = new Joystick(directInput, dev.InstanceGuid);
                        js.SetCooperativeLevel(Handle, CooperativeLevel.NonExclusive | CooperativeLevel.Background);
                        string path = js.Properties.InterfacePath.ToLower();

                        if (path.Contains("17ef") && (path.Contains("6184") || path.Contains("61ed") || path.Contains("6183") || path.Contains("61ec")))
                        {
                            js.Properties.BufferSize = 128;
                            try { js.Acquire(); } catch { }

                            if (path.Contains("col02"))
                            {
                                jsRight = js;
                                LogEvent($"Found Right Gamepad COL02: {dev.InstanceName}");
                            }
                            else if (path.Contains("col01"))
                            {
                                jsLeft = js;
                                LogEvent($"Found Left Gamepad COL01: {dev.InstanceName}");
                            }
                        }
                    }
                    catch { }
                }

                // Open raw HID controller via controller-hidapi.net
                try
                {
                    int[] pids = { 0x61ED, 0x6184, 0x6183, 0x61EB };
                    foreach (int pid in pids)
                    {
                        try
                        {
                            var c = new controller_hidapi.net.LegionController(0x17EF, (ushort)pid);
                            c.OnControllerInputReceived += (data) =>
                            {
                                if (data != null && data.Length >= 64)
                                {
                                    lock (rawLock)
                                    {
                                        Buffer.BlockCopy(data, 0, rawHidData, 0, 64);
                                        hidPacketCount++;
                                    }
                                }
                            };
                            c.Open();
                            rawController = c;
                            LogEvent($"Opened Raw HID controller: VID 0x17EF, PID 0x{pid:X4}");
                            break;
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    LogEvent($"Raw HID connection note: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                LogEvent($"DirectInput init error: {ex.Message}");
            }
        }

        private void PollTimer_Tick(object? sender, EventArgs e)
        {
            // 1. Poll Right Gamepad
            if (jsRight != null && !jsRight.IsDisposed)
            {
                try
                {
                    var st = jsRight.GetCurrentState();
                    lblRightStatus.Text = $"Right Gamepad (COL02): ✅ Connected & Polling";
                    lblRightStatus.ForeColor = Color.FromArgb(0, 210, 106);

                    // Physical right stick mapping
                    mappedX = (int)Math.Clamp((st.Y - 32768.0) / 32768.0 * 32767.0, -32768, 32767);
                    mappedY = (int)Math.Clamp((32767.0 - st.X) / 32768.0 * 32767.0, -32768, 32767);

                    lblStickValues.Text = $"Right Stick Raw: X={st.X}, Y={st.Y}\n" +
                                          $"Mapped (Natural): X={mappedX}, Y={mappedY} (Neutral=0)";

                    var activeBtns = new List<string>();
                    if (st.Buttons[0]) activeBtns.Add("A");
                    if (st.Buttons[1]) activeBtns.Add("B");
                    if (st.Buttons[2] || st.Buttons[3]) activeBtns.Add("X");
                    if (st.Buttons[4]) activeBtns.Add("Y");
                    if (st.Buttons[6]) activeBtns.Add("M2");
                    if (st.Buttons[7]) activeBtns.Add("RB (Aim)");
                    if (st.Buttons[11]) activeBtns.Add("Legion R / Start");
                    if (st.Buttons[8] || st.Buttons[9] || st.Buttons[10] || st.Buttons[14]) activeBtns.Add("RightStickClick");

                    lblButtonStates.Text = $"Buttons Active: {(activeBtns.Count > 0 ? string.Join(", ", activeBtns) : "None")}";
                    if (activeBtns.Contains("RB (Aim)"))
                        lblButtonStates.ForeColor = Color.FromArgb(255, 159, 28);
                    else
                        lblButtonStates.ForeColor = Color.White;

                    pnlCrosshair.Invalidate();
                }
                catch
                {
                    lblRightStatus.Text = "Right Gamepad (COL02): ⚠️ Acquire lost, retrying...";
                    lblRightStatus.ForeColor = Color.Orange;
                    try { jsRight.Acquire(); } catch { }
                }
            }
            else
            {
                lblRightStatus.Text = "Right Gamepad (COL02): ❌ Not Found";
                lblRightStatus.ForeColor = Color.FromArgb(255, 80, 80);
            }

            // 2. Poll Left Gamepad
            if (jsLeft != null && !jsLeft.IsDisposed)
            {
                try
                {
                    var stL = jsLeft.GetCurrentState();
                    lblLeftStatus.Text = $"Left Gamepad (COL01): ✅ Connected (X={stL.X}, Y={stL.Y})";
                    lblLeftStatus.ForeColor = Color.FromArgb(0, 210, 106);
                }
                catch
                {
                    lblLeftStatus.Text = "Left Gamepad (COL01): ⚠️ Disconnected / Off";
                    lblLeftStatus.ForeColor = Color.Gray;
                }
            }
            else
            {
                lblLeftStatus.Text = "Left Gamepad (COL01): ⭕ Disconnected (Single Right Mode)";
                lblLeftStatus.ForeColor = Color.Gray;
            }

            // 3. Raw HID Inspection
            byte[] snapshot = new byte[64];
            lock (rawLock)
            {
                Buffer.BlockCopy(rawHidData, 0, snapshot, 0, 64);
            }

            DateTime now = DateTime.UtcNow;
            double dt = (now - lastHidTime).TotalSeconds;
            if (dt >= 1.0)
            {
                hidHz = hidPacketCount / dt;
                hidPacketCount = 0;
                lastHidTime = now;
            }

            bool isMisaligned = snapshot[1] == 0 && snapshot.Skip(2).Any(b => b != 0);
            byte lState = isMisaligned ? snapshot[10] : snapshot[12];
            byte rState = isMisaligned ? snapshot[11] : snapshot[13];
            byte lt = isMisaligned ? snapshot[20] : snapshot[22];
            byte rt = isMisaligned ? snapshot[21] : snapshot[23];

            lblHidStatus.Text = $"Raw HID Report: {hidHz:F0} Hz | Misaligned: {(isMisaligned ? "⚠️ YES (-2 offset)" : "✅ NO")}\n" +
                                $"Left Status (B{ (isMisaligned ? 10 : 12) }): {lState} | Right Status (B{ (isMisaligned ? 11 : 13) }): {rState}\n" +
                                $"Triggers: LT={lt}, RT={rt}\n" +
                                $"Bytes 0..15: {string.Join(" ", snapshot.Take(16).Select(b => b.ToString("X2")))}";

            // Gyro Telemetry from Right Joycon
            int rGyroIdx = isMisaligned ? 52 : 54;
            int rAcceIdx = isMisaligned ? 46 : 48;

            short rawGx = (short)(snapshot[rGyroIdx + 2] << 8 | snapshot[rGyroIdx + 3]);
            short rawGz = (short)(snapshot[rGyroIdx] << 8 | snapshot[rGyroIdx + 1]);
            short rawGy = (short)(snapshot[rGyroIdx + 4] << 8 | snapshot[rGyroIdx + 5]);

            float gX = rawGx * -(2000.0f / short.MaxValue);
            float gZ = rawGz * (2000.0f / short.MaxValue);
            float gY = rawGy * -(2000.0f / short.MaxValue);

            lblGyroValues.Text = $"Right IMU Gyro (deg/s): Pitch={gX:F1}, Yaw={gY:F1}, Roll={gZ:F1}\n" +
                                 $"Raw Gyro Bytes: [{snapshot[rGyroIdx]:X2} {snapshot[rGyroIdx+1]:X2}] " +
                                 $"[{snapshot[rGyroIdx+2]:X2} {snapshot[rGyroIdx+3]:X2}] " +
                                 $"[{snapshot[rGyroIdx+4]:X2} {snapshot[rGyroIdx+5]:X2}]";

            // 4. Mouse Position & Delta
            Point curMouse = Cursor.Position;
            int mdx = curMouse.X - lastMousePos.X;
            int mdy = curMouse.Y - lastMousePos.Y;
            lastMousePos = curMouse;

            lblMouseStatus.Text = $"Mouse Cursor: X={curMouse.X}, Y={curMouse.Y} | ΔX={mdx}, ΔY={mdy}";
        }

        private void PnlCrosshair_Paint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = pnlCrosshair.Width;
            int h = pnlCrosshair.Height;
            int cx = w / 2;
            int cy = h / 2;

            using var penGrid = new Pen(Color.FromArgb(40, 40, 55), 1);
            g.DrawLine(penGrid, cx, 0, cx, h);
            g.DrawLine(penGrid, 0, cy, w, cy);
            g.DrawEllipse(penGrid, cx - 40, cy - 40, 80, 80);

            // Stick position (mappedX, mappedY in -32768..32767)
            float normX = Math.Clamp(mappedX / 32767.0f, -1.0f, 1.0f);
            float normY = Math.Clamp(mappedY / 32767.0f, -1.0f, 1.0f);

            // In screen coords, positive Y is down, so invert normY
            int px = cx + (int)(normX * (cx - 10));
            int py = cy - (int)(normY * (cy - 10));

            using var brushDot = new SolidBrush(Color.FromArgb(0, 210, 106));
            using var penDot = new Pen(Color.White, 2);
            g.FillEllipse(brushDot, px - 6, py - 6, 12, 12);
            g.DrawEllipse(penDot, px - 6, py - 6, 12, 12);
        }

        private void LogEvent(string msg)
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
            recentEvents.Add(line);
            logBuffer.AppendLine(line);

            if (txtLog != null && !txtLog.IsDisposed)
            {
                txtLog.AppendText(line + Environment.NewLine);
            }
        }

        private void SaveDiagnosticLog()
        {
            try
            {
                string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                string file = Path.Combine(downloads, $"LegionDiagnosticReport_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                var sb = new StringBuilder();
                sb.AppendLine("=== Legion Go Companion Standalone Diagnostic Report ===");
                sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"OS: {Environment.OSVersion}");
                sb.AppendLine($"Right Stick Mapped: X={mappedX}, Y={mappedY}");
                sb.AppendLine($"DirectInput Right Connected: {jsRight != null && !jsRight.IsDisposed}");
                sb.AppendLine($"DirectInput Left Connected: {jsLeft != null && !jsLeft.IsDisposed}");
                sb.AppendLine();
                sb.AppendLine("--- Raw HID Snapshot ---");
                lock (rawLock)
                {
                    sb.AppendLine(string.Join(" ", rawHidData.Select(b => b.ToString("X2"))));
                }
                sb.AppendLine();
                sb.AppendLine("--- Recent Event Log ---");
                sb.Append(logBuffer.ToString());

                File.WriteAllText(file, sb.ToString());
                MessageBox.Show($"Diagnostic report saved to:\n{file}", "Report Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
                LogEvent($"Saved report to {file}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save report: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            pollTimer.Stop();
            try { jsRight?.Unacquire(); jsRight?.Dispose(); } catch { }
            try { jsLeft?.Unacquire(); jsLeft?.Dispose(); } catch { }
            try { directInput?.Dispose(); } catch { }
            try { rawController?.Close(); } catch { }
            base.OnFormClosing(e);
        }
    }
}
