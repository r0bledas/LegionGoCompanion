using HandheldCompanion.Shared;
using HandheldCompanion.Utils;
using Nefarius.Drivers.HidHide;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace HandheldCompanion;

public static class HidHide
{
    private static object hidLock = new();

    private static string? GetCliPath()
    {
        var installPath = RegistryUtils.GetString(@"SOFTWARE\Nefarius Software Solutions e.U.\HidHide", "Path");
        if (string.IsNullOrEmpty(installPath)) return null;
        string cliPath = Path.Combine(installPath, "x64", "HidHideCLI.exe");
        return File.Exists(cliPath) ? cliPath : null;
    }

    private static bool RunCli(string arguments, out string output)
    {
        output = string.Empty;
        try
        {
            string? cliPath = GetCliPath();
            if (string.IsNullOrEmpty(cliPath)) return false;

            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = cliPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            if (!p.Start()) return false;
            try { p.StandardInput.Close(); } catch { }

            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(3000))
            {
                try { p.Kill(); } catch { }
                LogManager.LogWarning("HidHideCLI timed out for args: {0}", arguments);
                return false;
            }

            output = outTask.IsCompletedSuccessfully ? outTask.Result : string.Empty;
            string err = errTask.IsCompletedSuccessfully ? errTask.Result : string.Empty;
            if (p.ExitCode != 0)
            {
                LogManager.LogWarning("HidHideCLI failed (exit={0}) for args '{1}': {2}", p.ExitCode, arguments, err.Trim());
                return false;
            }
            return true;
        }
        catch { return false; }
    }

    private static readonly HidHideControlService _service = new();

    static HidHide()
    {
        // verifying HidHide is installed
        if (!_service.IsInstalled)
        {
            LogManager.LogCritical("HidHide is missing. Please get it from: {0}", "https://github.com/ViGEm/HidHide/releases");
            MessageBox.Show("Unable to start Handheld Companion, the HidHide application is missing.\n\nPlease get it from: https://github.com/ViGEm/HidHide/releases", "Error");
            throw new InvalidOperationException();
        }
    }

    public static List<string> GetRegisteredApplications()
    {
        lock (hidLock)
        {
            try
            {
                return _service.ApplicationPaths.ToList();
            }
            catch { }

            return [];
        }
    }

    public static List<string> GetRegisteredDevices()
    {
        lock (hidLock)
        {
            try
            {
                return _service.BlockedInstanceIds.Select(x => x.ToUpper()).ToList();
            }
            catch { }

            return [];
        }
    }

    public static bool IsRegistered(string InstanceId)
    {
        try
        {
            List<string> registered = GetRegisteredDevices();
            return registered.Contains(InstanceId.ToUpper());
        }
        catch { }

        return false;
    }

    public static bool UnregisterApplication(string fileName)
    {
        lock (hidLock)
        {
            try
            {
                if (_service.ApplicationPaths.Contains(fileName))
                {
                    _service.RemoveApplicationPath(fileName);
                    LogManager.LogInformation("HideDevice RemoveApplicationPath: {0}", fileName);
                }
                return true;
            }
            catch
            {
                if (RunCli($"--app-unreg \"{fileName}\"", out _))
                {
                    LogManager.LogInformation("HideDevice RemoveApplicationPath (CLI): {0}", fileName);
                    return true;
                }
                return false;
            }
        }
    }

    public static bool RegisterApplication(string fileName)
    {
        lock (hidLock)
        {
            try
            {
                if (_service.IsAppListInverted)
                {
                    _service.IsAppListInverted = false;
                    LogManager.LogInformation("HideDevice IsAppListInverted set to false (whitelist mode)");
                }
                if (!_service.ApplicationPaths.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                {
                    _service.AddApplicationPath(fileName);
                    LogManager.LogInformation("HideDevice AddApplicationPath: {0}", fileName);
                }
                return true;
            }
            catch
            {
                RunCli("--inv-off", out _);
                if (RunCli($"--app-reg \"{fileName}\"", out _))
                {
                    LogManager.LogInformation("HideDevice AddApplicationPath (CLI): {0}", fileName);
                    return true;
                }
                return false;
            }
        }
    }

    public static bool SetCloaking(bool status)
    {
        lock (hidLock)
        {
            try
            {
                if (status && _service.IsAppListInverted)
                {
                    _service.IsAppListInverted = false;
                    LogManager.LogInformation("HideDevice IsAppListInverted set to false (whitelist mode)");
                }
                if (_service.IsActive != status)
                {
                    _service.IsActive = status;
                    LogManager.LogInformation("HideDevice IsActive: {0}", status);
                }
                return true;
            }
            catch
            {
                if (status)
                    RunCli("--inv-off", out _);
                string arg = status ? "--cloak-on" : "--cloak-off";
                if (RunCli(arg, out _))
                {
                    LogManager.LogInformation("HideDevice SetCloaking (CLI): {0}", status);
                    return true;
                }
                return false;
            }
        }
    }

    public static bool UnhidePath(string deviceInstancePath)
    {
        if (string.IsNullOrEmpty(deviceInstancePath))
            return false;

        lock (hidLock)
        {
            try
            {
                if (_service.BlockedInstanceIds.Any(x => x.Equals(deviceInstancePath, StringComparison.OrdinalIgnoreCase)))
                {
                    foreach (var match in _service.BlockedInstanceIds.Where(x => x.Equals(deviceInstancePath, StringComparison.OrdinalIgnoreCase)).ToList())
                    {
                        _service.RemoveBlockedInstanceId(match);
                        LogManager.LogInformation("HideDevice RemoveBlockedInstanceId: {0}", match);
                    }
                }
                return true;
            }
            catch
            {
                if (RunCli($"--dev-unhide \"{deviceInstancePath}\"", out _))
                {
                    LogManager.LogInformation("HideDevice RemoveBlockedInstanceId (CLI): {0}", deviceInstancePath);
                }
                return true;
            }
        }
    }

    public static bool HidePath(string deviceInstancePath)
    {
        if (string.IsNullOrEmpty(deviceInstancePath))
            return false;

        lock (hidLock)
        {
            try
            {
                if (!_service.BlockedInstanceIds.Any(x => x.Equals(deviceInstancePath, StringComparison.OrdinalIgnoreCase)))
                {
                    _service.AddBlockedInstanceId(deviceInstancePath);
                    LogManager.LogInformation("HideDevice AddBlockedInstanceId: {0}", deviceInstancePath);
                }
                return true;
            }
            catch
            {
                if (RunCli($"--dev-hide \"{deviceInstancePath}\"", out _))
                {
                    LogManager.LogInformation("HideDevice AddBlockedInstanceId (CLI): {0}", deviceInstancePath);
                }
                return true;
            }
        }
    }

    public static List<HidHideDevice> GetHidHideDevices(string arg = "--dev-all")
    {
        try
        {
            // using --dev-gaming sometimes doesn't report controllers or have empty BaseContainerDeviceInstancePath
            bool success = RunCli(arg, out string jsonString);
            if (success)
            {
                jsonString = jsonString.Trim();
                if (string.IsNullOrEmpty(jsonString))
                    return [];

                return JsonConvert.DeserializeObject<List<HidHideDevice>>(jsonString) ?? [];
            }
        }
        catch { }

        return [];
    }

    public static HidHideDevice? GetHidHideDevice(string deviceInstancePath)
    {
        List<HidHideDevice> hidHideDevices = GetHidHideDevices();

        if (hidHideDevices.Count != 0)
            return hidHideDevices.FirstOrDefault(device => device.Devices.Any(a => a.BaseContainerDeviceInstancePath == deviceInstancePath || a.DeviceInstancePath == deviceInstancePath));

        return null;
    }
}

