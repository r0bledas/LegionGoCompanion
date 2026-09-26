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
        public bool DetachedOnly { get; set; } = true;
        public PointerActivationButton ActivationButton { get; set; } = PointerActivationButton.RB;
        public PointerActivationType ActivationType { get; set; } = PointerActivationType.HoldToAim;
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
        public bool DetachedOnly { get; set; } = true;
        public PointerActivationButton ActivationButton { get; set; } = PointerActivationButton.RB;
        public PointerActivationType ActivationType { get; set; } = PointerActivationType.HoldToAim;
        public float Sensitivity { get; set; } = 1.5f;
        public bool InvertX { get; set; } = false;
        public bool InvertY { get; set; } = false;
        public bool RightClickOnRB { get; set; } = true;

        // Runtime states
        private bool _isToggledOn = false;
        private bool _prevButtonPressed = false;
        private bool _prevRbPressed = false;
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
                if (!lego.IsRightDetached)
                {
                    // Controller is attached/docked; release any held mouse buttons and remain dormant
                    ReleaseHeldButtons();
                    _subpixelX = 0f;
                    _subpixelY = 0f;
                    return;
                }
            }

            // 2. Determine if aiming should be active based on activation button and type
            bool isAiming = false;
            ButtonFlags buttonFlag = GetActivationButtonFlag(ActivationButton);

            if (ActivationButton == PointerActivationButton.AlwaysActive || ActivationType == PointerActivationType.AlwaysActive)
            {
                isAiming = true;
            }
            else if (buttonFlag != ButtonFlags.None)
            {
                bool buttonDown = controllerState.ButtonState[buttonFlag] || 
                    (ActivationButton == PointerActivationButton.M1 && (controllerState.ButtonState[ButtonFlags.B11] || controllerState.ButtonState[ButtonFlags.B6])) ||
                    (controllerState.ButtonState[ButtonFlags.R1] && (ActivationButton == PointerActivationButton.RB || ActivationButton == PointerActivationButton.M1));

                if (ActivationType == PointerActivationType.HoldToAim)
                {
                    isAiming = buttonDown;
                    // Swallow activation button so it doesn't trigger desktop bindings
                    if (buttonDown)
                    {
                        controllerState.ButtonState[buttonFlag] = false;
                        controllerState.ButtonState[ButtonFlags.R1] = false;
                        if (ActivationButton == PointerActivationButton.M1)
                        {
                            controllerState.ButtonState[ButtonFlags.B11] = false;
                            controllerState.ButtonState[ButtonFlags.B6] = false;
                        }
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
                    if (ActivationButton == PointerActivationButton.M1)
                    {
                        controllerState.ButtonState[ButtonFlags.B11] = false;
                        controllerState.ButtonState[ButtonFlags.B6] = false;
                    }
                }
            }

            // 3. Right Click on RB (if enabled and RB is not the activation button)
            if (RightClickOnRB && ActivationButton != PointerActivationButton.RB)
            {
                bool rbDown = controllerState.ButtonState[ButtonFlags.R1];
                if (rbDown != _prevRbPressed)
                {
                    if (rbDown)
                        MouseSimulator.MouseDown(MouseActionsType.RightButton);
                    else
                        MouseSimulator.MouseUp(MouseActionsType.RightButton);
                    _prevRbPressed = rbDown;
                }
                // Swallow RB so it doesn't trigger default desktop binding (e.g. Space)
                controllerState.ButtonState[ButtonFlags.R1] = false;
            }

            // 4. Translate Gyro to Mouse (when aiming is active)
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
                    if (mag < DEADZONE)
                    {
                        playerX = 0f;
                        playerY = 0f;
                    }

                    // Base multiplier calibrated for natural pointer speed at 60-144 Hz
                    const float BASE_SPEED = 28.0f;
                    float speedScale = BASE_SPEED * Sensitivity * delta;

                    float gyroDx = -playerY * speedScale;
                    float gyroDy = -playerX * speedScale; // pitch up (-dy) moves cursor up

                    if (InvertX) gyroDx = -gyroDx;
                    if (InvertY) gyroDy = -gyroDy;

                    _subpixelX += gyroDx;
                    _subpixelY += gyroDy;

                    int moveX = (int)_subpixelX;
                    int moveY = (int)_subpixelY;

                    _subpixelX -= moveX;
                    _subpixelY -= moveY;

                    if (moveX != 0 || moveY != 0)
                    {
                        MouseSimulator.MoveBy(moveX, moveY);
                    }
                }
            }
            else
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
            if (_prevRbPressed)
            {
                MouseSimulator.MouseUp(MouseActionsType.RightButton);
                _prevRbPressed = false;
            }
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
