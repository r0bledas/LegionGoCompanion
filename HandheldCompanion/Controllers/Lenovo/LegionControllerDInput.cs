using HandheldCompanion.Inputs;
using HandheldCompanion.Managers;
using HandheldCompanion.Shared;
using HandheldCompanion.Utils;
using Nefarius.Utilities.DeviceManagement.PnP;
using SharpDX.DirectInput;
using System;

namespace HandheldCompanion.Controllers.Lenovo
{
    public class LegionControllerDInput : LegionController
    {
        private DirectInput? directInput;
        private Joystick? joystickLeft;
        private Joystick? joystickRight;
        private bool isDualDInput;

        public LegionControllerDInput() : base()
        { }

        public LegionControllerDInput(PnPDetails details) : base(details)
        { }

        public override bool IsConnected() =>
            (joystickLeft is not null && !joystickLeft.IsDisposed) ||
            (joystickRight is not null && !joystickRight.IsDisposed) ||
            (Controller is not null && Controller.Reading);

        public bool IsRightJoystickConnected => joystickRight is not null && !joystickRight.IsDisposed;
        public bool IsLeftJoystickConnected => joystickLeft is not null && !joystickLeft.IsDisposed;

        public override bool IsReady => IsRightJoystickConnected || IsLeftJoystickConnected || base.IsReady;
        public override bool IsLeftConnected => IsLeftJoystickConnected || base.IsLeftConnected;
        public override bool IsRightConnected => IsRightJoystickConnected || base.IsRightConnected;
        public override bool IsRightDetached => true;

        private DateTime lastAcquireAttempt = DateTime.MinValue;

