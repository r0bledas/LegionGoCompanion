using System;
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
        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

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
    }
}

