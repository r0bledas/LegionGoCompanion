# Legion Go Companion - Comprehensive Project Handoff & Architecture Context

> **Target Audience**: Next AI Agent / Developer continuing the project.
> **Date**: September 2, 2026
> **Repository**: https://github.com/r0bledas/LegionGoCompanion
> **Local Workspace**: C:\Users\T450\Utilities\LegionGoCompanion

---

## 1. Project Overview & Mission

**LegionGoCompanion** is a specialized, stripped-down, performance-first fork of Handheld Companion (HC) built **exclusively** for the original **Lenovo Legion Go (Model 83E1)**.

### Core Motivation
- **The Problem with Upstream Handheld Companion**: Upstream HC is bloated with support for dozens of handheld manufacturers (ASUS ROG Ally, GPD, AYANEO, OneXPlayer, Ayn, MSI Claw, Valve Steam Deck, etc.), heavy 3D controller models/overlays, complex XAML/WPF UI dependencies, slow startup times, and flashbang console windows (`usbip.exe`).
- **The Mission**:
  1. Strip out 100% of non-Lenovo device code and 3D rendering overhead.
  2. Replace WPF/XAML with a lightweight, touch-optimized **Windows Forms (WinForms)** UI inspired by clean, zero-bloat native utilities like **OmniRDP**.
  3. Keep the rock-solid hardware and controller emulation logic (RyzenSMU TDP, Lenovo EC WMI fan/LED control, VIIPER/ViGEm virtual DS4 gyro aiming for games like Fortnite, and XInput Xbox 360).
  4. Ensure a completely silent, instant-launch experience with zero console flashbangs and clean folder structures.

---

## 2. Hardware Architecture & Reverse Engineering Specifications

### A. Device Identity
- **Manufacturer**: `LENOVO`
- **Model Code**: `83E1` (Original Lenovo Legion Go, AMD Ryzen Z1 Extreme APU)
- **Controller Bus**: Detachable USB/Bluetooth controllers via `SapientiaUsb` / `LegionGoTablet.cs` / `LegionController.cs`.

---

### B. Power (TDP) & Lenovo EC WMI Integration
TDP control requires interacting with both the AMD APU power limits and the Lenovo Embedded Controller (EC) to sync thermal profiles and power button LED colors.

1. **AMD APU Power Limits (RyzenSMU / PawnIO)**:
   - Handled via `PerformanceManager.SetTDP(double tdpWatts, bool immediate = true)`.
   - Directly sets APU Fast, Slow, and Sustained power limits via PawnIO/RyzenSMU kernel calls.

2. **Lenovo EC Power Modes & Power Button LED Color Codes**:
   - Exposed via WMI calls in `LegionGo.cs`: `SetSmartFanMode(int mode)` and `set_long_limit(int)` / `set_short_limit(int)`.
   - **Color Mapping Table**:
     | Mode Enum (`LegionMode`) | Hex Code | Wattage Range | Power Button LED Color |
     | :--- | :--- | :--- | :--- |
     | **Quiet** | `0x01` | 8W - 10W | **Blue** |
     | **Balanced** | `0x02` | 15W | **White** |
     | **Performance** | `0x03` | 25W - 30W | **Red** |
     | **Custom** | `0xFF` | 5W, 20W, 35W, 40W (Any non-standard) | **Purple** |

---

### C. Fan Control
- **EC WMI Fan Methods** in `LegionGo.cs`:
  - `SetFanTable(FanTable table)`: Writes custom temperature-to-RPM curves.
  - `SetSmartFanMode(int mode)`: Switches between auto curve and manual overrides.
  - `SetFanFullSpeed(bool enabled)`: Toggles instant 100% full fan speed (dust blow/max cooling).

---

### D. Controller & Gyro Emulation (VIIPER / ViGEmBus)
- **Emulation Modes**:
  1. **DualShock 4 (`HIDmode.DualShock4Controller`)**:
     - Uses VIIPER (`libviiper.dll` / USB/IP virtual bus).
     - Target IDs: VID `0x054C`, PID `0x05C4`.
     - Injects real-time IMU motion data (`GamepadMotion` / `MotionManager.cs`) into DS4 motion reports for gyro aiming in games like Fortnite.
  2. **Xbox 360 (`HIDmode.Xbox360Controller`)**:
     - Standard XInput target for universal PC game compatibility.
  3. **Direct Passthrough (`HIDmode.NoController`)**:
     - Disconnects virtual controller targets.
     - Calls `LegionGo.SetPassthrough(true)` so the physical Legion Go controllers communicate directly with Windows.
- **Connect / Disconnect Sound FX**:
  - `SystemSounds.Asterisk` plays on detaching the previous virtual controller.
  - `SystemSounds.Exclamation` plays upon successful attachment.

---

### E. Anti-Flashbang Startup (Silent USB/IP)
- `libviiper.dll` attaches virtual USB devices by spawning `usbip.exe`.
- In `Program.cs`, a high-speed background thread (`StartWindowSuppressor()`) runs for the first 15 seconds of startup, intercepting any console window allocated to `usbip.exe` or `cmd.exe` and calling `ShowWindow(hWnd, SW_HIDE)` before it draws on screen.

