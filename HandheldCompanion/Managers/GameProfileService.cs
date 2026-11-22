using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HandheldCompanion.Devices;
using HandheldCompanion.Devices.Lenovo;
using HandheldCompanion.Shared;

namespace HandheldCompanion.Managers
{
    public class GamePerformanceProfile
    {
        public string ProcessName { get; set; } = string.Empty;
        public int TdpWatts { get; set; } = 0;            // 0 = unchanged
        public string FanMode { get; set; } = "Auto";     // "", "Auto", "100", "70", "50"
        public int ResolutionHeight { get; set; } = 0;    // 0 = unchanged; 800, 1000, 1200, 1440, 1600
        public int RefreshRate { get; set; } = 0;         // 0 = unchanged; 60, 144
        public bool? CpuBoost { get; set; } = null;       // null = unchanged
        public int Epp { get; set; } = -1;                // -1 = unchanged; 0..100
        public int GpuClockMhz { get; set; } = -1;        // -1 = unchanged; 0 = Auto; else MHz

        public string Summary
        {
            get
            {
                var parts = new List<string>();
                if (TdpWatts > 0) parts.Add($"{TdpWatts}W");
                if (!string.IsNullOrEmpty(FanMode)) parts.Add(FanMode == "Auto" ? "Fan Auto" : $"Fan {FanMode}%");
                if (ResolutionHeight > 0) parts.Add($"{ResolutionHeight}p");
                if (RefreshRate > 0) parts.Add($"{RefreshRate}Hz");
                if (CpuBoost.HasValue) parts.Add(CpuBoost.Value ? "Boost On" : "Boost Off");
                if (Epp >= 0) parts.Add($"EPP {Epp}");
                if (GpuClockMhz == 0) parts.Add("GPU Auto");
                else if (GpuClockMhz > 0) parts.Add($"GPU {GpuClockMhz}MHz");
                return parts.Count > 0 ? string.Join("  |  ", parts) : "No overrides";
            }
        }
    }

    public class GameProfilesConfig
    {
        public bool Enabled { get; set; } = true;
        public List<GamePerformanceProfile> Profiles { get; set; } = new();
    }

    public sealed class GameProfileService
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public static GameProfileService Instance { get; } = new();

