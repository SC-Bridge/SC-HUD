namespace schud.UI;

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Models;
using Models.Keyboard;
using Settings;

public partial class SettingsWindow : Window
{
    private const string ProdUrl    = "https://scbridge.app";
    private const string StagingUrl = "https://staging.scbridge.app";

    private readonly SettingsManager _settings;
    private readonly Action<byte>? _opacityPreview;
    private readonly Action<byte>? _bgOpacityPreview;
    private readonly Action<int, int>? _sizePreview;
    private readonly Action<int>? _zoomPreview;

    private readonly HashSet<KeyboardKey> _heldKeys = [];
    private KeyboardShortcut _recordedShortcut = KeyboardShortcut.None;
    private string _pendingUrl = ProdUrl;

    public SettingsWindow(SettingsManager settings, Action<byte>? opacityPreview = null, Action<byte>? bgOpacityPreview = null, Action<int, int>? sizePreview = null, Action<int>? zoomPreview = null)
    {
        _settings = settings;
        _opacityPreview = opacityPreview;
        _bgOpacityPreview = bgOpacityPreview;
        _sizePreview = sizePreview;
        _zoomPreview = zoomPreview;
        InitializeComponent();
        LoadCurrentSettings();
    }

    // -------------------------------------------------------------------------
    // Initialise controls from current settings
    // -------------------------------------------------------------------------

    private void LoadCurrentSettings()
    {
        var current = _settings.Current;

        _pendingUrl = current.OverlayUrl;
        UpdateEnvIndicator();

        _recordedShortcut = current.ToggleHotkey.Copy();
        HotkeyBox.Text = _recordedShortcut.ToString();

        // Invert the quadratic curve: opacity_linear = (slider/100)^2
        // so slider = sqrt(opacity_linear) * 100
        var linear = current.OverlayOpacity / 255.0;
        var pct = (int)Math.Round(Math.Sqrt(linear) * 100);
        OpacitySlider.Value = Math.Clamp(pct, 10, 100);
        OpacityLabel.Text = $"{(int)OpacitySlider.Value}%";

        var bgLinear = current.BackgroundOpacity / 255.0;
        var bgPct = (int)Math.Round(Math.Sqrt(bgLinear) * 100);
        BgOpacitySlider.Value = Math.Clamp(bgPct, 0, 100);
        BgOpacityLabel.Text = $"{(int)BgOpacitySlider.Value}%";

        WidthSlider.Value  = Math.Clamp(current.WebViewWidthPct,  10, 100);
        HeightSlider.Value = Math.Clamp(current.WebViewHeightPct, 10, 100);
        WidthLabel.Text    = $"{(int)WidthSlider.Value}%";
        HeightLabel.Text   = $"{(int)HeightSlider.Value}%";

        ZoomSlider.Value = Math.Clamp(current.WebViewZoomPct, 50, 150);
        ZoomLabel.Text   = $"{(int)ZoomSlider.Value}%";

        AutoStartBox.IsChecked = current.AutoStartWithWindows;
    }

    // -------------------------------------------------------------------------
    // Hotkey recorder
    // -------------------------------------------------------------------------

    private void OnHotkeyBoxKeyDown(object sender, KeyEventArgs e)
    {
        // Let Tab navigate normally
        var rawKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (rawKey == Key.Tab) return;

        e.Handled = true;

        var key = WpfKeyMapper.ToKeyboardKey(rawKey);
        if (key == KeyboardKey.Unknown) return;

        _heldKeys.Add(key);
        _recordedShortcut = new KeyboardShortcut(_heldKeys);
        HotkeyBox.Text = _recordedShortcut.ToString();
    }

    private void OnHotkeyBoxKeyUp(object sender, KeyEventArgs e)
    {
        var rawKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (rawKey == Key.Tab) return;

        e.Handled = true;

        var key = WpfKeyMapper.ToKeyboardKey(rawKey);
        _heldKeys.Remove(key);
        // Shortcut stays displayed; _recordedShortcut is the finalized value
    }

