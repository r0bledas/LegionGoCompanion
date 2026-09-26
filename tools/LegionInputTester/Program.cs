using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using SharpDX.DirectInput;

namespace LegionInputTester
{
    public static class InputUtils
    {
        public static double MapRange(double value, double fromLow, double fromHigh, double toLow, double toHigh)
        {
            return (value - fromLow) / (fromHigh - fromLow) * (toHigh - toLow) + toLow;
        }
    }

    static class Program
    {
        public static string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tester_debug.log");

        public static void Log(string msg)
        {
            try
            {
                string line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
                Console.WriteLine(line);
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch { }
        }

        [STAThread]
        static void Main(string[] args)
        {
            File.WriteAllText(LogPath, $"=== LegionInputTester Started {DateTime.Now} ===\n");
            Log("Setting up application...");

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                Log($"[CRASH] Unhandled: {ex?.Message}\n{ex?.StackTrace}");
            };

            Application.ThreadException += (s, e) =>
            {
                Log($"[THREAD EXCEPTION] {e.Exception.Message}\n{e.Exception.StackTrace}");
            };

            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                try
                {
                    var hidHide = new Nefarius.Drivers.HidHide.HidHideControlService();
                    string exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    if (!string.IsNullOrEmpty(exe))
                    {
                        hidHide.AddApplicationPath(exe);
                        Log($"Registered with HidHide: {exe}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"HidHide registration notice: {ex.Message}");
                }

                Log("Starting TesterForm...");
                Application.Run(new TesterForm());
                Log("TesterForm closed.");
            }
            catch (Exception ex)
            {
                Log($"[FATAL] {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Fatal error: {ex.Message}\n\n{ex.StackTrace}", "Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    public class LiveState
    {
        public int RightX { get; set; } = 32768;
        public int RightY { get; set; } = 32768;
        public int RightZ { get; set; } = 32768;
        public int RightRz { get; set; } = 32768;
        public List<int> ActiveButtons { get; set; } = new();
        public byte Byte20BackButtons { get; set; }
        public byte TriggerL { get; set; }
        public byte TriggerR { get; set; }
        public double PollingHz { get; set; }
        public double AvgIntervalMs { get; set; }
        public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
    }

    public class StepRecord
    {
        public string StepName { get; set; } = string.Empty;
        public string Instruction { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public Dictionary<string, int> DirectInputAxes { get; set; } = new();
        public List<int> PressedButtons { get; set; } = new();
        public int PovValue { get; set; } = -1;
        public byte Byte20BackButtons { get; set; }
        public byte Byte22TriggerL { get; set; }
        public byte Byte23TriggerR { get; set; }
    }

    public class PollingStats
    {
        public int TotalSamples { get; set; }
        public double MinDeltaMs { get; set; }
        public double MaxDeltaMs { get; set; }
        public double AvgDeltaMs { get; set; }
        public double EstimatedHz { get; set; }
        public List<double> RecentIntervalsMs { get; set; } = new();
    }

    public class TestReport
    {
        public DateTime RunTime { get; set; } = DateTime.UtcNow;
        public string DeviceInfo { get; set; } = string.Empty;
        public LiveState CurrentLive { get; set; } = new();
        public List<StepRecord> Steps { get; set; } = new();
        public PollingStats? StickPollingStats { get; set; }
    }

    public enum StepType
    {
        StickNeutral,
        StickDirection,
        Button,
        Trigger,
        PollingRate
    }

    public class StepDefinition
    {
        public string Name { get; set; } = "";
        public string Instruction { get; set; } = "";
        public StepType Type { get; set; }
        public int ButtonIndex { get; set; } = -1;
        public byte HidMask { get; set; } = 0;
    }

    public class TesterForm : Form
    {
        private DirectInput? directInput;
        private Joystick? joystickLeft;  // COL01
        private Joystick? joystickRight; // COL02
        private Joystick? singleJoystick;
        private controller_hidapi.net.LegionController? rawHidController;

        private byte[] lastHidData = new byte[64];
        private readonly object hidLock = new();

        private System.Windows.Forms.Timer pollTimer;
        private Stopwatch stopwatch = new();
        private long lastChangeTick = 0;
        private List<double> deltaHistory = new();
        private int pollSampleCount = 0;
        private Stopwatch pollingTestStopwatch = new();
        private bool isPollingRateTesting = false;

        private long lastLiveJsonSaveTick = 0;

        // State Machine for Gated Input Capture
        private enum InputGateState
        {
            WaitingForRelease,  // Must return to center / release button first
            WaitingForInput,    // Prompting user to hold the input
            HoldingInput,       // User is actively holding, timer counting down
            CompletedStep       // Captured! Brief pause before transitioning
        }

        private InputGateState gateState = InputGateState.WaitingForRelease;
        private Stopwatch holdStopwatch = new();
        private const int RequiredHoldMs = 700; // Hold steady for 700ms to capture clean average
        private List<int> holdXSamples = new();
        private List<int> holdYSamples = new();
        private List<int> holdZSamples = new();
        private List<int> holdRzSamples = new();

        // Neutral calibration center
        private int neutralCenterX = 33600;
        private int neutralCenterY = 33600;
        private const int DeadzoneThreshold = 4500;  // Hall effect deadzone window (~13%)
        private const int DeflectionThreshold = 12000; // Deflection trigger threshold

        // UI Controls
        private Label lblStepTitle = null!;
        private Label lblGateStatus = null!;
        private Label lblStepInstruction = null!;
        private ProgressBar progressHold = null!;
        private ProgressBar progressOverall = null!;
        private Button btnManualCapture = null!;
        private Button btnSkipStep = null!;
        private Button btnSave = null!;

        private Label lblLiveRightStick = null!;
        private Label lblLiveLeftStick = null!;
        private Label lblLiveButtons = null!;
        private Label lblLiveHid = null!;
        private Label lblLivePolling = null!;
        private Panel pnlCrosshair = null!;

        private int currentStepIndex = 0;
        private readonly List<StepDefinition> testSteps = new()
        {
            new() { Name = "Neutral Center", Instruction = "Release the right stick so it rests naturally at the center.", Type = StepType.StickNeutral },
            new() { Name = "Right Stick UP", Instruction = "Push the Right Stick straight UP and hold steady.", Type = StepType.StickDirection },
            new() { Name = "Right Stick RIGHT", Instruction = "Push the Right Stick straight RIGHT and hold steady.", Type = StepType.StickDirection },
            new() { Name = "Right Stick DOWN", Instruction = "Push the Right Stick straight DOWN and hold steady.", Type = StepType.StickDirection },
            new() { Name = "Right Stick LEFT", Instruction = "Push the Right Stick straight LEFT and hold steady.", Type = StepType.StickDirection },
            new() { Name = "Right Stick Click (RS / R3)", Instruction = "Click and hold down on the right thumbstick.", Type = StepType.Button, ButtonIndex = 9 },
            new() { Name = "Button A", Instruction = "Press and hold button A.", Type = StepType.Button, ButtonIndex = 0 },
            new() { Name = "Button B", Instruction = "Press and hold button B.", Type = StepType.Button, ButtonIndex = 1 },
            new() { Name = "Button X", Instruction = "Press and hold button X.", Type = StepType.Button, ButtonIndex = 2 },
            new() { Name = "Button Y", Instruction = "Press and hold button Y.", Type = StepType.Button, ButtonIndex = 4 },
            new() { Name = "Right Bumper (RB)", Instruction = "Press and hold the Right Bumper (RB).", Type = StepType.Button, ButtonIndex = 7 },
            new() { Name = "Right Trigger (RT)", Instruction = "Pull and hold the Right Trigger (RT) all the way.", Type = StepType.Trigger },
            new() { Name = "M1 Button", Instruction = "Press and hold the M1 button (upper back edge).", Type = StepType.Button, HidMask = 0x10 },
            new() { Name = "M2 Button", Instruction = "Press and hold the M2 button (lower back edge).", Type = StepType.Button, ButtonIndex = 6, HidMask = 0x08 },
            new() { Name = "M3 Button", Instruction = "Press and hold the M3 button (side grip paddle).", Type = StepType.Button, HidMask = 0x04 },
            new() { Name = "Y3 Button", Instruction = "Press and hold the Y3 button (lower grip paddle).", Type = StepType.Button, HidMask = 0x20 },
            new() { Name = "Polling Rate Test", Instruction = "Swirl the right stick continuously in circles for 3 seconds.", Type = StepType.PollingRate }
        };

        private TestReport report = new();

        public TesterForm()
        {
            Program.Log("TesterForm initializing with improved gating & larger layout...");
            Text = "Legion Go Input & Polling Tester";
            Size = new Size(1220, 840);
            MinimumSize = new Size(1000, 720);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            BackColor = Color.FromArgb(24, 24, 27);
            ForeColor = Color.FromArgb(244, 244, 245);
            Font = new Font("Segoe UI", 10F, FontStyle.Regular);

            BuildUI();
            InitializeDirectInputAndHid();

            stopwatch.Start();
            pollTimer = new System.Windows.Forms.Timer { Interval = 8 }; // 125 Hz UI polling
            pollTimer.Tick += OnPollTick;
            pollTimer.Start();

            // Begin at Step 0
            currentStepIndex = 0;
            gateState = InputGateState.WaitingForRelease;
            ShowCurrentStep();
            SaveReportFiles(false);
            Program.Log("TesterForm displayed successfully.");
        }

        private void BuildUI()
        {
            // Header
            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 65,
                BackColor = Color.FromArgb(18, 18, 20),
                Padding = new Padding(25, 12, 25, 12)
            };
            var lblTitle = new Label
            {
                Text = "LEGION GO CONTROLLER DIAGNOSTIC & INPUT TESTER",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = Color.FromArgb(6, 182, 212),
                AutoSize = true,
                Location = new Point(25, 18)
            };
            pnlHeader.Controls.Add(lblTitle);

            // Left Wizard Panel - Generously Sized (620px width)
            var pnlWizard = new Panel
            {
                Dock = DockStyle.Left,
                Width = 620,
                BackColor = Color.FromArgb(32, 32, 36),
                Padding = new Padding(25)
            };

            lblStepTitle = new Label
            {
                Text = "Step 1 of 17: Neutral Center",
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = Color.FromArgb(59, 130, 246),
                Location = new Point(25, 20),
                Size = new Size(570, 35)
            };
            pnlWizard.Controls.Add(lblStepTitle);

            lblGateStatus = new Label
            {
                Text = "👉 RELEASE STICK TO CENTER FIRST",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = Color.FromArgb(250, 204, 21),
                Location = new Point(25, 60),
                Size = new Size(570, 32)
            };
            pnlWizard.Controls.Add(lblGateStatus);

            lblStepInstruction = new Label
            {
                Text = "Release the right stick so it rests naturally at the center.",
                Font = new Font("Segoe UI", 12F, FontStyle.Regular),
                ForeColor = Color.White,
                Location = new Point(25, 95),
                Size = new Size(570, 55)
            };
            pnlWizard.Controls.Add(lblStepInstruction);

            var lblHoldProgressTitle = new Label
            {
                Text = "Hold Duration:",
                Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                ForeColor = Color.FromArgb(161, 161, 170),
                Location = new Point(25, 155),
                Size = new Size(120, 20)
            };
            pnlWizard.Controls.Add(lblHoldProgressTitle);

            progressHold = new ProgressBar
            {
                Location = new Point(25, 180),
                Size = new Size(570, 18),
                Maximum = RequiredHoldMs,
                Value = 0
            };
            pnlWizard.Controls.Add(progressHold);

            var lblOverallTitle = new Label
            {
                Text = "Overall Progress:",
                Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                ForeColor = Color.FromArgb(161, 161, 170),
                Location = new Point(25, 205),
                Size = new Size(120, 20)
            };
            pnlWizard.Controls.Add(lblOverallTitle);

            progressOverall = new ProgressBar
            {
                Location = new Point(25, 230),
                Size = new Size(570, 14),
                Maximum = testSteps.Count,
                Value = 0
            };
            pnlWizard.Controls.Add(progressOverall);

            // Action Buttons
            btnManualCapture = new Button
            {
                Text = "Manual Capture & Next >>",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                BackColor = Color.FromArgb(6, 182, 212),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(25, 260),
                Size = new Size(290, 50),
                Cursor = Cursors.Hand
            };
            btnManualCapture.FlatAppearance.BorderSize = 0;
            btnManualCapture.Click += (s, e) => CaptureCurrentStepAndAdvance();
            pnlWizard.Controls.Add(btnManualCapture);

            btnSkipStep = new Button
            {
                Text = "Skip",
                Font = new Font("Segoe UI", 11F, FontStyle.Regular),
                BackColor = Color.FromArgb(63, 63, 70),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(325, 260),
                Size = new Size(110, 50),
                Cursor = Cursors.Hand
            };
            btnSkipStep.FlatAppearance.BorderSize = 0;
            btnSkipStep.Click += (s, e) => AdvanceStep();
            pnlWizard.Controls.Add(btnSkipStep);

            btnSave = new Button
            {
                Text = "Save & Finish",
                Font = new Font("Segoe UI", 11F, FontStyle.Regular),
                BackColor = Color.FromArgb(39, 39, 42),
                ForeColor = Color.FromArgb(161, 161, 170),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(445, 260),
                Size = new Size(150, 50),
                Cursor = Cursors.Hand
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += (s, e) => FinishAndSave();
            pnlWizard.Controls.Add(btnSave);

            // Instructions text panel
            var pnlHelpBox = new Panel
            {
                Location = new Point(25, 330),
                Size = new Size(570, 420),
                BackColor = Color.FromArgb(24, 24, 27),
                Padding = new Padding(15)
            };
            var lblHelpText = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(212, 212, 216),
                Text = "How this test works:\n\n" +
                       "1. GATED DEFLECTION: Between every direction, you MUST let the stick return to center. The app will turn Yellow until you center it.\n\n" +
                       "2. HOLD STEADY: Once centered, push in the prompted direction and hold steady for ~0.7 seconds. The hold bar will fill and advance automatically!\n\n" +
                       "3. HALL EFFECT DEADZONE: A small 12% deadzone is calibrated around center so drift will never false-trigger steps.\n\n" +
                       "4. BUTTONS: Press and hold the prompted button until captured.\n\n" +
                       "5. MANUAL OVERRIDE: You can tap 'Manual Capture' or press Enter / Space anytime."
            };
            pnlHelpBox.Controls.Add(lblHelpText);
            pnlWizard.Controls.Add(pnlHelpBox);

            // Right Monitor Panel
            var pnlMonitor = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(24, 24, 27),
                Padding = new Padding(25)
            };

            var lblMonitorHeader = new Label
            {
                Text = "LIVE SENSOR & DIRECTINPUT MONITOR",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(161, 161, 170),
                Location = new Point(25, 20),
                AutoSize = true
            };
            pnlMonitor.Controls.Add(lblMonitorHeader);

            lblLivePolling = new Label
            {
                Text = "Polling: 0 Hz | Interval: 0 ms",
                Font = new Font("Consolas", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(34, 197, 94),
                Location = new Point(25, 55),
                Size = new Size(520, 28)
            };
            pnlMonitor.Controls.Add(lblLivePolling);

            lblLiveRightStick = new Label
            {
                Text = "Right Stick (COL02):\n  X: 0 | Y: 0 | Z: 0 | Rz: 0",
                Font = new Font("Consolas", 10.5F, FontStyle.Regular),
                ForeColor = Color.White,
                Location = new Point(25, 85),
                Size = new Size(520, 50)
            };
            pnlMonitor.Controls.Add(lblLiveRightStick);

            lblLiveLeftStick = new Label
            {
                Text = "Left Stick (COL01): X: 0 | Y: 0",
                Font = new Font("Consolas", 10.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(161, 161, 170),
                Location = new Point(25, 140),
                Size = new Size(520, 28)
            };
            pnlMonitor.Controls.Add(lblLiveLeftStick);

            lblLiveButtons = new Label
            {
                Text = "Active Buttons: None",
                Font = new Font("Consolas", 10.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(250, 204, 21),
                Location = new Point(25, 170),
                Size = new Size(520, 40)
            };
            pnlMonitor.Controls.Add(lblLiveButtons);

            lblLiveHid = new Label
            {
                Text = "Raw HID (MI_02):\n  Byte20: 00 | LT: 00 | RT: 00",
                Font = new Font("Consolas", 10.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(168, 85, 247),
                Location = new Point(25, 215),
                Size = new Size(520, 50)
            };
            pnlMonitor.Controls.Add(lblLiveHid);

            // Stick Crosshair Canvas (240x240)
            pnlCrosshair = new Panel
            {
                Location = new Point(25, 275),
                Size = new Size(240, 240),
                BackColor = Color.FromArgb(15, 15, 18),
                BorderStyle = BorderStyle.FixedSingle
            };
            pnlCrosshair.Paint += DrawStickCrosshair;
            pnlMonitor.Controls.Add(pnlCrosshair);

            Controls.Add(pnlMonitor);
            Controls.Add(pnlWizard);
            Controls.Add(pnlHeader);

            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
                {
                    CaptureCurrentStepAndAdvance();
                    e.Handled = true;
                }
            };
        }

        private int crosshairX = 120;
        private int crosshairY = 120;

        private void DrawStickCrosshair(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Center lines
            using var penGrid = new Pen(Color.FromArgb(40, 40, 48), 1);
            g.DrawLine(penGrid, 120, 0, 120, 240);
            g.DrawLine(penGrid, 0, 120, 240, 120);

            // Outer boundary circle
            using var penCircle = new Pen(Color.FromArgb(60, 60, 75), 1.5f);
            g.DrawEllipse(penCircle, 15, 15, 210, 210);

            // Deadzone center circle
            using var penDeadzone = new Pen(Color.FromArgb(80, 80, 40), 1);
            g.DrawEllipse(penDeadzone, 120 - 15, 120 - 15, 30, 30);

            // Stick position dot
            using var brushDot = new SolidBrush(Color.FromArgb(6, 182, 212));
            g.FillEllipse(brushDot, crosshairX - 7, crosshairY - 7, 14, 14);
        }

        private void InitializeDirectInputAndHid()
        {
            Program.Log("Initializing DirectInput...");
            try
            {
                directInput = new DirectInput();
                var devices = directInput.GetDevices(DeviceClass.All, DeviceEnumerationFlags.AllDevices);

                report.DeviceInfo = $"Found {devices.Count} DirectInput devices:";
                Program.Log(report.DeviceInfo);

                foreach (var d in devices)
                {
                    string info = $"\n- {d.InstanceName} (Guid: {d.InstanceGuid}, Product: {d.ProductName})";
                    report.DeviceInfo += info;
                    Program.Log(info);

                    try
                    {
                        var js = new Joystick(directInput, d.InstanceGuid);
                        try
                        {
                            js.SetCooperativeLevel(IntPtr.Zero, CooperativeLevel.NonExclusive | CooperativeLevel.Background);
                        }
                        catch { }
                        js.Properties.BufferSize = 128;
                        js.Acquire();
                        Program.Log($"  Acquired joystick: {d.InstanceName}");

                        string path = "";
                        try { path = js.Properties.InterfacePath.ToLower(); } catch { }
                        string name = d.InstanceName.ToLower();

                        if (path.Contains("col02") || name.Contains("col02"))
                        {
                            joystickRight = js;
                            Program.Log("  -> Identified as joystickRight (COL02)");
                        }
                        else if (path.Contains("col01") || name.Contains("col01"))
                        {
                            joystickLeft = js;
                            Program.Log("  -> Identified as joystickLeft (COL01)");
                        }
                        else
                        {
                            if (joystickRight == null) joystickRight = js;
                            else if (singleJoystick == null) singleJoystick = js;
                            Program.Log("  -> Identified as generic joystick");
                        }
                    }
                    catch (Exception ex)
                    {
                        Program.Log($"  Failed to acquire: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log($"DirectInput error: {ex.Message}");
            }

            Program.Log("Initializing Raw HID (64-byte)...");
            try
            {
                ushort vid = 0x17EF;
                ushort[] pids = { 0x6184, 0x61ED, 0x6183, 0x61EC };
                foreach (var pid in pids)
                {
                    try
                    {
                        rawHidController = new controller_hidapi.net.LegionController(vid, pid, 64);
                        rawHidController.OnControllerInputReceived += data =>
                        {
                            lock (hidLock)
                            {
                                Buffer.BlockCopy(data, 0, lastHidData, 0, Math.Min(data.Length, lastHidData.Length));
                            }
                        };
                        rawHidController.Open();
                        Program.Log($"Raw HID successfully opened for PID 0x{pid:X4}");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Program.Log($"Could not open raw HID for PID 0x{pid:X4}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log($"Raw HID init warning: {ex.Message}");
            }
        }

        private int lastX = -1, lastY = -1;

        private void OnPollTick(object? sender, EventArgs e)
        {
            JoystickState? stateR = null;
            JoystickState? stateL = null;

            if (joystickRight != null && !joystickRight.IsDisposed)
            {
                try { stateR = joystickRight.GetCurrentState(); } catch { }
            }
            if (joystickLeft != null && !joystickLeft.IsDisposed)
            {
                try { stateL = joystickLeft.GetCurrentState(); } catch { }
            }
            if (stateR == null && singleJoystick != null && !singleJoystick.IsDisposed)
            {
                try { stateR = singleJoystick.GetCurrentState(); } catch { }
            }

            long nowTick = stopwatch.ElapsedMilliseconds;
            if (stateR != null)
            {
                bool changed = stateR.X != lastX || stateR.Y != lastY;
                if (changed)
                {
                    if (lastChangeTick > 0)
                    {
                        double delta = nowTick - lastChangeTick;
                        deltaHistory.Add(delta);
                        if (deltaHistory.Count > 50) deltaHistory.RemoveAt(0);
                    }
                    lastChangeTick = nowTick;
                    lastX = stateR.X;
                    lastY = stateR.Y;

                    if (isPollingRateTesting)
                    {
                        pollSampleCount++;
                    }
                }
            }

            double avgDelta = deltaHistory.Count > 0 ? deltaHistory.Average() : 0;
            double hz = avgDelta > 0 ? 1000.0 / avgDelta : 0;
            lblLivePolling.Text = $"Polling: {hz:F1} Hz | Avg Interval: {avgDelta:F1} ms (samples: {deltaHistory.Count})";

            var pressedButtonIndices = new List<int>();
            int physX = 0, physY = 0;
            if (stateR != null)
            {
                // Inverted 90-degree CCW rotation for physical orientation:
                // Hardware Y -> Physical X (-32768 to 32767)
                // Hardware X -> Physical Y (32767 to -32768)
                physX = (int)InputUtils.MapRange(stateR.Y, 0, 65535, -32768, 32767);
                physY = (int)InputUtils.MapRange(stateR.X, 65535, 0, -32768, 32767);

                lblLiveRightStick.Text = $"Right Stick (COL02):\n  Physical Upright: X: {physX,5} | Y: {physY,5}\n  Raw DirectInput: X: {stateR.X,5} | Y: {stateR.Y,5} | Z: {stateR.Z,5} | Rz: {stateR.RotationZ,5}";

                // Map physical -32768..32767 to 15..225 on 240x240 canvas (physical UP = screen UP)
                crosshairX = (int)(120 + (physX / 32768.0) * 95);
                crosshairY = (int)(120 - (physY / 32768.0) * 95);
                crosshairX = Math.Clamp(crosshairX, 15, 225);
                crosshairY = Math.Clamp(crosshairY, 15, 225);
                pnlCrosshair.Invalidate();

                for (int b = 0; b < stateR.Buttons.Length; b++)
                {
                    if (stateR.Buttons[b]) pressedButtonIndices.Add(b);
                }
            }
            else
            {
                lblLiveRightStick.Text = "Right Stick (COL02): Disconnected / Not Acquired";
            }

            if (stateL != null)
            {
                lblLiveLeftStick.Text = $"Left Stick (COL01): X: {stateL.X,5} | Y: {stateL.Y,5}";
            }

            byte b18, b20, b21, b22, b23, b25;
            ushort padX, padY;
            lock (hidLock)
            {
                b18 = lastHidData[18];
                b20 = lastHidData[20];
                b21 = lastHidData[21];
                b22 = lastHidData[22];
                b23 = lastHidData[23];
                b25 = lastHidData[25];
                padX = (ushort)(lastHidData[26] << 8 | lastHidData[27]);
                padY = (ushort)(lastHidData[28] << 8 | lastHidData[29]);
            }

            var activeNames = new List<string>();
            foreach (int b in pressedButtonIndices) activeNames.Add($"B{b}");
            if ((b20 & 0x10) != 0) activeNames.Add("M1 (0x10)");
            if ((b20 & 0x08) != 0) activeNames.Add("M2 (0x08)");
            if ((b20 & 0x04) != 0) activeNames.Add("M3 (0x04)");
            if ((b20 & 0x20) != 0) activeNames.Add("Y3 (0x20)");
            if ((b18 & 0x40) != 0) activeNames.Add("Legion R (0x40)");
            if ((b21 & 0x80) != 0) activeNames.Add("Scroll Click");
            if (b25 == 129) activeNames.Add("Scroll UP");
            if (b25 == 255) activeNames.Add("Scroll DOWN");
            if (pressedButtonIndices.Contains(6)) activeNames.Add("M2 (B6)");
            if (pressedButtonIndices.Contains(8) || pressedButtonIndices.Contains(9) || pressedButtonIndices.Contains(10) || pressedButtonIndices.Contains(14)) activeNames.Add("RS Click");

            lblLiveButtons.Text = "Active Buttons: " + (activeNames.Count > 0 ? string.Join(", ", activeNames.Distinct()) : "None");
            lblLiveHid.Text = $"Raw HID (MI_02):\n  Byte20: 0x{b20:X2} | LT: {b22,3} | RT: {b23,3} | Pad: ({padX}, {padY})";

            // Live state update
            report.CurrentLive = new LiveState
            {
                RightX = stateR?.X ?? 32768,
                RightY = stateR?.Y ?? 32768,
                RightZ = stateR?.Z ?? 32768,
                RightRz = stateR?.RotationZ ?? 32768,
                ActiveButtons = pressedButtonIndices,
                Byte20BackButtons = b20,
                TriggerL = b22,
                TriggerR = b23,
                PollingHz = hz,
                AvgIntervalMs = avgDelta,
                LastUpdate = DateTime.UtcNow
            };

            // Stream live state to file every 250ms
            if (nowTick - lastLiveJsonSaveTick > 250)
            {
                lastLiveJsonSaveTick = nowTick;
                SaveReportFiles(false);
            }

            // Polling Rate 3-Second Test
            if (isPollingRateTesting)
            {
                long elapsed = pollingTestStopwatch.ElapsedMilliseconds;
                long remaining = Math.Max(0, 3000 - elapsed);
                lblStepInstruction.Text = $"Move the Right Stick continuously in circles!\nTime remaining: {(remaining / 1000.0):F1}s | Captured samples: {pollSampleCount}";
                progressHold.Maximum = 3000;
                progressHold.Value = Math.Min(3000, (int)elapsed);

                if (elapsed >= 3000)
                {
                    isPollingRateTesting = false;
                    RecordPollingTestResult();
                    AdvanceStep();
                }
                return;
            }

            // Gated State Machine Execution
            RunGatedStateMachine(stateR, pressedButtonIndices, b18, b20, b23, physX, physY);
        }

        private void RunGatedStateMachine(JoystickState? stateR, List<int> pressedButtons, byte b18, byte b20, byte b23, int physX, int physY)
        {
            if (currentStepIndex >= testSteps.Count || stateR == null) return;
            var step = testSteps[currentStepIndex];
            if (step.Type == StepType.PollingRate) return;

            bool isStickCentered = Math.Abs(physX) < DeadzoneThreshold && Math.Abs(physY) < DeadzoneThreshold;

            bool isReleaseSatisfied = false;
            if (step.Type == StepType.StickNeutral || step.Type == StepType.StickDirection)
            {
                isReleaseSatisfied = isStickCentered;
            }
            else if (step.Type == StepType.Button)
            {
                bool btnStillDown = (step.ButtonIndex >= 0 && pressedButtons.Contains(step.ButtonIndex)) ||
                                    (step.HidMask != 0 && (b20 & step.HidMask) != 0);
                isReleaseSatisfied = !btnStillDown;
            }
            else if (step.Type == StepType.Trigger)
            {
                isReleaseSatisfied = b23 < 30;
            }

            switch (gateState)
            {
                case InputGateState.WaitingForRelease:
                    progressHold.Value = 0;
                    if (isReleaseSatisfied)
                    {
                        gateState = InputGateState.WaitingForInput;
                        lblGateStatus.Text = "👉 READY: Perform prompted input and hold steady!";
                        lblGateStatus.ForeColor = Color.FromArgb(6, 182, 212);
                    }
                    else
                    {
                        lblGateStatus.Text = "👉 RELEASE TO CENTER: Let go first!";
                        lblGateStatus.ForeColor = Color.FromArgb(250, 204, 21);
                    }
                    break;

                case InputGateState.WaitingForInput:
                    bool inputTriggered = false;
                    if (step.Type == StepType.StickNeutral)
                    {
                        if (isStickCentered) inputTriggered = true;
                    }
                    else if (step.Type == StepType.StickDirection)
                    {
                        if (step.Name.Contains("UP", StringComparison.OrdinalIgnoreCase))
                            inputTriggered = physY > DeflectionThreshold;
                        else if (step.Name.Contains("RIGHT", StringComparison.OrdinalIgnoreCase))
                            inputTriggered = physX > DeflectionThreshold;
                        else if (step.Name.Contains("DOWN", StringComparison.OrdinalIgnoreCase))
                            inputTriggered = physY < -DeflectionThreshold;
                        else if (step.Name.Contains("LEFT", StringComparison.OrdinalIgnoreCase))
                            inputTriggered = physX < -DeflectionThreshold;
                    }
                    else if (step.Type == StepType.Button)
                    {
                        if (step.Name.Contains("Click", StringComparison.OrdinalIgnoreCase))
                        {
                            if (pressedButtons.Contains(8) || pressedButtons.Contains(9) || pressedButtons.Contains(10) || pressedButtons.Contains(14))
                                inputTriggered = true;
                        }
                        else
                        {
                            if (step.ButtonIndex >= 0 && pressedButtons.Contains(step.ButtonIndex))
                                inputTriggered = true;
                            if (step.HidMask != 0 && (b20 & step.HidMask) != 0)
                                inputTriggered = true;
                        }
                    }
                    else if (step.Type == StepType.Trigger)
                    {
                        if (b23 > 60 || pressedButtons.Contains(5))
                            inputTriggered = true;
                    }

                    if (inputTriggered)
                    {
                        gateState = InputGateState.HoldingInput;
                        holdStopwatch.Restart();
                        holdXSamples.Clear();
                        holdYSamples.Clear();
                        holdZSamples.Clear();
                        holdRzSamples.Clear();
                        progressHold.Maximum = RequiredHoldMs;
                    }
                    break;

                case InputGateState.HoldingInput:
                    holdXSamples.Add(stateR.X);
                    holdYSamples.Add(stateR.Y);
                    holdZSamples.Add(stateR.Z);
                    holdRzSamples.Add(stateR.RotationZ);

                    long holdElapsed = holdStopwatch.ElapsedMilliseconds;
                    progressHold.Value = Math.Min(RequiredHoldMs, (int)holdElapsed);

                    double remainSec = Math.Max(0, (RequiredHoldMs - holdElapsed) / 1000.0);
                    lblGateStatus.Text = $"👉 HOLDING STEADY... {remainSec:F1}s remaining";
                    lblGateStatus.ForeColor = Color.FromArgb(34, 197, 94);

                    bool releasedPrematurely = false;
                    if (step.Type == StepType.StickDirection)
                    {
                        if (step.Name.Contains("UP", StringComparison.OrdinalIgnoreCase) && physY <= DeflectionThreshold)
                            releasedPrematurely = true;
                        else if (step.Name.Contains("RIGHT", StringComparison.OrdinalIgnoreCase) && physX <= DeflectionThreshold)
                            releasedPrematurely = true;
                        else if (step.Name.Contains("DOWN", StringComparison.OrdinalIgnoreCase) && physY >= -DeflectionThreshold)
                            releasedPrematurely = true;
                        else if (step.Name.Contains("LEFT", StringComparison.OrdinalIgnoreCase) && physX >= -DeflectionThreshold)
                            releasedPrematurely = true;
                    }
                    else if (step.Type == StepType.Button)
                    {
                        bool isStillPressed = false;
                        if (step.Name.Contains("Click", StringComparison.OrdinalIgnoreCase))
                            isStillPressed = pressedButtons.Contains(8) || pressedButtons.Contains(9) || pressedButtons.Contains(10) || pressedButtons.Contains(14);
                        else
                            isStillPressed = (step.ButtonIndex >= 0 && pressedButtons.Contains(step.ButtonIndex)) || (step.HidMask != 0 && (b20 & step.HidMask) != 0);

                        if (!isStillPressed) releasedPrematurely = true;
                    }
                    else if (step.Type == StepType.Trigger && b23 < 40)
                    {
                        releasedPrematurely = true;
                    }

                    if (releasedPrematurely)
                    {
                        gateState = InputGateState.WaitingForInput;
                        progressHold.Value = 0;
                        lblGateStatus.Text = "⚠️ Released too soon! Please hold again.";
                        lblGateStatus.ForeColor = Color.FromArgb(239, 68, 68);
                    }
                    else if (holdElapsed >= RequiredHoldMs)
                    {
                        gateState = InputGateState.CompletedStep;
                        CaptureCurrentStepAndAdvance();
                    }
                    break;

                case InputGateState.CompletedStep:
                    // Handled in CaptureCurrentStepAndAdvance
                    break;
            }
        }

        private void ShowCurrentStep()
        {
            if (currentStepIndex >= testSteps.Count)
            {
                FinishAndSave();
                return;
            }

            var step = testSteps[currentStepIndex];
            lblStepTitle.Text = $"Step {currentStepIndex + 1} of {testSteps.Count}: {step.Name}";
            lblStepInstruction.Text = step.Instruction;
            progressOverall.Value = currentStepIndex;
            progressHold.Value = 0;

            if (step.Type == StepType.PollingRate)
            {
                btnManualCapture.Text = "Start 3-Second Polling Test";
                lblGateStatus.Text = "👉 Click button below when ready to swirl stick!";
                lblGateStatus.ForeColor = Color.FromArgb(6, 182, 212);
            }
            else
            {
                btnManualCapture.Text = "Manual Capture & Next >>";
                gateState = InputGateState.WaitingForRelease;
                lblGateStatus.Text = "👉 RELEASE STICK TO CENTER FIRST";
                lblGateStatus.ForeColor = Color.FromArgb(250, 204, 21);
            }
        }

        private void CaptureCurrentStepAndAdvance()
        {
            if (currentStepIndex >= testSteps.Count)
                return;

            var step = testSteps[currentStepIndex];
            if (step.Type == StepType.PollingRate)
            {
                isPollingRateTesting = true;
                pollSampleCount = 0;
                deltaHistory.Clear();
                pollingTestStopwatch.Restart();
                btnManualCapture.Enabled = false;
                return;
            }

            var record = new StepRecord
            {
                StepName = step.Name,
                Instruction = step.Instruction,
                Timestamp = DateTime.UtcNow
            };

            JoystickState? state = null;
            if (joystickRight != null && !joystickRight.IsDisposed)
            {
                try { state = joystickRight.GetCurrentState(); } catch { }
            }
            if (state == null && singleJoystick != null && !singleJoystick.IsDisposed)
            {
                try { state = singleJoystick.GetCurrentState(); } catch { }
            }

            // If we gathered hold samples, use their average for precision!
            int avgX = holdXSamples.Count > 0 ? (int)holdXSamples.Average() : (state?.X ?? 32768);
            int avgY = holdYSamples.Count > 0 ? (int)holdYSamples.Average() : (state?.Y ?? 32768);
            int avgZ = holdZSamples.Count > 0 ? (int)holdZSamples.Average() : (state?.Z ?? 32768);
            int avgRz = holdRzSamples.Count > 0 ? (int)holdRzSamples.Average() : (state?.RotationZ ?? 32768);

            // Calibrate neutral center on neutral step
            if (step.Type == StepType.StickNeutral)
            {
                neutralCenterX = avgX;
                neutralCenterY = avgY;
                Program.Log($"Calibrated neutral center: X={neutralCenterX}, Y={neutralCenterY}");
            }

            record.DirectInputAxes["X"] = avgX;
            record.DirectInputAxes["Y"] = avgY;
            record.DirectInputAxes["Z"] = avgZ;
            record.DirectInputAxes["RotationX"] = state?.RotationX ?? 0;
            record.DirectInputAxes["RotationY"] = state?.RotationY ?? 0;
            record.DirectInputAxes["RotationZ"] = avgRz;
            record.PovValue = state?.PointOfViewControllers[0] ?? -1;

            if (state != null)
            {
                for (int i = 0; i < state.Buttons.Length; i++)
                {
                    if (state.Buttons[i]) record.PressedButtons.Add(i);
                }
            }

            if (joystickLeft != null && !joystickLeft.IsDisposed)
            {
                try
                {
                    var stateL = joystickLeft.GetCurrentState();
                    record.DirectInputAxes["Left_X"] = stateL.X;
                    record.DirectInputAxes["Left_Y"] = stateL.Y;
                }
                catch { }
            }

            lock (hidLock)
            {
                record.Byte20BackButtons = lastHidData[20];
                record.Byte22TriggerL = lastHidData[22];
                record.Byte23TriggerR = lastHidData[23];
            }

            report.Steps.Add(record);
            Program.Log($"CAPTURED [{step.Name}]: X={avgX}, Y={avgY}, Buttons=[{string.Join(",", record.PressedButtons)}], HID B20=0x{record.Byte20BackButtons:X2}");

            SaveReportFiles(false);

            // Advance to next step and require release
            currentStepIndex++;
            gateState = InputGateState.WaitingForRelease;
            ShowCurrentStep();
        }

        private void RecordPollingTestResult()
        {
            var stats = new PollingStats
            {
                TotalSamples = pollSampleCount,
                MinDeltaMs = deltaHistory.Count > 0 ? deltaHistory.Min() : 0,
                MaxDeltaMs = deltaHistory.Count > 0 ? deltaHistory.Max() : 0,
                AvgDeltaMs = deltaHistory.Count > 0 ? deltaHistory.Average() : 0,
                EstimatedHz = deltaHistory.Count > 0 && deltaHistory.Average() > 0 ? 1000.0 / deltaHistory.Average() : 0,
                RecentIntervalsMs = new List<double>(deltaHistory)
            };
            report.StickPollingStats = stats;
            Program.Log($"Polling test complete: Avg interval={stats.AvgDeltaMs:F1}ms, Hz={stats.EstimatedHz:F1}");
            btnManualCapture.Enabled = true;
        }

        private void AdvanceStep()
        {
            currentStepIndex++;
            gateState = InputGateState.WaitingForRelease;
            ShowCurrentStep();
        }

        private void SaveReportFiles(bool isFinal)
        {
            string[] paths = {
                Path.Combine(@"C:\Users\LLG\Documents\LegionGoCompanionWorkspace", "test_output.json"),
                Path.Combine(@"C:\Users\LLG\Downloads", "test_output.json")
            };

            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(report, options);

                foreach (var p in paths)
                {
                    try { File.WriteAllText(p, json); } catch { }
                }

                if (isFinal)
                {
                    string txtPath = Path.Combine(@"C:\Users\LLG\Documents\LegionGoCompanionWorkspace", "test_output.txt");
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("=== LEGION GO INPUT TEST RESULTS ===");
                    sb.AppendLine($"Timestamp: {report.RunTime}");
                    sb.AppendLine($"Devices: {report.DeviceInfo}");
                    sb.AppendLine();
                    sb.AppendLine("--- RECORDED STEPS ---");
                    foreach (var s in report.Steps)
                    {
                        sb.AppendLine($"[{s.StepName}]");
                        sb.AppendLine($"  Axes: " + string.Join(", ", s.DirectInputAxes.Select(kv => $"{kv.Key}={kv.Value}")));
                        sb.AppendLine($"  Buttons: " + (s.PressedButtons.Count > 0 ? string.Join(", ", s.PressedButtons) : "None"));
                        sb.AppendLine($"  HID Byte20 (Back): 0x{s.Byte20BackButtons:X2} | LT: {s.Byte22TriggerL} | RT: {s.Byte23TriggerR}");
                        sb.AppendLine();
                    }

                    if (report.StickPollingStats != null)
                    {
                        sb.AppendLine("--- POLLING STATISTICS ---");
                        sb.AppendLine($"  Total Samples: {report.StickPollingStats.TotalSamples}");
                        sb.AppendLine($"  Min Interval: {report.StickPollingStats.MinDeltaMs:F1} ms");
                        sb.AppendLine($"  Max Interval: {report.StickPollingStats.MaxDeltaMs:F1} ms");
                        sb.AppendLine($"  Avg Interval: {report.StickPollingStats.AvgDeltaMs:F1} ms");
                        sb.AppendLine($"  Estimated Hz: {report.StickPollingStats.EstimatedHz:F1} Hz");
                    }

                    File.WriteAllText(txtPath, sb.ToString());
                    Program.Log($"Saved final report to {txtPath}");
                }
            }
            catch (Exception ex)
            {
                Program.Log($"Error saving report: {ex.Message}");
            }
        }

        private void FinishAndSave()
        {
            pollTimer.Stop();
            SaveReportFiles(true);

            string jsonPath = Path.Combine(@"C:\Users\LLG\Documents\LegionGoCompanionWorkspace", "test_output.json");
            lblStepTitle.Text = "TEST COMPLETE!";
            lblStepTitle.ForeColor = Color.FromArgb(34, 197, 94);
            lblGateStatus.Text = "✅ ALL STEPS SAVED!";
            lblGateStatus.ForeColor = Color.FromArgb(34, 197, 94);
            lblStepInstruction.Text = $"Results successfully saved to:\n{jsonPath}\n\nYou can close this window now.";
            btnManualCapture.Enabled = false;
            btnSkipStep.Enabled = false;
            progressOverall.Value = progressOverall.Maximum;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveReportFiles(true);
            try { rawHidController?.Close(); } catch { }
            try { joystickRight?.Unacquire(); joystickRight?.Dispose(); } catch { }
            try { joystickLeft?.Unacquire(); joystickLeft?.Dispose(); } catch { }
            try { singleJoystick?.Unacquire(); singleJoystick?.Dispose(); } catch { }
            try { directInput?.Dispose(); } catch { }
            base.OnFormClosing(e);
        }
    }
}
