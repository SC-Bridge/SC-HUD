# SC HUD

A lightweight Windows tray application that brings an embedded browser overlay over your game with a single keypress.

Press **F3** while Star Citizen (or any configured game) is in focus → a transparent WebView2 browser window appears over the game, loading your configured URL. Press **F3** again → it disappears and the game regains focus.

---

## Use Case

Playing Star Citizen and need to check [scbridge.app](https://scbridge.app)? Press F3. The overlay loads, you're already logged in (session is persisted), check what you need, press F3 — back in the game.

User can adjust window scaling, width, hight, and opacity.

---

## Requirements

- Windows 10 / 11 (x64)
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (pre-installed on Windows 11)
- Target game must be running

---

## Configuration

Settings are stored at `%APPDATA%\schud\settings.json`. Edit via the tray icon → **Settings**.

| Setting | Default | Description |
|---|---|---|
| `ToggleHotkey` | F3 | Key combination to show/hide the overlay |
| `GameProcessName` | `StarCitizen.exe` | Process name of the game to detect |
| `OverlayUrl` | `https://scbridge.app` | URL to display in the overlay |
| `AllowedOrigins` | `["*.scbridge.app"]` | Origins the embedded browser may navigate to |
| `OverlayOpacity` | 230 (90%) | Overlay opacity (0–255) |
| `HideWhenGameLosesFocus` | true | Auto-hide overlay when game is minimised |
| `AutoStartWithWindows` | false | Launch SC HUD on Windows startup |

---

## Architecture

See [PLAN.md](PLAN.md) for full architecture.
See [PHASES.md](PHASES.md) for implementation roadmap.
See [DECISIONS.md](DECISIONS.md) for architecture decision records.
See [RISKS.md](RISKS.md) for known risks and mitigations.

---

## Running

```bash
dotnet run --project src/schud
```

Or double-click the published executable — no elevation required.

```
src\schud\bin\Release\net9.0-windows\win-x64\publish\schud.exe
```

The app starts in the system tray. Look for the tray icon to access settings or exit.

> **Star Citizen users:** Star Citizen runs elevated via EAC. For SC HUD to automatically return focus to the game after hiding the overlay, run SC HUD as Administrator. Everything else works without elevation.

---

## Building

```bash
dotnet build
dotnet publish -r win-x64 --self-contained true -p:PublishSingleFile=true -c Release
```

---

