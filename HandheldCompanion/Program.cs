using System;
using System.Windows.Forms;
using HandheldCompanion.Managers;
using HandheldCompanion.Views;
using HandheldCompanion.Shared;

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
            
            Application.Run(new MainForm());
        }
    }
}
