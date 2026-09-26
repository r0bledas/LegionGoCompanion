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
        private Joystick? joystickLeft;
        private Joystick? joystickRight;
        private bool isDualDInput;

        public LegionControllerDInput() : base()
        { }

        public LegionControllerDInput(PnPDetails details) : base(details)
        { }

        public override bool IsConnected() =>
            (joystickLeft is not null && !joystickLeft.IsDisposed) ||
            (joystickRight is not null && !joystickRight.IsDisposed);

        public override void AttachDetails(PnPDetails details)
        {
            base.AttachDetails(details);

            try { joystickLeft?.Dispose(); joystickLeft = null; } catch { }
            try { joystickRight?.Dispose(); joystickRight = null; } catch { }

            using (DirectInput directInput = new())
            {
                var devices = directInput.GetDevices(DeviceType.Gamepad, DeviceEnumerationFlags.AllDevices);
                if (devices.Count == 0)
                    devices = directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AllDevices);

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

                        string devicePath = candidate.Properties.InterfacePath;
                        string symLink = DeviceManager.SymLinkToInstanceId(devicePath, DeviceInterfaceIds.HidDevice.ToString());

                        bool isMatch = symLink.Equals(details.SymLink, StringComparison.InvariantCultureIgnoreCase) ||
                            (devicePath.Contains("17EF", StringComparison.OrdinalIgnoreCase) &&
                              (devicePath.Contains("6183", StringComparison.OrdinalIgnoreCase) ||
                               devicePath.Contains("6184", StringComparison.OrdinalIgnoreCase) ||
                               devicePath.Contains("61EC", StringComparison.OrdinalIgnoreCase) ||
                               devicePath.Contains("61ED", StringComparison.OrdinalIgnoreCase)));

                        if (!isMatch)
                        {
                            candidate.Dispose();
                            continue;
                        }

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
            try { joystickLeft?.Dispose(); joystickLeft = null; } catch { }
            try { joystickRight?.Dispose(); joystickRight = null; } catch { }
            base.Gone();
        }

        protected override bool UpdateState()
        {
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
                    if (ex.ResultCode == ResultCode.NotAcquired && IsPlugged)
                        try { joystickLeft.Acquire(); } catch { }
                    else if (ex.ResultCode == ResultCode.InputLost && Details is not null)
                        AttachDetails(Details);
                }
            }

            // 2. Poll Right Joystick (in dual_dinput wireless mode)
            if (joystickRight is not null && !joystickRight.IsDisposed)
            {
                try
                {
                    JoystickState stateR = joystickRight.GetCurrentState();
                    anyPolled = true;

                    // On COL02 (Right Gamepad), the physical thumbstick is mapped to X & Y!
                    Inputs.AxisState[AxisFlags.RightStickX] = (short)InputUtils.MapRange(stateR.X, ushort.MinValue, ushort.MaxValue, short.MinValue, short.MaxValue);
                    Inputs.AxisState[AxisFlags.RightStickY] = (short)InputUtils.MapRange(stateR.Y, ushort.MaxValue, ushort.MinValue, short.MinValue, short.MaxValue);

                    // Right Buttons
                    Inputs.ButtonState[ButtonFlags.B1] |= stateR.Buttons[0];
                    Inputs.ButtonState[ButtonFlags.B2] |= stateR.Buttons[1];
                    Inputs.ButtonState[ButtonFlags.B3] |= stateR.Buttons[2] || stateR.Buttons[3];
                    Inputs.ButtonState[ButtonFlags.B4] |= stateR.Buttons[4];
                    Inputs.ButtonState[ButtonFlags.R1] |= stateR.Buttons[7];
                    Inputs.ButtonState[ButtonFlags.Start] |= stateR.Buttons[11];
                    Inputs.ButtonState[ButtonFlags.RightStickClick] |= stateR.Buttons[9] || stateR.Buttons[14];
                }
                catch (SharpDX.SharpDXException ex)
                {
                    if (ex.ResultCode == ResultCode.NotAcquired && IsPlugged)
                        try { joystickRight.Acquire(); } catch { }
                    else if (ex.ResultCode == ResultCode.InputLost && Details is not null)
                        AttachDetails(Details);
                }
            }

            // 3. Triggers (L2 and R2 from 64-byte raw HID packet)
            byte L2 = (byte)InputUtils.MapRange(data[22], byte.MinValue, byte.MaxValue, ushort.MinValue, ushort.MaxValue);
            byte R2 = (byte)InputUtils.MapRange(data[23], byte.MinValue, byte.MaxValue, ushort.MinValue, ushort.MaxValue);
            Inputs.AxisState[AxisFlags.L2] = L2;
            Inputs.AxisState[AxisFlags.R2] = R2;

            Inputs.ButtonState[ButtonFlags.L2Soft] |= L2 > TriggerThreshold;
            Inputs.ButtonState[ButtonFlags.R2Soft] |= R2 > TriggerThreshold;
            Inputs.ButtonState[ButtonFlags.L2Full] |= L2 > TriggerThreshold * 8;
            Inputs.ButtonState[ButtonFlags.R2Full] |= R2 > TriggerThreshold * 8;

            return anyPolled || IsConnected();
        }
    }
}