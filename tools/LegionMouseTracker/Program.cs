using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace LegionMouseTracker
{
    public class MouseSample
    {
        public double TimestampMs { get; set; }
        public int ScreenX { get; set; }
        public int ScreenY { get; set; }
        public int DeltaX { get; set; }
        public int DeltaY { get; set; }
        public double IntervalMs { get; set; }
        public double InstantHz { get; set; }
        public double SpeedPxPerSec { get; set; }
        public bool IsStutterSpike { get; set; }
    }

    public class SessionReport
    {
        public string Mode { get; set; } = "Desktop Mode";
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime EndTime { get; set; } = DateTime.UtcNow;
        public double TotalDurationSec { get; set; }
        public int TotalSamples { get; set; }
        public double AverageIntervalMs { get; set; }
        public double AverageHz { get; set; }
        public double MinIntervalMs { get; set; }
        public double MaxIntervalMs { get; set; }
        public int SpikesOver50Ms { get; set; }
        public int SpikesOver100Ms { get; set; }
        public int SpikesOver250Ms { get; set; }
        public int SpikesOver500Ms { get; set; }
        public double CumulativeDistancePx { get; set; }
        public List<MouseSample> Samples { get; set; } = new();
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrackerForm());
            }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tracker_error.txt"), ex.ToString());
                }
                catch { }
                MessageBox.Show(ex.ToString(), "LegionMouseTracker Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    public class TrackerForm : Form
    {
        // P/Invoke for Global Mouse Hook
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200;

        private IntPtr _hookID = IntPtr.Zero;
        private LowLevelMouseProc _hookProc = null!;

        // Recording & State
        private bool _isRecording = false;
        private string _currentMode = "Desktop Mode";
        private readonly List<MouseSample> _activeSessionSamples = new();
        private readonly Queue<MouseSample> _recentTrail = new();
        private readonly Queue<double> _recentIntervals = new();
        private readonly object _lock = new();

        private double _lastTimestampMs = 0;
        private int _lastX = 0;
        private int _lastY = 0;
        private bool _hasFirstSample = false;

        // UI Controls
        private ComboBox cmbMode = null!;
        private Button btnRecord = null!;
        private Button btnClear = null!;
        private Button btnExport = null!;
        private Label lblStatus = null!;
        private Label lblCurrentPos = null!;
        private Label lblCurrentDelta = null!;
        private Label lblInterval = null!;
        private Label lblHz = null!;
        private Label lblStutterCount = null!;
        private Label lblMaxFreeze = null!;
        private Label lblAvgInterval = null!;
        private Panel pnlProjectionCanvas = null!;
        private Panel pnlTimelineGraph = null!;
        private ListBox lstStutters = null!;
        private System.Windows.Forms.Timer uiTimer = null!;

        // Projection Canvas coordinates
        private float _canvasCenterX = 250;
        private float _canvasCenterY = 250;
        private float _virtualCursorX = 250;
        private float _virtualCursorY = 250;
        private float _canvasScale = 1.0f;

        public TrackerForm()
        {
            Text = "Legion Mouse & Motion Tracker - Stutter & Projection Diagnostic";
            Size = new Size(1300, 880);
            MinimumSize = new Size(1100, 760);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(20, 20, 24);
            ForeColor = Color.FromArgb(240, 240, 245);
            Font = new Font("Segoe UI", 10F, FontStyle.Regular);

            BuildUI();

            _hookProc = HookCallback;
            _hookID = SetHook(_hookProc);

            uiTimer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60fps UI refresh
            uiTimer.Tick += (s, e) => UpdateUI();
            uiTimer.Start();

            // Fallback high-resolution cursor polling timer (in case WH_MOUSE_LL is filtered by UIPI/UAC)
            var cursorPollTimer = new System.Windows.Forms.Timer { Interval = 8 }; // 125 Hz
            cursorPollTimer.Tick += (s, e) =>
            {
                if (GetCursorPos(out POINT pt))
                {
                    ProcessMouseMove(pt.X, pt.Y);
                }
            };
            cursorPollTimer.Start();

            FormClosing += (s, e) =>
            {
                if (_hookID != IntPtr.Zero)
                    UnhookWindowsHookEx(_hookID);
            };
        }

        private IntPtr SetHook(LowLevelMouseProc proc)
        {
            try
            {
                using var curProcess = Process.GetCurrentProcess();
                using var curModule = curProcess.MainModule;
                IntPtr hMod = IntPtr.Zero;
                if (curModule?.ModuleName != null)
                {
                    try { hMod = GetModuleHandle(curModule.ModuleName); } catch { }
                }
                return SetWindowsHookEx(WH_MOUSE_LL, proc, hMod, 0);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_MOUSEMOVE)
            {
                try
                {
                    var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    ProcessMouseMove(hookStruct.pt.X, hookStruct.pt.Y);
                }
                catch { }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private void ProcessMouseMove(int x, int y)
        {
            double nowMs = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency * 1000.0;

            lock (_lock)
            {
                if (!_hasFirstSample)
                {
                    _lastX = x;
                    _lastY = y;
                    _lastTimestampMs = nowMs;
                    _hasFirstSample = true;
                    return;
                }

                int dx = x - _lastX;
                int dy = y - _lastY;

                // Ignore stationary duplicate events
                if (dx == 0 && dy == 0)
                    return;

                double dt = nowMs - _lastTimestampMs;
                _lastX = x;
                _lastY = y;
                _lastTimestampMs = nowMs;

                double hz = dt > 0.0001 ? 1000.0 / dt : 0;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                double speed = dt > 0.0001 ? (dist / (dt / 1000.0)) : 0;
                bool isStutter = dt > 50.0;

                var sample = new MouseSample
                {
                    TimestampMs = nowMs,
                    ScreenX = x,
                    ScreenY = y,
                    DeltaX = dx,
                    DeltaY = dy,
                    IntervalMs = dt,
                    InstantHz = hz,
                    SpeedPxPerSec = speed,
                    IsStutterSpike = isStutter
                };

                _recentTrail.Enqueue(sample);
                while (_recentTrail.Count > 350)
                    _recentTrail.Dequeue();

                _recentIntervals.Enqueue(dt);
                while (_recentIntervals.Count > 120)
                    _recentIntervals.Dequeue();

                // Accumulate relative cursor on projection canvas
                _virtualCursorX += (float)dx * _canvasScale;
                _virtualCursorY += (float)dy * _canvasScale;

                // Keep virtual cursor bounded within canvas area
                if (pnlProjectionCanvas != null)
                {
                    float pad = 40f;
                    if (_virtualCursorX < pad) _virtualCursorX = pad;
                    if (_virtualCursorX > pnlProjectionCanvas.Width - pad) _virtualCursorX = pnlProjectionCanvas.Width - pad;
                    if (_virtualCursorY < pad) _virtualCursorY = pad;
                    if (_virtualCursorY > pnlProjectionCanvas.Height - pad) _virtualCursorY = pnlProjectionCanvas.Height - pad;
                }

                if (_isRecording)
                {
                    _activeSessionSamples.Add(sample);
                }
            }
        }

        private void BuildUI()
        {
            // 1. Top Control Bar (Height 85)
            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 85,
                BackColor = Color.FromArgb(28, 28, 34),
                Padding = new Padding(20, 15, 20, 15)
            };

            var lblTitle = new Label
            {
                Text = "LEGION MOUSE & MOTION TRACKER",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = Color.FromArgb(245, 245, 250),
                Location = new Point(20, 12),
                AutoSize = true
            };
            pnlTop.Controls.Add(lblTitle);

            var lblSubtitle = new Label
            {
                Text = "High-precision delta, interval & stutter timing analyzer for Desktop & Pointer modes",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = Color.FromArgb(160, 160, 175),
                Location = new Point(22, 38),
                AutoSize = true
            };
            pnlTop.Controls.Add(lblSubtitle);

            // Controls on right of top bar
            var lblModeLabel = new Label
            {
                Text = "Target Mode:",
                Location = new Point(620, 26),
                AutoSize = true,
                ForeColor = Color.FromArgb(200, 200, 215)
            };
            pnlTop.Controls.Add(lblModeLabel);

            cmbMode = new ComboBox
            {
                Location = new Point(720, 23),
                Width = 160,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(38, 38, 46),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };
            cmbMode.Items.AddRange(new object[] { "Desktop Mode", "Pointer Mode (Gyro)" });
            cmbMode.SelectedIndex = 0;
            cmbMode.SelectedIndexChanged += (s, e) =>
            {
                _currentMode = cmbMode.SelectedItem?.ToString() ?? "Desktop Mode";
            };
            pnlTop.Controls.Add(cmbMode);

            btnRecord = new Button
            {
                Text = "🔴 Start Recording",
                Location = new Point(895, 20),
                Size = new Size(160, 42),
                BackColor = Color.FromArgb(225, 29, 72),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnRecord.FlatAppearance.BorderSize = 0;
            btnRecord.Click += (s, e) => ToggleRecording();
            pnlTop.Controls.Add(btnRecord);

            btnClear = new Button
            {
                Text = "Clear Canvas",
                Location = new Point(1065, 20),
                Size = new Size(100, 42),
                BackColor = Color.FromArgb(45, 45, 55),
                ForeColor = Color.FromArgb(220, 220, 230),
                FlatStyle = FlatStyle.Flat
            };
            btnClear.FlatAppearance.BorderSize = 0;
            btnClear.Click += (s, e) => ClearCanvas();
            pnlTop.Controls.Add(btnClear);

            btnExport = new Button
            {
                Text = "Export JSON",
                Location = new Point(1175, 20),
                Size = new Size(100, 42),
                BackColor = Color.FromArgb(59, 130, 246),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnExport.FlatAppearance.BorderSize = 0;
            btnExport.Click += (s, e) => ExportReport();
            pnlTop.Controls.Add(btnExport);

            Controls.Add(pnlTop);

            // 2. Main Body Split Container
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 640,
                BackColor = Color.FromArgb(30, 30, 36)
            };

            // LEFT PANEL: Projection & Trajectory Visualizer
            var pnlLeft = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(20, 20, 24),
                Padding = new Padding(20)
            };

            var lblProjTitle = new Label
            {
                Text = "📍 REAL-TIME PROJECTION & TRAJECTORY CANVAS",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(230, 230, 240),
                Dock = DockStyle.Top,
                Height = 30
            };
            pnlLeft.Controls.Add(lblProjTitle);

            pnlProjectionCanvas = new DoubleBufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(14, 14, 18),
                BorderStyle = BorderStyle.FixedSingle
            };
            pnlProjectionCanvas.Paint += DrawProjectionCanvas;
            pnlProjectionCanvas.Resize += (s, e) =>
            {
                _canvasCenterX = pnlProjectionCanvas.Width / 2f;
                _canvasCenterY = pnlProjectionCanvas.Height / 2f;
            };
            pnlLeft.Controls.Add(pnlProjectionCanvas);

            // Legend below canvas
            var lblLegend = new Label
            {
                Text = "🟢 Smooth (<20ms / >50Hz)   🟡 Minor Delay (20-60ms)   🟠 Jitter (60-150ms)   🔴 Severe Stutter (>150ms / 500ms Freeze)",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(180, 180, 195),
                Dock = DockStyle.Bottom,
                Height = 25,
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlLeft.Controls.Add(lblLegend);

            split.Panel1.Controls.Add(pnlLeft);

            // RIGHT PANEL: Diagnostics, Timeline Graph & Stutter Log
            var pnlRight = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(24, 24, 30),
                Padding = new Padding(20)
            };

            // Metrics Group (Cards)
            var pnlCards = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 160,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true
            };

            pnlCards.Controls.Add(CreateMetricCard("Cursor Position", out lblCurrentPos, "(0, 0)", Color.FromArgb(147, 197, 253)));
            pnlCards.Controls.Add(CreateMetricCard("Delta (ΔX, ΔY)", out lblCurrentDelta, "(0, 0)", Color.FromArgb(253, 224, 71)));
            pnlCards.Controls.Add(CreateMetricCard("Update Interval (Δt)", out lblInterval, "0.0 ms", Color.FromArgb(52, 211, 153)));
            pnlCards.Controls.Add(CreateMetricCard("Polling Rate", out lblHz, "0 Hz", Color.FromArgb(96, 165, 250)));
            pnlCards.Controls.Add(CreateMetricCard("Stutter Count (>50ms)", out lblStutterCount, "0 spikes", Color.FromArgb(248, 113, 113)));
            pnlCards.Controls.Add(CreateMetricCard("Worst Freeze Duration", out lblMaxFreeze, "0.0 ms", Color.FromArgb(244, 63, 94)));
            pnlRight.Controls.Add(pnlCards);

            // Timeline Graph Label
            var lblTimelineTitle = new Label
            {
                Text = "📊 INTERVAL TIMELINE GRAPH (STUTTER SPIKE DETECTOR)",
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(230, 230, 240),
                Dock = DockStyle.Top,
                Height = 28
            };
            pnlRight.Controls.Add(lblTimelineTitle);

            pnlTimelineGraph = new DoubleBufferedPanel
            {
                Dock = DockStyle.Top,
                Height = 170,
                BackColor = Color.FromArgb(14, 14, 18),
                BorderStyle = BorderStyle.FixedSingle
            };
            pnlTimelineGraph.Paint += DrawTimelineGraph;
            pnlRight.Controls.Add(pnlTimelineGraph);

            // Status label
            lblStatus = new Label
            {
                Text = "STATUS: Ready. Move the right stick or use pointer mode to see live movement.",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(74, 222, 128),
                Dock = DockStyle.Top,
                Height = 32,
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlRight.Controls.Add(lblStatus);

            // Log Title
            var lblLogTitle = new Label
            {
                Text = "🚨 DETECTED STUTTER & FREEZE LOG (>50ms)",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 113, 113),
                Dock = DockStyle.Top,
                Height = 25
            };
            pnlRight.Controls.Add(lblLogTitle);

            lstStutters = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(16, 16, 20),
                ForeColor = Color.FromArgb(254, 202, 202),
                Font = new Font("Consolas", 9F, FontStyle.Regular),
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false
            };
            pnlRight.Controls.Add(lstStutters);

            split.Panel2.Controls.Add(pnlRight);
            Controls.Add(split);
        }

        private Panel CreateMetricCard(string title, out Label valLabel, string defaultVal, Color accent)
        {
            var pnl = new Panel
            {
                Size = new Size(185, 70),
                BackColor = Color.FromArgb(32, 32, 40),
                Margin = new Padding(4),
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblT = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                ForeColor = Color.FromArgb(160, 160, 175),
                Location = new Point(8, 6),
                AutoSize = true
            };
            pnl.Controls.Add(lblT);

            valLabel = new Label
            {
                Text = defaultVal,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = accent,
                Location = new Point(8, 28),
                AutoSize = true
            };
            pnl.Controls.Add(valLabel);

            return pnl;
        }

        private void ToggleRecording()
        {
            lock (_lock)
            {
                if (!_isRecording)
                {
                    _isRecording = true;
                    _activeSessionSamples.Clear();
                    btnRecord.Text = "⏹️ Stop Recording";
                    btnRecord.BackColor = Color.FromArgb(16, 185, 129);
                    lblStatus.Text = $"STATUS: RECORDING ACTIVE ({_currentMode}). Move right thumbstick or gyro...";
                    lblStatus.ForeColor = Color.FromArgb(248, 113, 113);
                    lstStutters.Items.Add($"[{DateTime.Now:HH:mm:ss}] === RECORDING STARTED ({_currentMode}) ===");
                }
                else
                {
                    _isRecording = false;
                    btnRecord.Text = "🔴 Start Recording";
                    btnRecord.BackColor = Color.FromArgb(225, 29, 72);
                    lblStatus.Text = $"STATUS: Recording finished. {_activeSessionSamples.Count} samples captured.";
                    lblStatus.ForeColor = Color.FromArgb(74, 222, 128);
                    lstStutters.Items.Add($"[{DateTime.Now:HH:mm:ss}] === RECORDING STOPPED: {_activeSessionSamples.Count} samples ===");

                    ExportReport(autoPrompt: true);
                }
            }
        }

        private void ClearCanvas()
        {
            lock (_lock)
            {
                _recentTrail.Clear();
                _recentIntervals.Clear();
                _virtualCursorX = pnlProjectionCanvas.Width / 2f;
                _virtualCursorY = pnlProjectionCanvas.Height / 2f;
                lstStutters.Items.Clear();
            }
            pnlProjectionCanvas.Invalidate();
            pnlTimelineGraph.Invalidate();
        }

        private void UpdateUI()
        {
            MouseSample? lastSample = null;
            List<MouseSample> trailCopy;
            List<double> intervalsCopy;

            lock (_lock)
            {
                if (_recentTrail.Count > 0)
                    lastSample = _recentTrail.Last();
                trailCopy = _recentTrail.ToList();
                intervalsCopy = _recentIntervals.ToList();
            }

            if (lastSample != null)
            {
                lblCurrentPos.Text = $"({lastSample.ScreenX}, {lastSample.ScreenY})";
                lblCurrentDelta.Text = $"(ΔX:{lastSample.DeltaX,3}, ΔY:{lastSample.DeltaY,3})";
                lblInterval.Text = $"{lastSample.IntervalMs:F1} ms";
                lblHz.Text = $"{lastSample.InstantHz:F0} Hz";

                // Interval Color Coding
                if (lastSample.IntervalMs < 20.0)
                    lblInterval.ForeColor = Color.FromArgb(52, 211, 153); // Green
                else if (lastSample.IntervalMs < 50.0)
                    lblInterval.ForeColor = Color.FromArgb(250, 204, 21); // Yellow
                else if (lastSample.IntervalMs < 150.0)
                    lblInterval.ForeColor = Color.FromArgb(251, 146, 60); // Orange
                else
                    lblInterval.ForeColor = Color.FromArgb(244, 63, 94);  // Red

                if (lastSample.IntervalMs > 50.0)
                {
                    string alert = $"[{DateTime.Now:HH:mm:ss.fff}] ⚠️ STUTTER SPIKE: {lastSample.IntervalMs:F1} ms freeze (ΔX:{lastSample.DeltaX}, ΔY:{lastSample.DeltaY})";
                    if (lstStutters.Items.Count == 0 || !lstStutters.Items[^1].ToString()!.Contains($"{lastSample.IntervalMs:F1} ms"))
                    {
                        lstStutters.Items.Add(alert);
                        lstStutters.TopIndex = lstStutters.Items.Count - 1;
                    }
                }
            }

            if (intervalsCopy.Count > 0)
            {
                int spikes50 = intervalsCopy.Count(dt => dt > 50.0);
                double maxFreeze = intervalsCopy.Max();
                lblStutterCount.Text = $"{spikes50} spikes";
                lblMaxFreeze.Text = $"{maxFreeze:F1} ms";

                if (maxFreeze > 400.0)
                    lblMaxFreeze.ForeColor = Color.FromArgb(239, 68, 68);
                else
                    lblMaxFreeze.ForeColor = Color.FromArgb(244, 63, 94);
            }

            pnlProjectionCanvas.Invalidate();
            pnlTimelineGraph.Invalidate();
        }

        private void DrawProjectionCanvas(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = pnlProjectionCanvas.Width;
            int h = pnlProjectionCanvas.Height;

            // Background Grid
            using (var gridPen = new Pen(Color.FromArgb(26, 26, 34), 1))
            {
                for (int x = 0; x < w; x += 40)
                    g.DrawLine(gridPen, x, 0, x, h);
                for (int y = 0; y < h; y += 40)
                    g.DrawLine(gridPen, 0, y, w, y);
            }

            // Center Crosshair
            using (var centerPen = new Pen(Color.FromArgb(45, 45, 58), 1) { DashStyle = DashStyle.Dash })
            {
                g.DrawLine(centerPen, w / 2f, 0, w / 2f, h);
                g.DrawLine(centerPen, 0, h / 2f, w, h / 2f);
            }

            List<MouseSample> samples;
            lock (_lock)
            {
                samples = _recentTrail.ToList();
            }

            if (samples.Count < 2) return;

            // Draw Trail
            float currX = _virtualCursorX;
            float currY = _virtualCursorY;

            // Reconstruct path backward from current position
            var points = new List<PointF>();
            float traceX = currX;
            float traceY = currY;
            points.Add(new PointF(traceX, traceY));

            for (int i = samples.Count - 1; i >= 1; i--)
            {
                traceX -= samples[i].DeltaX;
                traceY -= samples[i].DeltaY;
                points.Add(new PointF(traceX, traceY));
            }
            points.Reverse();

            // Draw line segments color-coded by interval
            for (int i = 0; i < points.Count - 1 && i < samples.Count - 1; i++)
            {
                double dt = samples[i + 1].IntervalMs;
                Color col;
                float penWidth;

                if (dt < 20.0)
                {
                    col = Color.FromArgb(180, 52, 211, 153); // Green (smooth)
                    penWidth = 2.0f;
                }
                else if (dt < 60.0)
                {
                    col = Color.FromArgb(200, 250, 204, 21); // Yellow
                    penWidth = 2.5f;
                }
                else if (dt < 150.0)
                {
                    col = Color.FromArgb(220, 251, 146, 60); // Orange
                    penWidth = 3.5f;
                }
                else
                {
                    col = Color.FromArgb(255, 239, 68, 68); // Red (stutter)
                    penWidth = 5.0f;
                }

                using var segPen = new Pen(col, penWidth);
                g.DrawLine(segPen, points[i], points[i + 1]);

                // Highlight severe freeze points with outer halo
                if (dt > 150.0)
                {
                    using var haloBrush = new SolidBrush(Color.FromArgb(120, 239, 68, 68));
                    g.FillEllipse(haloBrush, points[i + 1].X - 8, points[i + 1].Y - 8, 16, 16);
                    using var dotBrush = new SolidBrush(Color.FromArgb(255, 255, 255));
                    g.FillEllipse(dotBrush, points[i + 1].X - 3, points[i + 1].Y - 3, 6, 6);

                    // Draw freeze text tag
                    string freezeTag = $"{dt:F0}ms freeze!";
                    using var tagFont = new Font("Segoe UI", 8F, FontStyle.Bold);
                    using var tagBrush = new SolidBrush(Color.FromArgb(255, 220, 220));
                    g.DrawString(freezeTag, tagFont, tagBrush, points[i + 1].X + 6, points[i + 1].Y - 12);
                }
            }

            // Draw Head Point & Projection Velocity Vector
            var head = points[^1];
            var lastSamp = samples[^1];

            using (var headBrush = new SolidBrush(Color.FromArgb(99, 102, 241)))
            {
                g.FillEllipse(headBrush, head.X - 6, head.Y - 6, 12, 12);
            }

            // Projection Vector (speed vector)
            float vecX = head.X + lastSamp.DeltaX * 4f;
            float vecY = head.Y + lastSamp.DeltaY * 4f;
            using (var vecPen = new Pen(Color.FromArgb(236, 72, 153), 2.5f) { EndCap = LineCap.ArrowAnchor })
            {
                g.DrawLine(vecPen, head.X, head.Y, vecX, vecY);
            }
        }

        private void DrawTimelineGraph(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = pnlTimelineGraph.Width;
            int h = pnlTimelineGraph.Height;

            List<double> intervals;
            lock (_lock)
            {
                intervals = _recentIntervals.ToList();
            }

            // Draw Grid & Baseline Thresholds
            // Max height represents 600 ms
            const double maxGraphMs = 600.0;

            float y125Hz = h - (float)(8.0 / maxGraphMs * h);
            float y60Hz = h - (float)(16.6 / maxGraphMs * h);
            float y50ms = h - (float)(50.0 / maxGraphMs * h);
            float y100ms = h - (float)(100.0 / maxGraphMs * h);
            float y500ms = h - (float)(500.0 / maxGraphMs * h);

            using var gridPen = new Pen(Color.FromArgb(26, 26, 32));
            g.DrawLine(gridPen, 0, y50ms, w, y50ms);
            g.DrawLine(gridPen, 0, y100ms, w, y100ms);

            // 500ms Freeze Line (Red Dashed)
            using (var redLine = new Pen(Color.FromArgb(239, 68, 68), 1.5f) { DashStyle = DashStyle.Dash })
            {
                g.DrawLine(redLine, 0, y500ms, w, y500ms);
            }
            using var font = new Font("Segoe UI", 7.5F);
            using var textBrush = new SolidBrush(Color.FromArgb(239, 68, 68));
            g.DrawString("500 ms (SEVERE STUTTER SPIKE)", font, textBrush, 10, y500ms - 14);

            // 125Hz Baseline (Green)
            using (var greenLine = new Pen(Color.FromArgb(52, 211, 153), 1f))
            {
                g.DrawLine(greenLine, 0, y125Hz, w, y125Hz);
            }
            using var greenBrush = new SolidBrush(Color.FromArgb(52, 211, 153));
            g.DrawString("8 ms (Target 125 Hz)", font, greenBrush, w - 120, y125Hz - 14);

            if (intervals.Count == 0) return;

            float barWidth = Math.Max(2f, (float)w / 120f);
            for (int i = 0; i < intervals.Count; i++)
            {
                double dt = intervals[i];
                float barHeight = (float)Math.Min(h, (dt / maxGraphMs) * h);
                float x = i * barWidth;
                float y = h - barHeight;

                Color barCol;
                if (dt < 20.0) barCol = Color.FromArgb(52, 211, 153);
                else if (dt < 60.0) barCol = Color.FromArgb(250, 204, 21);
                else if (dt < 150.0) barCol = Color.FromArgb(251, 146, 60);
                else barCol = Color.FromArgb(239, 68, 68);

                using var barBrush = new SolidBrush(barCol);
                g.FillRectangle(barBrush, x, y, barWidth - 1f, barHeight);
            }
        }

        private void ExportReport(bool autoPrompt = false)
        {
            List<MouseSample> samplesToExport;
            lock (_lock)
            {
                samplesToExport = _isRecording || _activeSessionSamples.Count > 0
                    ? _activeSessionSamples.ToList()
                    : _recentTrail.ToList();
            }

            if (samplesToExport.Count == 0)
            {
                if (!autoPrompt)
                    MessageBox.Show("No mouse samples recorded yet. Move the mouse or run a recording first.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var report = new SessionReport
            {
                Mode = _currentMode,
                StartTime = DateTime.UtcNow.AddMilliseconds(-samplesToExport.Last().TimestampMs + samplesToExport.First().TimestampMs),
                EndTime = DateTime.UtcNow,
                TotalDurationSec = (samplesToExport.Last().TimestampMs - samplesToExport.First().TimestampMs) / 1000.0,
                TotalSamples = samplesToExport.Count,
                AverageIntervalMs = samplesToExport.Average(s => s.IntervalMs),
                AverageHz = samplesToExport.Average(s => s.InstantHz),
                MinIntervalMs = samplesToExport.Min(s => s.IntervalMs),
                MaxIntervalMs = samplesToExport.Max(s => s.IntervalMs),
                SpikesOver50Ms = samplesToExport.Count(s => s.IntervalMs > 50.0),
                SpikesOver100Ms = samplesToExport.Count(s => s.IntervalMs > 100.0),
                SpikesOver250Ms = samplesToExport.Count(s => s.IntervalMs > 250.0),
                SpikesOver500Ms = samplesToExport.Count(s => s.IntervalMs > 500.0),
                CumulativeDistancePx = samplesToExport.Sum(s => Math.Sqrt(s.DeltaX * s.DeltaX + s.DeltaY * s.DeltaY)),
                Samples = samplesToExport
            };

            string safeMode = _currentMode.Replace(" ", "_").Replace("(", "").Replace(")", "");
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string fileName = $"LegionMouseReport_{safeMode}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            string fullPath = Path.Combine(downloads, fileName);

            try
            {
                string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(fullPath, json);

                string summary = $"Test Session Summary ({report.Mode}):\n\n" +
                                 $"Total Samples: {report.TotalSamples}\n" +
                                 $"Duration: {report.TotalDurationSec:F2} seconds\n" +
                                 $"Average Polling Rate: {report.AverageHz:F1} Hz\n" +
                                 $"Average Interval: {report.AverageIntervalMs:F2} ms\n" +
                                 $"Max Freeze Duration: {report.MaxIntervalMs:F1} ms\n" +
                                 $"Stutter Spikes (>50ms): {report.SpikesOver50Ms}\n" +
                                 $"Severe Freezes (>500ms): {report.SpikesOver500Ms}\n\n" +
                                 $"Report saved to:\n{fullPath}";

                lstStutters.Items.Add($"[{DateTime.Now:HH:mm:ss}] 📁 Saved report: {fileName}");
                lstStutters.TopIndex = lstStutters.Items.Count - 1;

                if (!autoPrompt)
                    MessageBox.Show(summary, "Session Export Completed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save report: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    public class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();
        }
    }
}
