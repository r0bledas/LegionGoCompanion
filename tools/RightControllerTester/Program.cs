using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
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
        public string ExpectedType { get; set; } = "Stick"; // Stick, StickClick, Button, Trigger
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

        private string rightDevicePath = "";
        private string rightDeviceName = "";
        private DateTime lastAcquireRetry = DateTime.MinValue;
        private Point lastMousePos = Cursor.Position;
        private int curRawStickRX = 128;
        private int curRawStickRY = 128;

        // XInput state
        private int curXinputRX = 0;
        private int curXinputRY = 0;
        private byte curXinputRT = 0;
        private bool curXinputRightThumbBtn = false;
        private bool prevXinputRightThumbBtn = false;
        private bool curXinputA = false, prevXinputA = false;
        private bool curXinputB = false, prevXinputB = false;
        private bool curXinputX = false, prevXinputX = false;
        private bool curXinputY = false, prevXinputY = false;

        // Steps
        private readonly List<StepDefinition> steps = new();
        private int currentStepIndex = 0;
        private readonly Dictionary<int, StepResult> stepResults = new();
        private bool stepCompleted = false;
        private DateTime stepActivatedAt = DateTime.MinValue;

        // UI Controls
        private Label lblStepHeader = null!;
        private Label lblStepInstruction = null!;
        private Label lblDetectionStatus = null!;
        private ProgressBar progressBar = null!;
        private Button btnSkip = null!;
        private Button btnNext = null!;
        private Button btnPrevious = null!;
        private Button btnExportNow = null!;
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
            Text = "Legion Go - Right Controller Diagnostic and Input Tester";
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = FormWindowState.Maximized;
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
            steps.Add(new StepDefinition { Id = 1, TargetInputName = "StickUp", Title = "Right Stick: Push UP", Instruction = "Push the Right Thumbstick directly UP.", ExpectedType = "Stick" });
            steps.Add(new StepDefinition { Id = 2, TargetInputName = "StickDown", Title = "Right Stick: Push DOWN", Instruction = "Push the Right Thumbstick directly DOWN.", ExpectedType = "Stick" });
            steps.Add(new StepDefinition { Id = 3, TargetInputName = "StickLeft", Title = "Right Stick: Push LEFT", Instruction = "Push the Right Thumbstick directly LEFT.", ExpectedType = "Stick" });
            steps.Add(new StepDefinition { Id = 4, TargetInputName = "StickRight", Title = "Right Stick: Push RIGHT", Instruction = "Push the Right Thumbstick directly RIGHT.", ExpectedType = "Stick" });
            steps.Add(new StepDefinition { Id = 5, TargetInputName = "StickClick", Title = "Right Stick: CLICK (R3)", Instruction = "Keep the stick centered and press straight down on it until it clicks.", ExpectedType = "StickClick" });
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
                Padding = new Padding(20),
                BackColor = Color.FromArgb(18, 18, 26)
            };
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80F));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 180F));

            // Top Header Panel
            var pnlTop = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(26, 26, 38), Padding = new Padding(16) };
            var lblTitle = new Label
            {
                Text = "Right Controller Input and Diagnostic Tester",
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 180, 216),
                AutoSize = true,
                Location = new Point(16, 12)
            };

            btnExportNow = new Button
            {
                Text = "Export Results to File Now",
                Size = new Size(240, 36),
                BackColor = Color.FromArgb(40, 167, 69),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnExportNow.Location = new Point(pnlTop.Width - 260, 12);
            btnExportNow.FlatAppearance.BorderSize = 0;
            btnExportNow.Click += (s, e) => ExportCurrentResults(showMessage: true);

            progressBar = new ProgressBar
            {
                Location = new Point(16, 52),
                Size = new Size(1100, 14),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                Maximum = steps.Count,
                Value = 1
            };
            pnlTop.Controls.Add(lblTitle);
            pnlTop.Controls.Add(btnExportNow);
            pnlTop.Controls.Add(progressBar);
            rootLayout.Controls.Add(pnlTop, 0, 0);

            // Middle: Content Container
            var middleContainer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 12) };

            // Step Content Panel
            pnlStepContent = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(28, 28, 42),
                Padding = new Padding(30)
            };

            lblStepHeader = new Label
            {
                Text = "Step 1 of 14: Right Stick: Push UP",
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 214, 10),
                AutoSize = true,
                Location = new Point(30, 25)
            };

            lblStepInstruction = new Label
            {
                Text = "Push the Right Thumbstick directly UP.",
                Font = new Font("Segoe UI", 14F, FontStyle.Regular),
                ForeColor = Color.FromArgb(235, 235, 245),
                AutoSize = true,
                Location = new Point(30, 75)
            };

            lblDetectionStatus = new Label
            {
                Text = "Waiting for input...",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(160, 160, 180),
                AutoSize = true,
                Location = new Point(30, 145)
            };

            var btnPanel = new FlowLayoutPanel
            {
                Location = new Point(30, 230),
                Size = new Size(1100, 70),
                FlowDirection = FlowDirection.LeftToRight
            };

            btnSkip = new Button
            {
                Text = "Skip Step (Input Not Registered)",
                Size = new Size(320, 52),
                BackColor = Color.FromArgb(180, 40, 50),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 25, 0)
            };
            btnSkip.FlatAppearance.BorderSize = 0;
            btnSkip.Click += (s, e) => SkipCurrentStep();

            btnNext = new Button
            {
                Text = "Next Step >",
                Size = new Size(200, 52),
                BackColor = Color.FromArgb(0, 180, 216),
                ForeColor = Color.Black,
                Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Enabled = false,
                Margin = new Padding(0, 0, 25, 0)
            };
            btnNext.FlatAppearance.BorderSize = 0;
            btnNext.Click += (s, e) => AdvanceToNextStep();

            btnPrevious = new Button
            {
                Text = "< Previous",
                Size = new Size(160, 52),
                BackColor = Color.FromArgb(60, 60, 80),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 11F, FontStyle.Regular),
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
                Padding = new Padding(30),
                Visible = false
            };

            var lblReportTitle = new Label
            {
                Text = "Test Completed - Diagnostic Summary and Results",
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 180, 216),
                AutoSize = true,
                Location = new Point(30, 15)
            };

            txtFinalReport = new TextBox
            {
                Location = new Point(30, 60),
                Size = new Size(1100, 320),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(16, 16, 24),
                ForeColor = Color.FromArgb(0, 255, 200),
                Font = new Font("Consolas", 10.5F, FontStyle.Regular)
            };

            btnSaveAndClose = new Button
            {
                Text = "Save Report and Close",
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                Location = new Point(30, 400),
                Size = new Size(260, 48),
                BackColor = Color.FromArgb(40, 167, 69),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
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
                Padding = new Padding(16)
            };

            var lblBottomTitle = new Label
            {
                Text = "Live Hardware Monitor (Real-Time Raw Sensor Readings)",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(180, 180, 200),
                AutoSize = true,
                Location = new Point(16, 8)
            };

            lblLiveMonitor = new Label
            {
                Location = new Point(16, 38),
                Size = new Size(1100, 120),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom,
                Font = new Font("Consolas", 10F, FontStyle.Regular),
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
            EnsureDirectInputAcquired();
            EnsureRawHidAcquired();
            EnsureXInputAcquired();
        }

        private void EnsureDirectInputAcquired()
        {
            if (directInput == null)
            {
                try { directInput = new DirectInput(); } catch { return; }
            }

            if (jsRight != null && !jsRight.IsDisposed)
            {
                try
                {
                    jsRight.GetCurrentState();
                    return; // Currently acquired and responding
                }
                catch
                {
                    try { jsRight.Acquire(); return; } catch { }
                    try { jsRight.Unacquire(); jsRight.Dispose(); } catch { }
                    jsRight = null;
                }
            }

            try
            {
                var devices = directInput.GetDevices(DeviceClass.All, DeviceEnumerationFlags.AllDevices);

                // Pass 1: Look specifically for COL02 (Right Controller Gamepad)
                foreach (var dev in devices)
                {
                    try
                    {
                        var js = new Joystick(directInput, dev.InstanceGuid);
                        try { js.SetCooperativeLevel(Handle, CooperativeLevel.NonExclusive | CooperativeLevel.Background); } catch { }
                        string path = js.Properties.InterfacePath.ToLower();

                        if (path.Contains("17ef") && (path.Contains("6184") || path.Contains("61ed") || path.Contains("6183") || path.Contains("61ec")))
                        {
                            if (path.Contains("col02"))
                            {
                                js.Properties.BufferSize = 128;
                                try { js.Acquire(); } catch { }
                                jsRight = js;
                                rightDevicePath = path;
                                rightDeviceName = dev.InstanceName;

                                var st = js.GetCurrentState();
                                baselineX = st.X;
                                baselineY = st.Y;
                                baselineZ = st.Z;
                                baselineRotationZ = st.RotationZ;
                                return;
                            }
                        }
                        js.Dispose();
                    }
                    catch { }
                }

                // Pass 2: If COL02 not found, try COL01 (Left or single/combined controller)
                foreach (var dev in devices)
                {
                    try
                    {
                        var js = new Joystick(directInput, dev.InstanceGuid);
                        try { js.SetCooperativeLevel(Handle, CooperativeLevel.NonExclusive | CooperativeLevel.Background); } catch { }
                        string path = js.Properties.InterfacePath.ToLower();

                        if (path.Contains("17ef") && (path.Contains("6184") || path.Contains("61ed") || path.Contains("6183") || path.Contains("61ec")))
                        {
                            if (path.Contains("col01"))
                            {
                                js.Properties.BufferSize = 128;
                                try { js.Acquire(); } catch { }
                                jsRight = js;
                                rightDevicePath = path;
                                rightDeviceName = dev.InstanceName;

                                var st = js.GetCurrentState();
                                baselineX = st.X;
                                baselineY = st.Y;
                                baselineZ = st.Z;
                                baselineRotationZ = st.RotationZ;
                                return;
                            }
                        }
                        js.Dispose();
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void EnsureRawHidAcquired()
        {
            if (rawController != null && rawController.Reading) return;

            try { rawController?.Close(); } catch { }
            rawController = null;

            int[] pids = { 0x61ED, 0x6184, 0x6183, 0x61EB };
            foreach (int pid in pids)
            {
                try
                {
                    // Open Interface 2 (MI_02) specifically: 64-byte vendor report with back buttons, gyro, triggers
                    var c = new controller_hidapi.net.LegionController(0x17EF, (ushort)pid, 64, 2);
                    c.OnControllerInputReceived += (data) =>
                    {
                        if (data != null && data.Length >= 64)
                        {
                            lock (rawLock)
                            {
                                Buffer.BlockCopy(data, 0, rawHidData, 0, 64);
                                rawMisaligned = (rawHidData[1] == 0 && rawHidData.Skip(2).Any(b => b != 0));
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

        private void EnsureXInputAcquired()
        {
            if (xinputController != null && xinputController.IsConnected) return;

            for (int i = 0; i < 4; i++)
            {
                var ctrl = new Controller((UserIndex)i);
                if (ctrl.IsConnected)
                {
                    xinputController = ctrl;
                    break;
                }
            }
        }

        private void PollTimer_Tick(object? sender, EventArgs e)
        {
            // Dynamic re-acquisition check every 1 second
            if ((DateTime.UtcNow - lastAcquireRetry).TotalSeconds >= 1.0)
            {
                lastAcquireRetry = DateTime.UtcNow;
                EnsureDirectInputAcquired();
                EnsureRawHidAcquired();
                EnsureXInputAcquired();
            }

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

            // 2. Read Raw HID MI_02
            lock (rawLock)
            {
                prevFrontByte = curFrontByte;
                prevBackByte = curBackByte;

                int frontIdx = rawMisaligned ? 16 : 18;
                int backIdx = rawMisaligned ? 18 : 20;
                int rtIdx = rawMisaligned ? 21 : 23;

                curFrontByte = rawHidData.Length > frontIdx ? rawHidData[frontIdx] : (byte)0;
                curBackByte = rawHidData.Length > backIdx ? rawHidData[backIdx] : (byte)0;
                curRtByte = rawHidData.Length > rtIdx ? rawHidData[rtIdx] : (byte)0;

                if (rawHidData.Length > 17)
                {
                    curRawStickRX = rawHidData[16];
                    curRawStickRY = rawHidData[17];
                }
            }

            // 3. Read XInput
            if (xinputController != null && xinputController.IsConnected)
            {
                try
                {
                    var xState = xinputController.GetState().Gamepad;
                    curXinputRX = xState.RightThumbX;
                    curXinputRY = xState.RightThumbY;
                    curXinputRT = xState.RightTrigger;

                    prevXinputRightThumbBtn = curXinputRightThumbBtn;
                    curXinputRightThumbBtn = xState.Buttons.HasFlag(GamepadButtonFlags.RightThumb);

                    prevXinputA = curXinputA; curXinputA = xState.Buttons.HasFlag(GamepadButtonFlags.A);
                    prevXinputB = curXinputB; curXinputB = xState.Buttons.HasFlag(GamepadButtonFlags.B);
                    prevXinputX = curXinputX; curXinputX = xState.Buttons.HasFlag(GamepadButtonFlags.X);
                    prevXinputY = curXinputY; curXinputY = xState.Buttons.HasFlag(GamepadButtonFlags.Y);
                }
                catch { }
            }

            // 4. Read Mouse Delta (for pointer mode awareness)
            Point mousePos = Cursor.Position;
            int mouseDeltaX = mousePos.X - lastMousePos.X;
            int mouseDeltaY = mousePos.Y - lastMousePos.Y;
            lastMousePos = mousePos;

            // 5. Update Live Monitor String
            UpdateLiveMonitorDisplay(mouseDeltaX, mouseDeltaY);

            // 6. Test Step Logic
            if (currentStepIndex < steps.Count && !stepCompleted)
            {
                CheckCurrentStepInput();
            }
        }

        private void UpdateLiveMonitorDisplay(int mouseDeltaX, int mouseDeltaY)
        {
            var sb = new StringBuilder();
            string diDev = jsRight != null ? (rightDevicePath.Contains("col02") ? "COL02 (Right Gamepad)" : "COL01") : "Disconnected (Searching...)";
            sb.Append($"[DirectInput {diDev}] X: {curX,5} (Delta {curX - baselineX,6}) | Y: {curY,5} (Delta {curY - baselineY,6}) | Z: {curZ,5} | Rz: {curRotationZ,5}\n");

            var pressedBtns = new List<int>();
            for (int i = 0; i < curButtons.Length; i++)
            {
                if (curButtons[i]) pressedBtns.Add(i);
            }
            string btnsStr = pressedBtns.Count > 0 ? string.Join(", ", pressedBtns) : "None";
            sb.Append($"[DirectInput Buttons] Active: {btnsStr}\n");

            bool m1 = (curBackByte & 0x10) != 0;
            bool m2 = (curBackByte & 0x08) != 0;
            bool m3 = (curBackByte & 0x04) != 0;
            bool y3 = (curBackByte & 0x20) != 0;
            bool legR = (curFrontByte & 0x01) != 0;

            string hidStatus = rawController != null && rawController.Reading ? "Connected" : "Disconnected";
            sb.Append($"[Raw HID MI_02 ({hidStatus})] Back: 0x{curBackByte:X2} (M1:{m1}, M2:{m2}, M3:{m3}, Y3:{y3}) | Front: 0x{curFrontByte:X2} (LegionR:{legR}) | RT: {curRtByte}\n");

            string xinputStr = (xinputController != null && xinputController.IsConnected) ? $"RX={curXinputRX}, RY={curXinputRY}" : "Off";
            sb.Append($"[XInput / Mouse] XInput: {xinputStr} | Mouse Cursor Delta: ({mouseDeltaX}, {mouseDeltaY})");

            lblLiveMonitor.Text = sb.ToString();
        }

        private void CheckCurrentStepInput()
        {
            if ((DateTime.UtcNow - stepActivatedAt).TotalMilliseconds < 400)
                return; // Grace period so previous release/transition doesn't immediately trigger new step

            var def = steps[currentStepIndex];

            switch (def.ExpectedType)
            {
                case "Stick":
                    CheckStickInput(def);
                    break;
                case "StickClick":
                    CheckStickClickInput(def);
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

            // 1. DirectInput
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

                OnStepDetected(res, requireStickNeutralNotice: true);
                return;
            }

            // 2. XInput Fallback
            if (xinputController != null && xinputController.IsConnected)
            {
                int xDelta = Math.Abs(curXinputRX) > Math.Abs(curXinputRY) ? curXinputRX : curXinputRY;
                string xName = Math.Abs(curXinputRX) > Math.Abs(curXinputRY) ? "RightStickX" : "RightStickY";
                if (Math.Abs(xDelta) >= DEFLECTION_THRESHOLD)
                {
                    string dir = xDelta > 0 ? "INCREASED (+)" : "DECREASED (-)";
                    var res = new StepResult
                    {
                        StepId = def.Id,
                        StepName = def.TargetInputName,
                        Success = true,
                        Skipped = false,
                        DetectedSource = "XInput",
                        DetectedIdentifier = xName,
                        RawValue = xDelta,
                        BaselineValue = 0,
                        Delta = xDelta,
                        Details = $"XInput {xName} {dir} to {xDelta}"
                    };
                    OnStepDetected(res, requireStickNeutralNotice: true);
                    return;
                }
            }

            // 3. Raw HID Stick Fallback (Bytes 16 and 17 on MI_02, centered at ~128)
            int rawDeltaX = curRawStickRX - 128;
            int rawDeltaY = curRawStickRY - 128;
            int rawMax = Math.Abs(rawDeltaX) > Math.Abs(rawDeltaY) ? rawDeltaX : rawDeltaY;
            string rawName = Math.Abs(rawDeltaX) > Math.Abs(rawDeltaY) ? "RawStickByte16" : "RawStickByte17";
            if (Math.Abs(rawMax) >= 35)
            {
                string dir = rawMax > 0 ? "INCREASED (+)" : "DECREASED (-)";
                var res = new StepResult
                {
                    StepId = def.Id,
                    StepName = def.TargetInputName,
                    Success = true,
                    Skipped = false,
                    DetectedSource = "RawHID",
                    DetectedIdentifier = rawName,
                    RawValue = rawMax + 128,
                    BaselineValue = 128,
                    Delta = rawMax,
                    Details = $"Raw HID {rawName} {dir} to {rawMax + 128}"
                };
                OnStepDetected(res, requireStickNeutralNotice: true);
                return;
            }
        }

        private void CheckStickClickInput(StepDefinition def)
        {
            // For Stick Click (R3), ensure the stick is NOT deflected (must be near neutral center)
            int dx = Math.Abs(curX - baselineX);
            int dy = Math.Abs(curY - baselineY);
            if (dx > 8000 || dy > 8000 || Math.Abs(curXinputRX) > 8000 || Math.Abs(curXinputRY) > 8000)
            {
                // Stick is deflected; ignore until centered to avoid confusing deflection with stick click
                return;
            }

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
                        Details = $"Stick Click detected on DirectInput Button {i}"
                    };
                    OnStepDetected(res, requireStickNeutralNotice: false);
                    return;
                }
            }

            // 2. XInput RightThumb Click
            if (curXinputRightThumbBtn && !prevXinputRightThumbBtn)
            {
                var res = new StepResult
                {
                    StepId = def.Id,
                    StepName = def.TargetInputName,
                    Success = true,
                    Skipped = false,
                    DetectedSource = "XInput",
                    DetectedIdentifier = "RightThumbClick",
                    RawValue = 1,
                    BaselineValue = 0,
                    Delta = 1,
                    Details = "Stick Click detected on XInput RightThumb"
                };
                OnStepDetected(res, requireStickNeutralNotice: false);
                return;
            }
        }

        private void CheckButtonInput(StepDefinition def)
        {
            // 1. Target-Specific Raw HID Back Buttons (MI_02)
            if (def.TargetInputName == "ButtonM1" && (curBackByte & 0x10) != 0)
            {
                TriggerButtonDetected(def, "RawHID", "Back Button M1 (0x10)", curBackByte);
                return;
            }
            if (def.TargetInputName == "ButtonM2" && (curBackByte & 0x08) != 0)
            {
                TriggerButtonDetected(def, "RawHID", "Back Button M2 (0x08)", curBackByte);
                return;
            }
            if (def.TargetInputName == "ButtonM3" && (curBackByte & 0x04) != 0)
            {
                TriggerButtonDetected(def, "RawHID", "Back Button M3 (0x04)", curBackByte);
                return;
            }
            if (def.TargetInputName == "ButtonY3" && (curBackByte & 0x20) != 0)
            {
                TriggerButtonDetected(def, "RawHID", "Back Button Y3 (0x20)", curBackByte);
                return;
            }

            // 2. DirectInput Buttons
            for (int i = 0; i < curButtons.Length; i++)
            {
                if (curButtons[i] && !prevButtons[i])
                {
                    TriggerButtonDetected(def, "DirectInput", $"Button {i}", 1);
                    return;
                }
            }

            // 3. Generic Raw HID Back Buttons change
            byte diffBack = (byte)(curBackByte & ~prevBackByte);
            if (diffBack != 0)
            {
                string btnName = (diffBack & 0x10) != 0 ? "M1 (0x10)" :
                                 (diffBack & 0x08) != 0 ? "M2 (0x08)" :
                                 (diffBack & 0x04) != 0 ? "M3 (0x04)" :
                                 (diffBack & 0x20) != 0 ? "Y3 (0x20)" :
                                 (diffBack & 0x40) != 0 ? "Y2 (0x40)" :
                                 (diffBack & 0x80) != 0 ? "Y1 (0x80)" : $"0x{diffBack:X2}";

                TriggerButtonDetected(def, "RawHID", $"BackButton {btnName}", curBackByte);
                return;
            }

            // 4. Raw HID Front Buttons (LegionR / LegionL)
            byte diffFront = (byte)(curFrontByte & ~prevFrontByte);
            if (diffFront != 0)
            {
                string btnName = (diffFront & 0x01) != 0 ? "LegionR (0x01)" :
                                 (diffFront & 0x02) != 0 ? "LegionL (0x02)" : $"0x{diffFront:X2}";

                TriggerButtonDetected(def, "RawHID", $"FrontButton {btnName}", curFrontByte);
                return;
            }

            // 5. XInput Face Buttons
            if (curXinputA && !prevXinputA) { TriggerButtonDetected(def, "XInput", "Button A", 1); return; }
            if (curXinputB && !prevXinputB) { TriggerButtonDetected(def, "XInput", "Button B", 1); return; }
            if (curXinputX && !prevXinputX) { TriggerButtonDetected(def, "XInput", "Button X", 1); return; }
            if (curXinputY && !prevXinputY) { TriggerButtonDetected(def, "XInput", "Button Y", 1); return; }
        }

        private void TriggerButtonDetected(StepDefinition def, string source, string identifier, int val)
        {
            var res = new StepResult
            {
                StepId = def.Id,
                StepName = def.TargetInputName,
                Success = true,
                Skipped = false,
                DetectedSource = source,
                DetectedIdentifier = identifier,
                RawValue = val,
                BaselineValue = 0,
                Delta = 1,
                Details = $"{source} {identifier} detected"
            };
            OnStepDetected(res, requireStickNeutralNotice: false);
        }

        private void CheckTriggerInput(StepDefinition def)
        {
            // 1. Raw HID Right Trigger (byte 23 on MI_02)
            if (curRtByte > 100)
            {
                var res = new StepResult
                {
                    StepId = def.Id,
                    StepName = def.TargetInputName,
                    Success = true,
                    Skipped = false,
                    DetectedSource = "RawHID",
                    DetectedIdentifier = "RightTriggerByte (RT)",
                    RawValue = curRtByte,
                    BaselineValue = 0,
                    Delta = curRtByte,
                    Details = $"Raw HID Right Trigger value {curRtByte} (>100)"
                };
                OnStepDetected(res, requireStickNeutralNotice: false);
                return;
            }

            // 2. DirectInput Trigger (Z axis deflection)
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
                OnStepDetected(res, requireStickNeutralNotice: false);
                return;
            }

            // 3. XInput Right Trigger
            if (curXinputRT > 100)
            {
                var res = new StepResult
                {
                    StepId = def.Id,
                    StepName = def.TargetInputName,
                    Success = true,
                    Skipped = false,
                    DetectedSource = "XInput",
                    DetectedIdentifier = "RightTrigger (RT)",
                    RawValue = curXinputRT,
                    BaselineValue = 0,
                    Delta = curXinputRT,
                    Details = $"XInput Right Trigger value {curXinputRT} (>100)"
                };
                OnStepDetected(res, requireStickNeutralNotice: false);
                return;
            }
        }

        private void OnStepDetected(StepResult res, bool requireStickNeutralNotice)
        {
            stepResults[currentStepIndex] = res;
            stepCompleted = true;

            string suffix = requireStickNeutralNotice ? " - Release stick, then click Next Step >." : " - Click Next Step > to proceed.";
            lblDetectionStatus.Text = "SUCCESS: " + res.Details + suffix;
            lblDetectionStatus.ForeColor = Color.FromArgb(76, 201, 240);
            btnNext.Enabled = true;

            // Automatically export on every successful detection
            ExportCurrentResults(showMessage: false);
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

            // Automatically export on every skip
            ExportCurrentResults(showMessage: false);

            AdvanceToNextStep();
        }

        private void AdvanceToNextStep()
        {
            // If currently on a stick step, ensure the user has actually released the stick before advancing
            if (currentStepIndex < steps.Count)
            {
                var def = steps[currentStepIndex];
                if (def.ExpectedType == "Stick")
                {
                    int dx = Math.Abs(curX - baselineX);
                    int dy = Math.Abs(curY - baselineY);
                    int rawDx = Math.Abs(curRawStickRX - 128);
                    int rawDy = Math.Abs(curRawStickRY - 128);
                    int xix = Math.Abs(curXinputRX);
                    int xiy = Math.Abs(curXinputRY);
                    if (dx > 8000 || dy > 8000 || rawDx > 25 || rawDy > 25 || xix > 8000 || xiy > 8000)
                    {
                        lblDetectionStatus.Text = "Please let go of the thumbstick so it returns to center before clicking Next Step >.";
                        lblDetectionStatus.ForeColor = Color.FromArgb(255, 183, 3);
                        return;
                    }
                }
            }

            stepCompleted = false;
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
                currentStepIndex--;
                UpdateStepUI();
            }
        }

        private void UpdateStepUI()
        {
            if (currentStepIndex >= steps.Count) return;

            stepActivatedAt = DateTime.UtcNow;

            var def = steps[currentStepIndex];
            progressBar.Value = currentStepIndex + 1;
            lblStepHeader.Text = $"Step {currentStepIndex + 1} of {steps.Count}: {def.Title}";
            lblStepInstruction.Text = def.Instruction;

            // Synchronize button states to prevent any held buttons from immediately triggering the new step
            Array.Copy(curButtons, prevButtons, curButtons.Length);
            prevFrontByte = curFrontByte;
            prevBackByte = curBackByte;
            prevXinputRightThumbBtn = curXinputRightThumbBtn;
            prevXinputA = curXinputA;
            prevXinputB = curXinputB;
            prevXinputX = curXinputX;
            prevXinputY = curXinputY;

            if (stepResults.TryGetValue(currentStepIndex, out var prevRes))
            {
                if (prevRes.Skipped)
                {
                    lblDetectionStatus.Text = "Previously Skipped. Provide input again or click Next Step >.";
                    lblDetectionStatus.ForeColor = Color.FromArgb(255, 183, 3);
                }
                else
                {
                    lblDetectionStatus.Text = $"Previously Detected: {prevRes.Details}";
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

            ExportCurrentResults(showMessage: false);

            var summary = GenerateDiagnosisSummary();
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
                sb.AppendLine($"  - {r.StepName,-16}: {status}");
            }
            sb.AppendLine();
            sb.AppendLine("Report automatically exported to: tools/input_test_results.json");
            sb.AppendLine("================================================================================");

            txtFinalReport.Text = sb.ToString();
        }

        private void ExportCurrentResults(bool showMessage)
        {
            try
            {
                var summary = GenerateDiagnosisSummary();
                string json = JsonConvert.SerializeObject(summary, Formatting.Indented);

                // Write to known paths
                string[] paths = new string[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "input_test_results.json"),
                    Path.Combine(Directory.GetCurrentDirectory(), "tools", "input_test_results.json"),
                    Path.Combine(Directory.GetCurrentDirectory(), "input_test_results.json"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "LegionGoCompanionWorkspace", "LegionGoCompanion-main", "tools", "input_test_results.json")
                };

                foreach (var p in paths)
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(p) ?? "";
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        {
                            File.WriteAllText(p, json);
                        }
                    }
                    catch { }
                }

                if (showMessage)
                {
                    MessageBox.Show($"Results exported successfully to:\n{paths[1]}\n\nThe agent can now inspect the file directly.", "Export Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                if (showMessage)
                {
                    MessageBox.Show("Export error: " + ex.Message, "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
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
                    summary.StickOrientationDiagnosis = "CONFIRMED 90 DEGREE CCW SHIFT: Physical Vertical (Up/Down) drives DirectInput Axis X, and Physical Horizontal (Left/Right) drives DirectInput Axis Y.";
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
            ExportCurrentResults(showMessage: false);
            try
            {
                Clipboard.SetText(txtFinalReport.Text);
            }
            catch { }
            MessageBox.Show("Report saved to tools/input_test_results.json and copied to clipboard.\n\nThe application will now close.", "Test Finished", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
    }
}
