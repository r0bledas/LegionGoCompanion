using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace HandheldCompanion
{
    // https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/pnputil-return-values
    public class PnPUtilResult
    {
        public int ExitCode;
        public string StandardOutput;

        public PnPUtilResult(int exitCode, string output)
        {
            ExitCode = exitCode;
            StandardOutput = output;
        }
    }

    public static class PnPUtil
    {
        private const int ERROR_SUCCESS = 0;
        private const int ERROR_NO_MORE_ITEMS = 259;
        private const int ERROR_SUCCESS_REBOOT_REQUIRED = 3010;
        private const int ERROR_SUCCESS_REBOOT_INITIATED = 1641;

        public static bool RestartDevice(string InstanceId)
        {
            var pnpResult = GetPnPUtilResult($"/restart-device \"{InstanceId}\"");
            return ValidateChangeDeviceStatusResult(InstanceId, pnpResult);
        }

        public static bool EnableDevice(string InstanceId)
        {
            var pnpResult = GetPnPUtilResult($"/enable-device \"{InstanceId}\"");
            return ValidateChangeDeviceStatusResult(InstanceId, pnpResult);
        }

        public static bool DisableDevice(string InstanceId)
        {
            var pnpResult = GetPnPUtilResult($"/disable-device \"{InstanceId}\"");
            return ValidateChangeDeviceStatusResult(InstanceId, pnpResult);
        }

        public static bool EnableDevices(string Class)
        {
            var pnpResult = GetPnPUtilResult($"/enable-device /class \"{Class}\"");
            return pnpResult.ExitCode == ERROR_SUCCESS;
        }

        public static List<string> GetDevices(string className, string status = "/connected")
        {
            // A list of string to store the Instance ID values
            List<string> instanceIDs = [];

            // A regular expression to match the Instance ID pattern
            Regex regex = new Regex(@"Instance ID:\s+(.*)");

            // Loop through each line of the input string
            string input = GetPnPUtilOutput($"/enum-devices {status} /class {className}");
            foreach (string line in input.Split('\r'))
            {
                // Try to match the line with the regular expression
                Match match = regex.Match(line);

                // If there is a match, add the Instance ID value to the list
                if (match.Success)
                {
                    instanceIDs.Add(match.Groups[1].Value);
                }
            }

            return instanceIDs;
        }

        public static Process StartPnPUtil(string arguments)
        {
            Process process = new();

            process.StartInfo.FileName = "pnputil.exe";
            process.StartInfo.Arguments = arguments;

            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;

            process.Start();

            return process;
        }

        private static string GetPnPUtilOutput(string arguments)
        {
            Process process = StartPnPUtil(arguments);
            var output = process.StandardOutput.ReadToEnd();

            return output;
        }

        private static PnPUtilResult GetPnPUtilResult(string arguments)
        {
            using Process process = StartPnPUtil(arguments);
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            var exitCode = process.ExitCode;

            return new PnPUtilResult(exitCode, output);
        }

        // this function validates the results for /enable-device, /disable-device and /restart-device.
        private static bool ValidateChangeDeviceStatusResult(string instanceId, PnPUtilResult pnpResult)
        {
            if (pnpResult.ExitCode != ERROR_SUCCESS)
                return false;

            string text = pnpResult.StandardOutput ?? string.Empty;
            if (text.Contains("Failed", System.StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Access is denied", System.StringComparison.OrdinalIgnoreCase) ||
                text.Contains("reboot", System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return text.Contains(instanceId, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
