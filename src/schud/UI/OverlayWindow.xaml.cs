namespace schud.UI;

using System.IO;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Models;
using Settings;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

public partial class OverlayWindow : Window
{
    private readonly SettingsManager _settings;
    private readonly ILogger<OverlayWindow> _logger;

    private HWND _hwnd;
    private CoreWebView2Environment? _env;
    private bool _webViewReady;
    private bool _isVisible;
    private bool _navigationErrorShown;
    private HWND _previousForegroundHwnd;
    private double _opacity  = 1.0;
    private int    _zoomPct  = 100;

    public event EventHandler? OverlayShown;
    public event EventHandler? OverlayHidden;
    public event EventHandler? SettingsRequested;
    public event Action<string, string>? BalloonTipRequested;

    public OverlayWindow(
        SettingsManager settings,
        ILogger<OverlayWindow> logger)
    {
        _settings = settings;
        _logger = logger;

        InitializeComponent();

        _settings.SettingsChanged += OnSettingsChanged;
    }

    public void Toggle()
    {
        if (_isVisible)
            HideOverlay();
        else
            ShowOverlay();
    }

    public void EnsureHidden()
    {
        if (_isVisible)
            HideOverlay();
    }

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new HWND(new WindowInteropHelper(this).Handle);

        // Hide from Alt+Tab.
        var exStyle = PInvoke.GetWindowLong(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLong(
            _hwnd,
            WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE,
            exStyle | (int)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW);

        ApplyOpacity(_settings.Current.OverlayOpacity);
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        await InitializeWebViewAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _settings.SettingsChanged -= OnSettingsChanged;
        base.OnClosed(e);
    }

    // -------------------------------------------------------------------------
    // Show / Hide
    // -------------------------------------------------------------------------

    private void ShowOverlay()
    {
        if (!_webViewReady)
        {
            _logger.LogDebug("Overlay toggled before WebView2 was ready — ignoring");
            return;
        }

        // Remember what had focus so we can return to it on hide.
        _previousForegroundHwnd = PInvoke.GetForegroundWindow();

        CoverPrimaryScreen();

        _isVisible = true;
        Visibility = Visibility.Visible;
        // First synchronous layout pass — gets WPF layout moving.
        UpdateLayout();

        // Bring the overlay to the foreground by temporarily sharing input state with
        // whatever window currently owns it.  AttachThreadInput is the most reliable
        // way to do this — SetForegroundWindow alone is silently ignored when the
        // calling thread does not own the foreground lock.
        ForceToForeground();

        // Deferred pass: wait for WPF to process WM_SETFOCUS and for the
        // WebView2 HwndHost to catch up on the resize, then give it focus
        // and snap the cursor to the centre (needs accurate ActualWidth/Height).
        Dispatcher.InvokeAsync(() =>
        {
            if (!_isVisible) return;
            UpdateLayout();
            var wpfFocused = WebView.Focus();
            _logger.LogDebug("ShowOverlay (deferred): WebView.Focus() returned {Result}", wpfFocused);
            var center = PointToScreen(new Point(ActualWidth / 2, ActualHeight / 2));
            PInvoke.SetCursorPos((int)center.X, (int)center.Y);
        }, System.Windows.Threading.DispatcherPriority.Render);

        OverlayShown?.Invoke(this, EventArgs.Empty);
        _logger.LogDebug("Overlay shown");
    }

    private void HideOverlay()
    {
        _isVisible = false;
        Visibility = Visibility.Collapsed;

        if (_previousForegroundHwnd != HWND.Null && PInvoke.IsWindow(_previousForegroundHwnd))
            PInvoke.SetForegroundWindow(_previousForegroundHwnd);

        _previousForegroundHwnd = HWND.Null;
        OverlayHidden?.Invoke(this, EventArgs.Empty);
        _logger.LogDebug("Overlay hidden");
    }

    // -------------------------------------------------------------------------
    // Settings hot-reload
    // -------------------------------------------------------------------------

    private void OnSettingsChanged(object? sender, SchudSettings settings)
    {
        Dispatcher.Invoke(() =>
        {
            ApplyOpacity(settings.OverlayOpacity);
            ApplyZoom(settings.WebViewZoomPct);
            ApplySize(settings.WebViewWidthPct, settings.WebViewHeightPct);

            if (_webViewReady)
            {
                var current = WebView.CoreWebView2.Source;
                if (!string.Equals(current, settings.OverlayUrl, StringComparison.OrdinalIgnoreCase))
                    RestartNavigation(settings.OverlayUrl);
            }
        });
    }

