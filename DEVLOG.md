# DEVLOG

## 2026-03-27 — v0.3.14: Fix F3 hotkey stops working after screen lock / UAC

### Bug fix

**Symptom**: After running the HUD for 30–240 minutes, pressing F3 to restore the overlay does nothing. Restarting the HUD fixes it.

**Root cause**: `GlobalHotkeyListener` tracked held keys in a `HashSet<VIRTUAL_KEY>` and matched the hotkey via `SetEquals` (exact match on key-up). Windows silently drops `WM_KEYUP` events for keys held during screen lock (Win+L), UAC elevation dialogs, Remote Desktop sessions, and other high-integrity process focus transitions. After one such event, a phantom key (e.g. `VK_LWIN`) sat in `_heldKeys` permanently — every subsequent F3 press produced `{VK_LWIN, VK_F3}` ≠ `{VK_F3}`, so the hotkey never fired.

**Fix** (`src/Services/GlobalHotkeyListener.cs`): On each `WM_KEYDOWN`, prune stale entries from `_heldKeys` using `GetAsyncKeyState` before adding the new key. Any key the OS reports as not physically held is evicted, self-healing phantom state on the very next keypress.

---

## 2026-03-25 — v0.3.9–0.3.12: MSI installer polish + MSI update path fix

### MSI installer fixes (v0.3.4–0.3.9)