---

## 3. UI Design Principles & Architecture

The user explicitly requested that all WPF/XAML code be removed and replaced with a clean, touch-first WinForms interface inspired by **OmniRDP**:
- **Background**: Clean White (`#FFFFFF`).
- **Sidebar**: Light Gray (`#F0F0F0`) with simple navigation buttons.
- **Controls**: **NO SLIDERS**, **NO CHECKBOXES**, **NO SMALL DROPDOWNS**.
- **Touch Button Grid**: Giant touch cards (`210x100` px) with bold 13pt Segoe UI text, gray inactive borders (`#D2D2D2`), and bold blue active highlights (`#0066CC`).
- **Text Layout**: Generous padding to ensure no text collisions or clipping.

### Panel Layout:
1. **`TdpPowerView.cs`**:
   - Giant buttons for `5W`, `10W`, `15W`, `20W`, `25W`, `30W`, `35W`, `40W`.
2. **`FanControlView.cs`**:
   - Giant buttons for `AUTO / BALANCED`, `FULL SPEED (100%)`, `30%`, `50%`, `70%`, `85%`.
3. **`ControllerView.cs`**:
   - Giant buttons for `DUALSHOCK 4 (Gyro/Fortnite)`, `XBOX 360 (XInput)`, and `PASSTHROUGH (Native)`.
4. **`SettingsView.cs`**:
   - Giant buttons for `BATTERY BYPASS (80% Cap)`, `OPEN LOGS FOLDER`, and `RESTART COMPANION`.

---

## 4. Release History & Version Log

| Version | Key Changes & Milestones |
| :--- | :--- |
| **v0.1.0-alpha** | Initial fork stripped down to Lenovo Legion Go (`83E1`) only; deleted all 3D models and non-Lenovo device classes. |
| **v0.1.1-alpha** | Fixed UAC Referral error by setting `uiAccess="false"` in `RELEASE.manifest`. |
| **v0.1.2-alpha** | Configured `LogManager` logging paths and added global unhandled exception dialogs in `Program.cs`. |
| **v0.1.3-alpha** | Exposed hardware backends for live TDP application via RyzenSMU/PawnIO. |
| **v0.1.4-alpha** | Exposed WMI fan controls (`SetFanTable`, `SetSmartFanMode`) and Lenovo EC power limits. |
| **v0.1.5-alpha** | Redesigned UI to clean white theme; nuked sliders; built initial Controller and Settings panels. |
| **v0.1.6-alpha** | Added exact Lenovo Legion Go OEM LED color mapping (Quiet=Blue, Balanced=White, Performance=Red, Custom=Purple). |
| **v0.1.7-alpha** | Implemented Win32 `ShowWindow(SW_HIDE)` startup suppressor to eliminate `usbip` console flashbangs. |
| **v0.1.8-alpha** | Re-architected release packaging: all DLLs placed in `bin/` with `Launch Legion Go Companion.bat` at root for zero-scroll launching. |
| **v0.1.9-alpha** | Enhanced `VirtualManager` state transitions to return `Task<bool>` and wired robust error handling in `ControllerView.cs`. |

---

## 5. Build, Packaging & Release Instructions

### Build Command:
```powershell
cd C:\Users\T450\Utilities\LegionGoCompanion
dotnet build HandheldCompanion.sln -c Release -p:Platform=x64 -maxcpucount:1
```

### Packaging into Clean Zero-Scroll Zip:
```powershell
$tempDir = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), [System.Guid]::NewGuid().ToString())
$binDir = "$tempDir\bin"
New-Item -ItemType Directory -Path $binDir -Force

Copy-Item -Path "C:\Users\T450\Utilities\LegionGoCompanion\bin\Release\net10.0-windows10.0.19041.0\win-x64\*" -Destination $binDir -Recurse -Force

$launcherBat = @"
@echo off
start "" "%~dp0bin\HandheldCompanion.exe"
"@
Set-Content "$tempDir\Launch Legion Go Companion.bat" $launcherBat

$zipPath = "C:\Users\T450\Utilities\LegionGoCompanion\LegionGoCompanion-v<VERSION>.zip"
Compress-Archive -Path "$tempDir\*" -DestinationPath $zipPath -Force
```

---

## 6. Future Roadmap & Pending Feature Objectives

1. **Desktop Mouse Mode Layout**:
   - Map Legion Go right stick / trackpad to Windows virtual mouse cursor and triggers to Left/Right click when Desktop mode is activated.
2. **OSD (On-Screen Display) Overlay in WinForms/GDI+**:
   - Create a lightweight transparent overlay window for in-game stats (FPS, TDP, battery percentage, CPU/GPU temps).
3. **Quick Access Sidebar / Hotkey Trigger**:
   - Wire Legion L / Legion R physical hotkeys to toggle the companion window or a compact flyout menu.
4. **Auto-TDP / Dynamic Battery Profiles**:
   - Optional profile switcher based on AC vs. Battery power state.

---
*Generated and verified from workspace `C:\Users\T450\Utilities\LegionGoCompanion`.*
