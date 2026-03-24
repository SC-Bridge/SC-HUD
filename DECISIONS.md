# OverLayer — Architecture Decision Records

---

## ADR-001: Embedded WebView2, not external browser window manipulation

**Decision:** Embed Microsoft WebView2 in OverLayer's own WPF window, not manipulate an external Chrome/Firefox/Edge process.

**Rationale:**
- Full control over window transparency, z-order, and focus behaviour
- No dependency on a specific browser being installed or open
- No Win32 manipulation of third-party windows (avoids UWP compatibility issues, layered window conflicts, focus wars)
- This pattern is proven end-to-end with WebView2 + WPF transparency

**Trade-off:** Loses the "use any program" generality of the external window approach. Accepted — the use case is a game companion browser, not a generic window manager.

---

## ADR-002: Single configurable URL

**Decision:** The overlay shows one configured URL. Multiple URLs / profiles are V2.

**Rationale:**
- Keeps settings UI simple
- The primary use case (Star Citizen + scbridge.app) is a single-site workflow
- Multiple profiles can be added without breaking the settings schema (just wrap `OverLayerSettings` in a list)

---

## ADR-003: Single project, no multi-layer architecture

**Decision:** One `.csproj`, all code in `src/OverLayer/`. No separate Domain/Infrastructure/Common projects.

**Rationale:**
- OverLayer is a single-purpose utility (~2000–3000 LOC total)
- Multi-project structure adds build overhead and assembly boundary complexity with no benefit at this scale
- Multi-project structure is appropriate for products with external API integrations, a database, and multiple host targets — none of which apply here

---

## ADR-004: .NET 9, upgrade to .NET 10 LTS when available

**Decision:** Target `net9.0-windows`.

**Rationale:**
- .NET 9 is the current stable release
- .NET 10 LTS is the target once released (upgrade is one line in `.csproj`)
- .NET 9 ships all required WPF/WebView2 improvements

---

## ADR-005: CsWin32 for all P/Invoke

**Decision:** Use `Microsoft.Windows.CsWin32` (source-generator-based P/Invoke) for all Win32 calls.

**Rationale:**
- Type-safe, handles `HWND` vs `IntPtr` confusion at compile time
- Automatically correct calling conventions and struct layouts
- The `NativeMethods.txt` pattern is proven and well-supported

---

## ADR-006: WPF for host app, `NotifyIcon` for tray

**Decision:** WPF application with `System.Windows.Forms.NotifyIcon` for the system tray icon (via `<UseWindowsForms>true</UseWindowsForms>`).

**Rationale:**
- WPF is required for WebView2 integration (Microsoft.Web.WebView2.Wpf)
- `NotifyIcon` avoids adding a third-party tray library for MVP
- Settings window is plain WPF (no Blazor, no MudBlazor) — the UI is 5 fields, not a component library

---

## ADR-007: `GetWindowRect` not `GetClientRect` for game bounds

**Decision:** Use `GetWindowRect` (full window including borders) to determine the game window position.

**Rationale:**
- Star Citizen runs borderless fullscreen — `GetWindowRect` and `GetClientRect` are equivalent in this case
- For windowed mode, `GetWindowRect` covers the full frame, `GetClientRect` returns client area only
- Covering the full window is the correct behaviour for the overlay

---

## ADR-008: No Velopack auto-updater in V1

**Decision:** V1 ships as a plain GitHub Releases `.zip` / `.exe`. Auto-update is V2.

**Rationale:**
- Velopack adds complexity (installer, update server, delta packaging)
- For a utility at this stage, GitHub Releases + manual update is sufficient
- Velopack can be adopted in V2 without structural changes

---

## ADR-009: App runs as invoker (no mandatory elevation)

**Decision:** Run as `asInvoker`. Elevation is optional, not required.

**Rationale:**
- OverLayer is a general-purpose overlay — most use cases don't need elevation
- The hotkey hook (`WH_KEYBOARD_LL`), WebView2, and window positioning all work without elevation
- The only degraded behaviour when non-elevated: `SetForegroundWindow` on an elevated game process (e.g. Star Citizen via EAC) silently fails — the overlay hides but focus doesn't auto-return to the game
- Users who need full Star Citizen focus integration can run OverLayer as Administrator manually