        private static readonly HashSet<string> IgnoredProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "HandheldCompanion", "LegionGoCompanion", "ApplicationFrameHost",
            "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "TextInputHost",
            "SystemSettings", "LockApp", "dwm", "csrss", "svchost", "Idle", "System",
            "Taskmgr", "cmd", "powershell", "pwsh", "conhost", "WindowsTerminal"
        };

        private readonly object _lock = new();
        private readonly string _configPath;
        private GameProfilesConfig _config = new();
        private System.Threading.Timer? _watchTimer;

        private GamePerformanceProfile? _activeProfile;
        private GamePerformanceProfile? _baselineSnapshot;

        public event Action<string?>? ActiveProfileChanged;

        public bool Enabled
        {
            get { lock (_lock) return _config.Enabled; }
            set
            {
                lock (_lock)
                {
                    _config.Enabled = value;
                    SaveLocked();
                    UpdateWatcherLocked();
                }
            }
        }

        public string? ActiveProfileName
        {
            get { lock (_lock) return _activeProfile?.ProcessName; }
        }

        private GameProfileService()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HandheldCompanion");
            Directory.CreateDirectory(dir);
            _configPath = Path.Combine(dir, "game_profiles.json");
            Load();
            lock (_lock)
            {
                UpdateWatcherLocked();
            }
        }

        public List<GamePerformanceProfile> GetProfiles()
        {
            lock (_lock)
            {
                return _config.Profiles.ToList();
            }
        }

        public void AddOrUpdateProfile(GamePerformanceProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.ProcessName))
                return;

            string cleanName = Path.GetFileNameWithoutExtension(profile.ProcessName.Trim());
            profile.ProcessName = cleanName;

            lock (_lock)
            {
                int idx = _config.Profiles.FindIndex(p => string.Equals(p.ProcessName, cleanName, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                    _config.Profiles[idx] = profile;
                else
                    _config.Profiles.Add(profile);

                SaveLocked();
                UpdateWatcherLocked();
            }
        }

        public void RemoveProfile(string processName)
        {
            lock (_lock)
            {
                _config.Profiles.RemoveAll(p => string.Equals(p.ProcessName, processName, StringComparison.OrdinalIgnoreCase));
                if (_activeProfile != null && string.Equals(_activeProfile.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
                {
                    _activeProfile = null;
                    RestoreBaselineAsync();
                }
                SaveLocked();
                UpdateWatcherLocked();
            }
        }

        public static List<string> GetRunningWindowedProcesses()
        {
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(p.ProcessName) && !IgnoredProcesses.Contains(p.ProcessName))
                        {
                            results.Add(p.ProcessName);
                        }
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }
            return results.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void UpdateWatcherLocked()
        {
            bool shouldRun = _config.Enabled && _config.Profiles.Count > 0;
            if (shouldRun && _watchTimer == null)
            {
                _watchTimer = new System.Threading.Timer(_ => PollForegroundAndActive(), null, 2500, 2500);
            }
            else if (!shouldRun && _watchTimer != null)
            {
                _watchTimer.Dispose();
                _watchTimer = null;
                if (_activeProfile != null)
                {
                    _activeProfile = null;
                    RestoreBaselineAsync();
                }
            }
        }

        private void PollForegroundAndActive()
        {
            try
            {
                GamePerformanceProfile? active;
                List<GamePerformanceProfile> profiles;
                lock (_lock)
                {
                    if (!_config.Enabled || _config.Profiles.Count == 0)
                        return;
                    active = _activeProfile;
                    profiles = _config.Profiles.ToList();
                }

                // If a profile is active, keep it active until the game process exits (so brief Alt-Tab doesn't thrash display mode)
                if (active != null)
                {
                    var running = Process.GetProcessesByName(active.ProcessName);
                    bool stillRunning = running.Length > 0;
                    foreach (var p in running) p.Dispose();

                    if (!stillRunning)
                    {
                        LogManager.LogInformation("[GameProfile] '{0}' exited; restoring desktop baseline.", active.ProcessName);
                        lock (_lock) _activeProfile = null;
                        RestoreBaselineAsync();
                        ActiveProfileChanged?.Invoke(null);
                    }
                    return;
                }

                // Check foreground window process first
                string? fgName = GetForegroundProcessName();
                if (string.IsNullOrEmpty(fgName))
                    return;

                var match = profiles.FirstOrDefault(p => string.Equals(p.ProcessName, fgName, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    LogManager.LogInformation("[GameProfile] Activating profile for '{0}': {1}", match.ProcessName, match.Summary);
                    lock (_lock)
                    {
                        _baselineSnapshot = CaptureCurrentBaseline();
                        _activeProfile = match;
                    }
                    ApplyProfileAsync(match);
                    ActiveProfileChanged?.Invoke(match.ProcessName);
                }
            }
            catch { }
        }

        private static string? GetForegroundProcessName()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return null;
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0) return null;
                using var proc = Process.GetProcessById((int)pid);
                return proc.ProcessName;
            }
            catch { return null; }
        }

        private static GamePerformanceProfile CaptureCurrentBaseline()
        {
            int savedTdp = ManagerFactory.settingsManager?.GetInt("SelectedTdp") ?? 15;
            if (savedTdp < 5 || savedTdp > 35) savedTdp = 15;

            int curResH = 0;
            int curHz = 0;
            try
            {
                var dev = ManagerFactory.multimediaManager?.PrimaryDesktop?.screen?.DeviceName ?? System.Windows.Forms.Screen.PrimaryScreen?.DeviceName;
                if (!string.IsNullOrEmpty(dev))
                {
                    var dm = MultimediaManager.GetDisplay(dev);
                    curResH = Math.Min(dm.dmPelsWidth, dm.dmPelsHeight);
                    curHz = dm.dmDisplayFrequency;
                }
            }
            catch { }

            return new GamePerformanceProfile
            {
                ProcessName = "__baseline__",
                TdpWatts = savedTdp,
                FanMode = "Auto",
                ResolutionHeight = curResH,
                RefreshRate = curHz,
                CpuBoost = PerformanceManager.GetCpuBoostEnabled(),
                Epp = (int)PerformanceManager.GetEppValue(),
                GpuClockMhz = PerformanceManager.GetManualGpuClock()
            };
        }

        private void RestoreBaselineAsync()
        {
            GamePerformanceProfile? snap;
            lock (_lock)
            {
                snap = _baselineSnapshot;
                _baselineSnapshot = null;
            }
            if (snap != null)
            {
                ApplyProfileAsync(snap);
            }
        }

        private static void ApplyProfileAsync(GamePerformanceProfile profile)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    if (profile.TdpWatts >= 5 && profile.TdpWatts <= 35)
                    {
                        PerformanceManager.SetTDP(profile.TdpWatts, immediate: true);
                    }

                    if (!string.IsNullOrEmpty(profile.FanMode) && IDevice.GetCurrent() is LegionGo legoFan)
                    {
                        try
                        {
                            if (string.Equals(profile.FanMode, "Auto", StringComparison.OrdinalIgnoreCase))
                            {
                                legoFan.SetFanFullSpeed(false);
                                legoFan.SetSmartFanMode((int)LegionGo.LegionMode.Balanced);
                            }
                            else if (profile.FanMode == "100")
                            {
                                legoFan.SetFanFullSpeed(true);
                            }
                            else if (ushort.TryParse(profile.FanMode, out ushort pct))
                            {
                                legoFan.SetFanFullSpeed(false);
                                ushort c = (ushort)Math.Clamp((int)pct, 0, 100);
                                legoFan.SetFanTable(new FanTable(new ushort[] { c, c, c, c, c, c, c, c, c, c }));
                            }
                        }
                        catch { }
                    }

                    if (profile.CpuBoost.HasValue)
                    {
                        PerformanceManager.SetCpuBoostEnabled(profile.CpuBoost.Value);
                    }

                    if (profile.Epp >= 0)
                    {
                        PerformanceManager.SetEppValue((uint)profile.Epp);
                    }

                    if (profile.GpuClockMhz >= 0)
                    {
                        PerformanceManager.SetManualGpuClock(profile.GpuClockMhz);
                    }

                    if (profile.ResolutionHeight > 0 || profile.RefreshRate > 0)
                    {
                        var mm = ManagerFactory.multimediaManager;
                        if (mm != null)
                        {
                            var dev = mm.PrimaryDesktop?.screen?.DeviceName ?? System.Windows.Forms.Screen.PrimaryScreen?.DeviceName;
                            int targetW = 0, targetH = 0;
                            int targetHz = profile.RefreshRate;

                            if (profile.ResolutionHeight > 0)
                            {
                                (targetW, targetH) = profile.ResolutionHeight switch
                                {
                                    800 => (1280, 800),
                                    1000 => (1600, 1000),
                                    1200 => (1920, 1200),
                                    1440 => (2304, 1440),
                                    1600 => (2560, 1600),
                                    _ => (0, 0)
                                };
                            }

                            if (!string.IsNullOrEmpty(dev))
                            {
                                var curDm = MultimediaManager.GetDisplay(dev);
                                if (targetW == 0 || targetH == 0)
                                {
                                    targetW = Math.Max(curDm.dmPelsWidth, curDm.dmPelsHeight);
                                    targetH = Math.Min(curDm.dmPelsWidth, curDm.dmPelsHeight);
                                }
                                if (targetHz <= 0)
                                {
                                    targetHz = curDm.dmDisplayFrequency;
                                }
                            }

                            if (targetW > 0 && targetH > 0)
                            {
                                mm.SetResolution(targetW, targetH, targetHz);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogManager.LogError("[GameProfile] Apply error: {0}", ex.Message);
                }
            });
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    string json = File.ReadAllText(_configPath);
                    var loaded = JsonSerializer.Deserialize<GameProfilesConfig>(json);
                    if (loaded != null)
                    {
                        _config = loaded;
                    }
                }
            }
            catch { }
        }

        private void SaveLocked()
        {
            try
            {
                string json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configPath, json);
            }
            catch { }
        }
    }
}
