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
using Newtonsoft.Json;
using SharpDX.DirectInput;
using SharpDX.XInput;

namespace RightControllerTester
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

            try
            {
                Application.Run(new TesterForm());
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.txt"), ex.ToString());
            }
        }
    }

    public class StepDefinition
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Instruction { get; set; } = "";
        public string ExpectedType { get; set; } = "Stick"; // Stick, Button, Trigger
        public string TargetInputName { get; set; } = "";
    }

    public class StepResult
    {
        public int StepId { get; set; }
        public string StepName { get; set; } = "";
        public bool Skipped { get; set; }
        public bool Success { get; set; }
        public string DetectedSource { get; set; } = ""; // DirectInput, RawHID, XInput
        public string DetectedIdentifier { get; set; } = ""; // Axis X, Button 0, Byte 18 bit 0x01
        public int RawValue { get; set; }
        public int BaselineValue { get; set; }
        public int Delta { get; set; }
        public string Details { get; set; } = "";
    }

    public class DiagnosticSummary
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string RightControllerDeviceName { get; set; } = "";
        public string DirectInputPath { get; set; } = "";
        public bool RawHidConnected { get; set; }
        public bool XInputConnected { get; set; }
        public Dictionary<string, StepResult> Results { get; set; } = new();
        public string StickOrientationDiagnosis { get; set; } = "";
    }

    public class TesterForm : Form
    {
        // Devices
        private DirectInput? directInput;
        private Joystick? jsRight;
        private Joystick? jsLeft;
        private controller_hidapi.net.LegionController? rawController;
        private Controller? xinputController;

        private byte[] rawHidData = new byte[64];
        private readonly object rawLock = new();
        private bool rawMisaligned = false;

        // Baseline values
        private int baselineX = 32768;
        private int baselineY = 32768;
        private int baselineZ = 32768;
        private int baselineRotationZ = 32768;

        // Current DirectInput state
        private int curX = 32768;
        private int curY = 32768;
        private int curZ = 32768;
        private int curRotationZ = 32768;
        private bool[] curButtons = new bool[128];
        private bool[] prevButtons = new bool[128];

        // Raw HID state
        private byte curFrontByte = 0;
        private byte curBackByte = 0;
        private byte curRtByte = 0;
        private byte prevFrontByte = 0;
        private byte prevBackByte = 0;

        // Steps
        private readonly List<StepDefinition> steps = new();
        private int currentStepIndex = 0;
        private readonly Dictionary<int, StepResult> stepResults = new();
        private bool stepCompleted = false;
        private int autoAdvanceCountdown = 0;

        // UI Controls
        private Label lblStepHeader = null!;
        private Label lblStepInstruction = null!;
        private Label lblDetectionStatus = null!;
        private ProgressBar progressBar = null!;
        private Button btnSkip = null!;
        private Button btnNext = null!;
        private Button btnPrevious = null!;
        private Label lblLiveMonitor = null!;
        private TextBox txtFinalReport = null!;
        private Button btnSaveAndClose = null!;
        private Panel pnlStepContent = null!;
        private Panel pnlReportContent = null!;
        private System.Windows.Forms.Timer pollTimer = null!;

        public TesterForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            Text = "Legion Go - Internal Right Controller Diagnostic & Axis Tester";
            Size = new Size(1150, 850);
            MinimumSize = new Size(1000, 750);
            BackColor = Color.FromArgb(18, 18, 26);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            StartPosition = FormStartPosition.CenterScreen;

            InitializeStepDefinitions();
            InitializeComponents();
            InitHardware();

            pollTimer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60 Hz
            pollTimer.Tick += PollTimer_Tick;
            pollTimer.Start();

            UpdateStepUI();
        }

        private void InitializeStepDefinitions()
        {
            steps.Add(new StepDefinition { Id = 1, TargetInputName = "StickUp", Title = "Right Stick: Push UP", Instruction = "Push the Right Thumbstick directly UP and hold it.", ExpectedType = "Stick" });
            steps.Add(new StepDefinition { Id = 2, TargetInputName = "StickDown", Title = "Right Stick: Push DOWN", Instruction = "Push the Right Thumbstick directly DOWN and hold it.", ExpectedType = "Stick" });
            steps.Add(new StepDefinition { Id = 3, TargetInputName = "StickLeft", Title = "Right Stick: Push LEFT", Instruction = "Push the Right Thumbstick directly LEFT and hold it.", ExpectedType = "Stick" });
            steps.Add(new StepDefinition { Id = 4, TargetInputName = "StickRight", Title = "Right Stick: Push RIGHT", Instruction = "Push the Right Thumbstick directly RIGHT and hold it.", ExpectedType = "Stick" });
            steps.Add(new StepDefinition { Id = 5, TargetInputName = "StickClick", Title = "Right Stick: CLICK (R3)", Instruction = "Press down firmly on the Right Thumbstick until it clicks.", ExpectedType = "Button" });
            steps.Add(new StepDefinition { Id = 6, TargetInputName = "ButtonA", Title = "Face Button: A", Instruction = "Press face button A on the right controller.", ExpectedType = "Button" });
            steps.Add(new StepDefinition { Id = 7, TargetInputName = "ButtonB", Title = "Face Button: B", Instruction = "Press face button B on the right controller.", ExpectedType = "Button" });
            steps.Add(new StepDefinition { Id = 8, TargetInputName = "ButtonX", Title = "Face Button: X", Instruction = "Press face button X on the right controller.", ExpectedType = "Button" });
            steps.Add(new StepDefinition { Id = 9, TargetInputName = "ButtonY", Title = "Face Button: Y", Instruction = "Press face button Y on the right controller.", ExpectedType = "Button" });
            steps.Add(new StepDefinition { Id = 10, TargetInputName = "ButtonY3", Title = "Back Button: Y3", Instruction = "Press rear back button Y3 (lowest rear paddle/grip button).", ExpectedType = "Button" });
            steps.Add(new StepDefinition { Id = 11, TargetInputName = "ButtonM3", Title = "Back Button: M3", Instruction = "Press side/bottom button M3 (bottom side paddle).", ExpectedType = "Button" });
            steps.Add(new StepDefinition { Id = 12, TargetInputName = "ButtonM1", Title = "Back Button: M1", Instruction = "Press upper rear button M1 (top rear grip button).", ExpectedType = "Button" });
            steps.Add(new StepDefinition { Id = 13, TargetInputName = "ButtonM2", Title = "Back Button: M2", Instruction = "Press lower rear button M2 (middle rear grip button).", ExpectedType = "Button" });
            steps.Add(new StepDefinition { Id = 14, TargetInputName = "RightTrigger", Title = "Right Trigger: RT", Instruction = "Pull the Right Trigger (RT / R2) all the way in.", ExpectedType = "Trigger" });
        }

        private void InitializeComponents()
        {
            var rootLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1,
                Padding = new Padding(16),
                BackColor = Color.FromArgb(18, 18, 26)
            };
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 160F));

            // Top Header Panel
            var pnlTop = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(26, 26, 38), Padding = new Padding(12) };
            var lblTitle = new Label
            {
                Text = "🎮 Right Controller Input & Calibration Tester",
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 180, 216),
                AutoSize = true,
                Location = new Point(12, 10)
            };
            progressBar = new ProgressBar
            {
                Location = new Point(12, 44),
                Size = new Size(1080, 12),
                Maximum = steps.Count,
                Value = 1
            };
            pnlTop.Controls.Add(lblTitle);
            pnlTop.Controls.Add(progressBar);
            rootLayout.Controls.Add(pnlTop, 0, 0);

            // Middle: Content Container
            var middleContainer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 10) };

            // Step Content Panel
            pnlStepContent = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(28, 28, 42),
                Padding = new Padding(24)
            };

            lblStepHeader = new Label
            {
                Text = "Step 1 of 14: Push Right Stick UP",
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 214, 10),
                AutoSize = true,
                Location = new Point(24, 20)
            };

            lblStepInstruction = new Label
            {
                Text = "Push the Right Thumbstick directly UP and hold it.",
                Font = new Font("Segoe UI", 13F, FontStyle.Regular),
                ForeColor = Color.FromArgb(230, 230, 245),
                AutoSize = true,
                Location = new Point(24, 65)
            };

            lblDetectionStatus = new Label
            {
                Text = "Waiting for input...",
                Font = new Font("Segoe UI", 13.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(160, 160, 180),
                AutoSize = true,
                Location = new Point(24, 125)
            };

            var btnPanel = new FlowLayoutPanel
            {
                Location = new Point(24, 200),
                Size = new Size(1050, 60),
                FlowDirection = FlowDirection.LeftToRight
            };

            btnSkip = new Button
            {
                Text = "⏭ Skip Step (Input Not Registered)",
                Size = new Size(300, 48),
                BackColor = Color.FromArgb(220, 53, 69),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 20, 0)
            };
            btnSkip.FlatAppearance.BorderSize = 0;
            btnSkip.Click += (s, e) => SkipCurrentStep();

            btnNext = new Button
            {
                Text = "Next Step ➔",
                Size = new Size(180, 48),
                BackColor = Color.FromArgb(0, 180, 216),
                ForeColor = Color.Black,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Enabled = false,
                Margin = new Padding(0, 0, 20, 0)
            };
            btnNext.FlatAppearance.BorderSize = 0;
            btnNext.Click += (s, e) => AdvanceToNextStep();

            btnPrevious = new Button
            {
                Text = "⬅ Previous",
                Size = new Size(140, 48),
                BackColor = Color.FromArgb(60, 60, 80),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Regular),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnPrevious.FlatAppearance.BorderSize = 0;
            btnPrevious.Click += (s, e) => GoToPreviousStep();

            btnPanel.Controls.Add(btnSkip);
            btnPanel.Controls.Add(btnNext);
            btnPanel.Controls.Add(btnPrevious);

            pnlStepContent.Controls.Add(lblStepHeader);
            pnlStepContent.Controls.Add(lblStepInstruction);
            pnlStepContent.Controls.Add(lblDetectionStatus);
            pnlStepContent.Controls.Add(btnPanel);

            // Report Panel (shown when test ends)
            pnlReportContent = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(28, 28, 42),
                Padding = new Padding(24),
                Visible = false
            };

            var lblReportTitle = new Label
            {
                Text = "📋 Test Completed - Diagnostic Summary & Recorded Results",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 180, 216),
                AutoSize = true,
                Location = new Point(24, 15)
            };

            txtFinalReport = new TextBox
            {
                Location = new Point(24, 55),
                Size = new Size(1050, 280),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(16, 16, 24),
                ForeColor = Color.FromArgb(0, 255, 200),
                Font = new Font("Consolas", 10F, FontStyle.Regular)
            };

            btnSaveAndClose = new Button
            {
                Text = "💾 Save Report & Close",
                Location = new Point(24, 350),
                Size = new Size(240, 44),
                BackColor = Color.FromArgb(40, 167, 69),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnSaveAndClose.FlatAppearance.BorderSize = 0;
            btnSaveAndClose.Click += (s, e) => SaveReportAndExit();

            pnlReportContent.Controls.Add(lblReportTitle);
            pnlReportContent.Controls.Add(txtFinalReport);
            pnlReportContent.Controls.Add(btnSaveAndClose);

            middleContainer.Controls.Add(pnlStepContent);
            middleContainer.Controls.Add(pnlReportContent);
            rootLayout.Controls.Add(middleContainer, 0, 1);

            // Bottom: Live Monitor Panel
            var pnlBottom = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(22, 22, 32),
                Padding = new Padding(12)
            };

            var lblBottomTitle = new Label
            {
                Text = "🔍 Live Hardware Monitor (Real-Time Raw Sensor Readings)",
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(180, 180, 200),
                AutoSize = true,
                Location = new Point(12, 8)
            };

            lblLiveMonitor = new Label
            {
                Location = new Point(12, 34),
                Size = new Size(1080, 110),
                Font = new Font("Consolas", 9.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(210, 210, 230),
                Text = "Initializing monitor..."
            };

            pnlBottom.Controls.Add(lblBottomTitle);
            pnlBottom.Controls.Add(lblLiveMonitor);
            rootLayout.Controls.Add(pnlBottom, 0, 2);

            Controls.Add(rootLayout);
        }

        private void InitHardware()
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
                            }
                            else if (path.Contains("col01"))
                            {
                                jsLeft = js;
                            }
                        }
                    }
                    catch { }
                }

                // If COL02 wasn't found specifically, check any game controller
                if (jsRight == null)
                {
                    foreach (var dev in devices)
                    {
                        if (dev.Type == SharpDX.DirectInput.DeviceType.Gamepad || dev.Type == SharpDX.DirectInput.DeviceType.Joystick || dev.Type == SharpDX.DirectInput.DeviceType.Supplemental)
                        {
                            try
                            {
                                var js = new Joystick(directInput, dev.InstanceGuid);
                                js.SetCooperativeLevel(Handle, CooperativeLevel.NonExclusive | CooperativeLevel.Background);
                                string path = js.Properties.InterfacePath.ToLower();
                                if (!path.Contains("col04") && !path.Contains("col03"))
                                {
                                    js.Properties.BufferSize = 128;
                                    try { js.Acquire(); } catch { }
                                    jsRight = js;
                                    break;
                                }
                            }
                            catch { }
                        }
                    }
                }

                // Initial baseline read
                if (jsRight != null)
                {
                    try
                    {
                        var state = jsRight.GetCurrentState();
                        baselineX = state.X;
                        baselineY = state.Y;
                        baselineZ = state.Z;
                        baselineRotationZ = state.RotationZ;
                    }
                    catch { }
                }

                // Open raw HID controller
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
                                        if (rawHidData[1] == 0)
                                            rawMisaligned = true;
                                    }
                                }
                            };
                            c.Open();
                            rawController = c;
                            break;
                        }
                        catch { }
                    }
                }
                catch { }

                // Check XInput
                for (int i = 0; i < 4; i++)
                {
                    var xCtrl = new Controller((UserIndex)i);
                    if (xCtrl.IsConnected)
                    {
                        xinputController = xCtrl;
                        break;
                    }
                }
            }
            catch { }
        }

        private void PollTimer_Tick(object? sender, EventArgs e)
        {
            // 1. Read DirectInput
            if (jsRight != null)
            {
                try
                {
                    var state = jsRight.GetCurrentState();
                    curX = state.X;
                    curY = state.Y;
                    curZ = state.Z;
                    curRotationZ = state.RotationZ;

                    Array.Copy(curButtons, prevButtons, curButtons.Length);
                    for (int i = 0; i < Math.Min(state.Buttons.Length, curButtons.Length); i++)
                        curButtons[i] = state.Buttons[i];
                }
                catch
                {
                    try { jsRight.Acquire(); } catch { }
                }
            }

            // 2. Read Raw HID
            lock (rawLock)
            {
                prevFrontByte = curFrontByte;
                prevBackByte = curBackByte;

                int frontIdx = rawMisaligned ? 16 : 14;
                int backIdx = rawMisaligned ? 18 : 16;
                int rtIdx = rawMisaligned ? 23 : 21;

                curFrontByte = rawHidData.Length > frontIdx ? rawHidData[frontIdx] : (byte)0;
                curBackByte = rawHidData.Length > backIdx ? rawHidData[backIdx] : (byte)0;
                curRtByte = rawHidData.Length > rtIdx ? rawHidData[rtIdx] : (byte)0;
            }

            // 3. Update Live Monitor String
            UpdateLiveMonitorDisplay();

            // 4. Test Step Logic
            if (currentStepIndex < steps.Count && !stepCompleted)
            {
                CheckCurrentStepInput();
            }
            else if (stepCompleted)
            {
                autoAdvanceCountdown--;
                if (autoAdvanceCountdown <= 0)
                {
                    AdvanceToNextStep();
                }
            }
        }

        private void UpdateLiveMonitorDisplay()
        {
            var sb = new StringBuilder();
            sb.Append($"[DirectInput COL02] X: {curX,5} (Δ {curX - baselineX,6}) | Y: {curY,5} (Δ {curY - baselineY,6}) | Z: {curZ,5} | Rz: {curRotationZ,5}\n");

            var pressedBtns = new List<int>();
            for (int i = 0; i < curButtons.Length; i++)
            {
                if (curButtons[i]) pressedBtns.Add(i);
            }
            string btnsStr = pressedBtns.Count > 0 ? string.Join(", ", pressedBtns) : "None";
            sb.Append($"[DirectInput Buttons] Active: {btnsStr}\n");

            sb.Append($"[Raw HID 0x17EF] BackButtons Byte: 0x{curBackByte:X2} (M1:{(curBackByte & 1) != 0} M2:{(curBackByte & 2) != 0} M3:{(curBackByte & 4) != 0} Y3:{(curBackByte & 0x20) != 0}) | RT Trigger: {curRtByte} | Front Byte: 0x{curFrontByte:X2} (LegionR:{(curFrontByte & 1) != 0})");

            lblLiveMonitor.Text = sb.ToString();
        }

        private void CheckCurrentStepInput()
        {
            var def = steps[currentStepIndex];

            switch (def.ExpectedType)
            {
                case "Stick":
                    CheckStickInput(def);
                    break;
                case "Button":
                    CheckButtonInput(def);
                    break;
                case "Trigger":
                    CheckTriggerInput(def);
                    break;
            }
        }

        private void CheckStickInput(StepDefinition def)
        {
            const int DEFLECTION_THRESHOLD = 12000;

            int dx = curX - baselineX;
            int dy = curY - baselineY;
            int dz = curZ - baselineZ;
            int drz = curRotationZ - baselineRotationZ;

            int maxDelta = 0;
            string bestAxis = "";
            int rawVal = 0;
            int baseVal = 0;

            if (Math.Abs(dx) > Math.Abs(maxDelta)) { maxDelta = dx; bestAxis = "X"; rawVal = curX; baseVal = baselineX; }
            if (Math.Abs(dy) > Math.Abs(maxDelta)) { maxDelta = dy; bestAxis = "Y"; rawVal = curY; baseVal = baselineY; }
            if (Math.Abs(dz) > Math.Abs(maxDelta)) { maxDelta = dz; bestAxis = "Z"; rawVal = curZ; baseVal = baselineZ; }
            if (Math.Abs(drz) > Math.Abs(maxDelta)) { maxDelta = drz; bestAxis = "RotationZ"; rawVal = curRotationZ; baseVal = baselineRotationZ; }

            if (Math.Abs(maxDelta) >= DEFLECTION_THRESHOLD)
            {
                string dir = maxDelta > 0 ? "INCREASED (+)" : "DECREASED (-)";
                var res = new StepResult
                {
                    StepId = def.Id,
                    StepName = def.TargetInputName,
                    Success = true,
                    Skipped = false,
                    DetectedSource = "DirectInput",
                    DetectedIdentifier = $"Axis {bestAxis}",
                    RawValue = rawVal,
                    BaselineValue = baseVal,
                    Delta = maxDelta,
                    Details = $"Axis {bestAxis} {dir} to {rawVal} (Delta: {maxDelta})"
                };

                OnStepDetected(res);
            }
        }

        private void CheckButtonInput(StepDefinition def)
        {
            // 1. DirectInput Buttons
            for (int i = 0; i < curButtons.Length; i++)
            {
                if (curButtons[i] && !prevButtons[i])
                {
                    var res = new StepResult
                    {
                        StepId = def.Id,
                        StepName = def.TargetInputName,
                        Success = true,
                        Skipped = false,
                        DetectedSource = "DirectInput",
                        DetectedIdentifier = $"Button {i}",
                        RawValue = 1,
                        BaselineValue = 0,
                        Delta = 1,
                        Details = $"DirectInput Button {i} pressed"
                    };
                    OnStepDetected(res);
                    return;
                }
            }

            // 2. Raw HID Back Buttons
            byte diffBack = (byte)(curBackByte & ~prevBackByte);
            if (diffBack != 0)
            {
                string btnName = (diffBack & 1) != 0 ? "M1 (0x01)" :
                                 (diffBack & 2) != 0 ? "M2 (0x02)" :
                                 (diffBack & 4) != 0 ? "M3 (0x04)" :
                                 (diffBack & 8) != 0 ? "Y1 (0x08)" :
                                 (diffBack & 0x10) != 0 ? "Y2 (0x10)" :
                                 (diffBack & 0x20) != 0 ? "Y3 (0x20)" : $"0x{diffBack:X2}";

                var res = new StepResult
                {
                    StepId = def.Id,
                    StepName = def.TargetInputName,
                    Success = true,
                    Skipped = false,
                    DetectedSource = "RawHID",
                    DetectedIdentifier = $"BackButton Byte bit {btnName}",
                    RawValue = curBackByte,
                    BaselineValue = prevBackByte,
                    Delta = diffBack,
                    Details = $"Raw HID Back Buttons byte bit {btnName} pressed"
                };
                OnStepDetected(res);
                return;
            }

            // 3. Raw HID Front Buttons
            byte diffFront = (byte)(curFrontByte & ~prevFrontByte);
            if (diffFront != 0)
            {
                string btnName = (diffFront & 1) != 0 ? "LegionR (0x01)" :
                                 (diffFront & 2) != 0 ? "LegionL (0x02)" : $"0x{diffFront:X2}";

                var res = new StepResult
                {
                    StepId = def.Id,
                    StepName = def.TargetInputName,
                    Success = true,
                    Skipped = false,
                    DetectedSource = "RawHID",
                    DetectedIdentifier = $"FrontButton Byte bit {btnName}",
                    RawValue = curFrontByte,
                    BaselineValue = prevFrontByte,
                    Delta = diffFront,
                    Details = $"Raw HID Front Buttons byte bit {btnName} pressed"
                };
                OnStepDetected(res);
                return;
            }
        }

        private void CheckTriggerInput(StepDefinition def)
        {
            // Raw HID Right Trigger
            if (curRtByte > 100)
            {
                var res = new StepResult
                {
                    StepId = def.Id,
                    StepName = def.TargetInputName,
                    Success = true,
                    Skipped = false,
                    DetectedSource = "RawHID",
                    DetectedIdentifier = "RightTriggerByte",
                    RawValue = curRtByte,
                    BaselineValue = 0,
                    Delta = curRtByte,
                    Details = $"Raw HID Right Trigger value {curRtByte} (>100)"
                };
                OnStepDetected(res);
                return;
            }

            // DirectInput axis check for trigger (Z axis deflection)
            if (Math.Abs(curZ - baselineZ) > 15000)
            {
                var res = new StepResult
                {
                    StepId = def.Id,
                    StepName = def.TargetInputName,
                    Success = true,
                    Skipped = false,
                    DetectedSource = "DirectInput",
                    DetectedIdentifier = "Axis Z",
                    RawValue = curZ,
                    BaselineValue = baselineZ,
                    Delta = curZ - baselineZ,
                    Details = $"DirectInput Axis Z deflected to {curZ} (Delta: {curZ - baselineZ})"
                };
                OnStepDetected(res);
                return;
            }
        }

        private void OnStepDetected(StepResult res)
        {
            stepResults[currentStepIndex] = res;
            stepCompleted = true;
            autoAdvanceCountdown = 45; // ~0.75 seconds

            lblDetectionStatus.Text = $"✅ SUCCESS: {res.Details}";
            lblDetectionStatus.ForeColor = Color.FromArgb(76, 201, 240);
            btnNext.Enabled = true;
        }

        private void SkipCurrentStep()
        {
            var def = steps[currentStepIndex];
            stepResults[currentStepIndex] = new StepResult
            {
                StepId = def.Id,
                StepName = def.TargetInputName,
                Success = false,
                Skipped = true,
                DetectedSource = "None",
                DetectedIdentifier = "SKIPPED",
                Details = "User skipped this input (not registered or not present)"
            };

            AdvanceToNextStep();
        }

        private void AdvanceToNextStep()
        {
            stepCompleted = false;
            autoAdvanceCountdown = 0;
            currentStepIndex++;

            if (currentStepIndex >= steps.Count)
            {
                ShowFinalReport();
            }
            else
            {
                UpdateStepUI();
            }
        }

        private void GoToPreviousStep()
        {
            if (currentStepIndex > 0)
            {
                stepCompleted = false;
                autoAdvanceCountdown = 0;
                currentStepIndex--;
                UpdateStepUI();
            }
        }

        private void UpdateStepUI()
        {
            if (currentStepIndex >= steps.Count) return;

            var def = steps[currentStepIndex];
            progressBar.Value = currentStepIndex + 1;
            lblStepHeader.Text = $"Step {currentStepIndex + 1} of {steps.Count}: {def.Title}";
            lblStepInstruction.Text = def.Instruction;

            if (stepResults.TryGetValue(currentStepIndex, out var prevRes))
            {
                if (prevRes.Skipped)
                {
                    lblDetectionStatus.Text = "⏭ Previously Skipped. Provide input again or click Next.";
                    lblDetectionStatus.ForeColor = Color.FromArgb(255, 183, 3);
                }
                else
                {
                    lblDetectionStatus.Text = $"✅ Previously Detected: {prevRes.Details}";
                    lblDetectionStatus.ForeColor = Color.FromArgb(76, 201, 240);
                }
                btnNext.Enabled = true;
            }
            else
            {
                lblDetectionStatus.Text = "Waiting for input...";
                lblDetectionStatus.ForeColor = Color.FromArgb(160, 160, 180);
                btnNext.Enabled = false;
            }

            btnPrevious.Enabled = currentStepIndex > 0;
        }

        private void ShowFinalReport()
        {
            pollTimer.Stop();
            pnlStepContent.Visible = false;
            pnlReportContent.Visible = true;

            var summary = GenerateDiagnosisSummary();
            string json = JsonConvert.SerializeObject(summary, Formatting.Indented);

            // Write files to local tools directory and project root
            try
            {
                string localFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "input_test_results.json");
                File.WriteAllText(localFile, json);

                string toolsDirFile = Path.Combine(Directory.GetCurrentDirectory(), "tools", "input_test_results.json");
                File.WriteAllText(toolsDirFile, json);
            }
            catch { }

            var sb = new StringBuilder();
            sb.AppendLine("================================================================================");
            sb.AppendLine("               LEGION GO RIGHT CONTROLLER INPUT DIAGNOSTIC REPORT               ");
            sb.AppendLine("================================================================================");
            sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Stick Orientation Diagnosis: {summary.StickOrientationDiagnosis}");
            sb.AppendLine();
            sb.AppendLine("--- RECORDED INPUTS ---");
            foreach (var kvp in summary.Results)
            {
                var r = kvp.Value;
                string status = r.Skipped ? "[SKIPPED]" : $"[OK] -> {r.DetectedSource} {r.DetectedIdentifier} (val={r.RawValue}, delta={r.Delta})";
                sb.AppendLine($"  • {r.StepName,-16}: {status}");
            }
            sb.AppendLine();
            sb.AppendLine("JSON report saved to: tools/input_test_results.json");
            sb.AppendLine("================================================================================");

            txtFinalReport.Text = sb.ToString();

            try
            {
                string summaryFile = Path.Combine(Directory.GetCurrentDirectory(), "tools", "input_test_summary.txt");
                File.WriteAllText(summaryFile, sb.ToString());
            }
            catch { }
        }

        private DiagnosticSummary GenerateDiagnosisSummary()
        {
            var summary = new DiagnosticSummary
            {
                RightControllerDeviceName = jsRight?.Properties.InstanceName ?? "Unknown DirectInput Device",
                DirectInputPath = jsRight?.Properties.InterfacePath ?? "Not Found",
                RawHidConnected = rawController != null,
                XInputConnected = xinputController?.IsConnected == true
            };

            foreach (var kvp in stepResults)
            {
                var def = steps[kvp.Key];
                summary.Results[def.TargetInputName] = kvp.Value;
            }

            // Analyze 90-degree shift
            bool hasUp = summary.Results.TryGetValue("StickUp", out var resUp) && !resUp.Skipped;
            bool hasDown = summary.Results.TryGetValue("StickDown", out var resDown) && !resDown.Skipped;
            bool hasLeft = summary.Results.TryGetValue("StickLeft", out var resLeft) && !resLeft.Skipped;
            bool hasRight = summary.Results.TryGetValue("StickRight", out var resRight) && !resRight.Skipped;

            if (hasUp && hasRight)
            {
                string upAxis = resUp!.DetectedIdentifier;
                string rightAxis = resRight!.DetectedIdentifier;

                if (upAxis.Contains("X") && rightAxis.Contains("Y"))
                {
                    summary.StickOrientationDiagnosis = "CONFIRMED 90° CCW SHIFT: Physical Vertical (Up/Down) drives DirectInput Axis X, and Physical Horizontal (Left/Right) drives DirectInput Axis Y.";
                }
                else if (upAxis.Contains("Y") && rightAxis.Contains("X"))
                {
                    summary.StickOrientationDiagnosis = "STANDARD (NO SHIFT): Physical Horizontal drives Axis X, Physical Vertical drives Axis Y.";
                }
                else
                {
                    summary.StickOrientationDiagnosis = $"CUSTOM MAPPING DETECTED: Up drives {upAxis}, Right drives {rightAxis}.";
                }
            }
            else
            {
                summary.StickOrientationDiagnosis = "Partial/Incomplete stick data (one or more directions were skipped).";
            }

            return summary;
        }

        private void SaveReportAndExit()
        {
            try
            {
                Clipboard.SetText(txtFinalReport.Text);
            }
            catch { }
            MessageBox.Show("Report copied to clipboard and saved to tools/input_test_results.json.\n\nThe application will now close.", "Test Finished", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
    }
}
