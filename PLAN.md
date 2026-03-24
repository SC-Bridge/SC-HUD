# OverLayer — Project Plan

> A universal game overlay that embeds a Chromium browser (WebView2) and displays a configurable URL as a transparent, always-on-top overlay when a hotkey is pressed.

---

## Concept

OverLayer is a small Windows tray application. When the user presses a configurable hotkey (default: F3) while a target game is in focus, a transparent frameless window containing an embedded WebView2 browser appears over the game. The window displays a configured URL (default: `https://scbridge.app`). Pressing F3 again hides it and returns focus to the game.

**Key design decisions:**
- **Embedded WebView2** (not an external browser) — full control over transparency, rendering, and focus
- **Single configurable URL** — the overlay is purpose-built for one site
- **Transparent overlay window** — the game is visible underneath
- **Proven infrastructure** — keyboard hook, game window tracker, Win32 helpers

---

## Architecture

```
OverLayer (WPF, .NET 8, single project)
│
├── Program.cs                    Generic Host bootstrap, single-instance mutex
├── App.xaml / App.xaml.cs        WPF Application lifecycle
├── Constants.cs                  App name, mutex GUID, paths
│
├── Models/
│   ├── Keyboard/
│   │   ├── KeyboardKey.cs
│   │   ├── KeyboardShortcut.cs
│   │   └── KeyboardKeyUtils.cs
│   └── OverLayerSettings.cs      Root settings record
│
├── Settings/
│   └── SettingsManager.cs        JSON load/save to %APPDATA%\OverLayer\settings.json
│
├── PInvoke/
│   ├── PInvokeExtensions.cs      Win32 helpers
│   ├── WindowsKeyMap.cs          Key → VIRTUAL_KEY mapping
│   └── NativeMethods.txt         CsWin32 symbol declarations
│
├── Services/
│   ├── GlobalHotkeyListener.cs   WH_KEYBOARD_LL hook (ported)
│   ├── GameWindowTracker.cs      Configurable game window detection (adapted)
│   └── AutoStartManager.cs       Windows registry auto-start (ported)
│
├── UI/
│   ├── OverlayWindow.xaml        Transparent frameless WebView2 window
│   ├── OverlayWindow.xaml.cs     F3 toggle logic, focus management
│   ├── SettingsWindow.xaml       Settings dialog
│   ├── SettingsWindow.xaml.cs
│   └── TrayIcon.cs               System tray icon + context menu
│
└── Workers/
    └── ProcessExitWatcher.cs     (ported verbatim)
```

---

## Settings Model

```csharp
public record OverLayerSettings
{
    // Hotkey to toggle the overlay
    public KeyboardShortcut ToggleHotkey { get; set; } = new([KeyboardKey.F3]);

    // Game window to detect (overlay only activates when this is in focus)
    public string GameProcessName { get; set; } = "StarCitizen.exe";

    // Origin URL — the embedded browser starts here and may navigate to subdomains
    public string OverlayUrl { get; set; } = "https://scbridge.app";

    // Allowed navigation origins — subdomains and auth redirects the browser is permitted to follow
    // Wildcards supported: "*.scbridge.app" matches all subdomains
    public List<string> AllowedOrigins { get; set; } = ["*.scbridge.app", "scbridge.app"];

    // Overlay appearance
    public byte OverlayOpacity { get; set; } = 230;       // 0–255, default ~90%
    public bool BlurBackground { get; set; } = false;

    // Behaviour
    public bool AutoStartWithWindows { get; set; } = false;
    public bool HideWhenGameLosesFocus { get; set; } = true;

    // Internal
    public Guid InstallationId { get; init; } = Guid.NewGuid();
}
```

---

## Overlay Window Mechanics

The `OverlayWindow` is a WPF window with:
- `WindowStyle="None"` — frameless
- `AllowsTransparency="True"` — WPF transparency pipeline
- `ShowInTaskbar="False"` — hidden from taskbar
- `Topmost="True"` — always above the game
- Background: near-transparent (`#010000` at 5% opacity) for WPF hit-testing
- `WS_EX_TOOLWINDOW` — hides from Alt+Tab

