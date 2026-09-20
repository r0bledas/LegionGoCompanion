using System;
using System.Collections.Generic;
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
        Native = 3
    }

    public static class ControllerModeService
    {
        public static event Action<ControllerTargetMode>? ModeChanged;

        public static ControllerTargetMode GetCurrentMode()
        {
            try
            {
                if (ManagerFactory.layoutManager?.GetCurrentMode() == LayoutModes.Desktop)
                    return ControllerTargetMode.Desktop;

                if (VirtualManager.HIDmode == HIDmode.DualShock4Controller)
                    return ControllerTargetMode.DS4;
                else if (VirtualManager.HIDmode == HIDmode.Xbox360Controller)
                    return ControllerTargetMode.X360;
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
            _ => mode.ToString()
        };

        public static async Task<bool> ApplyMode(ControllerTargetMode mode)
        {
            try
            {
                switch (mode)
                {
                    case ControllerTargetMode.Desktop:
                        ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", true);
                        ControllerManager.TargetController?.Hide(false);
                        SetLegionPassthrough(false);
                        await VirtualManager.SetControllerMode(HIDmode.NoController);
                        await VirtualManager.SetControllerStatus(HIDstatus.Disconnected);
                        ManagerFactory.layoutManager.SetLayoutMode(LayoutModes.Desktop);
                        ManagerFactory.settingsManager.SetProperty("LayoutMode", (int)LayoutModes.Desktop);
                        PlaySound();
                        break;

                    case ControllerTargetMode.DS4:
                        ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", true);
                        ControllerManager.TargetController?.Hide(false);
                        SetLegionPassthrough(false);
                        ManagerFactory.layoutManager.SetLayoutMode(LayoutModes.Gamepad);
                        ManagerFactory.settingsManager.SetProperty("LayoutMode", (int)LayoutModes.Gamepad);
                        ManagerFactory.settingsManager.SetProperty("HIDmode", (int)HIDmode.DualShock4Controller);
                        await VirtualManager.SetControllerMode(HIDmode.DualShock4Controller);
                        await VirtualManager.SetControllerStatus(HIDstatus.Connected);
                        PlaySound();
                        break;

                    case ControllerTargetMode.X360:
                        ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", true);
                        ControllerManager.TargetController?.Hide(false);
                        SetLegionPassthrough(false);
                        ManagerFactory.layoutManager.SetLayoutMode(LayoutModes.Gamepad);
                        ManagerFactory.settingsManager.SetProperty("LayoutMode", (int)LayoutModes.Gamepad);
                        ManagerFactory.settingsManager.SetProperty("HIDmode", (int)HIDmode.Xbox360Controller);
                        await VirtualManager.SetControllerMode(HIDmode.Xbox360Controller);
                        await VirtualManager.SetControllerStatus(HIDstatus.Connected);
                        PlaySound();
                        break;

                    case ControllerTargetMode.Native:
                        await VirtualManager.SetControllerMode(HIDmode.NoController);
                        await VirtualManager.SetControllerStatus(HIDstatus.Disconnected);
                        ManagerFactory.settingsManager.SetProperty("HIDcloakonconnect", false);
                        ControllerManager.TargetController?.Unhide(false);
                        SetLegionPassthrough(true);
                        ManagerFactory.layoutManager.SetLayoutMode(LayoutModes.Gamepad);
                        ManagerFactory.settingsManager.SetProperty("LayoutMode", (int)LayoutModes.Gamepad);
                        ManagerFactory.settingsManager.SetProperty("HIDmode", (int)HIDmode.NoController);
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
    }
}