    private void RestartNavigation(string url)
    {
        _webViewReady = false;
        _navigationErrorShown = false;
        WebView.CoreWebView2.Stop();
        WebView.CoreWebView2.Navigate(url);
        _logger.LogInformation("WebView2 restarted — navigating to {Url}", url);
    }

    // -------------------------------------------------------------------------
    // Focus helpers
    // -------------------------------------------------------------------------

    private unsafe void ForceToForeground()
    {
        var fgHwnd   = PInvoke.GetForegroundWindow();
        uint pid;
        var fgThread = PInvoke.GetWindowThreadProcessId(fgHwnd, &pid);
        var uiThread = PInvoke.GetCurrentThreadId();

        _logger.LogDebug(
            "ForceToForeground: fgHwnd={Fg:X} fgThread={FgT} uiThread={UiT}",
            (nint)fgHwnd.Value, fgThread, uiThread);

        // Attach our UI thread's input queue to the foreground thread so that
        // SetForegroundWindow and SetFocus cannot be blocked by the lock.
        var attached = fgThread != uiThread
            && PInvoke.AttachThreadInput(fgThread, uiThread, true);

        var fgResult    = PInvoke.SetForegroundWindow(_hwnd);
        var topResult   = PInvoke.BringWindowToTop(_hwnd);
        // SetFocus inside the attach window gives the HwndHost full activation,
        // preventing the first mouse click from being consumed by WM_MOUSEACTIVATE.
        var focusResult = PInvoke.SetFocus(_hwnd);

        _logger.LogDebug(
            "ForceToForeground: attached={Att} SetForeground={Fg} BringToTop={Top} SetFocus={Foc}",
            attached, (bool)fgResult, (bool)topResult, focusResult != HWND.Null);

        if (attached)
            PInvoke.AttachThreadInput(fgThread, uiThread, false);
    }

    // -------------------------------------------------------------------------
    // Opacity — inject CSS opacity on the html element.
    // Window.Opacity and SetLayeredWindowAttributes both fail for WebView2
    // because its DirectComposition surface bypasses the host window's alpha.
    // CSS opacity runs inside Chromium's GPU compositor and correctly blends
    // the page (including its background) against the transparent WPF window,
    // giving true see-through transparency over whatever is behind the overlay.
    // -------------------------------------------------------------------------

    public void ApplySize(int widthPct, int heightPct)
    {
        widthPct  = Math.Clamp(widthPct,  10, 100);
        heightPct = Math.Clamp(heightPct, 10, 100);

        if (widthPct == 100 && heightPct == 100)
        {
            WebView.ClearValue(FrameworkElement.WidthProperty);
            WebView.ClearValue(FrameworkElement.HeightProperty);
            WebView.HorizontalAlignment = HorizontalAlignment.Stretch;
            WebView.VerticalAlignment   = VerticalAlignment.Stretch;
            WebView.Margin = new Thickness(0);
        }
        else
        {
            WebView.HorizontalAlignment = HorizontalAlignment.Center;
            WebView.VerticalAlignment   = VerticalAlignment.Center;
            WebView.Width  = ActualWidth  * widthPct  / 100.0;
            WebView.Height = ActualHeight * heightPct / 100.0;
            WebView.Margin = new Thickness(0);
        }

        _logger.LogDebug("ApplySize: {W}% × {H}%", widthPct, heightPct);
    }

    public void ApplyOpacity(byte value)
    {
        _opacity = value / 255.0;
        _logger.LogDebug("ApplyOpacity: byte={Value} → {Opacity:F3}", value, _opacity);
        if (_webViewReady)
            _ = WebView.CoreWebView2.ExecuteScriptAsync(BuildOverlayStyleScript(_opacity, _zoomPct));
    }

    public void ApplyZoom(int zoomPct)
    {
        _zoomPct = Math.Clamp(zoomPct, 10, 200);
        _logger.LogDebug("ApplyZoom: {Zoom}%", _zoomPct);
        if (_webViewReady)
            _ = WebView.CoreWebView2.ExecuteScriptAsync(BuildOverlayStyleScript(_opacity, _zoomPct));
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (e.TryGetWebMessageAsString() == "open-settings")
            Dispatcher.Invoke(() => SettingsRequested?.Invoke(this, EventArgs.Empty));
    }

