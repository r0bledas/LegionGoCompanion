using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HandheldCompanion.Actions;
using HandheldCompanion.Controllers;
using HandheldCompanion.Controllers.Lenovo;
using HandheldCompanion.Helpers;
using HandheldCompanion.Inputs;
using HandheldCompanion.Shared;
using HandheldCompanion.Simulators;
using HandheldCompanion.Utils;
using Newtonsoft.Json;

namespace HandheldCompanion.Managers
{
    public enum PointerActivationButton
    {
        M1 = 0,             // ButtonFlags.B11 (Upper Back Button)
        M2 = 1,             // ButtonFlags.B5 (Lower Back Button)
        M3 = 2,             // ButtonFlags.R4 (Side/Bottom Button)
        RightStickClick = 3,// ButtonFlags.RightStickClick
        RB = 4,              // ButtonFlags.R1
        Y3 = 5,              // ButtonFlags.R5 (Lower Right Back Button)
        AlwaysActive = 6    // Always Active (no button required)
    }

    public enum PointerActivationType
    {
        HoldToAim = 0,      // Default: Active while holding button
        Toggle = 1,         // Press button to toggle on / off
        AlwaysActive = 2    // Always active
    }

    public class PointerModeConfig
    {
        public bool DetachedOnly { get; set; } = false;
        public PointerActivationButton ActivationButton { get; set; } = PointerActivationButton.AlwaysActive;
        public PointerActivationType ActivationType { get; set; } = PointerActivationType.AlwaysActive;
        public float Sensitivity { get; set; } = 1.5f;
        public bool InvertX { get; set; } = false;
        public bool InvertY { get; set; } = false;
        public bool RightClickOnRB { get; set; } = true;
    }

    public class PointerModeService
    {
        private static readonly Lazy<PointerModeService> _instance = new(() => new PointerModeService());
        public static PointerModeService Instance => _instance.Value;

        private readonly string _configPath;
        private readonly object _lock = new();

        public bool IsActive { get; private set; } = false;

        // Settings
        public bool DetachedOnly { get; set; } = false;
        public PointerActivationButton ActivationButton { get; set; } = PointerActivationButton.AlwaysActive;
        public PointerActivationType ActivationType { get; set; } = PointerActivationType.AlwaysActive;
        public float Sensitivity { get; set; } = 1.5f;
        public bool InvertX { get; set; } = false;
        public bool InvertY { get; set; } = false;
        public bool RightClickOnRB { get; set; } = true;

        // Runtime states
        private bool _isToggledOn = false;
        private bool _prevButtonPressed = false;
        private bool _prevRbPressed = false;
        private bool _prevRtPressed = false;
        private bool _prevAPressed = false;
        private bool _prevBPressed = false;
        private bool _prevR3Pressed = false;

        private bool _isLeftMousePressedByRt = false;
        private bool _isLeftMousePressedByA = false;
        private bool _isRightMousePressedByRb = false;
        private bool _isRightMousePressedByB = false;
        private bool _isMiddleMousePressedByR3 = false;

        private float _subpixelX = 0f;
        private float _subpixelY = 0f;

        public event Action<bool>? ActiveChanged;

        public PointerModeService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(appData, "HandheldCompanion");
            Directory.CreateDirectory(dir);
            _configPath = Path.Combine(dir, "pointer_mode.json");
            LoadConfig();
        }

        public void SetActive(bool active)
        {
            if (IsActive == active) return;
            IsActive = active;
            _isToggledOn = false;
            _subpixelX = 0f;
            _subpixelY = 0f;
            ReleaseHeldButtons();
            ActiveChanged?.Invoke(active);
        }