**WiX 4.x AppFiles generation**
- WiX 4.0.6 does not support `<Files>` as a child of `<ComponentGroup>` (WiX v5+ feature).
- Fixed by dynamically generating `AppFiles.wxs` in CI with explicit `<Component>/<File>` per staged artifact.
- Each `<File>` gets an explicit `Id` attribute (`f_comp_*`) to prevent WiX duplicate-symbol errors when
  identical DLLs appear in multiple locations (`WebView2Loader.dll` in root and `runtimes\win-x64\native\`).
- `runtimes\` subdirectory excluded from staging; root copy is sufficient for the self-contained publish.

**Install path selection (v0.3.7)**
- Added `WixToolset.UI.wixext/4.0.5` to CI (`wix extension add` runs from inside `installer\` so the
  extension is placed where `wix build` looks: `installer\.wix\extensions\`).
- `installer.wxs` uses WiX 4 syntax: `<ui:WixUI Id="WixUI_InstallDir" InstallDirectory="INSTALLFOLDER" />`
  with `xmlns:ui="http://wixtoolset.org/schemas/v4/wxs/ui"`.

**MIT license on EULA screen (v0.3.9)**
- Added `installer/license.rtf` with MIT license text.
- Wired via `<WixVariable Id="WixUILicenseRtf" Value="license.rtf" />` (package-level, not a child of `ui:WixUI`).

### MSI update path fix (v0.3.11)

**Problem**
- `SelfUpdateService` used a PowerShell exe-swap to replace the running binary.
- This silently fails for MSI installs because `Program Files` requires elevation to write.

**Fix**
- `UpdateInfo` gains a `MsiUrl` field; `UpdateChecker` now resolves both the `-portable.exe`
  and `-setup.msi` assets from the GitHub release.
- `SelfUpdateService.ApplyAsync` detects whether the running exe is under `Program Files`
  (`IsMsiInstall()`) and branches:
  - **MSI install**: downloads `-setup.msi` to `%TEMP%\schud_update\`, runs
    `msiexec.exe /i <path> /passive /norestart` via `UseShellExecute=true` — Windows handles
    UAC elevation; `MajorUpgrade` in the WiX definition handles the clean upgrade.
  - **Portable install**: existing PowerShell exe-swap unchanged.
- `SplashWindow` now stores the full `UpdateInfo` (was just `_downloadUrl`) and passes it to `ApplyAsync`.

**Confirmed working**: install v0.3.11 MSI → update banner detects v0.3.12 → UAC prompt → passive
progress bar → app restarts from updated installation.

---

## 2026-03-25 — Release v0.3.0: auto-update + splash rework

### Changes

**Version bump**
- `schud.csproj`: `0.2.0` → `0.3.0` across `Version`, `FileVersion`, `AssemblyVersion`.

**Update checker (`src/Services/UpdateChecker.cs`)**
- New static service; calls `https://api.github.com/repos/SC-Bridge/SC-HUD/releases/latest` on startup.
- Parses `tag_name`, compares major.minor.patch against the running assembly version.
- Returns `UpdateInfo { HasUpdate, Version, DownloadUrl }` — resolves the `.exe` asset from the release.
- 5-second timeout; all exceptions swallowed silently (returns "no update").
- `User-Agent` header set to `SC-HUD/{version}` for GitHub API compliance.
- Accepts optional `ILogger?` for structured log output.

**Self-update service (`src/Services/SelfUpdateService.cs`)**
- New static service; downloads the `.exe` asset to `%TEMP%\schud_update\schud.exe`.
- Writes a PowerShell script (`update.ps1`) that: waits 2s, copies new exe over current, restarts.
- Launches the script hidden, then calls the `quit` callback to shut down the running process.
- Accepts optional `ILogger?`.

**Splash screen rework (`src/UI/SplashWindow.xaml` + `.cs`)**
- Splash no longer shown immediately on startup; it now waits for the update check to complete first.
- Update banner added in the top ~36px (below the cyan accent line, above the logo area).
  - Collapsed by default; shown only when `HasUpdate == true`.
  - Shows `v{X.Y.Z} available` label + `Update & Restart` button.
  - On click: button hides, label changes to `Downloading...`, `SelfUpdateService.ApplyAsync` runs.
  - On failure: label changes to `Update failed — try again`, Retry button re-appears.
- Click-to-close removed — splash is dismissed by hotkey only (existing behaviour unchanged).
- `ILogger<SplashWindow>` injected via constructor for structured logging.

**App startup order (`src/App.xaml.cs`)**
- Overlay now shown off-screen first so WebView2 begins loading in the background.
- `UpdateChecker.CheckAsync()` runs while WebView2 warms up.
- Splash created and shown only after the check resolves — already populated with the banner if needed.

**Fallback tag**
- `v0.2.0-pre-update-check` tag created locally before work began.

---

## 2026-03-25 — CI: GitHub Actions build workflow + MSI installer

### Added
- `.github/workflows/build.yml` — triggers on `v*` tags; runs `dotnet publish`
  (self-contained, win-x64), packages a portable `.zip` and a WiX v4 `.msi`,
  optionally signs via SignPath, uploads both to the GitHub Release.
- `installer/installer.wxs` — WiX v4 installer: installs to `Program Files\SC HUD\`,
  harvests all publish output recursively, adds Start Menu shortcut, supports
  clean major upgrades. UpgradeCode GUID `14718258-65BF-4312-AEFA-812D695AF95F` is
  fixed and must never change across versions.
- To trigger a release: `git tag vX.X.X && git push origin vX.X.X`

---

## 2026-03-25 — Release v0.2.0

### Changes

**Version bump**
- `<Version>0.2.0</Version>` added to `src/schud.csproj` (no version had been defined previously;
  `v0.1.0` tag already existed on the remote from an earlier push).
- Tagged `v0.2.0` and pushed to `https://github.com/SC-Bridge/SC-HUD`.

**Build**
- `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true -o publish`
- Output: `publish/schud.exe` (~157 MB).

---

## 2026-03-25 — Fix overlay click-through and alt-tab behaviour

### Problem
Clicking the HUD after opening it required one "warmup" click that went to the
game instead of the website.  Alt-tabbing while the HUD was open hid the HUD
but yanked Star Citizen back to the foreground, preventing a clean switch.

### Root cause
`MA_NOACTIVATE` in `WndProc` prevented the overlay from ever becoming the Win32
active window.  Chromium's browser process checks `GetActiveWindow() == hostHwnd`
on every `WM_LBUTTONDOWN` and absorbs the first click for focus establishment
when that test fails.  Nothing short of making the overlay the real Win32 active
window can bypass this check.

### Fix — Arkanis pattern (`AttachThreadInput` + `BringWindowToTop`)
- Removed `MA_NOACTIVATE` from `WndProc`.  The overlay now accepts normal Win32
  activation.
- `ShowOverlay()` calls `StealForeground()`: captures the current foreground HWND
  (SC), attaches the UI thread's input queue to SC's thread via
  `AttachThreadInput`, calls `BringWindowToTop` to make the overlay the foreground
  window, then immediately detaches.  Chrome now sees `GetActiveWindow() == overlayHwnd`
  and dispatches every click as a real action — no warmup click needed.
- `HideOverlay(restoreFocus: bool)` — two paths:
  - **F3 / ESC close** (`restoreFocus: true`): calls `SetForegroundWindow(scHwnd)`
    to hand control back to the game.
  - **Alt-tab / external focus change** (`restoreFocus: false`): does nothing —
    the alt-tab destination keeps the foreground, SC is not yanked back.
- `EnsureHidden()` uses `restoreFocus: false` (triggered by `ScAnchorService`).
- `EnsureHiddenRestoring()` uses `restoreFocus: true` (triggered by ESC key).
- `Toggle()` uses `restoreFocus: true` (triggered by hotkey F3).

### Credit
Technique discovered by studying the Arkanis overlay project
(`ArkanisCorporation/ArkanisOverlay`) which uses the same pattern.

---

## 2026-03-21 — Production prep & settings overhaul

### Changes

**Settings window — URL field removed**
- `OverlayUrl` is no longer user-editable in the UI.
- Default hardcoded to `https://scbridge.app` in `OverLayerSettings.cs`.
- All staging references scrubbed from `README.md`, `PLAN.md`, `PHASES.md`, `RISKS.md`.

**Settings window — SC_Companion dark theme**
- Full visual restyle matching SC_Companion colour palette:
  `#0d1b2a` background, `#132238` panels, `#2a4a6b` borders, `#22d3ee` accent.
- Section-based layout (CONTROLS / OVERLAY / SYSTEM) replacing flat grid.
- Custom `ControlTemplate` on buttons with rounded corners and hover/press states.
- DWM caption colour set to `#0c1a28` (5% darker than window background) via
  `DwmSetWindowAttribute(DWMWA_CAPTION_COLOR)` on `OnSourceInitialized`.

**Ctrl+Shift+D — staging toggle**
- Developer hotkey available while settings window is open.
- Swaps `OverlayUrl` between `https://scbridge.app` and `https://staging.scbridge.app`.
- Live env indicator (cyan dot = production, amber dot = staging) shown bottom-left
  of settings window next to Save/Cancel buttons.
- URL is committed to disk on Save along with all other settings.

**WebView2 restart on URL change**
- Previously, changing `OverlayUrl` via `WebView.Source =` caused a crash on
  cross-origin navigation.
- Fixed: `RestartNavigation(url)` stops any in-flight navigation via
  `CoreWebView2.Stop()`, then calls `CoreWebView2.Navigate(url)` directly.
- `_webViewReady` is set `false` during navigation and restored to `true` in
  `OnNavigationCompleted` (success path), keeping the toggle blocked until ready.

**Settings window z-order fix**
- WebView2's DirectComposition surface renders above all other windows regardless
  of `Topmost`, making the settings window unreachable when the overlay was visible.
- Fix: `OpenSettings` sets `_overlay.Topmost = false` before showing settings.
  A `Closed` handler on `SettingsWindow` restores `_overlay.Topmost = true`.
- Overlay remains visible and live-preview (opacity/size/zoom) still works.

**HUD overflow mitigation**
- At zoom >100% the zoomed content extended past the lower screen boundary.
- Injected CSS now includes `overflow:hidden`, `max-width:100vw`, `max-height:100vh`
  on `html` and `body` to clip content to the WebView2 viewport.
- Known issue: may not fully contain all cases — tracked in TODO.md.

**Initial git commit**
- `.gitignore` expanded to exclude `bin/`, `obj/`, `.idea/`, `.vs/`, `.claude/`,
  `.codereview/`.
- All 33 source files committed and pushed to `https://github.com/Diftic/OverLayer`.

---

## 2026-03-24 — Distribution build, splash screen, scroll fix, settings additions

### Changes

**Self-contained .exe build**
- Published with `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`.
- Output: `publish/schud.exe` (~165 MB). No .NET runtime required on target machine.
- WebView2 runtime must still be installed separately (system-level, already present on
  most Windows 11 machines via Edge).

**Splash screen**
- Added `SplashWindow.xaml` — shown on startup, stays visible until the user presses the
  configured toggle hotkey (F3 default) for the first time.
- Displays SCBridge logo (`Assets/logo.png`, embedded resource) and "Press F3 to show the HUD".
- Hotkey label reads from `SettingsManager` so it reflects a user-changed hotkey.
- Logo asset: `src/Assets/logo.png` (copied from OneDrive, embedded via `<Resource>` in csproj).
- DWM caption colour matches SettingsWindow (`#0c1a28`).

**Scroll fix — CSS overflow removed**
- `BuildOverlayStyleScript` previously injected `overflow:hidden` (later `overflow:clip`)
  on `html` and `body` to prevent zoom overflow. Both values disable the scroll container
  on the `html` element, breaking all page scrolling.
- Fix: removed all `overflow` and `max-width/height` rules from the injected CSS entirely.
  The WebView2 HWND boundary clips content at the screen edge without CSS assistance.

**Scroll fix — global WH_MOUSE_LL hook + JS dispatch**
- `OverlayWindow` installs a `WH_MOUSE_LL` low-level mouse hook on a dedicated background
  thread (same pattern as `GlobalHotkeyListener`).
- When `WM_MOUSEWHEEL` is intercepted while the overlay is visible, the hook dispatches
  to the WPF UI thread and calls `CoreWebView2.ExecuteScriptAsync` with a JS snippet that:
  1. Walks up the DOM from `document.elementFromPoint(x, y)` to find the nearest
     `overflow: auto/scroll` container and scrolls it.
  2. Falls back to `window.scrollBy(0, delta)` for document-level scroll.
- This bypasses Win32 focus routing (WM_MOUSEWHEEL normally goes to the focused window,
  which may be the underlying game) and requires no Chrome HWND enumeration.

**Settings icon resize**
- Settings gear icon injected via JS: size increased 6× (`216×216px`, `font-size:120px`),
  then reduced 50% (`108×108px`, `font-size:60px`). Colour changed to red. Opacity 60%.

**Background Opacity setting**
- Added `BackgroundOpacity` (byte) to `SchudSettings` — default 13 (~5%).
- Overlay window background colour changed from `#010000` to `#1a1a1a` (dark grey).
  Background brush named `OverlayBg` in XAML for programmatic opacity control.
- `ApplyBackgroundOpacity(byte)` method on `OverlayWindow` — live-updated on settings change.
- Settings window: new **Background Opacity** slider (0–100%) in OVERLAY section,
  with live preview and quadratic curve matching the HUD Opacity slider.

---

## 2026-03-24 — Alt-tab anchor fixes + tray icon

### Changes

**SC anchor — foreground hook added**
- `ScAnchorService` now installs two hooks: `EVENT_SYSTEM_FOREGROUND` (new) +
  `EVENT_SYSTEM_MINIMIZESTART/END` (existing).
- Foreground hook is the primary trigger — fires reliably when SC loses/gains foreground
  even in full-screen exclusive DirectX mode where minimize events don't fire.
- Foreground changes to own process (settings window) are silently ignored via PID check.

**Settings window — overlay parked off-screen**
- When settings opens, overlay is moved to `Left = -PrimaryScreenWidth` instead of
  only setting `Topmost = false`.
- Removes the fullscreen transparent WPF window from the visible Z-order entirely,
  preventing it from interfering with game focus transitions and Alt+Tab routing.
- WebView2 remains active while off-screen so live preview CSS callbacks still apply.
- On settings close: overlay moved back to `Left = 0` before `Topmost = true` is restored.

**Tray icon — SC-Companion branding**
- Replaced generated circle icons with embedded `Assets/icon.ico` (copied from SC-Companion).
- Active state (HUD open) applies a green tint overlay via `TintIcon`.
- Falls back to a cyan circle if the embedded resource is missing.

---

## 2026-03-24 — SC window anchor + ESC to close HUD

### Changes

**SC anchor (`ScAnchorService`)**
- New `src/Services/ScAnchorService.cs` — monitors Star Citizen window state via
  `SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS)`.
- Callback filters events by process name (`StarCitizen.exe`) using a manual `GetWindowThreadProcessId`
  DllImport and `Process.GetProcessById`.
- `GameMinimized` event hides the overlay and records whether it was visible.
- `GameRestored` event re-shows the overlay only if it was visible before minimizing.
- Must be started on the WPF UI thread — `WINEVENT_OUTOFCONTEXT` delivers callbacks via the caller's message pump.
- `OverlayWindow`: added `IsOverlayVisible` property and `EnsureVisible()` method alongside existing `EnsureHidden()`.

**ESC to close HUD (hidden feature)**
- `GlobalHotkeyListener` fires new `EscapePressed` event when ESC is the only key released.
- Key is **not suppressed** — passes through to Star Citizen normally.
- `App.xaml.cs` wires `EscapePressed → overlay.EnsureHidden`.

---

## 2026-03-24 — Project restructure: collapse src/schud/ to src/

### Changes

**Directory collapse**
- Removed redundant `src/schud/` nesting — single project, no benefit to extra level.
- All 23 source files moved to `src/` preserving full git history (renamed, not deleted).
- `schud.slnx` updated to reference `src/schud.csproj`.

---

## 2026-03-24 — Repository migrated to SC-Bridge/SC-HUD

### Changes

**Repository setup**
- Source moved to `https://github.com/SC-Bridge/SC-HUD`.
- `.gitignore` created at repo root excluding `bin/`, `obj/`, `.vs/`, `.idea/`.
- 24 source files committed and pushed (source only — no build artifacts).

**Build artifacts removed**
- `bin/` and `obj/` directories deleted from local disk.
- Included outdated `OverLayer.exe` (publish build, several patches behind)
  and all compiled dependencies — none of these belong in source control.