public partial class HidHideDevice
{
    [JsonProperty("friendlyName")]
    public string FriendlyName { get; set; } = string.Empty;

    [JsonProperty("devices")]
    public HidHideSubDevice[] Devices { get; set; } = [];
}

public partial class HidHideSubDevice
{
    [JsonProperty("present")]
    public bool Present { get; set; }

    [JsonProperty("gamingDevice")]
    public bool GamingDevice { get; set; }

    [JsonProperty("symbolicLink")]
    public string SymbolicLink { get; set; } = string.Empty;

    [JsonProperty("vendor")]
    public string Vendor { get; set; } = string.Empty;

    [JsonProperty("product")]
    public string Product { get; set; } = string.Empty;

    [JsonProperty("serialNumber")]
    public string SerialNumber { get; set; } = string.Empty;

    [JsonProperty("usage")]
    public string Usage { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonProperty("deviceInstancePath")]
    public string DeviceInstancePath { get; set; } = string.Empty;

    [JsonProperty("baseContainerDeviceInstancePath")]
    public string BaseContainerDeviceInstancePath { get; set; } = string.Empty;

    [JsonProperty("baseContainerClassGuid")]
    public string BaseContainerClassGuid { get; set; } = string.Empty;

    [JsonProperty("baseContainerDeviceCount")]
    public long BaseContainerDeviceCount { get; set; }
}