# OverLayer — Risks & Gotchas

---

## R1: Elevated game process blocks `SetForegroundWindow`

**Problem:** Star Citizen typically runs as Administrator. A non-elevated OverLayer cannot call `SetForegroundWindow` on an elevated window, and cannot receive `SetForegroundWindow` from the game thread.

**Mitigation:**
- Run OverLayer elevated (add `<requestedExecutionLevel level="requireAdministrator">` to the app manifest).
- Alternatively, detect at startup: if the game process is elevated and OverLayer is not, show a tray balloon warning.
- The `AttachThreadInput` + `BringWindowToTop` pattern is the reliable workaround — implement it in `ShowOverlay()`.

---

## R2: WebView2 runtime not installed

**Problem:** WebView2 requires the Edge WebView2 Runtime. It ships pre-installed on Windows 11, but not guaranteed on all Windows 10 machines.

**Mitigation:**
- Use the **Evergreen Bootstrapper** (`MicrosoftEdgeWebview2Setup.exe`): embed it as a resource and run it silently on first launch if WebView2 is not detected.
- `WebView2` WPF control throws `WebView2RuntimeNotFoundException` if missing — catch this in `OverlayWindow.Loaded` and show a helpful message with a download link.

---

## R3: WebView2 background transparency

**Problem:** Setting `DefaultBackgroundColor = Transparent` on WebView2 makes the WebView2 surface transparent, but only if the *web page itself* also has a transparent background (`background: transparent` in CSS). Sites like `scmdb.net` have their own background — you will see the site's background, not the game through it.

**Reality check:**
- This is actually fine for the use case. The user presses F3 to *use* the site, not to see the game through it.
- If partial see-through is desired, use window-level opacity (`SetLayeredWindowAttributes`) instead — this fades the *entire* overlay window including the site content.
- Full per-pixel transparency (see game through transparent areas of the site) is only achievable if the site itself has transparent backgrounds in those areas.

---

## R4: Focus captured by the game anti-cheat

**Problem:** Star Citizen's anti-cheat (EAC) monitors for injected overlays and external focus manipulation. OverLayer uses standard Win32 calls (`SetForegroundWindow`, `BringWindowToTop`) — these are not injected and are used by many legitimate overlay apps (Discord, GeForce Experience, Steam).

**Mitigation:**
- OverLayer does NOT inject code into the game process.
- OverLayer does NOT use `SetWindowsHookEx` with `WH_KEYBOARD` (which requires injection) — it uses `WH_KEYBOARD_LL` which runs out-of-process.
- These techniques are used by many legitimate overlay apps (Discord, GeForce Experience, Steam) without EAC issues.

---

## R5: Game window position changes while overlay is visible

**Problem:** If the game window is moved or resized while the overlay is visible, the overlay will be misaligned.

**Mitigation:**
- `GameWindowTracker` already raises `GameWindowPositionChanged` (debounced at 120ms).
- Subscribe to this event in `OverlayWindow` — if the overlay is visible, call `SetWindowPos` to re-align.

---

## R6: DPI scaling on multi-monitor setups

**Problem:** `GetWindowRect` returns physical pixel coordinates. If the game is on a 4K monitor at 150% DPI and the overlay window is created on a 1080p monitor at 100% DPI, `SetWindowPos` coordinates will be misinterpreted.

**Mitigation:**
- Declare the app as `PerMonitorV2` DPI aware in the manifest.
- Use `GetDpiForMonitor` to get the DPI of the game's monitor.
- WPF's default DPI handling will need to be overridden for the overlay window positioning — use physical pixel coordinates throughout, bypassing WPF's logical unit system.
- Implement a `GetDpiScaleFactor` helper using `GetDpiForMonitor`.

---

## R7: WebView2 navigation to configured URL fails

**Problem:** The configured URL may be unreachable (no internet, staging site down, etc.).

**Mitigation:**
- Handle `WebView2.NavigationCompleted` — if `IsSuccess == false`, navigate to a local HTML error page embedded in the app resources.
- Do not crash — the overlay window should still be usable, just showing an error.
- Staging environments go down. The `OverlayUrl` setting is user-configurable so switching to a production URL requires no code change.

---

## R10: OAuth / login popup windows

**Problem:** `scbridge.app` authentication may open a popup window via `window.open()` (common for OAuth providers — Google, GitHub, Discord SSO). WebView2 blocks new windows by default, silently breaking the login flow.

**Mitigation:**
- Subscribe to `CoreWebView2.NewWindowRequested`.
- Open a secondary `AuthPopupWindow` containing its own `WebView2` instance.
- Set `e.NewWindow = popupWindow.WebView.CoreWebView2` and `e.Handled = true`.
- The popup window should be styled similarly to the main overlay (topmost, no taskbar entry).
- Close the popup automatically when it navigates to a URL matching the main origin (the OAuth callback).

---

## R11: Login session lost between app updates

**Problem:** If OverLayer is uninstalled or its `%APPDATA%\OverLayer\WebView2Cache` folder is deleted, the user is logged out of `scbridge.app`.

**Mitigation:**
- This is expected behaviour — document it.
- Never delete the `WebView2Cache` folder during updates unless explicitly asked.
- V2: Add a "Clear browser data" button in Settings for intentional logout.

---

## R12: Subdomain cookies not shared

**Problem:** If `scbridge.app` sets cookies on the root domain but the auth system uses a subdomain (e.g., `auth.scbridge.app`), cookies may not be shared correctly in WebView2 if the `Domain` attribute is not set properly on the server side.

**Mitigation:**
- This is a server-side concern for `scbridge.app` — cookies intended to be shared across subdomains must set `Domain=.scbridge.app` (leading dot).
- If subdomain auth breaks in WebView2 but works in Chrome, check the cookie `Domain` attribute in DevTools.
- WebView2 supports `CoreWebView2.OpenDevToolsWindow()` — add a hidden dev option in Settings to open DevTools for debugging exactly this.

---

## R13: Staging site certificate issues

**Problem:** Staging environments often use self-signed or Let's Encrypt certificates that may not be fully trusted, or may have cert errors if the staging environment is misconfigured.

**Mitigation:**
- Handle `CoreWebView2.ServerCertificateErrorDetected` — for staging only, optionally allow the user to proceed past cert errors (with a clear warning).
- Do NOT silently ignore cert errors in production builds — this should be a visible, opt-in setting (`AllowInsecureCertificates: false` by default).

---

## R8: `WH_KEYBOARD_LL` hook timeout

**Problem:** If OverLayer's hook processing takes too long (>300ms by default on Windows), the OS will silently remove the hook. This can happen if the main thread is blocked.

**Mitigation:**
- The hook must run on a dedicated background thread with its own message loop. Do NOT process the hook on the UI thread.
- Keep the `HookProc` callback fast — only set a flag or post a message, do the actual work on another thread.
- This pattern is already correctly implemented in `GlobalKeyboardShortcutListener` — preserve it.

---

## R9: Single-instance enforcement

**Problem:** User accidentally launches OverLayer twice. Two hook listeners would fight over focus and produce unpredictable behaviour.

**Mitigation:**
- Global named mutex: `Global\OverLayer-{guid}` (generate a fixed GUID for the project).
- If mutex is already held: show a tray balloon "OverLayer is already running" and exit immediately.
- Use a global named mutex: `Global\OverLayer-{guid}` with a fixed project GUID.
