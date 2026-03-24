namespace schud.Services;

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility; // WINEVENTPROC

/// <summary>
///     Monitors the Star Citizen game window and keeps the HUD in sync.
///
///     When SC loses the foreground (alt-tab, task-switch) the HUD is hidden.
///     When SC regains the foreground the HUD is restored.  SC itself is left
///     in its current window state — borderless/windowed stays maximised.
///     Manual minimise/restore is also handled via MINIMIZESTART/END events.
/// </summary>
/// <remarks>
///     Must be started on the WPF UI thread; WINEVENT_OUTOFCONTEXT delivers
///     callbacks via the calling thread's message pump.
/// </remarks>
public sealed class ScAnchorService : IDisposable
{
    private const string ScProcessName = "StarCitizen";

    private readonly ILogger<ScAnchorService> _logger;
    private readonly uint _ownPid = (uint)Environment.ProcessId;

    // Delegates kept alive — GC must not collect them while hooks are live.
    private WINEVENTPROC? _fgProc;
    private WINEVENTPROC? _minProc;
    private UnhookWinEventSafeHandle? _fgHook;
    private UnhookWinEventSafeHandle? _minHook;

    // Last known SC window handle — used to minimise it on alt-tab.
    private HWND _scHwnd = HWND.Null;

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

        _fgProc  = OnForegroundChanged;
        _fgHook  = PInvoke.SetWinEventHook(
            PInvoke.EVENT_SYSTEM_FOREGROUND,
            PInvoke.EVENT_SYSTEM_FOREGROUND,
            default, _fgProc, 0, 0, flags);

        _minProc = OnMinimize;
        _minHook = PInvoke.SetWinEventHook(
            PInvoke.EVENT_SYSTEM_MINIMIZESTART,
            PInvoke.EVENT_SYSTEM_MINIMIZEEND,
            default, _minProc, 0, 0, flags);

        var ok = _fgHook is { IsInvalid: false } && _minHook is { IsInvalid: false };
        if (ok)
            _logger.LogInformation("SC anchor hooks installed — watching for {Process}", ScProcessName);
        else
            _logger.LogWarning("SC anchor: one or more hooks failed to install");
    }

    public void Dispose()
    {
        _fgHook?.Dispose();
        _minHook?.Dispose();
        _fgHook = _minHook = null;
        _logger.LogDebug("SC anchor hooks removed");
    }

    // -------------------------------------------------------------------------
    // Foreground callback
    // -------------------------------------------------------------------------

    private void OnForegroundChanged(
        HWINEVENTHOOK hWinEventHook, uint @event, HWND hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (hwnd == HWND.Null) return;

        if (IsStarCitizen(hwnd))
        {
            _scHwnd = hwnd; // keep HWND up to date
            _logger.LogDebug("SC gained foreground");
            GameRestored?.Invoke(this, EventArgs.Empty);
        }
        else if (!IsOwnProcess(hwnd))
        {
            // An external app gained foreground — hide the HUD.
            // SC is left in its current state (borderless/windowed stays maximised).
            _logger.LogDebug("SC lost foreground to external window");
            GameMinimized?.Invoke(this, EventArgs.Empty);
        }
        // Foreground changed to our own process (settings window) — do nothing.
    }

    // -------------------------------------------------------------------------
    // Minimize/restore callback
    // -------------------------------------------------------------------------

    private void OnMinimize(
        HWINEVENTHOOK hWinEventHook, uint @event, HWND hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (hwnd == HWND.Null) return;
        if (!IsStarCitizen(hwnd)) return;

        if (@event == PInvoke.EVENT_SYSTEM_MINIMIZESTART)
        {
            _scHwnd = hwnd;
            _logger.LogDebug("SC minimised");
            GameMinimized?.Invoke(this, EventArgs.Empty);
        }
        else if (@event == PInvoke.EVENT_SYSTEM_MINIMIZEEND)
        {
            _scHwnd = hwnd;
            _logger.LogDebug("SC restored");
            GameRestored?.Invoke(this, EventArgs.Empty);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

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
