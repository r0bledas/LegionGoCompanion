# LegionGoCompanion (The Anti-Bloat Crusade)

A ruthlessly optimized, stripped-down, bare-metal fork of Handheld Companion built **exclusively** for the original Lenovo Legion Go. 

### The Rant (Why this exists)
The original Handheld Companion is an incredible feat of reverse engineering, but holy shit, the UI is an absolute heresy of bloatware and non-responsiveness. 

Why the fuck does a background utility tool need a 150MB WPF UI framework? Why is it loading massive 3D .obj models of 30 different devices into memory on startup just so I can change my TDP? I don't give a single flying fuck about the AYANEO, the MSI Claw, or the GPD Win. I own a Legion Go. I want my TDP changed, I want my fan curve set, and I want the app to get the hell out of my way.

The original codebase is a monolithic enterprise nightmare. It's tightly coupled spaghetti trying to be the "one-size-fits-all" solution for half a dozen manufacturers, resulting in an app that takes forever to open and eats RAM like it's a AAA game.

This fork says **fuck that.**

### What we nuked from orbit:
1. **Nuked all non-Lenovo devices:** AYANEO, GPD, ASUS, MSI, Valve, OneXPlayer—gone. Dead. Burned.
2. **Nuked the 3D models:** No more massive .obj files bloating the repository. You know what your device looks like. You don't need a spinning 3D render of it.
3. **Nuked the WPF UI:** The bloated App.xaml has been excised. We replaced it with a headless, ultra-lightweight, blazing-fast WinForms base. No acrylic blur, no animations, no bullshit. It opens instantly.
4. **Nuked the Flashbangs:** Removed those annoying CMD prompts that pop up on startup and blind you. 

### Current State
- The backend logic (TDP, Fan Curves, Controllers) is completely isolated.
- The UI is being reconstructed into a lightweight, fast, no-bullshit utility that actually respects your system resources.

### License
CC BY-NC-SA 4.0 - Original backend logic by Valkirie/HandheldCompanion. Forked for personal Legion Go optimization and UI redemption.
