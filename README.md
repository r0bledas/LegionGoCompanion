# LegionGoCompanion

A ruthlessly optimized, stripped-down, lightweight fork of Handheld Companion built *exclusively* for the original Lenovo Legion Go. 

### Why does this exist?
The original Handheld Companion is an incredible feat of reverse engineering, but it suffers from one fatal flaw: it tries to support 30+ different handhelds across half a dozen manufacturers. This leads to massive architectural bloat, 150MB WPF UI frameworks, 3D model renderers loading on startup, and tight coupling of logic that shouldn't be entangled.

The UI became an absolute heresy of bloat and non-responsiveness.

This fork:
1. **Nukes all non-Lenovo devices:** AYANEO, GPD, ASUS, MSI, Valve, OneXPlayer—gone.
2. **Nukes the 3D models:** No more massive .obj files bloating the repository and RAM.
3. **Nukes the WPF UI:** The bloated App.xaml has been excised and replaced with a headless, ultra-lightweight WinForms base designed specifically for high-DPI (1600p) screens.
4. **Nukes the Flashbangs:** Removed annoying CMD prompts on startup. 

### Current State
- The backend logic (TDP, Fan Curves, Controllers) is completely isolated and fully compiling.
- The UI is currently being reconstructed into a lightweight, fast, no-bullshit utility.

### License
CC BY-NC-SA 4.0 - Original logic by Valkirie/HandheldCompanion. Forked for personal Legion Go optimization.
