# TODO

## Pending

- [ ] **SmartScreen warning on install** — unsigned MSI/exe triggers "Windows protected your PC" on first install. Fix requires code signing certificate. SignPath integration is already wired in the CI workflow (skipped until `SIGNPATH_ORGANIZATION_ID` is configured). Options: (a) purchase an EV code signing cert and configure SignPath, (b) use a cheaper OV cert via SignPath or similar, (c) document "More info → Run anyway" as a known step for early adopters until signing is in place.

- [ ] **Splash — ESC dismissal** — pressing ESC should also close the splash screen (currently only the configured toggle hotkey closes it).
- [ ] **Splash — hotkey label reflects settings** — splash currently shows "F3" hardcoded; label and dismiss behaviour should reflect whatever hotkey the user has configured in settings.

- [ ] **Test update banner** — install an older MSI release, then check for update to verify the banner appears, downloads, and restarts correctly.
- [ ] **MSI update path** — `SelfUpdateService` replaces the raw `.exe`; if installed via MSI the replacement may fail silently due to `Program Files` write permissions. Symptom: Start Menu shortcut breaks because the MSI-managed exe is replaced outside the MSI database (Windows then points the shortcut at the temp copy). Proper fix: for MSI installs, download the new MSI and run it silently (`msiexec /i ... /quiet`) instead of replacing the exe directly. Needs detection of whether running from an MSI install vs portable.

## Known Issues

- [ ] **HUD overflow on zoom >100%** — When scaling above 100%, zoomed content can extend past the lower screen boundary. CSS overflow mitigation was removed (it broke scrolling); the WebView2 HWND boundary provides some clipping but does not fully contain all cases. Proper fix: cap overlay dimensions to screen resolution before applying zoom.
