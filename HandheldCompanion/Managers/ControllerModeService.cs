using System;
using System.Collections.Generic;
using System.Linq;
using System.Media;
using System.Threading.Tasks;
using HandheldCompanion.Controllers;
using HandheldCompanion.Controllers.Lenovo;
using HandheldCompanion.Devices;
using HandheldCompanion.Devices.Lenovo;
using HandheldCompanion.Shared;
using HandheldCompanion.Utils;

namespace HandheldCompanion.Managers
{
    public enum ControllerTargetMode
    {
        Desktop = 0,
        DS4 = 1,
        X360 = 2,
        Native = 3,
        Pointer = 4
    }

    public static class ControllerModeService
    {
        public static event Action<ControllerTargetMode>? ModeChanged;
        public static bool IsApplyingMode { get; private set; }
        public static bool HasCycledForCloak { get; set; } = false;
        private static ControllerTargetMode _currentMode = ControllerTargetMode.Desktop;
        private static bool _initialized = false;

        private static void EnsureInitialized()
        {
            if (_initialized)
                return;

            try
            {
                int saved = ManagerFactory.settingsManager?.GetInt("TargetMode") ?? -1;
                if (saved >= 0 && Enum.IsDefined(typeof(ControllerTargetMode), saved))
                {
                    _currentMode = (ControllerTargetMode)saved;
                }
                else
                {
                    bool desktopOnStart = ManagerFactory.settingsManager?.GetBoolean("DesktopLayoutOnStart") ?? false;
                    _currentMode = desktopOnStart ? ControllerTargetMode.Desktop : ControllerTargetMode.X360;
                }
                _initialized = true;
            }
            catch
            {
                _currentMode = ControllerTargetMode.Desktop;
                _initialized = true;
            }
        }

        public static ControllerTargetMode GetCurrentMode()
        {
            EnsureInitialized();
            return _currentMode;
        }

        public static string GetModeDisplayName(ControllerTargetMode mode) => mode switch
        {
            ControllerTargetMode.Desktop => "Desktop",
            ControllerTargetMode.DS4 => "DualShock 4 (DS4)",
            ControllerTargetMode.X360 => "Xbox 360 (x360)",
            ControllerTargetMode.Native => "Native",
            ControllerTargetMode.Pointer => "Pointer",
            _ => mode.ToString()
        };