    // Injects a <style> tag that:
    //   1. Forces html/body background to transparent — clears any site-set root background.
    //   2. Sets opacity on html — fades all rendered content including their own backgrounds.
    //   3. Sets zoom on html — scales all content proportionally.
    //   4. Clamps html/body to the viewport — prevents zoomed content from overflowing
    //      past screen edges (overflow:hidden clips at the WebView2 boundary).
    // Using !important so site stylesheets cannot override any rule.
    private static string BuildOverlayStyleScript(double opacity, int zoomPct) =>
        $"(function(){{" +
        $"var s=document.getElementById('__ol_style');" +
        $"if(!s){{s=document.createElement('style');s.id='__ol_style';" +
        $"(document.head||document.documentElement).appendChild(s);}}" +
        $"s.textContent='html,body{{background:transparent!important;overflow:hidden!important;" +
        $"max-width:100vw!important;max-height:100vh!important}}" +
        $"html{{opacity:{opacity:F3}!important;zoom:{zoomPct}%!important}}';" +
        $"}})();";

    private static string BuildSettingsButtonScript() =>
        "(function(){" +
        "if(document.getElementById('__ol_settings_btn'))return;" +
        "var b=document.createElement('div');" +
        "b.id='__ol_settings_btn';" +
        "b.textContent='\u2699';" +
        "b.style.cssText='position:fixed;bottom:14px;right:14px;width:36px;height:36px;" +
            "background:rgba(0,0,0,0.45);color:white;font-size:20px;" +
            "display:flex;align-items:center;justify-content:center;" +
            "border-radius:6px;cursor:pointer;z-index:2147483647;" +
            "opacity:0.4;transition:opacity 0.15s;user-select:none;';" +
        "b.addEventListener('mouseover',function(){b.style.opacity='1';});" +
        "b.addEventListener('mouseout',function(){b.style.opacity='0.4';});" +
        "b.addEventListener('click',function(){window.chrome.webview.postMessage('open-settings');});" +
        "document.body.appendChild(b);" +
        "})();";

    // -------------------------------------------------------------------------
    // Positioning
    // -------------------------------------------------------------------------

    private void CoverPrimaryScreen()
    {
        // Cover the full primary screen — let the web content decide its own layout.
        var sw = SystemParameters.PrimaryScreenWidth;
        var sh = SystemParameters.PrimaryScreenHeight;
        Width  = sw;
        Height = sh;
        Left   = 0;
        Top    = 0;
    }

