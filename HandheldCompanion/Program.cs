using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using HandheldCompanion.Devices;
using HandheldCompanion.Managers;
using HandheldCompanion.Views;
using HandheldCompanion.Shared;
using System.Diagnostics;
using System.Globalization;

namespace HandheldCompanion
{
    static class Program
    {
        public const string ApplicationName = "HandheldCompanion";

        private const int SW_HIDE = 0;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ChangeWindowMessageFilter(uint message, uint dwFlag);

        private const uint MSGFLT_ADD = 1;
        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);
        public static uint WM_SHOWME = 0;
        public const string ShowWindowEventName = @"Global\LegionGoCompanion_ShowWindow_Event";

        private static bool CheckExistingInstanceAndSignal()
        {
            try
            {
                int currentPid = Process.GetCurrentProcess().Id;
                string currentName = Process.GetCurrentProcess().ProcessName;
                var processes = Process.GetProcessesByName(currentName);
                if (processes.Length > 1)
                {
                    // 1. Signal cross-integrity Global EventWaitHandle (works reliably across user/admin boundary)
                    try
                    {
                        if (EventWaitHandle.TryOpenExisting(ShowWindowEventName, out var handle))
                        {
                            handle.Set();
                            handle.Dispose();
                        }
                    }
                    catch { }

                    // 2. Broadcast window message as fallback
                    try
                    {
                        uint wm = (uint)RegisterWindowMessage("HANDHELD_COMPANION_SHOW_WINDOW");
                        PostMessage(HWND_BROADCAST, wm, IntPtr.Zero, IntPtr.Zero);
                    }
                    catch { }

                    return true;
                }
            }
            catch { }
            return false;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        private static void StartWindowSuppressor()
        {
            var suppressorThread = new Thread(() =>
            {
                var titleSb = new System.Text.StringBuilder(256);
                var classSb = new System.Text.StringBuilder(256);

                while (true)
                {
                    try
                    {
                        EnumWindows((hWnd, lParam) =>
                        {
                            classSb.Clear();
                            GetClassName(hWnd, classSb, classSb.Capacity);
                            string cls = classSb.ToString();
                            if (cls == "ConsoleWindowClass")
                            {
                                titleSb.Clear();
                                GetWindowText(hWnd, titleSb, titleSb.Capacity);
                                string title = titleSb.ToString();
                                if (title.Contains("usbip", StringComparison.OrdinalIgnoreCase) ||
                                    title.Contains("viiper", StringComparison.OrdinalIgnoreCase))
                                {
                                    ShowWindow(hWnd, SW_HIDE);
                                }
                            }
                            return true;
                        }, IntPtr.Zero);
                    }
                    catch { }

                    Thread.Sleep(20);
                }
            })
            {
                IsBackground = true,
                Priority = ThreadPriority.Lowest
            };
            suppressorThread.Start();
        }

        private static void KillPreviousInstances()
        {
            try
            {
                int currentPid = Process.GetCurrentProcess().Id;
                string currentName = Process.GetCurrentProcess().ProcessName;
                var targetNames = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    currentName,
                    "HandheldCompanion",
                    "LegionGoCompanion"
                };

                foreach (var name in targetNames)
                {
                    Process[] processes = Process.GetProcessesByName(name);
                    foreach (var p in processes)
                    {
                        if (p.Id != currentPid)
                        {
                            try
                            {
                                p.Kill();
                                p.WaitForExit(3000);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
        }

        private static void CleanOldFiles()
        {
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                var oldFiles = Directory.GetFiles(dir, "*.old", SearchOption.AllDirectories);
                foreach (var file in oldFiles)
                {
                    try { File.Delete(file); } catch { }
                }
            }
            catch { }
        }

        public static bool IsAdministrator()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryElevateViaScheduledTaskOrUac()
        {
            if (IsAdministrator())
                return false; // Already elevated

            // Elevate interactively via UAC so the GUI window appears on the user's active desktop
            try
            {
                string exe = Process.GetCurrentProcess().MainModule?.FileName ?? "HandheldCompanion.exe";
                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
                return true;
            }
            catch { }

            return false;
        }

        [STAThread]
        static void Main(string[] args)
        {
            // If another instance is already running, signal it to restore its window and exit immediately!
            if (CheckExistingInstanceAndSignal())
                return;

            if (!args.Contains("--no-elevate") && TryElevateViaScheduledTaskOrUac())
                return;

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Register message so lower-integrity processes can signal us to show window
            WM_SHOWME = (uint)RegisterWindowMessage("HANDHELD_COMPANION_SHOW_WINDOW");
            try { ChangeWindowMessageFilter(WM_SHOWME, MSGFLT_ADD); } catch { }

            // Terminate any previous dead instances
            KillPreviousInstances();

            // Clean up any temporary .old files from updates/deploys
            CleanOldFiles();

            // Suppress any console window flashbangs immediately
            StartWindowSuppressor();

            // Setup global exception handling so nothing fails silently
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                MessageBox.Show("Fatal startup error: " + (ex?.Message ?? e.ExceptionObject.ToString()) + "\n\n" + ex?.StackTrace, "LegionGoCompanion Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            Application.ThreadException += (s, e) =>
            {
                MessageBox.Show("UI Thread error: " + e.Exception.Message + "\n\n" + e.Exception.StackTrace, "LegionGoCompanion Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            try
            {
                string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ApplicationName);
                string logsPath = Path.Combine(settingsPath, "logs");
                Directory.CreateDirectory(logsPath);
                Environment.SetEnvironmentVariable("LOG_PATH", logsPath);

                LogManager.Initialize(ApplicationName);
                LogManager.LogInformation("Starting LegionGoCompanion (WinForms)...");

                // Setup Environment
                InputsManager.Start();
                TimerManager.Start();
                MotionManager.Start();
                ManagerFactory.settingsManager.Start();

                // Initialize hardware
                LogManager.LogInformation("Initializing IDevice...");
                IDevice.GetCurrent().Initialize(false, false);

                // Start factory managers
                LogManager.LogInformation("Loading ManagerFactory managers...");
                foreach (IManager manager in ManagerFactory.Managers)
                {
                    Task.Run(() => manager.Start());
                }

                // Start static managers
                LogManager.LogInformation("Loading static managers...");
                Task.Run(() => OSDManager.Start());
                Task.Run(() => SystemManager.Start());
                Task.Run(() => DynamicLightingManager.Start());
                Task.Run(() => VirtualManager.Start());
                Task.Run(() => SensorsManager.Start());
                Task.Run(() => ControllerManager.Start());
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "LegionGoCompanion.exe";
                Task.Run(() => TaskManager.Start(exePath));
                Task.Run(() => HidHide.RegisterApplication(exePath));
                Task.Run(() => PerformanceManager.Start());
                Task.Run(() => UpdateManager.Start());

                LogManager.LogInformation("Starting UI...");
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show("Startup Exception: " + ex.Message + "\n\n" + ex.StackTrace, "Startup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
