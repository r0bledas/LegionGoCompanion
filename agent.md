# Antigravity Agent Guidelines & Rules

## 1. Strict Version Increment Rule (MANDATORY)
- **EVERY SINGLE TIME** a change is made to the codebase—even the slightest or smallest fix—the version **MUST BE INCREMENTED**.
- **NEVER** overwrite, reuse, or rewrite an existing version or existing installer executable.
- For every build:
  1. Increment version numbers in:
     - `HandheldCompanion/HandheldCompanion.csproj` (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`)
     - `HandheldCompanion.iss` (`#define InstallerVersion`, `#define MyAppVersion`)
  2. The generated setup executable must strictly be named with its unique version:
     `C:\Users\LLG\Downloads\LegionGoCompanion-Setup-v<Version>.exe`
  3. Never regress or break a previously stable release. Always build progressively upon verified working versions.

---

## 2. Interactive Input Calibration & Step-by-Step Guided Verification
- When configuring or debugging controller inputs, stick orientations, button triggers, or gyro motion:
  - Provide an interactive, step-by-step guided wizard that prompts the user for specific inputs (e.g., "Push Right Stick UP", "Push Right Stick DOWN", "Push Right Stick LEFT", "Push Right Stick RIGHT", "Hold RB / Aim Button", "Tilt Controller UP").
  - Read and record the exact physical axis values, offsets, and button masks directly from hardware.
  - Translate the recorded calibration directly into mouse movement and test live within the application.

---

## 3. High-DPI UI & Text Scaling Standards
- The Lenovo Legion Go features a native 2560x1600 display running at 200%–250% Windows display scaling.
- All diagnostic and standalone tools must have flawless text and UI scaling:
  - `Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);`
  - `AutoScaleMode = AutoScaleMode.Dpi;`
  - High contrast clean dark theme with bold, high-contrast headings and large legible labels (no clipped text, no tiny unreadable fonts).
  - Absolutely **ZERO emojis** anywhere in the UI, labels, buttons, or dialogs.

---

## 4. Environment & Process Hygiene
- **Downloads Folder Maintenance**:
  - The Downloads directory (`C:\Users\LLG\Downloads`) must strictly contain only:
    1. The latest installer executable (e.g. `LegionGoCompanion-Setup-v<LatestVersion>.exe`)
    2. `github-keys.txt`
    3. `HandheldCompanion-main` folder
  - Older installers must be automatically removed upon compiling and placing the new version.
- **No Rogue Background Processes**:
  - Never leave stray or unwanted GUI processes running in the background. Always ensure tasks terminate cleanly.