        public static async Task<bool> ApplyMode(ControllerTargetMode mode)
        {
            if (IsApplyingMode)
                return false;

            IsApplyingMode = true;
            _initialized = true;
            _currentMode = mode;
            try
            {
                ManagerFactory.settingsManager.SetProperty("TargetMode", (int)mode);

                switch (mode)
                {
                    case ControllerTargetMode.Pointer:
                        PointerModeService.Instance.SetActive(true);
                        ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", false);
                        SetLegionPassthrough(true);
                        EnsurePhysicalControllerUnhidden();
                        ManagerFactory.settingsManager.SetProperty("LayoutMode", (int)LayoutModes.Desktop);
                        ManagerFactory.layoutManager.SetLayoutMode(LayoutModes.Desktop);
                        await VirtualManager.SetControllerMode(HIDmode.NoController).ConfigureAwait(false);
                        await VirtualManager.SetControllerStatus(HIDstatus.Disconnected).ConfigureAwait(false);
                        PlaySound();
                        break;

                    case ControllerTargetMode.Desktop:
                        PointerModeService.Instance.SetActive(false);
                        ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", true);
                        SetLegionPassthrough(false);
                        EnsurePhysicalControllerHidden();
                        ManagerFactory.settingsManager.SetProperty("LayoutMode", (int)LayoutModes.Desktop);
                        ManagerFactory.layoutManager.SetLayoutMode(LayoutModes.Desktop);
                        await VirtualManager.SetControllerMode(HIDmode.NoController).ConfigureAwait(false);
                        await VirtualManager.SetControllerStatus(HIDstatus.Disconnected).ConfigureAwait(false);
                        PlaySound();
                        break;

                    case ControllerTargetMode.DS4:
                        PointerModeService.Instance.SetActive(false);
                        UnhideVirtualControllers();
                        ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", true);
                        SetLegionPassthrough(false);
                        EnsurePhysicalControllerHidden();
                        // Persist DS4 mode in settings first so background layout checks never clobber it
                        ManagerFactory.settingsManager.SetProperty("HIDmode", (int)HIDmode.DualShock4Controller);
                        ManagerFactory.settingsManager.SetProperty("HIDstatus", (int)HIDstatus.Connected);
                        ManagerFactory.settingsManager.SetProperty("LayoutMode", (int)LayoutModes.Gamepad);
                        ManagerFactory.layoutManager.SetLayoutMode(LayoutModes.Gamepad);
                        VirtualManager.HIDstatus = HIDstatus.Connected;
                        await VirtualManager.SetControllerMode(HIDmode.DualShock4Controller).ConfigureAwait(false);
                        await VirtualManager.SetControllerStatus(HIDstatus.Connected).ConfigureAwait(false);
                        if (IDevice.GetCurrent() is LegionGoTablet tabletDS4)
                            tabletDS4.EnableGyros();
                        if (ControllerManager.TargetController is LegionControllerBase legoBaseDS4)
                            legoBaseDS4.SendGyroEnableCommands();
                        PlaySound();
                        break;

                    case ControllerTargetMode.X360:
                        PointerModeService.Instance.SetActive(false);
                        UnhideVirtualControllers();
                        ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", true);
                        SetLegionPassthrough(false);
                        EnsurePhysicalControllerHidden();
                        // Persist X360 mode in settings first
                        ManagerFactory.settingsManager.SetProperty("HIDmode", (int)HIDmode.Xbox360Controller);
                        ManagerFactory.settingsManager.SetProperty("HIDstatus", (int)HIDstatus.Connected);
                        ManagerFactory.settingsManager.SetProperty("LayoutMode", (int)LayoutModes.Gamepad);
                        ManagerFactory.layoutManager.SetLayoutMode(LayoutModes.Gamepad);
                        VirtualManager.HIDstatus = HIDstatus.Connected;
                        await VirtualManager.SetControllerMode(HIDmode.Xbox360Controller).ConfigureAwait(false);
                        await VirtualManager.SetControllerStatus(HIDstatus.Connected).ConfigureAwait(false);
                        PlaySound();
                        break;

                    case ControllerTargetMode.Native:
                        PointerModeService.Instance.SetActive(false);
                        ManagerFactory.settingsManager.SetProperty("HIDmode", (int)HIDmode.NoController);
                        ManagerFactory.settingsManager.SetProperty("HIDstatus", (int)HIDstatus.Disconnected);
                        ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", false);
                        SetLegionPassthrough(true);
                        EnsurePhysicalControllerUnhidden();
                        ManagerFactory.settingsManager.SetProperty("LayoutMode", (int)LayoutModes.Gamepad);
                        ManagerFactory.layoutManager.SetLayoutMode(LayoutModes.Gamepad);
                        await VirtualManager.SetControllerMode(HIDmode.NoController).ConfigureAwait(false);
                        await VirtualManager.SetControllerStatus(HIDstatus.Disconnected).ConfigureAwait(false);
                        PlaySound();
                        break;
                }

                ToastManager.SendToast($"Controller Mode: {GetModeDisplayName(mode)}");
                ModeChanged?.Invoke(mode);
                return true;
            }
            catch (Exception ex)
            {
                LogManager.LogError("ControllerModeService: ApplyMode failed for {0}: {1}", mode, ex.Message);
                return false;
            }
            finally
            {
                IsApplyingMode = false;
            }
        }

        public static void CycleToNextMode()
        {
            var enabled = CustomMappingService.Instance.GetEnabledCycleModes();
            if (enabled == null || enabled.Count == 0)
            {
                enabled = new List<ControllerTargetMode> { ControllerTargetMode.Desktop, ControllerTargetMode.X360 };
            }

            var current = GetCurrentMode();
            int idx = enabled.IndexOf(current);
            ControllerTargetMode next = idx >= 0
                ? enabled[(idx + 1) % enabled.Count]
                : enabled[0];

            _ = ApplyMode(next);
        }

        private static void SetLegionPassthrough(bool enabled)
        {
            if (IDevice.GetCurrent() is LegionGo lego)
            {
                lego.SetPassthrough(enabled);
            }
        }

        private static void PlaySound()
        {
            try { SystemSounds.Exclamation.Play(); } catch { }
        }

