namespace schud.Services;

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility; // WINEVENTPROC

/// <summary>
///     Monitors the Star Citizen game window and fires
///     <see cref="GameMinimized"/> / <see cref="GameRestored"/> when SC
///     loses or gains the foreground.  The overlay hides/shows in sync so
///     that alt-tabbing affects both the game and HUD together.
/// </summary>
/// <remarks>
///     Two hooks are used:
///     - EVENT_SYSTEM_FOREGROUND — fires reliably when any app loses/gains
///       focus, including full-screen exclusive DirectX titles that don't
///       send WM_SYSCOMMAND/SC_MINIMIZE on alt-tab.
///     - EVENT_SYSTEM_MINIMIZESTART/END — fallback for windowed mode where
///       SC can be minimized independently of foreground changes.
///
///     Both must be started on the WPF UI thread so that WINEVENT_OUTOFCONTEXT
///     callbacks are delivered via the WPF message pump.
/// </remarks>
public sealed class ScAnchorService : IDisposable
{
    private const string ScProcessName = "StarCitizen";

    private readonly ILogger<ScAnchorService> _logger;
    private readonly uint _ownPid = (uint)Environment.ProcessId;

    // Two delegates + two handles — GC must not collect delegates while hooks are live.
    private WINEVENTPROC? _fgProc;
    private WINEVENTPROC? _minProc;
    private UnhookWinEventSafeHandle? _fgHook;
    private UnhookWinEventSafeHandle? _minHook;

    public event EventHandler? GameMinimized;
    public event EventHandler? GameRestored;

    public ScAnchorService(ILogger<ScAnchorService> logger)
    {
        _logger = logger;
    }

    /// <summary>Call on the WPF UI thread only.</summary>
    public void Start()
    {
        var flags = PInvoke.WINEVENT_OUTOFCONTEXT | PInvoke.WINEVENT_SKIPOWNPROCESS;

        // Foreground hook — catches alt-tab in full-screen exclusive DirectX mode.
        _fgProc  = OnForegroundChanged;
        _fgHook  = PInvoke.SetWinEventHook(
            PInvoke.EVENT_SYSTEM_FOREGROUND,
            PInvoke.EVENT_SYSTEM_FOREGROUND,
            default, _fgProc, 0, 0, flags);

        // Minimize hook — handles windowed/borderless mode where SC minimizes properly.
        _minProc = OnMinimize;
        _minHook = PInvoke.SetWinEventHook(
            PInvoke.EVENT_SYSTEM_MINIMIZESTART,
            PInvoke.EVENT_SYSTEM_MINIMIZEEND,
            default, _minProc, 0, 0, flags);

        var fgOk  = _fgHook  is { IsInvalid: false };
        var minOk = _minHook is { IsInvalid: false };

        if (fgOk && minOk)
            _logger.LogInformation("SC anchor hooks installed — watching for {Process}", ScProcessName);
        else
            _logger.LogWarning("SC anchor: fg hook={Fg} min hook={Min}", fgOk, minOk);
    }

    public void Dispose()
    {
        _fgHook?.Dispose();
        _minHook?.Dispose();
        _fgHook = _minHook = null;
        _logger.LogDebug("SC anchor hooks removed");
    }

    // -------------------------------------------------------------------------
    // Foreground callback — most reliable trigger for full-screen exclusive apps.
    // -------------------------------------------------------------------------

    private void OnForegroundChanged(
        HWINEVENTHOOK hWinEventHook, uint @event, HWND hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (hwnd == HWND.Null) return;

        if (IsStarCitizen(hwnd))
        {
            _logger.LogDebug("SC gained foreground");
            GameRestored?.Invoke(this, EventArgs.Empty);
        }
        else if (!IsOwnProcess(hwnd))
        {
            // Some other app (desktop, taskbar, another game) is now foreground.
            _logger.LogDebug("SC lost foreground to external window");
            GameMinimized?.Invoke(this, EventArgs.Empty);
        }
        // If hwnd belongs to our own process (e.g. settings window), do nothing.
    }

    // -------------------------------------------------------------------------
    // Minimize callback — fallback for windowed mode.
    // -------------------------------------------------------------------------

    private void OnMinimize(
        HWINEVENTHOOK hWinEventHook, uint @event, HWND hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (hwnd == HWND.Null) return;
        if (!IsStarCitizen(hwnd)) return;

        if (@event == PInvoke.EVENT_SYSTEM_MINIMIZESTART)
        {
            _logger.LogDebug("SC window minimized");
            GameMinimized?.Invoke(this, EventArgs.Empty);
        }
        else if (@event == PInvoke.EVENT_SYSTEM_MINIMIZEEND)
        {
            _logger.LogDebug("SC window restored");
            GameRestored?.Invoke(this, EventArgs.Empty);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    // Manual import — avoids unsafe context for the out uint parameter.
    [DllImport("user32.dll", SetLastError = false)]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    private static unsafe bool IsStarCitizen(HWND hwnd)
    {
        GetWindowThreadProcessId((nint)hwnd.Value, out var pid);
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return string.Equals(proc.ProcessName, ScProcessName, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private unsafe bool IsOwnProcess(HWND hwnd)
    {
        GetWindowThreadProcessId((nint)hwnd.Value, out var pid);
        return pid == _ownPid;
    }
}