        public void ProcessTick(IController? tc, ControllerState controllerState, Dictionary<byte, GamepadMotion>? motions, float delta)
        {
            if (!IsActive || controllerState == null)
                return;

            // 1. Detached Only check
            if (DetachedOnly && tc is LegionController lego)
            {
                if (!lego.IsRightDetached && !lego.IsWireless() && !lego.IsRightConnected)
                {
                    // Controller is attached/docked; release any held mouse buttons and remain dormant
                    ReleaseHeldButtons();
                    _subpixelX = 0f;
                    _subpixelY = 0f;
                    return;
                }
            }

            // 2. Determine if gyro aiming should be active based on activation button and type
            bool isAiming = false;
            ButtonFlags buttonFlag = GetActivationButtonFlag(ActivationButton);

            if (ActivationButton == PointerActivationButton.AlwaysActive || ActivationType == PointerActivationType.AlwaysActive)
            {
                isAiming = true;
            }
            else if (buttonFlag != ButtonFlags.None)
            {
                bool buttonDown = controllerState.ButtonState[buttonFlag] || 
                    ((ActivationButton == PointerActivationButton.RB || ActivationButton == PointerActivationButton.M1) &&
                     (controllerState.ButtonState[ButtonFlags.R1] || controllerState.ButtonState[ButtonFlags.B11] || controllerState.ButtonState[ButtonFlags.B6]));

                if (ActivationType == PointerActivationType.HoldToAim)
                {
                    isAiming = buttonDown;
                    // Swallow activation button so it doesn't trigger desktop bindings
                    if (buttonDown)
                    {
                        controllerState.ButtonState[buttonFlag] = false;
                        controllerState.ButtonState[ButtonFlags.R1] = false;
                        controllerState.ButtonState[ButtonFlags.B11] = false;
                        controllerState.ButtonState[ButtonFlags.B6] = false;
                    }
                }
                else if (ActivationType == PointerActivationType.Toggle)
                {
                    if (buttonDown && !_prevButtonPressed)
                    {
                        _isToggledOn = !_isToggledOn;
                    }
                    _prevButtonPressed = buttonDown;
                    isAiming = _isToggledOn;
                    // Swallow activation button
                    controllerState.ButtonState[buttonFlag] = false;
                    controllerState.ButtonState[ButtonFlags.R1] = false;
                    controllerState.ButtonState[ButtonFlags.B11] = false;
                    controllerState.ButtonState[ButtonFlags.B6] = false;
                }
            }

            // 3. Mouse Clicks from Right Controller Inputs
            // Left Click via RT (Right Trigger)
            bool rtDown = controllerState.ButtonState[ButtonFlags.R2Soft] ||
                          controllerState.ButtonState[ButtonFlags.R2Full] ||
                          (controllerState.AxisState[AxisFlags.R2] > 60);

            if (rtDown != _prevRtPressed)
            {
                if (rtDown)
                {
                    _isLeftMousePressedByRt = true;
                    MouseSimulator.MouseDown(MouseActionsType.LeftButton);
                }
                else
                {
                    _isLeftMousePressedByRt = false;
                    if (!_isLeftMousePressedByA)
                        MouseSimulator.MouseUp(MouseActionsType.LeftButton);
                }
                _prevRtPressed = rtDown;
            }
            if (rtDown)
            {
                controllerState.ButtonState[ButtonFlags.R2Soft] = false;
                controllerState.ButtonState[ButtonFlags.R2Full] = false;
            }

            // Left Click via Physical Button A (B1)
            bool aDown = controllerState.ButtonState[ButtonFlags.B1];
            if (aDown != _prevAPressed)
            {
                if (aDown)
                {
                    _isLeftMousePressedByA = true;
                    MouseSimulator.MouseDown(MouseActionsType.LeftButton);
                }
                else
                {
                    _isLeftMousePressedByA = false;
                    if (!_isLeftMousePressedByRt)
                        MouseSimulator.MouseUp(MouseActionsType.LeftButton);
                }
                _prevAPressed = aDown;
            }
            if (aDown)
                controllerState.ButtonState[ButtonFlags.B1] = false;

            // Right Click via Physical Button B (B2)
            bool bDown = controllerState.ButtonState[ButtonFlags.B2];
            if (bDown != _prevBPressed)
            {
                if (bDown)
                {
                    _isRightMousePressedByB = true;
                    MouseSimulator.MouseDown(MouseActionsType.RightButton);
                }
                else
                {
                    _isRightMousePressedByB = false;
                    if (!_isRightMousePressedByRb)
                        MouseSimulator.MouseUp(MouseActionsType.RightButton);
                }
                _prevBPressed = bDown;
            }
            if (bDown)
                controllerState.ButtonState[ButtonFlags.B2] = false;

            // Right Click on RB (if enabled and RB is not used for aiming activation)
            if (RightClickOnRB && ActivationButton != PointerActivationButton.RB)
            {
                bool rbDown = controllerState.ButtonState[ButtonFlags.R1];
                if (rbDown != _prevRbPressed)
                {
                    if (rbDown)
                    {
                        _isRightMousePressedByRb = true;
                        MouseSimulator.MouseDown(MouseActionsType.RightButton);
                    }
                    else
                    {
                        _isRightMousePressedByRb = false;
                        if (!_isRightMousePressedByB)
                            MouseSimulator.MouseUp(MouseActionsType.RightButton);
                    }
                    _prevRbPressed = rbDown;
                }
                if (rbDown)
                    controllerState.ButtonState[ButtonFlags.R1] = false;
            }

            // Middle Click via Right Stick Click (R3)
            bool r3Down = controllerState.ButtonState[ButtonFlags.RightStickClick];
            if (r3Down != _prevR3Pressed)
            {
                if (r3Down)
                {
                    _isMiddleMousePressedByR3 = true;
                    MouseSimulator.MouseDown(MouseActionsType.MiddleButton);
                }
                else
                {
                    _isMiddleMousePressedByR3 = false;
                    MouseSimulator.MouseUp(MouseActionsType.MiddleButton);
                }
                _prevR3Pressed = r3Down;
            }
            if (r3Down)
                controllerState.ButtonState[ButtonFlags.RightStickClick] = false;

            // 4. Translate Right Stick to Mouse Cursor
            short rawStickX = controllerState.AxisState[AxisFlags.RightStickX];
            short rawStickY = controllerState.AxisState[AxisFlags.RightStickY];

            float stickDx = 0f;
            float stickDy = 0f;

            float stickMag = MathF.Sqrt((float)rawStickX * rawStickX + (float)rawStickY * rawStickY);
            const float STICK_DEADZONE = 3200f; // ~10% of 32767
            if (stickMag > STICK_DEADZONE)
            {
                float norm = Math.Clamp((stickMag - STICK_DEADZONE) / (32767f - STICK_DEADZONE), 0f, 1f);
                float curvedNorm = norm * (0.6f + 1.4f * norm);

                float dirX = rawStickX / stickMag;
                float dirY = rawStickY / stickMag;

                const float BASE_STICK_SPEED = 1400f;
                float stickSpeed = BASE_STICK_SPEED * Sensitivity * curvedNorm * delta;

                stickDx = dirX * stickSpeed;
                stickDy = -dirY * stickSpeed; // Positive stick Y is UP, which is negative Y on Windows screen coordinates

                if (InvertX) stickDx = -stickDx;
                if (InvertY) stickDy = -stickDy;
            }

            // Swallow Right Stick so it doesn't double-trigger layout actions
            controllerState.AxisState[AxisFlags.RightStickX] = 0;
            controllerState.AxisState[AxisFlags.RightStickY] = 0;

            // 5. Translate Gyro to Mouse (when aiming is active)
            float gyroDx = 0f;
            float gyroDy = 0f;

            if (isAiming && motions != null && delta > 0.00001f)
            {
                // Prefer Right Joy-Con IMU (index 1)
                if (!motions.TryGetValue(1, out GamepadMotion? motion) || motion == null)
                {
                    if (!motions.TryGetValue(tc?.gamepadIndex ?? 0, out motion) || motion == null)
                        motion = motions.Values.FirstOrDefault();
                }

                if (motion != null)
                {
                    // JoyShock player-space gyro gives tilt-compensated screen deltas (deg/s)
                    motion.GetPlayerSpaceGyro(out float playerX, out float playerY, 1.41f);

                    // Deadzone to prevent hand tremor / sensor drift
                    const float DEADZONE = 0.20f;
                    float mag = MathF.Sqrt(playerX * playerX + playerY * playerY);
                    if (mag >= DEADZONE)
                    {
                        // Base multiplier calibrated for natural pointer speed at 60-144 Hz
                        const float BASE_SPEED = 28.0f;
                        float speedScale = BASE_SPEED * Sensitivity * delta;

                        // JoyShock player-space gyro: playerY is yaw (horizontal aim), playerX is pitch (vertical aim)
                        gyroDx = -playerY * speedScale;
                        gyroDy = -playerX * speedScale; // pitch up (-dy in Windows screen coords) moves cursor up

                        if (InvertX) gyroDx = -gyroDx;
                        if (InvertY) gyroDy = -gyroDy;
                    }
                }
            }

            // 6. Blend Stick and Gyro Movement into Subpixel Accumulator
            _subpixelX += gyroDx + stickDx;
            _subpixelY += gyroDy + stickDy;

            int moveX = (int)_subpixelX;
            int moveY = (int)_subpixelY;

            _subpixelX -= moveX;
            _subpixelY -= moveY;

            if (moveX != 0 || moveY != 0)
            {
                MouseSimulator.MoveBy(moveX, moveY);
            }
            else if (stickMag <= STICK_DEADZONE && !isAiming)
            {
                _subpixelX = 0f;
                _subpixelY = 0f;
            }
        }

