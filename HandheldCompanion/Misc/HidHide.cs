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
            output = p.StandardOutput.ReadToEnd();
            return p.WaitForExit(3000);
        }
        catch { return false; }
    }

    static HidHide()
    {
        HidHideControlService service = new HidHideControlService();

        // verifying HidHide is installed
        if (!service.IsInstalled)
        {
            LogManager.LogCritical("HidHide is missing. Please get it from: {0}", "https://github.com/ViGEm/HidHide/releases");
            MessageBox.Show("Unable to start Handheld Companion, the HidHide application is missing.\n\nPlease get it from: https://github.com/ViGEm/HidHide/releases", "Error");
            throw new InvalidOperationException();
        }
    }

    public static List<string> GetRegisteredApplications()
    {
        try
        {
            lock (hidLock)
            {
                HidHideControlService service = new HidHideControlService();
                return service.ApplicationPaths.ToList();
            }
        }
        catch { }

        return [];
    }

    public static List<string> GetRegisteredDevices()
    {
        try
        {
            lock (hidLock)
            {
                HidHideControlService service = new HidHideControlService();
                return service.BlockedInstanceIds.Select(x => x.ToUpper()).ToList();
            }
        }
        catch { }

        return [];
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
        try
        {
            lock (hidLock)
            {
                HidHideControlService service = new HidHideControlService();
                if (service.ApplicationPaths.Contains(fileName))
                {
                    service.RemoveApplicationPath(fileName);
                    LogManager.LogInformation("HideDevice RemoveApplicationPath: {0}", fileName);
                }
            }
        }
        catch
        {
            LogManager.LogError("Failed to UnregisterApplication({0}), HidHideControlService is unreachable", fileName);

            if (RunCli($"--app-unreg \"{fileName}\"", out _))
            {
                LogManager.LogInformation("HideDevice RemoveApplicationPath: {0}", fileName);
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    public static bool RegisterApplication(string fileName)
    {
        try
        {
            lock (hidLock)
            {
                HidHideControlService service = new HidHideControlService();
                if (!service.ApplicationPaths.Contains(fileName))
                {
                    service.AddApplicationPath(fileName);
                    LogManager.LogInformation("HideDevice AddApplicationPath: {0}", fileName);
                }
            }
        }
        catch
        {
            LogManager.LogError("Failed to RegisterApplication({0}), HidHideControlService is unreachable", fileName);

            if (RunCli($"--app-reg \"{fileName}\"", out _))
            {
                LogManager.LogInformation("HideDevice AddApplicationPath: {0}", fileName);
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    public static bool SetCloaking(bool status)
    {
        try
        {
            lock (hidLock)
            {
                HidHideControlService service = new HidHideControlService { IsActive = status };
                LogManager.LogInformation("HideDevice IsActive: {0}", status);
            }
        }
        catch
        {
            LogManager.LogError("Failed to SetCloaking({0}), HidHideControlService is unreachable", status);

            string arg = status ? "--cloak-on" : "--cloak-off";
            if (RunCli(arg, out _))
            {
                LogManager.LogInformation("HideDevice SetCloaking: {0}", status);
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    public static bool UnhidePath(string deviceInstancePath)
    {
        if (string.IsNullOrEmpty(deviceInstancePath))
            return false;

        try
        {
            lock (hidLock)
            {
                HidHideControlService service = new HidHideControlService();
                if (service.BlockedInstanceIds.Contains(deviceInstancePath))
                {
                    service.RemoveBlockedInstanceId(deviceInstancePath);
                    LogManager.LogInformation("HideDevice RemoveBlockedInstanceId: {0}", deviceInstancePath);
                }
            }
        }
        catch
        {
            LogManager.LogError("Failed to UnhidePath({0}), HidHideControlService is unreachable", deviceInstancePath);

            bool success = RunCli($"--dev-unhide \"{deviceInstancePath}\"", out _);
            if (success)
            {
                LogManager.LogInformation("HideDevice RemoveBlockedInstanceId (CLI): {0}", deviceInstancePath);
            }
        }

        return true;
    }

    public static bool HidePath(string deviceInstancePath)
    {
        if (string.IsNullOrEmpty(deviceInstancePath))
            return false;

        try
        {
            lock (hidLock)
            {
                HidHideControlService service = new HidHideControlService();
                if (!service.BlockedInstanceIds.Contains(deviceInstancePath))
                {
                    service.AddBlockedInstanceId(deviceInstancePath);
                    LogManager.LogInformation("HideDevice AddBlockedInstanceId: {0}", deviceInstancePath);
                }
            }
        }
        catch
        {
            LogManager.LogError("Failed to HidePath({0}), HidHideControlService is unreachable", deviceInstancePath);

            bool success = RunCli($"--dev-hide \"{deviceInstancePath}\"", out _);
            if (success)
            {
                LogManager.LogInformation("HideDevice AddBlockedInstanceId (CLI): {0}", deviceInstancePath);
            }
        }

        return true;
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