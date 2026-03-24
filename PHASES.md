# OverLayer — Phased Implementation Plan

---

## Phase 1: Scaffold + Core Infrastructure

**Goal:** Compilable project with all ported infrastructure in place.

### Tasks

1. **Create solution and project**
   - `OverLayer.sln` + `src/OverLayer/OverLayer.csproj`
   - WPF, .NET 9, `win-x64`, `<UseWindowsForms>true</UseWindowsForms>`
   - `Directory.Build.props`, `Directory.Packages.props`
   - NuGet: `Microsoft.Windows.CsWin32`, `Microsoft.Web.WebView2`, `Microsoft.Extensions.Hosting`

2. **`NativeMethods.txt`** — CsWin32 symbol list
   - Symbols: `GetWindowRect`, `SetWindowPos`, `IsWindow`, `SetLayeredWindowAttributes`, `EnumWindows`, `HWND_TOPMOST`, `SWP_FRAMECHANGED`, `SWP_SHOWWINDOW`

3. **Port keyboard models**
   - `KeyboardKey.cs`, `KeyboardShortcut.cs`, `KeyboardKeyUtils.cs`
   - `WindowsKeyMap.cs`

4. **Port `ProcessExitWatcher.cs`** — verbatim copy, re-namespace

5. **`PInvokeExtensions.cs`** — Win32 helpers; include `GetWindowRect`, `IsWindow`

6. **`OverLayerSettings.cs`** — full settings record

7. **`SettingsManager.cs`** — JSON load/save to `%APPDATA%\OverLayer\settings.json`

8. **`Program.cs`** — Generic Host, single-instance mutex, `UseWPF()`

**Exit criteria:** `dotnet build` succeeds, no warnings.

---

## Phase 2: Core Services

**Goal:** Hotkey fires, game window is tracked.

### Tasks

1. **`GlobalHotkeyListener.cs`** — port from `GlobalKeyboardShortcutListener`
   - Update to read from `OverLayerSettings.ToggleHotkey`
   - Fire `HotkeyPressed` event

2. **`GameWindowTracker.cs`**
   - Replace hardcoded `CryENGINE` / `Star Citizen` with `OverLayerSettings.GameProcessName`
   - Expose `GameWindowHWnd` and `GameWindowRect` (via `GetWindowRect`, not `GetClientRect`)
   - Keep WinEvent hook thread-with-message-loop pattern intact
   - Raise: `GameWindowFound`, `GameWindowLost`, `GameWindowFocusChanged`, `GameWindowPositionChanged`

3. **`AutoStartManager.cs`** — port from `WindowsAutoStartManager`

4. **Register all as `IHostedService`** in `Program.cs`

**Exit criteria:** Add debug logging. Launch app, focus Star Citizen — log should show "Game window found". Press F3 — log should show "Hotkey pressed".

---

## Phase 3: Overlay Window (MVP)

**Goal:** F3 shows/hides a WebView2 overlay over Star Citizen.

### Tasks

1. **`OverlayWindow.xaml`**
   - `WindowStyle="None"`, `AllowsTransparency="True"`, `ShowInTaskbar="False"`, `Topmost="True"`
   - Background: `#010000` at 5% opacity
   - Extended styles: `WS_EX_TOOLWINDOW` (hides from Alt+Tab)
   - Content: `<wpf:WebView2>` control (Microsoft.Web.WebView2.Wpf)

2. **`OverlayWindow.xaml.cs`**
   - On `Loaded`: initialise WebView2, set `DefaultBackgroundColor = Transparent`, navigate to `OverLayerSettings.OverlayUrl`
   - Subscribe to `GlobalHotkeyListener.HotkeyPressed`
   - Subscribe to `GameWindowTracker` events
   - `ShowOverlay()`:
     - Get `GameWindowTracker.GameWindowRect`
     - `SetWindowPos` to match game rect exactly
     - `Visibility = Visible`
     - `BringWindowToTop` + `SetForegroundWindow`
   - `HideOverlay()`:
     - `Visibility = Collapsed`
     - `SetForegroundWindow(GameWindowTracker.GameWindowHWnd)`
   - Guard: only show if game window is currently focused

3. **`App.xaml.cs`**
   - Instantiate `OverlayWindow`, call `.Show()` (initially collapsed)

4. **`TrayIcon.cs`** (minimal)
   - `NotifyIcon` with "Exit" menu item only

**Exit criteria:** Press F3 in Star Citizen → WebView2 window appears loading `https://scbridge.app`. Press F3 → hides, game regains focus.

---

## Phase 4: Settings UI

**Goal:** User can configure URL, hotkey, game process, opacity without editing JSON.

### Tasks

1. **`SettingsWindow.xaml`** — plain WPF dialog
   - **URL** — text field (validates `Uri.TryCreate`)
   - **Game process** — text field + tooltip "e.g. StarCitizen.exe"
   - **Hotkey** — key recorder (press a key combination, displayed as text)
   - **Opacity** — slider 50–100% (maps to 127–255)
   - **Hide when game loses focus** — checkbox
   - **Start with Windows** — checkbox
   - **Save** / **Cancel** buttons

2. **Tray context menu** — add "Settings" item, opens `SettingsWindow`

3. **Settings hot-reload** — after Save, `SettingsManager` fires `SettingsChanged` event; `OverlayWindow` and services re-read relevant values on next activation

4. **Balloon tip** — if F3 pressed and game window not found: show tray balloon "OverLayer: Game not detected. Is {GameProcessName} running?"

**Exit criteria:** Open settings, change URL to a different site, press F3 — new site loads.

---

## Phase 5: Polish + Distribution

**Goal:** Shippable v1.0 build.

### Tasks

1. **Tray icon states**
   - Default: grey icon
   - Game detected: coloured icon
   - Overlay active: highlighted icon

2. **Opacity applied to WebView2 window**
   - `SetLayeredWindowAttributes` on the overlay window's HWND with `OverLayerSettings.OverlayOpacity`
   - Add `WS_EX_LAYERED` to the window's extended style

3. **DPI awareness**
   - Add `<ApplicationManifest>` with DPI-aware declaration
   - Use `GetDpiForMonitor` when computing game window rect → overlay window position

4. **Single-file publish**
   ```
   dotnet publish -r win-x64 --self-contained true -p:PublishSingleFile=true -c Release
   ```

5. **GitHub Actions CI** — build + publish artifact on tag push

6. **README.md** — installation, configuration, usage

**Exit criteria:** Single `.exe` file, installs WebView2 runtime if missing (WebView2 bootstrapper), runs cleanly on a clean Windows 11 machine.

---

## Phase 6: V2 Features (Future)

- Multiple URL profiles (named), switch via tray menu or hotkey
- Custom CSS/JS injection into WebView2 (site-specific tweaks without modifying the site)
- "Peek mode" — click-through `WS_EX_TRANSPARENT` with low opacity, see overlay without interacting
- Game detection by window title (not just process name)
- Plugin/extension model — allow third-party profile packs
