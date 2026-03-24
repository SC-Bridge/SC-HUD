# DEVLOG

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