        private void EnsureJoysticksAcquired()
        {
            if (joystickRight is not null && !joystickRight.IsDisposed && (joystickLeft is not null && !joystickLeft.IsDisposed || !isDualDInput))
                return;

            if ((DateTime.UtcNow - lastAcquireAttempt).TotalSeconds < 2.0)
                return;

            lastAcquireAttempt = DateTime.UtcNow;

            try
            {
                directInput ??= new DirectInput();
                var devices = directInput.GetDevices(DeviceClass.All, DeviceEnumerationFlags.AllDevices);
                foreach (DeviceInstance dev in devices)
                {
                    try
                    {
                        var candidate = new Joystick(directInput, dev.InstanceGuid);
                        try { candidate.SetCooperativeLevel(IntPtr.Zero, CooperativeLevel.NonExclusive | CooperativeLevel.Background); } catch { }
                        string path = candidate.Properties.InterfacePath.ToLower();
                        if (!path.Contains("17ef") || !(path.Contains("6183") || path.Contains("6184") || path.Contains("61ec") || path.Contains("61ed")))
                        {
                            candidate.Dispose();
                            continue;
                        }

                        if (path.Contains("col04") || path.Contains("col03") ||
                            ((path.Contains("mi_01") || path.Contains("mi_02") || path.Contains("mi_03")) &&
                             !path.Contains("col01") && !path.Contains("col02")))
                        {
                            candidate.Dispose();
                            continue;
                        }

                        candidate.Properties.BufferSize = 128;
                        try { candidate.Acquire(); } catch { }

                        if (path.Contains("col02", StringComparison.OrdinalIgnoreCase))
                        {
                            if (joystickRight is null || joystickRight.IsDisposed)
                            {
                                joystickRight = candidate;
                                isDualDInput = true;
                                LogManager.LogInformation("LegionControllerDInput: Acquired Right Joystick (COL02)");
                            }
                            else
                            {
                                candidate.Dispose();
                            }
                        }
                        else if (path.Contains("col01", StringComparison.OrdinalIgnoreCase))
                        {
                            if (joystickLeft is null || joystickLeft.IsDisposed)
                            {
                                joystickLeft = candidate;
                                isDualDInput = true;
                                LogManager.LogInformation("LegionControllerDInput: Acquired Left Joystick (COL01)");
                            }
                            else
                            {
                                candidate.Dispose();
                            }
                        }
                        else
                        {
                            if (!isDualDInput && (joystickLeft is null || joystickLeft.IsDisposed))
                            {
                                joystickLeft = candidate;
                                LogManager.LogInformation("LegionControllerDInput: Acquired Combined Joystick");
                            }
                            else
                            {
                                candidate.Dispose();
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        public override void AttachDetails(PnPDetails details)
        {
            base.AttachDetails(details);

            if (details.ProductID == 0x6184 || details.ProductID == 0x61ED || details.isDongle)
                isDualDInput = true;

            try { joystickLeft?.Unacquire(); joystickLeft?.Dispose(); joystickLeft = null; } catch { }
            try { joystickRight?.Unacquire(); joystickRight?.Dispose(); joystickRight = null; } catch { }
            try { directInput?.Dispose(); directInput = null; } catch { }

            directInput = new DirectInput();

            // Enumerate ALL device classes so Supplemental devices (such as COL02) are discovered
            var devices = directInput.GetDevices(DeviceClass.All, DeviceEnumerationFlags.AllDevices);

            foreach (DeviceInstance deviceInstance in devices)
            {
                try
                {
                    Joystick candidate = new(directInput, deviceInstance.InstanceGuid);
                    try
                    {
                        candidate.SetCooperativeLevel(IntPtr.Zero, CooperativeLevel.NonExclusive | CooperativeLevel.Background);
                    }
                    catch { }

                    string devicePath = candidate.Properties.InterfacePath.ToLower();
                    string symLink = DeviceManager.SymLinkToInstanceId(devicePath, DeviceInterfaceIds.HidDevice.ToString());

                    bool isMatch = symLink.Equals(details.SymLink, StringComparison.InvariantCultureIgnoreCase) ||
                        (devicePath.Contains("17ef") &&
                          (devicePath.Contains("6183") ||
                           devicePath.Contains("6184") ||
                           devicePath.Contains("61ec") ||
                           devicePath.Contains("61ed")));

                    if (!isMatch)
                    {
                        candidate.Dispose();
                        continue;
                    }

                    // Ignore non-gamepad sub-devices (such as touchpad col03, config col04, or vendor endpoints mi_01/02/03 without col01/02)
                    if (devicePath.Contains("col04") || devicePath.Contains("col03") ||
                        ((devicePath.Contains("mi_01") || devicePath.Contains("mi_02") || devicePath.Contains("mi_03")) &&
                         !devicePath.Contains("col01") && !devicePath.Contains("col02")))
                    {
                        candidate.Dispose();
                        continue;
                    }

                    candidate.Properties.BufferSize = 128;
                    try { candidate.Acquire(); } catch { }

                    if (devicePath.Contains("col02", StringComparison.OrdinalIgnoreCase))
                    {
                        joystickRight = candidate;
                        isDualDInput = true;
                    }
                    else if (devicePath.Contains("col01", StringComparison.OrdinalIgnoreCase))
                    {
                        joystickLeft = candidate;
                        isDualDInput = true;
                    }
                    else
                    {
                        // Single combined gamepad (Wired DInput)
                        joystickLeft = candidate;
                        isDualDInput = false;
                    }
                }
                catch { }
            }

            var primaryJs = joystickRight ?? joystickLeft;
            if (primaryJs is not null)
            {
                try { UserIndex = (byte)primaryJs.Properties.JoystickId; } catch { }
            }

            if (joystickLeft is null && joystickRight is null)
                LogManager.LogError($"Couldn't find matching DirectInput controller: VID:{details.GetVendorID()} and PID:{details.GetProductID()}");
        }

        public override void Plug()
        {
            try { joystickLeft?.Acquire(); } catch { }
            try { joystickRight?.Acquire(); } catch { }
            base.Plug();
        }

        public override void Unplug()
        {
            try { joystickLeft?.Unacquire(); } catch { }
            try { joystickRight?.Unacquire(); } catch { }
            base.Unplug();
        }

        public override void Gone()
        {
            try { joystickLeft?.Unacquire(); joystickLeft?.Dispose(); joystickLeft = null; } catch { }
            try { joystickRight?.Unacquire(); joystickRight?.Dispose(); joystickRight = null; } catch { }
            try { directInput?.Dispose(); directInput = null; } catch { }
            base.Gone();
        }

        protected override bool UpdateState()
        {
            EnsureJoysticksAcquired();

            ButtonState.Overwrite(InjectedButtons, Inputs.ButtonState);

            bool anyPolled = false;

            // 1. Poll Left / Combined Joystick
            if (joystickLeft is not null && !joystickLeft.IsDisposed)
            {
                try
                {
                    JoystickState state = joystickLeft.GetCurrentState();
                    anyPolled = true;

                    // Left Stick
                    Inputs.AxisState[AxisFlags.LeftStickX] = (short)InputUtils.MapRange(state.X, ushort.MinValue, ushort.MaxValue, short.MinValue, short.MaxValue);
                    Inputs.AxisState[AxisFlags.LeftStickY] = (short)InputUtils.MapRange(state.Y, ushort.MaxValue, ushort.MinValue, short.MinValue, short.MaxValue);

                    // Left D-Pad
                    int pov = state.PointOfViewControllers[0];
                    Inputs.ButtonState[ButtonFlags.DPadUp] |= pov is 0 or 4500 or 31500;
                    Inputs.ButtonState[ButtonFlags.DPadRight] |= pov is 4500 or 9000 or 13500;
                    Inputs.ButtonState[ButtonFlags.DPadDown] |= pov is 13500 or 18000 or 22500;
                    Inputs.ButtonState[ButtonFlags.DPadLeft] |= pov is 22500 or 27000 or 31500;

                    // Left Buttons
                    Inputs.ButtonState[ButtonFlags.L1] |= state.Buttons[6];
                    Inputs.ButtonState[ButtonFlags.Back] |= state.Buttons[10];
                    Inputs.ButtonState[ButtonFlags.LeftStickClick] |= state.Buttons[13];

                    if (!isDualDInput)
                    {
                        // Single combined gamepad (Wired DInput): also read right stick & buttons from this joystick
                        Inputs.AxisState[AxisFlags.RightStickX] = (short)InputUtils.MapRange(state.Z, ushort.MinValue, ushort.MaxValue, short.MinValue, short.MaxValue);
                        Inputs.AxisState[AxisFlags.RightStickY] = (short)InputUtils.MapRange(state.RotationZ, ushort.MaxValue, ushort.MinValue, short.MinValue, short.MaxValue);

                        Inputs.ButtonState[ButtonFlags.B1] |= state.Buttons[0];
                        Inputs.ButtonState[ButtonFlags.B2] |= state.Buttons[1];
                        Inputs.ButtonState[ButtonFlags.B3] |= state.Buttons[2] || state.Buttons[3];
                        Inputs.ButtonState[ButtonFlags.B4] |= state.Buttons[4];
                        Inputs.ButtonState[ButtonFlags.R1] |= state.Buttons[7];
                        Inputs.ButtonState[ButtonFlags.Start] |= state.Buttons[11];
                        Inputs.ButtonState[ButtonFlags.RightStickClick] |= state.Buttons[9] || state.Buttons[14];
                    }
                }
                catch (SharpDX.SharpDXException ex)
                {
                    if (IsPlugged)
                        try { joystickLeft.Acquire(); } catch { }
                }
            }
            else if (isDualDInput && !IsLeftConnected)
            {
                Inputs.AxisState[AxisFlags.LeftStickX] = 0;
                Inputs.AxisState[AxisFlags.LeftStickY] = 0;
            }

            // 2. Poll Right Joystick (in dual_dinput wireless mode)
            if (joystickRight is not null && !joystickRight.IsDisposed)
            {
                try
                {
                    JoystickState stateR = joystickRight.GetCurrentState();
                    anyPolled = true;

                    // On COL02 (Right Gamepad), the physical thumbstick is mounted sideways in single/detached mode:
                    // Physical LEFT decreases Y, RIGHT increases Y -> mapped to RightStickX
                    // Physical UP increases X, DOWN decreases X -> mapped to RightStickY (+32767 UP, -32768 DOWN)
                    Inputs.AxisState[AxisFlags.RightStickX] = (short)InputUtils.MapRange(stateR.Y, ushort.MinValue, ushort.MaxValue, short.MinValue, short.MaxValue);
                    Inputs.AxisState[AxisFlags.RightStickY] = (short)InputUtils.MapRange(stateR.X, ushort.MinValue, ushort.MaxValue, short.MinValue, short.MaxValue);

                    // Right Buttons (mapped from hardware diagnostic verification on COL02)
                    Inputs.ButtonState[ButtonFlags.B1] |= stateR.Buttons[2]; // Physical A -> B1
                    Inputs.ButtonState[ButtonFlags.B2] |= stateR.Buttons[0]; // Physical B -> B2
                    Inputs.ButtonState[ButtonFlags.B3] |= stateR.Buttons[3]; // Physical X -> B3
                    Inputs.ButtonState[ButtonFlags.B4] |= stateR.Buttons[1]; // Physical Y -> B4
                    Inputs.ButtonState[ButtonFlags.B5] |= stateR.Buttons[6]; // M2 button
                    Inputs.ButtonState[ButtonFlags.R1] |= stateR.Buttons[7]; // RB
                    Inputs.ButtonState[ButtonFlags.Start] |= stateR.Buttons[11]; // Legion R / Start
                    Inputs.ButtonState[ButtonFlags.RightStickClick] |= stateR.Buttons[8] || stateR.Buttons[9] || stateR.Buttons[10] || stateR.Buttons[14];
                }
                catch (SharpDX.SharpDXException ex)
                {
                    if (IsPlugged)
                        try { joystickRight.Acquire(); } catch { }
                }
            }

            // 3. Triggers (L2 and R2 from 64-byte raw HID packet, respecting shift if misaligned)
            int ltIdx = IsHidReportMisaligned() ? 20 : 22;
            int rtIdx = IsHidReportMisaligned() ? 21 : 23;

            byte rawL2 = (data != null && data.Length > ltIdx) ? data[ltIdx] : (byte)0;
            byte rawR2 = (data != null && data.Length > rtIdx) ? data[rtIdx] : (byte)0;

            Inputs.AxisState[AxisFlags.L2] = rawL2;
            Inputs.AxisState[AxisFlags.R2] = rawR2;

            Inputs.ButtonState[ButtonFlags.L2Soft] |= rawL2 > TriggerThreshold;
            Inputs.ButtonState[ButtonFlags.R2Soft] |= rawR2 > TriggerThreshold;
            Inputs.ButtonState[ButtonFlags.L2Full] |= rawL2 > 240;
            Inputs.ButtonState[ButtonFlags.R2Full] |= rawR2 > 240;

            return anyPolled || IsConnected() || (Controller is not null && Controller.Reading);
        }
    }
}