        private static ButtonFlags GetActivationButtonFlag(PointerActivationButton btn) => btn switch
        {
            PointerActivationButton.M1 => ButtonFlags.B11,
            PointerActivationButton.M2 => ButtonFlags.B5,
            PointerActivationButton.M3 => ButtonFlags.R4,
            PointerActivationButton.RightStickClick => ButtonFlags.RightStickClick,
            PointerActivationButton.RB => ButtonFlags.R1,
            PointerActivationButton.Y3 => ButtonFlags.R5,
            _ => ButtonFlags.None
        };

        private void ReleaseHeldButtons()
        {
            if (_isLeftMousePressedByRt || _isLeftMousePressedByA)
            {
                MouseSimulator.MouseUp(MouseActionsType.LeftButton);
                _isLeftMousePressedByRt = false;
                _isLeftMousePressedByA = false;
            }
            if (_isRightMousePressedByRb || _isRightMousePressedByB)
            {
                MouseSimulator.MouseUp(MouseActionsType.RightButton);
                _isRightMousePressedByRb = false;
                _isRightMousePressedByB = false;
            }
            if (_isMiddleMousePressedByR3)
            {
                MouseSimulator.MouseUp(MouseActionsType.MiddleButton);
                _isMiddleMousePressedByR3 = false;
            }
            _prevRtPressed = false;
            _prevAPressed = false;
            _prevBPressed = false;
            _prevRbPressed = false;
            _prevR3Pressed = false;
        }