    // -------------------------------------------------------------------------
    // WebView2
    // -------------------------------------------------------------------------

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var cacheFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Constants.AppDataFolderName,
                Constants.WebView2CacheFolderName);

            // Must be set before EnsureCoreWebView2Async — WebView2 ignores it after init.
            WebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0, 0, 0, 0);

            _env = await CoreWebView2Environment.CreateAsync(userDataFolder: cacheFolder);
            await WebView.EnsureCoreWebView2Async(_env);
            WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            WebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
            WebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            WebView.CoreWebView2.NavigationStarting  += OnNavigationStarting;
            WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            WebView.CoreWebView2.NewWindowRequested  += OnNewWindowRequested;
            WebView.CoreWebView2.WebMessageReceived  += OnWebMessageReceived;

            WebView.Source = new Uri(_settings.Current.OverlayUrl);

            _webViewReady = true;
            _logger.LogInformation("WebView2 ready — {Url}", _settings.Current.OverlayUrl);

            // Re-apply style and size after WebView2 init.
            ApplyOpacity(_settings.Current.OverlayOpacity);
            ApplyZoom(_settings.Current.WebViewZoomPct);
            ApplySize(_settings.Current.WebViewWidthPct, _settings.Current.WebViewHeightPct);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            _logger.LogError("WebView2 runtime is not installed");
            BalloonTipRequested?.Invoke(
                "WebView2 not installed",
                "SC HUD requires the Microsoft Edge WebView2 Runtime. Please install it and restart.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebView2 initialization failed");
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // Allow internal WebView2 URIs (NavigateToString uses data:, about:blank etc.)
        if (e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            return;

        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)) return;
        if (IsAllowedOrigin(uri)) return;

        // Non-allowed origin (e.g. OAuth provider redirect) — cancel the main-frame
        // navigation and open it in a shared-session popup so the auth cookie ends
        // up in the same WebView2 environment and the callback reaches the overlay.
        _logger.LogDebug("Opening external origin in popup: {Uri}", e.Uri);
        e.Cancel = true;
        OpenAsAuthPopup(e.Uri);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            _webViewReady = true;
            _navigationErrorShown = false;
            _ = WebView.CoreWebView2.ExecuteScriptAsync(BuildOverlayStyleScript(_opacity, _zoomPct));
            _ = WebView.CoreWebView2.ExecuteScriptAsync(BuildSettingsButtonScript());
            return;
        }

        // OperationCanceled is raised when we cancel the navigation ourselves
        // (e.g. intercepted OAuth redirect) — not a real error, do not show error page.
        if (e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled) return;

        if (_navigationErrorShown) return;
        _navigationErrorShown = true;

        _logger.LogWarning("Navigation failed: {Error}", e.WebErrorStatus);

        var encodedUrl = System.Net.WebUtility.HtmlEncode(_settings.Current.OverlayUrl);
        WebView.NavigateToString(
            "<html><body style='background:#1a1a1a;color:#ccc;font-family:sans-serif;padding:24px'>" +
            "<h2 style='color:#e88'>Could not load overlay</h2>" +
            $"<p>Failed to reach <code>{encodedUrl}</code>.</p>" +
            "<p>Check the URL in Settings and verify your internet connection.</p>" +
            "</body></html>");
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // If there is no shared environment yet, fall back to the external browser.
        if (_env is null)
        {
            e.Handled = true;
            OpenInDefaultBrowser(e.Uri);
            return;
        }
        // All other new-window requests (OAuth popups, account pickers, etc.) are
        // opened inside a shared WebView2 popup so the OAuth callback can post back
        // to the main window.  Note: OAuth popups triggered via JS window.open() are
        // NOT flagged IsUserInitiated by WebView2, so we cannot gate on that flag.

        var deferral = e.GetDeferral();
        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                var wv = new Microsoft.Web.WebView2.Wpf.WebView2();
                var popup = new Window
                {
                    Title = "Sign In",
                    Width  = e.WindowFeatures.HasSize ? e.WindowFeatures.Width  : 500,
                    Height = e.WindowFeatures.HasSize ? e.WindowFeatures.Height : 700,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Topmost = true,
                    ShowInTaskbar = false,
                    Content = wv,
                };

                // Show first so the HwndHost has an HWND before EnsureCoreWebView2Async.
                popup.Show();
                await wv.EnsureCoreWebView2Async(_env);

                // Allow the popup to close itself (window.close() at end of OAuth flow).
                wv.CoreWebView2.WindowCloseRequested += (_, _) => Dispatcher.Invoke(popup.Close);
                // OAuth chains can open further popups (e.g. account-picker).
                wv.CoreWebView2.NewWindowRequested += OnNewWindowRequested;

                e.NewWindow = wv.CoreWebView2;
                e.Handled = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create OAuth popup; falling back to browser");
                e.Handled = true;
                OpenInDefaultBrowser(e.Uri);
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    private void OpenAsAuthPopup(string uri)
    {
        if (_env is null)
        {
            _logger.LogWarning("Auth popup requested but WebView2 env not ready; falling back to browser");
            OpenInDefaultBrowser(uri);
            return;
        }

        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                var wv = new Microsoft.Web.WebView2.Wpf.WebView2();
                var popup = new Window
                {
                    Title = "Sign In",
                    Width  = 500,
                    Height = 700,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Topmost = true,
                    ShowInTaskbar = false,
                    Content = wv,
                };

                // Show the window first — EnsureCoreWebView2Async requires the
                // HwndHost to have a real HWND, which only exists after Show().
                popup.Show();
                await wv.EnsureCoreWebView2Async(_env);

                wv.CoreWebView2.NavigationStarting += (_, args) =>
                {
                    if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var dest)) return;
                    if (!IsAllowedOrigin(dest)) return;

                    // Auth callback is redirecting back to our origin.
                    // Cancel the popup navigation and hand the callback URL to the
                    // main window — the server needs to process it there to set the
                    // session cookie in the main WebView2 context.
                    _logger.LogInformation("Auth popup returned to allowed origin {Uri}; completing auth in main window", args.Uri);
                    args.Cancel = true;
                    Dispatcher.Invoke(() =>
                    {
                        popup.Close();
                        WebView.Source = dest;
                    });
                };

                wv.CoreWebView2.WindowCloseRequested += (_, _) => Dispatcher.Invoke(popup.Close);
                wv.CoreWebView2.NewWindowRequested  += OnNewWindowRequested;

                wv.Source = new Uri(uri);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open auth popup; falling back to browser");
                OpenInDefaultBrowser(uri);
            }
        });
    }

    private static void OpenInDefaultBrowser(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return;
        if (!uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;

        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
    }

    private bool IsAllowedOrigin(Uri uri)
    {
        if (!Uri.TryCreate(_settings.Current.OverlayUrl, UriKind.Absolute, out var overlayUri))
            return false;

        var host       = uri.Host;
        var configured = overlayUri.Host;

        // Allow exact configured host and its subdomains.
        if (host.Equals(configured, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + configured, StringComparison.OrdinalIgnoreCase))
            return true;

        // Also allow the parent domain and its subdomains so that auth callbacks
        // can redirect between subdomains (e.g. staging.example.com → example.com).
        var parts = configured.Split('.');
        if (parts.Length > 2)
        {
            var parent = string.Join('.', parts[^2..]);
            if (host.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + parent, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
