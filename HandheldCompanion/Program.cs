using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
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

        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

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