        public void SaveConfig()
        {
            try
            {
                lock (_lock)
                {
                    var cfg = new PointerModeConfig
                    {
                        DetachedOnly = DetachedOnly,
                        ActivationButton = ActivationButton,
                        ActivationType = ActivationType,
                        Sensitivity = Sensitivity,
                        InvertX = InvertX,
                        InvertY = InvertY,
                        RightClickOnRB = RightClickOnRB
                    };
                    string json = JsonConvert.SerializeObject(cfg, Formatting.Indented);
                    File.WriteAllText(_configPath, json);
                }
            }
            catch (Exception ex)
            {
                LogManager.LogError("PointerModeService: Failed to save config: {0}", ex.Message);
            }
        }

        public void LoadConfig()
        {
            try
            {
                lock (_lock)
                {
                    if (File.Exists(_configPath))
                    {
                        string json = File.ReadAllText(_configPath);
                        var cfg = JsonConvert.DeserializeObject<PointerModeConfig>(json);
                        if (cfg != null)
                        {
                            DetachedOnly = cfg.DetachedOnly;
                            ActivationButton = cfg.ActivationButton;
                            ActivationType = cfg.ActivationType;
                            Sensitivity = cfg.Sensitivity;
                            InvertX = cfg.InvertX;
                            InvertY = cfg.InvertY;
                            RightClickOnRB = cfg.RightClickOnRB;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogError("PointerModeService: Failed to load config: {0}", ex.Message);
            }
        }
    }
}
