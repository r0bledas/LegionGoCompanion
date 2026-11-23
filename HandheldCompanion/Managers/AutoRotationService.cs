using HandheldCompanion.Controllers;
using HandheldCompanion.Devices;
using HandheldCompanion.Helpers;
using HandheldCompanion.Shared;
using Microsoft.Win32;
using System;
using System.Threading;
using static HandheldCompanion.Managers.MultimediaManager;

namespace HandheldCompanion.Managers
{
    public sealed class AutoRotationService
    {
        private static readonly Lazy<AutoRotationService> _instance = new(() => new AutoRotationService());
        public static AutoRotationService Instance => _instance.Value;

        private Timer? _pollTimer;
        private readonly object _lock = new();
        private DMDO? _pendingOrientation;
        private DateTime _pendingSinceUtc = DateTime.MinValue;
        private DateTime _lastApplyUtc = DateTime.MinValue;

        private const float DominantAxisThreshold = 0.55f;
        private const float FlatZThreshold = 0.75f;
        private const int HoldDelayMs = 500;
        private const int CooldownMs = 1200;

        public bool IsEnabled { get; private set; }

        private AutoRotationService() { }

        public void InitializeFromSettings()
        {
            bool enabled = ManagerFactory.settingsManager?.HasSetting("AutoRotationEnabled") == true
                && ManagerFactory.settingsManager.GetBoolean("AutoRotationEnabled");
            SetEnabled(enabled, persist: false);
        }

        public void SetEnabled(bool enabled, bool persist = true)
        {
            lock (_lock)
            {
                IsEnabled = enabled;
                _pendingOrientation = null;

                if (persist)
                    ManagerFactory.settingsManager?.SetProperty("AutoRotationEnabled", enabled);

                SyncWindowsAutoRotationRegistry(enabled);

                if (enabled)
                {
                    _pollTimer ??= new Timer(_ => PollOrientation(), null, 300, 150);
                    _pollTimer.Change(300, 150);
                }
                else
                {
                    _pollTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }
        }

        private static void SyncWindowsAutoRotationRegistry(bool enabled)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AutoRotation", writable: true);
                key?.SetValue("Enable", enabled ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }
        }

        /// <summary>
        /// Classifies an accelerometer gravity vector (in g units) into a Windows display orientation.
        /// Returns null when the device is held nearly flat (tabletop) or between thresholds.
        /// </summary>
        public static DMDO? ClassifyOrientation(float accelX, float accelY, float accelZ)
        {
            float absX = MathF.Abs(accelX);
            float absY = MathF.Abs(accelY);
            float absZ = MathF.Abs(accelZ);

            // Ignore when lying flat on a table/lap
            if (absZ >= FlatZThreshold && absX < DominantAxisThreshold && absY < DominantAxisThreshold)
                return null;

            if (absY >= absX && absY >= DominantAxisThreshold)
            {
                // Gravity along Y: upright landscape vs upside-down landscape
                return accelY < 0 ? DMDO.DEFAULT : DMDO.D180;
            }

            if (absX > absY && absX >= DominantAxisThreshold)
            {
                // Gravity along X: rotated 90° vs 270°
                return accelX < 0 ? DMDO.D270 : DMDO.D90;
            }

            return null;
        }

        private void PollOrientation()
        {
            if (!IsEnabled) return;

            try
            {
                if (!TryReadAccelerometer(out float ax, out float ay, out float az))
                    return;

                DMDO? detected = ClassifyOrientation(ax, ay, az);
                if (detected is null)
                {
                    _pendingOrientation = null;
                    return;
                }

                var mm = ManagerFactory.multimediaManager;
                if (mm is null) return;

                DMDO current = mm.GetDisplayOrientation();
                if (detected.Value == current)
                {
                    _pendingOrientation = null;
                    return;
                }

                DateTime now = DateTime.UtcNow;
                if ((now - _lastApplyUtc).TotalMilliseconds < CooldownMs)
                    return;

                if (_pendingOrientation != detected.Value)
                {
                    _pendingOrientation = detected.Value;
                    _pendingSinceUtc = now;
                    return;
                }

                if ((now - _pendingSinceUtc).TotalMilliseconds >= HoldDelayMs)
                {
                    _pendingOrientation = null;
                    _lastApplyUtc = now;
                    if (mm.SetDisplayOrientation(detected.Value))
                    {
                        LogManager.LogInformation("AutoRotation: Rotated display to {0} (accel=({1:F2}, {2:F2}, {3:F2}))",
                            detected.Value, ax, ay, az);
                    }
                }
            }
            catch { }
        }

        private static bool TryReadAccelerometer(out float ax, out float ay, out float az)
        {
            ax = ay = az = 0f;

            var tc = ControllerManager.TargetController;
            if (tc?.gamepadMotions != null)
            {
                // Prefer Right (1) or Left (0) Joy-Con IMU, or default motion
                if ((tc.gamepadMotions.TryGetValue(1, out var m) || tc.gamepadMotions.TryGetValue(0, out m) || tc.gamepadMotions.TryGetValue(tc.gamepadIndex, out m))
                    && m != null
                    && (m.accelX != 0f || m.accelY != 0f || m.accelZ != 0f))
                {
                    ax = m.accelX;
                    ay = m.accelY;
                    az = m.accelZ;
                    return true;
                }
            }

            var devMotion = IDevice.GetCurrent()?.GamepadMotion;
            if (devMotion != null && (devMotion.accelX != 0f || devMotion.accelY != 0f || devMotion.accelZ != 0f))
            {
                ax = devMotion.accelX;
                ay = devMotion.accelY;
                az = devMotion.accelZ;
                return true;
            }

            return false;
        }
    }
}
