# Project Rules

## Immutable Releases & Versioning
- **Never rewrite, delete, replace, or re-release an existing GitHub Release or tag.** Once a version tag (e.g., `v1.4.0`) and its installer asset (e.g., `LegionGoCompanion-Setup-v1.4.0.exe`) have been published, that version number is permanently retired.
- **Always increment the version number** (patch/minor/major) in `HandheldCompanion/HandheldCompanion.csproj` (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`) and `HandheldCompanion.iss` (`InstallerVersion`, `MyAppVersion`) before creating any new build, installer, tag, or GitHub Release.
- Never publish or overwrite a release installer file with the same version number as an already existing release, because reusing/rewriting tags or assets breaks in-app updates, caches, and GitHub fetches.
