using System;
using System.Collections.Generic;
using System.Linq;
using System.Media;
using System.Threading.Tasks;
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
        private static ControllerTargetMode? _activeMode;

        public static ControllerTargetMode GetCurrentMode()
        {
            try
            {
                if (PointerModeService.Instance.IsActive)
                    return ControllerTargetMode.Pointer;

                if (ManagerFactory.layoutManager?.GetCurrentMode() == LayoutModes.Desktop)
                    return ControllerTargetMode.Desktop;

                if (VirtualManager.HIDmode == HIDmode.DualShock4Controller)
                    return ControllerTargetMode.DS4;
                else if (VirtualManager.HIDmode == HIDmode.Xbox360Controller)
                    return ControllerTargetMode.X360;
                else if (_activeMode.HasValue && _activeMode.Value == ControllerTargetMode.DS4 && IsApplyingMode)
                    return ControllerTargetMode.DS4;
                else
                    return ControllerTargetMode.Native;
            }
            catch
            {
                return ControllerTargetMode.Desktop;
            }
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
            _activeMode = mode;
            try
            {
                switch (mode)
                {
                    case ControllerTargetMode.Pointer:
                        ManagerFactory.settingsManager.SetProperty("TargetMode", (int)ControllerTargetMode.Pointer);
                        PointerModeService.Instance.SetActive(true);
                        ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", false);
                        ControllerManager.TargetController?.Unhide(false);
                        SetLegionPassthrough(true);
                        UnhideLegionControllers();
                        ManagerFactory.settingsManager.SetProperty("LayoutMode", (int)LayoutModes.Desktop);
                        ManagerFactory.layoutManager.SetLayoutMode(LayoutModes.Desktop);
                        await VirtualManager.SetControllerMode(HIDmode.NoController).ConfigureAwait(false);
                        await VirtualManager.SetControllerStatus(HIDstatus.Disconnected).ConfigureAwait(false);
                        PlaySound();
                        break;

                    case ControllerTargetMode.Desktop:
                        ManagerFactory.settingsManager.SetProperty("TargetMode", (int)ControllerTargetMode.Desktop);
                        PointerModeService.Instance.SetActive(false);
                        ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", true);
                        ControllerManager.TargetController?.Hide(false);
                        SetLegionPassthrough(false);
                        ManagerFactory.settingsManager.SetProperty("LayoutMode", (int)LayoutModes.Desktop);
                        ManagerFactory.layoutManager.SetLayoutMode(LayoutModes.Desktop);
                        await VirtualManager.SetControllerMode(HIDmode.NoController).ConfigureAwait(false);
                        await VirtualManager.SetControllerStatus(HIDstatus.Disconnected).ConfigureAwait(false);
                        PlaySound();
                        break;

                    case ControllerTargetMode.DS4:
                        ManagerFactory.settingsManager.SetProperty("TargetMode", (int)ControllerTargetMode.DS4);
                        PointerModeService.Instance.SetActive(false);
                        UnhideVirtualControllers();
                        ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", true);
                        ControllerManager.TargetController?.Hide(false);
                        SetLegionPassthrough(false);
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
                        PlaySound();
                        break;

                    case ControllerTargetMode.X360:
                        ManagerFactory.settingsManager.SetProperty("TargetMode", (int)ControllerTargetMode.X360);
                        PointerModeService.Instance.SetActive(false);
                        UnhideVirtualControllers();
                        ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", true);
                        ControllerManager.TargetController?.Hide(false);
                        SetLegionPassthrough(false);
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
                        ManagerFactory.settingsManager.SetProperty("TargetMode", (int)ControllerTargetMode.Native);
                        PointerModeService.Instance.SetActive(false);
                        ManagerFactory.settingsManager.SetProperty("HIDmode", (int)HIDmode.NoController);
                        ManagerFactory.settingsManager.SetProperty("HIDstatus", (int)HIDstatus.Disconnected);
                        ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", false);
                        ControllerManager.TargetController?.Unhide(false);
                        SetLegionPassthrough(true);
                        UnhideLegionControllers();
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
