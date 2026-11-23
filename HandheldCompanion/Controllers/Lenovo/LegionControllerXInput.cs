using HandheldCompanion.Inputs;
using HandheldCompanion.Shared;
using SharpDX.XInput;
using System;
using System.Runtime.InteropServices;

namespace HandheldCompanion.Controllers.Lenovo
{
    public class LegionControllerXInput : LegionController, IXInputController
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeXInputGamepad
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeXInputState
        {
            public uint dwPacketNumber;
            public NativeXInputGamepad Gamepad;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState14(uint dwUserIndex, out NativeXInputState pState);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState910(uint dwUserIndex, out NativeXInputState pState);

        private Controller? xinputController;
        private Gamepad gamepad;
        private bool _wasConnected = false;
        private bool _usingNativeFallback = false;

        public LegionControllerXInput() : base()
        { }

        public LegionControllerXInput(PnPDetails details) : base(details)
        {
            Capabilities |= ControllerCapabilities.Rumble;
        }

        public override void AttachDetails(PnPDetails details)
        {
            AttachController(details.XInputUserIndex);
            base.AttachDetails(details);
        }

        private static bool TryReadNativeSlot(uint slot, out NativeXInputState state)
        {
            state = default;
            try
            {
                if (XInputGetState14(slot, out state) == 0)
                    return true;
            }
            catch { }

            try
            {
                if (XInputGetState910(slot, out state) == 0)
                    return true;
            }
            catch { }

            return false;
        }

        private bool TryReadAnyNativeXInput(out NativeXInputState state)
        {
            if (UserIndex < 4 && TryReadNativeSlot(UserIndex, out state))
                return true;

            for (uint i = 0; i < 4; i++)
            {
                if (TryReadNativeSlot(i, out state))
                {
                    UserIndex = (byte)i;
                    return true;
                }
            }

            state = default;
            return false;
        }

        public override bool IsConnected()
        {
            if (xinputController?.IsConnected == true)
                return true;

            if (UserIndex < 4 && TryReadNativeSlot(UserIndex, out _))
                return true;

            return false;
        }

        public override void SetVibration(byte largeMotor, byte smallMotor)
        {
            if (xinputController?.IsConnected != true)
                return;

            try
            {
                xinputController?.SetVibration(new Vibration
                {
                    LeftMotorSpeed = (ushort)((double)largeMotor / byte.MaxValue * ushort.MaxValue * VibrationStrength),
                    RightMotorSpeed = (ushort)((double)smallMotor / byte.MaxValue * ushort.MaxValue * VibrationStrength)
                });
            }
            catch { }
        }

        public void AttachController(byte userIndex)
        {
            if (userIndex == byte.MaxValue)
                return;

            if (UserIndex == userIndex && xinputController?.IsConnected == true)
                return;

            UserIndex = userIndex;
            xinputController = new Controller((UserIndex)userIndex);
        }

        private void ApplyGamepadState(ushort buttons, byte leftTrigger, byte rightTrigger, short lx, short ly, short rx, short ry)
        {
            var flags = (GamepadButtonFlags)buttons;
            Inputs.ButtonState[ButtonFlags.B1] |= flags.HasFlag(GamepadButtonFlags.A);
            Inputs.ButtonState[ButtonFlags.B2] |= flags.HasFlag(GamepadButtonFlags.B);
            Inputs.ButtonState[ButtonFlags.B3] |= flags.HasFlag(GamepadButtonFlags.X);
            Inputs.ButtonState[ButtonFlags.B4] |= flags.HasFlag(GamepadButtonFlags.Y);
            Inputs.ButtonState[ButtonFlags.Start] |= flags.HasFlag(GamepadButtonFlags.Start);
            Inputs.ButtonState[ButtonFlags.Back] |= flags.HasFlag(GamepadButtonFlags.Back);
            Inputs.ButtonState[ButtonFlags.L2Soft] |= leftTrigger > TriggerThreshold;
            Inputs.ButtonState[ButtonFlags.R2Soft] |= rightTrigger > TriggerThreshold;
            Inputs.ButtonState[ButtonFlags.L2Full] |= leftTrigger > TriggerThreshold * 8;
            Inputs.ButtonState[ButtonFlags.R2Full] |= rightTrigger > TriggerThreshold * 8;
            Inputs.ButtonState[ButtonFlags.LeftStickClick] |= flags.HasFlag(GamepadButtonFlags.LeftThumb);
            Inputs.ButtonState[ButtonFlags.RightStickClick] |= flags.HasFlag(GamepadButtonFlags.RightThumb);
            Inputs.ButtonState[ButtonFlags.L1] |= flags.HasFlag(GamepadButtonFlags.LeftShoulder);
            Inputs.ButtonState[ButtonFlags.R1] |= flags.HasFlag(GamepadButtonFlags.RightShoulder);
            Inputs.ButtonState[ButtonFlags.DPadUp] |= flags.HasFlag(GamepadButtonFlags.DPadUp);
            Inputs.ButtonState[ButtonFlags.DPadDown] |= flags.HasFlag(GamepadButtonFlags.DPadDown);
            Inputs.ButtonState[ButtonFlags.DPadLeft] |= flags.HasFlag(GamepadButtonFlags.DPadLeft);
            Inputs.ButtonState[ButtonFlags.DPadRight] |= flags.HasFlag(GamepadButtonFlags.DPadRight);

            Inputs.AxisState[AxisFlags.LeftStickX] = lx;
            Inputs.AxisState[AxisFlags.LeftStickY] = ly;
            Inputs.AxisState[AxisFlags.RightStickX] = rx;
            Inputs.AxisState[AxisFlags.RightStickY] = ry;
            Inputs.AxisState[AxisFlags.L2] = leftTrigger;
            Inputs.AxisState[AxisFlags.R2] = rightTrigger;
        }

        protected override bool UpdateState()
        {
            ButtonState.Overwrite(InjectedButtons, Inputs.ButtonState);

            if (xinputController?.IsConnected != true)
            {
                for (int i = 0; i < 4; i++)
                {
                    try
                    {
                        var test = new Controller((UserIndex)i);
                        if (test.IsConnected)
                        {
                            UserIndex = (byte)i;
                            xinputController = test;
                            break;
                        }
                    }
                    catch { }
                }
            }

            if (xinputController?.IsConnected == true)
            {
                try
                {
                    gamepad = xinputController.GetState().Gamepad;
                    ApplyGamepadState(
                        (ushort)gamepad.Buttons,
                        gamepad.LeftTrigger,
                        gamepad.RightTrigger,
                        gamepad.LeftThumbX,
                        gamepad.LeftThumbY,
                        gamepad.RightThumbX,
                        gamepad.RightThumbY);

                    if (!_wasConnected || _usingNativeFallback)
                    {
                        _wasConnected = true;
                        _usingNativeFallback = false;
                        LogManager.LogInformation("LegionControllerXInput: Active on XInput slot {0} (SharpDX)", UserIndex);
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    LogManager.LogWarning("LegionControllerXInput: SharpDX GetState failed on slot {0}: {1}", UserIndex, ex.Message);
                    xinputController = null;
                }
            }

            // Direct native Win32 XInput fallback
            if (TryReadAnyNativeXInput(out var nativeState))
            {
                ApplyGamepadState(
                    nativeState.Gamepad.wButtons,
                    nativeState.Gamepad.bLeftTrigger,
                    nativeState.Gamepad.bRightTrigger,
                    nativeState.Gamepad.sThumbLX,
                    nativeState.Gamepad.sThumbLY,
                    nativeState.Gamepad.sThumbRX,
                    nativeState.Gamepad.sThumbRY);

                if (!_wasConnected || !_usingNativeFallback)
                {
                    _wasConnected = true;
                    _usingNativeFallback = true;
                    LogManager.LogInformation("LegionControllerXInput: Active on XInput slot {0} (Native XInput fallback)", UserIndex);
                }
                return true;
            }

            if (_wasConnected)
            {
                _wasConnected = false;
                LogManager.LogWarning("LegionControllerXInput: XInput connection lost across slots 0-3 (check HidHide whitelist/cloak)");
            }

            Inputs.AxisState[AxisFlags.LeftStickX] = 0;
            Inputs.AxisState[AxisFlags.LeftStickY] = 0;
            Inputs.AxisState[AxisFlags.RightStickX] = 0;
            Inputs.AxisState[AxisFlags.RightStickY] = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SetVibration(0, 0);
                xinputController = null;
            }

            base.Dispose(disposing);
        }
    }
}