        public static void EnsurePhysicalControllerHidden()
        {
            try
            {
                HidHide.SetCloaking(true);
                string? exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                    HidHide.RegisterApplication(exePath);

                foreach (var reg in HidHide.GetRegisteredDevices())
                {
                    if (reg.Contains("MI_01", StringComparison.OrdinalIgnoreCase) ||
                        reg.Contains("MI_02", StringComparison.OrdinalIgnoreCase) ||
                        reg.Contains("MI_03", StringComparison.OrdinalIgnoreCase))
                    {
                        HidHide.UnhidePath(reg);
                    }
                }

                var target = ControllerManager.TargetController;
                bool wasAlreadyHidden = target != null && !target.IsDummy() && target.IsHidden() && HasCycledForCloak;

                if (ManagerFactory.deviceManager?.PnPDevices != null)
                {
                    foreach (var dev in ManagerFactory.deviceManager.PnPDevices.Values.Where(d => d.VendorID == 0x17EF && (d.ProductID == 0x61EB || d.ProductID == 0x6182) && d.isXInput))
                    {
                        if (!string.IsNullOrEmpty(dev.deviceInstanceId))
                        {
                            if (!HidHide.IsRegistered(dev.deviceInstanceId))
                                wasAlreadyHidden = false;
                            HidHide.HidePath(dev.deviceInstanceId);
                        }
                    }
                }

                if (target != null && !target.IsDummy() && !target.IsVirtual())
                {
                    bool shouldCycle = !wasAlreadyHidden;
                    target.Hide(shouldCycle);
                    if (shouldCycle)
                        HasCycledForCloak = true;
                }
            }
            catch (Exception ex)
            {
                LogManager.LogWarning("EnsurePhysicalControllerHidden error: {0}", ex.Message);
            }
        }

        public static void EnsurePhysicalControllerUnhidden()
        {
            try
            {
                var target = ControllerManager.TargetController;
                bool wasHidden = (target != null && !target.IsDummy() && target.IsHidden()) ||
                                 HidHide.GetRegisteredDevices().Any(d => d.Contains("17EF&PID_61EB", StringComparison.OrdinalIgnoreCase));

                UnhideLegionControllers();
                HasCycledForCloak = false;
                HidHide.SetCloaking(false);

                if (target != null && !target.IsDummy() && !target.IsVirtual())
                {
                    target.Unhide(wasHidden);
                }
            }
            catch (Exception ex)
            {
                LogManager.LogWarning("EnsurePhysicalControllerUnhidden error: {0}", ex.Message);
            }
        }

        private static void UnhideVirtualControllers()
        {
            try
            {
                foreach (string devPath in HidHide.GetRegisteredDevices())
                {
                    if (devPath.Contains("054C", StringComparison.OrdinalIgnoreCase) ||
                        devPath.Contains("045E", StringComparison.OrdinalIgnoreCase) ||
                        devPath.Contains("17EF&PID_61ED", StringComparison.OrdinalIgnoreCase) ||
                        devPath.Contains("17EF&PID_6184", StringComparison.OrdinalIgnoreCase))
                    {
                        HidHide.UnhidePath(devPath);
                    }
                }
            }
            catch { }
        }

        private static void UnhideLegionControllers()
        {
            try
            {
                UnhideVirtualControllers();
                foreach (string devPath in HidHide.GetRegisteredDevices())
                {
                    if (devPath.Contains("17EF&PID_61EB", StringComparison.OrdinalIgnoreCase) ||
                        devPath.Contains("17EF&PID_6182", StringComparison.OrdinalIgnoreCase))
                    {
                        HidHide.UnhidePath(devPath);
                    }
                }
                if (ManagerFactory.deviceManager?.PnPDevices != null)
                {
                    foreach (var dev in ManagerFactory.deviceManager.PnPDevices.Values.Where(d => d.VendorID == 0x17EF && (d.ProductID == 0x6184 || d.ProductID == 0x61ED || d.ProductID == 0x6183 || d.ProductID == 0x61EC || d.ProductID == 0x61EB)))
                    {
                        if (!string.IsNullOrEmpty(dev.deviceInstanceId))
                            HidHide.UnhidePath(dev.deviceInstanceId);
                        if (!string.IsNullOrEmpty(dev.baseContainerDeviceInstanceId))
                            HidHide.UnhidePath(dev.baseContainerDeviceInstanceId);
                    }
                }
            }
            catch { }
        }
    }
}