Inside: a direct `WebView2` control (Microsoft.Web.WebView2.Wpf) pointing at `OverLayerSettings.OverlayUrl`.

WebView2 transparency:
```csharp
webView.DefaultBackgroundColor = Color.Transparent;
```

### WebView2 Profile & Data Persistence

WebView2 uses a persistent user data folder so that login sessions, cookies, and local storage survive across overlay show/hide cycles and app restarts:

```csharp
var env = await CoreWebView2Environment.CreateAsync(
    userDataFolder: Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OverLayer", "WebView2Cache"));
await webView.EnsureCoreWebView2Async(env);
```

This means: log in once to `scbridge.app` → stay logged in permanently (until the user clears the cache or logs out via the site).

### Subdomain Navigation

WebView2 follows navigation to subdomains freely — `*.scbridge.app` navigates work out of the box with no extra configuration. However, OverLayer subscribes to `NavigationStarting` to optionally block navigation to unrelated domains (prevents accidental link-following to external sites while the overlay is open):

```csharp
webView.NavigationStarting += (s, e) =>
{
    var uri = new Uri(e.Uri);
    if (!IsAllowedOrigin(uri, _settings.AllowedOrigins))
    {
        e.Cancel = true;
        // Open in default browser instead
        Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true });
    }
};
```

### Login Flow Considerations

`scbridge.app` uses authentication. WebView2 handles standard flows natively:

| Auth type | Behaviour in WebView2 |
|---|---|
| Session cookies | Persisted in user data folder — login survives restarts ✅ |
| OAuth 2.0 (redirect flow) | Followed automatically within the same WebView2 instance ✅ |
| OAuth popup window | WebView2 opens a new window — must be handled (see below) |
| SSO (same-domain) | Works — cookies are shared across `*.scbridge.app` subdomains ✅ |

**OAuth popup handling** — if the login flow opens a popup (`window.open`), WebView2 fires `NewWindowRequested`. By default this is blocked. We handle it by opening the popup in a secondary `WebView2` window:

```csharp
webView.CoreWebView2.NewWindowRequested += (s, e) =>
{
    var popupWindow = new AuthPopupWindow(e.Uri);
    e.NewWindow = popupWindow.WebView.CoreWebView2;
    e.Handled = true;
    popupWindow.Show();
};
```

On show: position and resize to exactly match the game window rect (`GetWindowRect`), then `BringWindowToTop` + `SetForegroundWindow`.

On hide: `Visibility = Collapsed`, then `SetForegroundWindow(gameHWnd)`.

---

## F3 Toggle Flow

```
F3 key press (game must be in focus)
    │
    ▼
GlobalHotkeyListener fires ConfiguredHotKeyPressed
    │
    ▼
OverlayWindow.OnHotkeyPressed()
    │
    ├─ If hidden → ShowOverlay()
    │     ├─ Resize/reposition to cover game window
    │     ├─ Set Visibility = Visible
    │     ├─ BringWindowToTop + SetForegroundWindow
    │     └─ Navigate WebView2 to OverlayUrl (if not already loaded)
    │
    └─ If visible → HideOverlay()
          ├─ Set Visibility = Collapsed
          └─ SetForegroundWindow(gameHWnd)
```

---

## What Is Out of Scope

- Blazor/MudBlazor UI — plain WPF settings window instead
- EF Core / SQLite / Quartz — no database or scheduling needed
- External API clients — OverLayer is a shell; the site handles its own data
- Analytics / telemetry — not included
- Multi-window HUD setup — single overlay window only
- Velopack auto-updater — plain GitHub Releases zip for v1

---

## Out of Scope (V1)

- Multiple profiles / multiple URLs
- Custom CSS injection into the WebView2
- "Peek mode" (click-through with partial opacity)
- UWP / Store app targets
- Non-Windows platforms
