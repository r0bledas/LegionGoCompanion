using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GregsStack.InputSimulatorStandard.Native;
using HandheldCompanion.Actions;
using HandheldCompanion.Inputs;
using HandheldCompanion.Shared;
using Newtonsoft.Json;

namespace HandheldCompanion.Managers
{
    public enum MappingType
    {
        None = 0,
        Controller = 1,
        Keyboard = 2
    }

    public class MappingItem
    {
        public ButtonFlags PhysicalButton { get; set; }
        public MappingType TargetType { get; set; } = MappingType.None;
        public ButtonFlags TargetButton { get; set; } = ButtonFlags.None;
        public VirtualKeyCode TargetKey { get; set; } = VirtualKeyCode.NONAME;
    }

    public class CustomMappingService
    {
        private static readonly Lazy<CustomMappingService> _instance = new(() => new CustomMappingService());
        public static CustomMappingService Instance => _instance.Value;

        private readonly string _filePath;
        private readonly object _lock = new();

        public static readonly ButtonFlags[] RemappableButtons = new[]
        {
            ButtonFlags.OEM2, // Legion L
            ButtonFlags.OEM1, // Legion R
            ButtonFlags.L4,   // Y1
            ButtonFlags.L5,   // Y2
            ButtonFlags.R5,   // Y3
            ButtonFlags.B11,  // M1
            ButtonFlags.B5,   // M2
            ButtonFlags.R4    // M3
        };

        public static readonly HashSet<ButtonFlags> RemappedButtons = new();

        private Dictionary<ButtonFlags, MappingItem> _mappings = new();

        public CustomMappingService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(appData, "HandheldCompanion");
            Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, "custom_mappings.json");
            Load();
        }

        public void Load()
        {
            lock (_lock)
            {
                _mappings.Clear();
                foreach (var btn in RemappableButtons)
                {
                    _mappings[btn] = new MappingItem { PhysicalButton = btn, TargetType = MappingType.None };
                }

                try
                {
                    if (File.Exists(_filePath))
                    {
                        string json = File.ReadAllText(_filePath);
                        var loaded = JsonConvert.DeserializeObject<List<MappingItem>>(json);
                        if (loaded != null)
                        {
                            foreach (var item in loaded)
                            {
                                if (_mappings.ContainsKey(item.PhysicalButton))
                                {
                                    _mappings[item.PhysicalButton] = item;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogManager.LogError("CustomMappingService: Failed to load mappings: {0}", ex.Message);
                }

                UpdateRemappedButtonsSet();
            }
        }

        public void Save()
        {
            lock (_lock)
            {
                try
                {
                    string json = JsonConvert.SerializeObject(_mappings.Values.ToList(), Formatting.Indented);
                    File.WriteAllText(_filePath, json);
                }
                catch (Exception ex)
                {
                    LogManager.LogError("CustomMappingService: Failed to save mappings: {0}", ex.Message);
                }

                UpdateRemappedButtonsSet();
            }
        }

        private void UpdateRemappedButtonsSet()
        {
            lock (RemappedButtons)
            {
                RemappedButtons.Clear();
                foreach (var kv in _mappings)
                {
                    if (kv.Value.TargetType != MappingType.None)
                    {
                        RemappedButtons.Add(kv.Key);
                    }
                }
            }
        }

        public MappingItem GetMapping(ButtonFlags button)
        {
            lock (_lock)
            {
                if (_mappings.TryGetValue(button, out var item))
                    return item;
                return new MappingItem { PhysicalButton = button, TargetType = MappingType.None };
            }
        }

        public void SetMapping(ButtonFlags button, MappingType type, ButtonFlags targetBtn, VirtualKeyCode targetKey)
        {
            lock (_lock)
            {
                _mappings[button] = new MappingItem
                {
                    PhysicalButton = button,
                    TargetType = type,
                    TargetButton = targetBtn,
                    TargetKey = targetKey
                };
            }
            Save();
            ApplyMappings();
        }

        public void ResetAll()
        {
            lock (_lock)
            {
                foreach (var btn in RemappableButtons)
                {
                    _mappings[btn] = new MappingItem { PhysicalButton = btn, TargetType = MappingType.None };
                }
            }
            Save();
            ApplyMappings();
        }

        public void ApplyMappings()
        {
            var plans = new Dictionary<ButtonFlags, IActions[]>();

            lock (_lock)
            {
                foreach (var kv in _mappings)
                {
                    var item = kv.Value;
                    if (item.TargetType == MappingType.Controller && item.TargetButton != ButtonFlags.None)
                    {
                        plans[item.PhysicalButton] = new IActions[] { new ButtonActions(item.TargetButton) };
                    }
                    else if (item.TargetType == MappingType.Keyboard && item.TargetKey != VirtualKeyCode.NONAME)
                    {
                        plans[item.PhysicalButton] = new IActions[] { new KeyboardActions(item.TargetKey) };
                    }
                }
            }

            ManagerFactory.layoutManager?.ApplyCustomMappings(plans);
        }

        public static bool IsRemapped(ButtonFlags button)
        {
            lock (RemappedButtons)
            {
                return RemappedButtons.Contains(button);
            }
        }

        public static string GetButtonDisplayName(ButtonFlags button)
        {
            return button switch
            {
                ButtonFlags.OEM2 => "Legion L (Lenovo Left)",
                ButtonFlags.OEM1 => "Legion R (Lenovo Right)",
                ButtonFlags.L4 => "Y1 (Upper Left Back)",
                ButtonFlags.L5 => "Y2 (Lower Left Back)",
                ButtonFlags.R5 => "Y3 (Lower Right Back)",
                ButtonFlags.B11 => "M1 (Top Right Back)",
                ButtonFlags.B5 => "M2 (Middle Right Back)",
                ButtonFlags.R4 => "M3 (Bottom Right Back)",
                _ => button.ToString()
            };
        }
    }
}