    // -------------------------------------------------------------------------
    // Opacity slider
    // -------------------------------------------------------------------------

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityLabel is null) return;
        OpacityLabel.Text = $"{(int)e.NewValue}%";

        // Live preview — same quadratic curve as OnSave
        var linear = Math.Pow(e.NewValue / 100.0, 2);
        _opacityPreview?.Invoke((byte)Math.Round(linear * 255));
    }

    private void OnBgOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BgOpacityLabel is null) return;
        BgOpacityLabel.Text = $"{(int)e.NewValue}%";

        var linear = Math.Pow(e.NewValue / 100.0, 2);
        _bgOpacityPreview?.Invoke((byte)Math.Round(linear * 255));
    }

    // -------------------------------------------------------------------------
    // Size sliders
    // -------------------------------------------------------------------------

    private void OnWidthChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (WidthLabel is null) return;
        WidthLabel.Text = $"{(int)e.NewValue}%";
        _sizePreview?.Invoke((int)WidthSlider.Value, (int)HeightSlider.Value);
    }

    private void OnHeightChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (HeightLabel is null) return;
        HeightLabel.Text = $"{(int)e.NewValue}%";
        _sizePreview?.Invoke((int)WidthSlider.Value, (int)HeightSlider.Value);
    }

    private void OnZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ZoomLabel is null) return;
        ZoomLabel.Text = $"{(int)e.NewValue}%";
        _zoomPreview?.Invoke((int)e.NewValue);
    }

    // -------------------------------------------------------------------------
    // Save / Cancel
    // -------------------------------------------------------------------------

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!_recordedShortcut.IsValid)
        {
            MessageBox.Show(
                "The hotkey combination is not valid. Use a function key, or a modifier + another key.",
                "Invalid Hotkey",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            HotkeyBox.Focus();
            return;
        }

        // Quadratic curve: slider % → perceived linear opacity
        // linear = (slider/100)^2  so 50% slider ≈ 25% actual opacity
        var linear = Math.Pow(OpacitySlider.Value / 100.0, 2);
        var opacityByte = (byte)Math.Round(linear * 255);

        var bgLinear = Math.Pow(BgOpacitySlider.Value / 100.0, 2);
        var bgOpacityByte = (byte)Math.Round(bgLinear * 255);

        var updated = _settings.Current with
        {
            OverlayUrl   = _pendingUrl,
            ToggleHotkey = _recordedShortcut,
            OverlayOpacity = opacityByte,
            BackgroundOpacity = bgOpacityByte,
            WebViewWidthPct  = (int)WidthSlider.Value,
            WebViewHeightPct = (int)HeightSlider.Value,
            WebViewZoomPct   = (int)ZoomSlider.Value,
            AutoStartWithWindows = AutoStartBox.IsChecked == true,
        };

        _settings.Save(updated);
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    // -------------------------------------------------------------------------
    // Ctrl+Shift+D — swap between production and staging URL
    // -------------------------------------------------------------------------

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e.Key == Key.D
            && (Keyboard.Modifiers & ModifierKeys.Control) != 0
            && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            _pendingUrl = _pendingUrl == ProdUrl ? StagingUrl : ProdUrl;
            UpdateEnvIndicator();
            e.Handled = true;
        }
    }

    private void UpdateEnvIndicator()
    {
        var isStaging = _pendingUrl == StagingUrl;
        EnvLabel.Text = isStaging ? "staging" : "production";
        EnvDot.Fill   = isStaging
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xf5, 0xa6, 0x23))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0xd3, 0xee));
    }

    // -------------------------------------------------------------------------
    // Title bar colour — 5% darker than window background (#0d1b2a → #0c1a28)
    // DWMWA_CAPTION_COLOR = 35, COLORREF = 0x00BBGGRR
    // -------------------------------------------------------------------------

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var color = 0x00281A0C; // #0c1a28 as COLORREF
        DwmSetWindowAttribute(hwnd, 35, ref color, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);
}
