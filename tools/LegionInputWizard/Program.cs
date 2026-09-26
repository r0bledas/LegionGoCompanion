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

namespace LegionInputWizard
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
                Application.Run(new WizardForm());
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.txt"), ex.ToString());
            }
        }
    }

    public enum WizardStep
    {
        Welcome = 0,
        NeutralStick = 1,
        StickUp = 2,
        StickDown = 3,
        StickLeft = 4,
        StickRight = 5,
        AimButton = 6,
        GyroPitchUp = 7,
        GyroYawRight = 8,
        CompleteAndTest = 9
    }

    public class CalibrationData
    {
        public int NeutralX { get; set; } = 32768;
        public int NeutralY { get; set; } = 32768;

        // Which DirectInput raw axis controls physical X (Left <-> Right)
        public string AxisXName { get; set; } = "Y"; // Default on COL02
        public bool InvertX { get; set; } = false;

        // Which DirectInput raw axis controls physical Y (Down <-> Up)
        public string AxisYName { get; set; } = "X"; // Default on COL02
        public bool InvertY { get; set; } = true;

        // Aim activation button index
        public int AimButtonIndex { get; set; } = 7; // RB
        public string AimButtonName { get; set; } = "RB";

        // Gyro axes
        public int GyroPitchAxis { get; set; } = 0; // 0=Gx, 1=Gy, 2=Gz
        public float GyroPitchSign { get; set; } = -1.0f;
        public int GyroYawAxis { get; set; } = 1;
        public float GyroYawSign { get; set; } = -1.0f;

        public float MouseSensitivity { get; set; } = 1.5f;
    }

    public class WizardForm : Form
    {
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);
        private const uint MOUSEEVENTF_MOVE = 0x0001;

        private DirectInput? directInput;
        private Joystick? jsRight;
        private controller_hidapi.net.LegionController? rawController;

        private byte[] rawHidData = new byte[64];
        private readonly object rawLock = new();

        private WizardStep currentStep = WizardStep.Welcome;
        private readonly CalibrationData cal = new();

        // UI Controls
        private Label lblStepTitle = null!;
        private Label lblInstruction = null!;
        private Label lblLiveTelemetry = null!;
        private ProgressBar prgDeflection = null!;
        private Button btnNext = null!;
        private Button btnBack = null!;
        private Button btnRestart = null!;
        private Button btnSaveConfig = null!;
        private CheckBox chkEnableMouse = null!;
        private TrackBar trkSens = null!;
        private Label lblSens = null!;
        private Panel pnlVisual = null!;
        private System.Windows.Forms.Timer loopTimer = null!;

        // Live measurements
        private int rawX = 32768;
        private int rawY = 32768;
        private int rawZ = 32768;
        private int rawRz = 32768;
        private readonly bool[] rawButtons = new bool[32];
        private float rawGx, rawGy, rawGz;

        // Mouse output accumulator
        private float subX = 0f;
        private float subY = 0f;
        private DateTime lastTick = DateTime.UtcNow;

        public WizardForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            Text = "Legion Go Input Calibration & Testing Wizard";
            Size = new Size(1000, 750);
            MinimumSize = new Size(880, 620);
            BackColor = Color.FromArgb(20, 20, 30);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 10.5F, FontStyle.Regular, GraphicsUnit.Point);
            StartPosition = FormStartPosition.CenterScreen;

            InitializeLayout();
            InitHardware();

            loopTimer = new System.Windows.Forms.Timer { Interval = 10 }; // 100 Hz polling
            loopTimer.Tick += LoopTimer_Tick;
            loopTimer.Start();

            UpdateStepUI();
        }

        private void InitializeLayout()
        {
            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 5,
                ColumnCount = 1,
                Padding = new Padding(24),
                BackColor = Color.FromArgb(20, 20, 30)
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55F));  // Header
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110F)); // Step Instructions
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));   // Interactive Visual & Telemetry
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));  // Deflection / Controls
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));  // Navigation Buttons
            Controls.Add(mainLayout);

            // 1. Header
            lblStepTitle = new Label
            {
                Text = "Step 1 of 8: Neutral Center Calibration",
                Font = new Font("Segoe UI", 18F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(0, 210, 106),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            mainLayout.Controls.Add(lblStepTitle, 0, 0);

            // 2. Big Step Instruction Banner
            lblInstruction = new Label
            {
                Text = "Please release the Right Stick completely so it rests in its neutral center position.",
                Font = new Font("Segoe UI", 13.5F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(240, 240, 255),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(28, 28, 42),
                Padding = new Padding(16, 8, 16, 8)
            };
            mainLayout.Controls.Add(lblInstruction, 0, 1);

            // 3. Middle Area: Visual Box & Telemetry
            var middlePanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 10, 0, 10)
            };
            middlePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            middlePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            mainLayout.Controls.Add(middlePanel, 0, 2);

            // Visual Crosshair / Feedback Box
            pnlVisual = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(14, 14, 22),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 0, 12, 0)
            };
            pnlVisual.Paint += PnlVisual_Paint;
            middlePanel.Controls.Add(pnlVisual, 0, 0);

            // Telemetry Box
            lblLiveTelemetry = new Label
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(14, 14, 22),
                BorderStyle = BorderStyle.FixedSingle,
                ForeColor = Color.FromArgb(0, 180, 216),
                Font = new Font("Consolas", 10.5F, FontStyle.Regular, GraphicsUnit.Point),
                Padding = new Padding(12),
                TextAlign = ContentAlignment.TopLeft,
                Margin = new Padding(12, 0, 0, 0)
            };
            middlePanel.Controls.Add(lblLiveTelemetry, 1, 0);

            // 4. Deflection / Progress Bar & Sens Slider
            var controlsRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 8, 0, 0)
            };
            mainLayout.Controls.Add(controlsRow, 0, 3);

            prgDeflection = new ProgressBar
            {
                Width = 260,
                Height = 32,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Margin = new Padding(0, 4, 20, 0)
            };
            controlsRow.Controls.Add(prgDeflection);

            chkEnableMouse = new CheckBox
            {
                Text = "🖱️ Live Mouse Control Active",
                Checked = false,
                AutoSize = true,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(255, 159, 28),
                Margin = new Padding(0, 8, 20, 0),
                Cursor = Cursors.Hand
            };
            controlsRow.Controls.Add(chkEnableMouse);

            lblSens = new Label
            {
                Text = "Sensitivity: 1.5x",
                AutoSize = true,
                Margin = new Padding(0, 10, 8, 0)
            };
            controlsRow.Controls.Add(lblSens);

            trkSens = new TrackBar
            {
                Minimum = 5,
                Maximum = 40,
                Value = 15,
                TickFrequency = 5,
                Width = 140,
                Margin = new Padding(0, 6, 0, 0)
            };
            trkSens.ValueChanged += (s, e) =>
            {
                cal.MouseSensitivity = trkSens.Value / 10.0f;
                lblSens.Text = $"Sensitivity: {cal.MouseSensitivity:F1}x";
            };
            controlsRow.Controls.Add(trkSens);

            // 5. Navigation Buttons
            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            mainLayout.Controls.Add(btnRow, 0, 4);

            btnNext = CreateActionButton("Next Step ➡️", Color.FromArgb(0, 210, 106), Color.Black, () => AdvanceStep());
            btnRow.Controls.Add(btnNext);

            btnBack = CreateActionButton("⬅️ Back", Color.FromArgb(50, 50, 70), Color.White, () => GoBackStep());
            btnRow.Controls.Add(btnBack);

            btnRestart = CreateActionButton("🔄 Restart Wizard", Color.FromArgb(50, 50, 70), Color.White, () =>
            {
                currentStep = WizardStep.Welcome;
                chkEnableMouse.Checked = false;
                UpdateStepUI();
            });
            btnRow.Controls.Add(btnRestart);

            btnSaveConfig = CreateActionButton("💾 Apply & Save to Companion", Color.FromArgb(0, 180, 216), Color.Black, () => SaveConfiguration());
            btnSaveConfig.Visible = false;
            btnRow.Controls.Add(btnSaveConfig);
        }

        private Button CreateActionButton(string text, Color bg, Color fg, Action onClick)
        {
            var btn = new Button
            {
                Text = text,
                AutoSize = true,
                Height = 42,
                Padding = new Padding(16, 4, 16, 4),
                BackColor = bg,
                ForeColor = fg,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Margin = new Padding(8, 0, 0, 0)
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += (s, e) => onClick();
            return btn;
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
                            if (path.Contains("col02") || jsRight == null)
                            {
                                js.Properties.BufferSize = 128;
                                try { js.Acquire(); } catch { }
                                jsRight = js;
                            }
                        }
                    }
                    catch { }
                }

                // Connect to raw HID report for IMU Gyro
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
        }

        private void LoopTimer_Tick(object? sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            float dt = (float)(now - lastTick).TotalSeconds;
            lastTick = now;
            if (dt <= 0.0001f || dt > 0.1f) dt = 0.016f;

            // 1. Read DirectInput state
            if (jsRight != null && !jsRight.IsDisposed)
            {
                try
                {
                    var st = jsRight.GetCurrentState();
                    rawX = st.X;
                    rawY = st.Y;
                    rawZ = st.Z;
                    rawRz = st.RotationZ;

                    for (int i = 0; i < Math.Min(st.Buttons.Length, rawButtons.Length); i++)
                        rawButtons[i] = st.Buttons[i];
                }
                catch
                {
                    try { jsRight.Acquire(); } catch { }
                }
            }

            // 2. Read Raw HID Gyro
            byte[] snapshot = new byte[64];
            lock (rawLock)
            {
                Buffer.BlockCopy(rawHidData, 0, snapshot, 0, 64);
            }

            bool isMisaligned = snapshot[1] == 0 && snapshot.Skip(2).Any(b => b != 0);
            int rGyroIdx = isMisaligned ? 52 : 54;

            short gx = (short)(snapshot[rGyroIdx + 2] << 8 | snapshot[rGyroIdx + 3]);
            short gz = (short)(snapshot[rGyroIdx] << 8 | snapshot[rGyroIdx + 1]);
            short gy = (short)(snapshot[rGyroIdx + 4] << 8 | snapshot[rGyroIdx + 5]);

            rawGx = gx * -(2000.0f / short.MaxValue);
            rawGz = gz * (2000.0f / short.MaxValue);
            rawGy = gy * -(2000.0f / short.MaxValue);

            // 3. Compute Deflection for Current Step
            int currentDeflectionPercent = 0;
            switch (currentStep)
            {
                case WizardStep.StickUp:
                case WizardStep.StickDown:
                case WizardStep.StickLeft:
                case WizardStep.StickRight:
                    int dX = Math.Abs(rawX - cal.NeutralX);
                    int dY = Math.Abs(rawY - cal.NeutralY);
                    int maxDelta = Math.Max(dX, dY);
                    currentDeflectionPercent = Math.Clamp((int)(maxDelta / 30000.0 * 100), 0, 100);
                    break;

                case WizardStep.AimButton:
                    for (int i = 0; i < rawButtons.Length; i++)
                    {
                        if (rawButtons[i])
                        {
                            currentDeflectionPercent = 100;
                            break;
                        }
                    }
                    break;

                case WizardStep.GyroPitchUp:
                    currentDeflectionPercent = Math.Clamp((int)(Math.Abs(rawGx) / 50.0f * 100), 0, 100);
                    break;

                case WizardStep.GyroYawRight:
                    currentDeflectionPercent = Math.Clamp((int)(Math.Abs(rawGy) / 50.0f * 100), 0, 100);
                    break;
            }
            prgDeflection.Value = currentDeflectionPercent;

            // 4. Update Telemetry Display
            var sb = new StringBuilder();
            sb.AppendLine("=== Hardware Telemetry ===");
            sb.AppendLine($"COL02 Raw X: {rawX} | Raw Y: {rawY}");
            sb.AppendLine($"COL02 Raw Z: {rawZ} | Raw Rz: {rawRz}");
            sb.AppendLine();
            sb.AppendLine($"Right IMU Gyro (deg/s):");
            sb.AppendLine($"  Pitch (Gx): {rawGx,6:F1} | Yaw (Gy): {rawGy,6:F1} | Roll (Gz): {rawGz,6:F1}");
            sb.AppendLine();

            var pressedList = new List<string>();
            for (int i = 0; i < rawButtons.Length; i++)
            {
                if (rawButtons[i])
                {
                    string name = i switch
                    {
                        0 => "A",
                        1 => "B",
                        2 => "X",
                        3 => "X-alt",
                        4 => "Y",
                        6 => "M2",
                        7 => "RB",
                        11 => "LegionR",
                        8 or 9 or 10 or 14 => "StickClick",
                        _ => $"Btn{i}"
                    };
                    pressedList.Add(name);
                }
            }
            sb.AppendLine($"Buttons Active: {(pressedList.Count > 0 ? string.Join(", ", pressedList) : "None")}");

            // 5. Test Mode: Live Mouse Translation
            if (currentStep == WizardStep.CompleteAndTest && chkEnableMouse.Checked)
            {
                // Calculate stick output using calibrated mapping
                int stickRawHoriz = (cal.AxisXName == "X") ? rawX : rawY;
                int stickRawVert = (cal.AxisYName == "X") ? rawX : rawY;

                float normHoriz = (stickRawHoriz - cal.NeutralX) / 32768.0f;
                float normVert = (stickRawVert - cal.NeutralY) / 32768.0f;

                if (cal.InvertX) normHoriz = -normHoriz;
                if (cal.InvertY) normVert = -normVert;

                // Deadzone
                const float DEADZONE = 0.12f;
                if (Math.Abs(normHoriz) < DEADZONE) normHoriz = 0;
                if (Math.Abs(normVert) < DEADZONE) normVert = 0;

                // Speed scale
                const float BASE_STICK_SPEED = 1800.0f; // px/sec
                float moveX = normHoriz * BASE_STICK_SPEED * cal.MouseSensitivity * dt;
                float moveY = -normVert * BASE_STICK_SPEED * cal.MouseSensitivity * dt; // screen Y down is positive

                // Check Gyro Aim
                bool aimDown = (cal.AimButtonIndex >= 0 && cal.AimButtonIndex < rawButtons.Length && rawButtons[cal.AimButtonIndex]);
                if (aimDown)
                {
                    float gyroPitch = (cal.GyroPitchAxis == 0 ? rawGx : (cal.GyroPitchAxis == 1 ? rawGy : rawGz)) * cal.GyroPitchSign;
                    float gyroYaw = (cal.GyroYawAxis == 0 ? rawGx : (cal.GyroYawAxis == 1 ? rawGy : rawGz)) * cal.GyroYawSign;

                    const float GYRO_SPEED = 24.0f;
                    moveX += gyroYaw * GYRO_SPEED * cal.MouseSensitivity * dt;
                    moveY += gyroPitch * GYRO_SPEED * cal.MouseSensitivity * dt;
                }

                subX += moveX;
                subY += moveY;

                int dx = (int)subX;
                int dy = (int)subY;
                subX -= dx;
                subY -= dy;

                if (dx != 0 || dy != 0)
                {
                    mouse_event(MOUSEEVENTF_MOVE, dx, dy, 0, UIntPtr.Zero);
                }

                sb.AppendLine();
                sb.AppendLine("=== Mouse Translation Output ===");
                sb.AppendLine($"Stick -> Mouse: dx={moveX:F1}, dy={moveY:F1}");
                sb.AppendLine($"Aim Active: {(aimDown ? "🎯 YES (Aiming with Gyro)" : "⚪ NO (Holding Stick Only)")}");
            }

            lblLiveTelemetry.Text = sb.ToString();
            pnlVisual.Invalidate();
        }

        private void PnlVisual_Paint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = pnlVisual.Width;
            int h = pnlVisual.Height;
            int cx = w / 2;
            int cy = h / 2;

            using var penGrid = new Pen(Color.FromArgb(40, 40, 60), 1);
            g.DrawLine(penGrid, cx, 0, cx, h);
            g.DrawLine(penGrid, 0, cy, w, cy);
            g.DrawEllipse(penGrid, cx - 60, cy - 60, 120, 120);

            // In Step mode, show instruction icon / prompt
            switch (currentStep)
            {
                case WizardStep.StickUp:
                    DrawArrow(g, cx, cy, 0, -50, "UP");
                    break;
                case WizardStep.StickDown:
                    DrawArrow(g, cx, cy, 0, 50, "DOWN");
                    break;
                case WizardStep.StickLeft:
                    DrawArrow(g, cx, cy, -50, 0, "LEFT");
                    break;
                case WizardStep.StickRight:
                    DrawArrow(g, cx, cy, 50, 0, "RIGHT");
                    break;
                case WizardStep.AimButton:
                    using (var font = new Font("Segoe UI", 16F, FontStyle.Bold))
                    using (var brush = new SolidBrush(Color.FromArgb(255, 159, 28)))
                    {
                        var text = "PRESS AIM BUTTON (RB)";
                        var sz = g.MeasureString(text, font);
                        g.DrawString(text, font, brush, cx - sz.Width / 2, cy - sz.Height / 2);
                    }
                    break;
                default:
                    // Render real stick dot
                    float nx = (rawX - 32768.0f) / 32768.0f;
                    float ny = (rawY - 32768.0f) / 32768.0f;

                    int px = cx + (int)(nx * (cx - 15));
                    int py = cy + (int)(ny * (cy - 15));

                    using (var brushDot = new SolidBrush(Color.FromArgb(0, 210, 106)))
                    using (var penDot = new Pen(Color.White, 2))
                    {
                        g.FillEllipse(brushDot, px - 8, py - 8, 16, 16);
                        g.DrawEllipse(penDot, px - 8, py - 8, 16, 16);
                    }
                    break;
            }
        }

        private void DrawArrow(Graphics g, int cx, int cy, int dx, int dy, string label)
        {
            using var pen = new Pen(Color.FromArgb(0, 210, 106), 5);
            pen.CustomEndCap = new AdjustableArrowCap(6, 6);
            g.DrawLine(pen, cx, cy, cx + dx, cy + dy);

            using var font = new Font("Segoe UI", 14F, FontStyle.Bold);
            using var brush = new SolidBrush(Color.White);
            g.DrawString(label, font, brush, cx + dx + 10, cy + dy - 10);
        }

        private void AdvanceStep()
        {
            switch (currentStep)
            {
                case WizardStep.Welcome:
                    currentStep = WizardStep.NeutralStick;
                    break;

                case WizardStep.NeutralStick:
                    cal.NeutralX = rawX;
                    cal.NeutralY = rawY;
                    currentStep = WizardStep.StickUp;
                    break;

                case WizardStep.StickUp:
                    // Determine which axis deflected
                    int diffX = rawX - cal.NeutralX;
                    int diffY = rawY - cal.NeutralY;
                    if (Math.Abs(diffX) > Math.Abs(diffY))
                    {
                        cal.AxisYName = "X";
                        cal.InvertY = diffX < 0; // Pushing up decreased X
                    }
                    else
                    {
                        cal.AxisYName = "Y";
                        cal.InvertY = diffY < 0;
                    }
                    currentStep = WizardStep.StickDown;
                    break;

                case WizardStep.StickDown:
                    currentStep = WizardStep.StickLeft;
                    break;

                case WizardStep.StickLeft:
                    // Determine which axis is horizontal
                    int diffXLeft = rawX - cal.NeutralX;
                    int diffYLeft = rawY - cal.NeutralY;
                    if (Math.Abs(diffXLeft) > Math.Abs(diffYLeft))
                    {
                        cal.AxisXName = "X";
                        cal.InvertX = diffXLeft > 0;
                    }
                    else
                    {
                        cal.AxisXName = "Y";
                        cal.InvertX = diffYLeft < 0; // Pushing left decreased Y -> standard is normal
                    }
                    currentStep = WizardStep.StickRight;
                    break;

                case WizardStep.StickRight:
                    currentStep = WizardStep.AimButton;
                    break;

                case WizardStep.AimButton:
                    // Find pressed button
                    for (int i = 0; i < rawButtons.Length; i++)
                    {
                        if (rawButtons[i])
                        {
                            cal.AimButtonIndex = i;
                            cal.AimButtonName = i switch
                            {
                                6 => "M2",
                                7 => "RB",
                                11 => "LegionR",
                                _ => $"Button {i}"
                            };
                            break;
                        }
                    }
                    currentStep = WizardStep.GyroPitchUp;
                    break;

                case WizardStep.GyroPitchUp:
                    if (Math.Abs(rawGx) > Math.Abs(rawGy))
                    {
                        cal.GyroPitchAxis = 0;
                        cal.GyroPitchSign = rawGx < 0 ? -1.0f : 1.0f;
                    }
                    else
                    {
                        cal.GyroPitchAxis = 1;
                        cal.GyroPitchSign = rawGy < 0 ? -1.0f : 1.0f;
                    }
                    currentStep = WizardStep.GyroYawRight;
                    break;

                case WizardStep.GyroYawRight:
                    if (Math.Abs(rawGy) > Math.Abs(rawGx))
                    {
                        cal.GyroYawAxis = 1;
                        cal.GyroYawSign = rawGy < 0 ? -1.0f : 1.0f;
                    }
                    else
                    {
                        cal.GyroYawAxis = 0;
                        cal.GyroYawSign = rawGx < 0 ? -1.0f : 1.0f;
                    }
                    currentStep = WizardStep.CompleteAndTest;
                    chkEnableMouse.Checked = true;
                    break;

                case WizardStep.CompleteAndTest:
                    SaveConfiguration();
                    break;
            }

            UpdateStepUI();
        }

        private void GoBackStep()
        {
            if (currentStep > WizardStep.Welcome)
            {
                currentStep--;
                UpdateStepUI();
            }
        }

        private void UpdateStepUI()
        {
            btnSaveConfig.Visible = (currentStep == WizardStep.CompleteAndTest);
            btnBack.Enabled = (currentStep > WizardStep.Welcome);

            switch (currentStep)
            {
                case WizardStep.Welcome:
                    lblStepTitle.Text = "Welcome to Legion Input Wizard";
                    lblInstruction.Text = "This wizard will calibrate your physical right controller inputs (Stick Up/Down/Left/Right, Aim Button, and Gyro), then activate live mouse testing.\n\nClick 'Next Step' to begin.";
                    btnNext.Text = "Start Calibration ➡️";
                    break;

                case WizardStep.NeutralStick:
                    lblStepTitle.Text = "Step 1 of 8: Neutral Center Calibration";
                    lblInstruction.Text = "Let go of the Right Stick so it sits freely at neutral center.\nKeep the controller resting flat or comfortably in your hand, then click 'Next Step'.";
                    btnNext.Text = "Recorded Neutral ➡️";
                    break;

                case WizardStep.StickUp:
                    lblStepTitle.Text = "Step 2 of 8: Right Stick UP";
                    lblInstruction.Text = "👉 Push the Right Stick physically UP towards the top of the controller and HOLD IT.\nNotice the green deflection bar fill up, then click 'Next Step'.";
                    btnNext.Text = "Next (Holding Up) ➡️";
                    break;

                case WizardStep.StickDown:
                    lblStepTitle.Text = "Step 3 of 8: Right Stick DOWN";
                    lblInstruction.Text = "👉 Push the Right Stick physically DOWN towards the bottom of the controller and HOLD IT.\nThen click 'Next Step'.";
                    btnNext.Text = "Next (Holding Down) ➡️";
                    break;

                case WizardStep.StickLeft:
                    lblStepTitle.Text = "Step 4 of 8: Right Stick LEFT";
                    lblInstruction.Text = "👉 Push the Right Stick physically LEFT and HOLD IT.\nThen click 'Next Step'.";
                    btnNext.Text = "Next (Holding Left) ➡️";
                    break;

                case WizardStep.StickRight:
                    lblStepTitle.Text = "Step 5 of 8: Right Stick RIGHT";
                    lblInstruction.Text = "👉 Push the Right Stick physically RIGHT and HOLD IT.\nThen click 'Next Step'.";
                    btnNext.Text = "Next (Holding Right) ➡️";
                    break;

                case WizardStep.AimButton:
                    lblStepTitle.Text = "Step 6 of 8: Gyro Aim Button";
                    lblInstruction.Text = "👉 Press and HOLD the button you want to use for Gyro Aiming (e.g. RB, M1, or M2).\nNotice the detected button in telemetry, then click 'Next Step'.";
                    btnNext.Text = "Next (Button Pressed) ➡️";
                    break;

                case WizardStep.GyroPitchUp:
                    lblStepTitle.Text = "Step 7 of 8: Gyro Pitch Calibration";
                    lblInstruction.Text = "👉 Tilt the controller NOSE UP towards the ceiling.\nNotice the Gyro Pitch reading deflect, then click 'Next Step'.";
                    btnNext.Text = "Next (Tilted Up) ➡️";
                    break;

                case WizardStep.GyroYawRight:
                    lblStepTitle.Text = "Step 8 of 8: Gyro Yaw Calibration";
                    lblInstruction.Text = "👉 Turn the controller to the RIGHT (pointing rightwards like a laser).\nNotice the Gyro Yaw reading deflect, then click 'Next Step'.";
                    btnNext.Text = "Complete Calibration ➡️";
                    break;

                case WizardStep.CompleteAndTest:
                    lblStepTitle.Text = "🎉 Calibration Complete! Live Mouse Test Active";
                    lblInstruction.Text = $"Stick Horizontal: Axis {cal.AxisXName} (Invert={cal.InvertX}) | Vertical: Axis {cal.AxisYName} (Invert={cal.InvertY})\nAim Button: {cal.AimButtonName} (Index {cal.AimButtonIndex}) | Sens: {cal.MouseSensitivity:F1}x\n\n👉 Move your Right Stick to control the mouse! Hold {cal.AimButtonName} to aim with Gyro!";
                    btnNext.Text = "Save & Finish ✔️";
                    break;
            }
        }

        private void SaveConfiguration()
        {
            try
            {
                string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                string reportFile = Path.Combine(downloads, $"LegionCalibrationReport_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                var sb = new StringBuilder();
                sb.AppendLine("=== Legion Input Wizard Calibration Report ===");
                sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Neutral Center: X={cal.NeutralX}, Y={cal.NeutralY}");
                sb.AppendLine($"Horizontal Axis: {cal.AxisXName} (Invert={cal.InvertX})");
                sb.AppendLine($"Vertical Axis: {cal.AxisYName} (Invert={cal.InvertY})");
                sb.AppendLine($"Aim Button: {cal.AimButtonName} (Index={cal.AimButtonIndex})");
                sb.AppendLine($"Gyro Pitch: Axis={cal.GyroPitchAxis}, Sign={cal.GyroPitchSign}");
                sb.AppendLine($"Gyro Yaw: Axis={cal.GyroYawAxis}, Sign={cal.GyroYawSign}");
                sb.AppendLine($"Mouse Sensitivity: {cal.MouseSensitivity:F1}");
                sb.AppendLine();
                sb.AppendLine("--- C# Mapping Code for Handheld Companion ---");
                sb.AppendLine($"Inputs.AxisState[AxisFlags.RightStickX] = (short)InputUtils.MapRange(stateR.{cal.AxisXName}, ushort.MinValue, ushort.MaxValue, {(cal.InvertX ? "short.MaxValue, short.MinValue" : "short.MinValue, short.MaxValue")});");
                sb.AppendLine($"Inputs.AxisState[AxisFlags.RightStickY] = (short)InputUtils.MapRange(stateR.{cal.AxisYName}, ushort.MinValue, ushort.MaxValue, {(cal.InvertY ? "short.MaxValue, short.MinValue" : "short.MinValue, short.MaxValue")});");

                File.WriteAllText(reportFile, sb.ToString());

                // Save to HandheldCompanion LocalAppData config
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HandheldCompanion");
                Directory.CreateDirectory(appData);
                string jsonFile = Path.Combine(appData, "input_calibration.json");
                File.WriteAllText(jsonFile, JsonConvert.SerializeObject(cal, Formatting.Indented));

                MessageBox.Show($"Calibration saved successfully!\n\nReport written to:\n{reportFile}\n\nConfiguration saved to:\n{jsonFile}", "Calibration Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save calibration: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            loopTimer.Stop();
            try { jsRight?.Unacquire(); jsRight?.Dispose(); } catch { }
            try { directInput?.Dispose(); } catch { }
            try { rawController?.Close(); } catch { }
            base.OnFormClosing(e);
        }
    }
}
