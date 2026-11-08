using System;
using System.Drawing;
using System.IO;
using System.Media;
using System.Threading.Tasks;
using HandheldCompanion.Devices;
using HandheldCompanion.Devices.Lenovo;
using HandheldCompanion.Shared;
using Newtonsoft.Json;

namespace HandheldCompanion.Managers
{
    public class JoystickLedConfig
    {
        public bool Enabled { get; set; } = true;
        public LegionGo.RgbMode Effect { get; set; } = LegionGo.RgbMode.Solid;
        public int Brightness { get; set; } = 80;
        public int Speed { get; set; } = 50;
        public byte LeftR { get; set; } = 0;
        public byte LeftG { get; set; } = 120;
        public byte LeftB { get; set; } = 215;
        public byte RightR { get; set; } = 0;
        public byte RightG { get; set; } = 120;
        public byte RightB { get; set; } = 215;
        public bool SyncLeftRight { get; set; } = true;
    }

    public class JoystickLedService
    {
        private static readonly Lazy<JoystickLedService> _instance = new(() => new JoystickLedService());
        public static JoystickLedService Instance => _instance.Value;

        private readonly string _filePath;
        private readonly object _lock = new();

        public bool Enabled { get; private set; } = true;
        public LegionGo.RgbMode Effect { get; private set; } = LegionGo.RgbMode.Solid;
        public int Brightness { get; private set; } = 80;
        public int Speed { get; private set; } = 50;
        public byte LeftR { get; private set; } = 0;
        public byte LeftG { get; private set; } = 120;
        public byte LeftB { get; private set; } = 215;
        public byte RightR { get; private set; } = 0;
        public byte RightG { get; private set; } = 120;
        public byte RightB { get; private set; } = 215;
        public bool SyncLeftRight { get; private set; } = true;

        public event Action? ConfigChanged;

        public Color LeftColor => Color.FromArgb(LeftR, LeftG, LeftB);
        public Color RightColor => SyncLeftRight ? Color.FromArgb(LeftR, LeftG, LeftB) : Color.FromArgb(RightR, RightG, RightB);

