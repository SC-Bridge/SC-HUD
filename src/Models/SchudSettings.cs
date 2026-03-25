namespace schud.Models;

using Keyboard;

public record SchudSettings
{
    public KeyboardShortcut ToggleHotkey { get; set; } = new([KeyboardKey.F3]);
    public string OverlayUrl { get; set; } = "https://scbridge.app";
    public byte OverlayOpacity { get; set; } = 230;
    public byte BackgroundOpacity { get; set; } = 13; // ~5% default
    public int WebViewWidthPct { get; set; } = 100;
    public int WebViewHeightPct { get; set; } = 100;
    public int WebViewZoomPct { get; set; } = 100;
    public bool AutoStartWithWindows { get; set; } = false;
    public Guid InstallationId { get; init; } = Guid.NewGuid();
}