        private JoystickLedService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(appData, "HandheldCompanion");
            Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, "joystick_led.json");
            LoadConfig();
        }

        public void LoadConfig()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_filePath))
                    {
                        string json = File.ReadAllText(_filePath);
                        var cfg = JsonConvert.DeserializeObject<JoystickLedConfig>(json);
                        if (cfg != null)
                        {
                            Enabled = cfg.Enabled;
                            Effect = Enum.IsDefined(typeof(LegionGo.RgbMode), cfg.Effect) ? cfg.Effect : LegionGo.RgbMode.Solid;
                            Brightness = Math.Clamp(cfg.Brightness, 0, 100);
                            Speed = Math.Clamp(cfg.Speed, 0, 100);
                            LeftR = cfg.LeftR;
                            LeftG = cfg.LeftG;
                            LeftB = cfg.LeftB;
                            RightR = cfg.RightR;
                            RightG = cfg.RightG;
                            RightB = cfg.RightB;
                            SyncLeftRight = cfg.SyncLeftRight;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogManager.LogError("JoystickLedService: Failed to load config: {0}", ex.Message);
                }
            }
        }

        public void SaveConfig()
        {
            lock (_lock)
            {
                try
                {
                    var cfg = new JoystickLedConfig
                    {
                        Enabled = Enabled,
                        Effect = Effect,
                        Brightness = Brightness,
                        Speed = Speed,
                        LeftR = LeftR,
                        LeftG = LeftG,
                        LeftB = LeftB,
                        RightR = RightR,
                        RightG = RightG,
                        RightB = RightB,
                        SyncLeftRight = SyncLeftRight
                    };
                    string json = JsonConvert.SerializeObject(cfg, Formatting.Indented);
                    File.WriteAllText(_filePath, json);
                }
                catch (Exception ex)
                {
                    LogManager.LogError("JoystickLedService: Failed to save config: {0}", ex.Message);
                }
            }
        }

        public void SetEnabled(bool enabled)
        {
            lock (_lock)
            {
                Enabled = enabled;
            }
            SaveConfig();
            ApplyToHardware();
            ConfigChanged?.Invoke();
        }

        public void ToggleEnabled()
        {
            bool next;
            lock (_lock)
            {
                Enabled = !Enabled;
                next = Enabled;
            }
            SaveConfig();
            ApplyToHardware();
            ConfigChanged?.Invoke();
            try
            {
                if (next)
                    SystemSounds.Exclamation.Play();
                else
                    SystemSounds.Asterisk.Play();
            }
            catch { }
        }

        public void SetEffect(LegionGo.RgbMode effect)
        {
            lock (_lock)
            {
                Effect = Enum.IsDefined(typeof(LegionGo.RgbMode), effect) ? effect : LegionGo.RgbMode.Solid;
                if (!Enabled) Enabled = true;
            }
            SaveConfig();
            ApplyToHardware();
            ConfigChanged?.Invoke();
        }

        public void CycleEffect()
        {
            lock (_lock)
            {
                if (!Enabled)
                {
                    Enabled = true;
                }
                else
                {
                    Effect = Effect switch
                    {
                        LegionGo.RgbMode.Solid => LegionGo.RgbMode.Pulse,
                        LegionGo.RgbMode.Pulse => LegionGo.RgbMode.Dynamic,
                        LegionGo.RgbMode.Dynamic => LegionGo.RgbMode.Spiral,
                        _ => LegionGo.RgbMode.Solid
                    };
                }
            }
            SaveConfig();
            ApplyToHardware();
            ConfigChanged?.Invoke();
            try { SystemSounds.Exclamation.Play(); } catch { }
        }

        public void SetColors(Color left, Color right, bool syncLeftRight)
        {
            lock (_lock)
            {
                SyncLeftRight = syncLeftRight;
                LeftR = left.R;
                LeftG = left.G;
                LeftB = left.B;
                if (syncLeftRight)
                {
                    RightR = left.R;
                    RightG = left.G;
                    RightB = left.B;
                }
                else
                {
                    RightR = right.R;
                    RightG = right.G;
                    RightB = right.B;
                }
                if (!Enabled) Enabled = true;
            }
            SaveConfig();
            ApplyToHardware();
            ConfigChanged?.Invoke();
        }

        public void SetLeftColor(Color left)
        {
            lock (_lock)
            {
                LeftR = left.R;
                LeftG = left.G;
                LeftB = left.B;
                if (SyncLeftRight)
                {
                    RightR = left.R;
                    RightG = left.G;
                    RightB = left.B;
                }
                if (!Enabled) Enabled = true;
            }
            SaveConfig();
            ApplyToHardware();
            ConfigChanged?.Invoke();
        }

        public void SetRightColor(Color right)
        {
            lock (_lock)
            {
                RightR = right.R;
                RightG = right.G;
                RightB = right.B;
                if (!Enabled) Enabled = true;
            }
            SaveConfig();
            ApplyToHardware();
            ConfigChanged?.Invoke();
        }

        public void SetSyncLeftRight(bool sync)
        {
            lock (_lock)
            {
                SyncLeftRight = sync;
                if (sync)
                {
                    RightR = LeftR;
                    RightG = LeftG;
                    RightB = LeftB;
                }
            }
            SaveConfig();
            ApplyToHardware();
            ConfigChanged?.Invoke();
        }

        public void SetBrightness(int brightness)
        {
            lock (_lock)
            {
                Brightness = Math.Clamp(brightness, 0, 100);
            }
            SaveConfig();
            ApplyToHardware();
            ConfigChanged?.Invoke();
        }

        public void SetSpeed(int speed)
        {
            lock (_lock)
            {
                Speed = Math.Clamp(speed, 0, 100);
            }
            SaveConfig();
            ApplyToHardware();
            ConfigChanged?.Invoke();
        }

        public void ApplyToHardware()
        {
            bool enabled;
            LegionGo.RgbMode effect;
            int brightness;
            int speed;
            byte lR, lG, lB, rR, rG, rB;

            lock (_lock)
            {
                enabled = Enabled;
                effect = Effect;
                brightness = Brightness;
                speed = Speed;
                lR = LeftR;
                lG = LeftG;
                lB = LeftB;
                rR = SyncLeftRight ? LeftR : RightR;
                rG = SyncLeftRight ? LeftG : RightG;
                rB = SyncLeftRight ? LeftB : RightB;
            }

            _ = Task.Run(() =>
            {
                try
                {
                    if (IDevice.GetCurrent() is LegionGoTablet tablet)
                    {
                        tablet.ApplyJoystickLedConfig(enabled, effect, lR, lG, lB, rR, rG, rB, brightness, speed);
                    }
                    else
                    {
                        LegionGoTablet.WriteStandaloneJoystickLedConfig(enabled, effect, lR, lG, lB, rR, rG, rB, brightness, speed);
                    }
                }
                catch (Exception ex)
                {
                    LogManager.LogError("JoystickLedService: ApplyToHardware error: {0}", ex.Message);
                }
            });
        }

        public static string GetEffectDisplayName(LegionGo.RgbMode mode)
        {
            return mode switch
            {
                LegionGo.RgbMode.Solid => "Solid Color",
                LegionGo.RgbMode.Pulse => "Pulse (Breathing)",
                LegionGo.RgbMode.Dynamic => "Dynamic (Rainbow)",
                LegionGo.RgbMode.Spiral => "Spiral (Wheel)",
                _ => "Solid Color"
            };
        }
    }
}
