namespace schud.Services;

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility; // WINEVENTPROC

/// <summary>
///     Monitors the Star Citizen game window using SetWinEventHook and fires
///     <see cref="GameMinimized"/> / <see cref="GameRestored"/> when SC is
///     minimized or restored.  The overlay uses these events to hide/show in
///     sync with the game so that alt-tabbing affects both together.
/// </summary>
/// <remarks>
///     <b>Threading:</b> <see cref="Start"/> must be called on the WPF UI thread.
///     WINEVENT_OUTOFCONTEXT delivers callbacks on the calling thread's message
///     pump, which WPF provides automatically for the UI thread.
/// </remarks>
public sealed class ScAnchorService : IDisposable
{
    private const string ScProcessName = "StarCitizen";

    private readonly ILogger<ScAnchorService> _logger;

    // Keep the delegate alive — GC must not collect it while the hook is active.
    private WINEVENTPROC? _proc;
    private UnhookWinEventSafeHandle? _hook;

    public event EventHandler? GameMinimized;
    public event EventHandler? GameRestored;

    public ScAnchorService(ILogger<ScAnchorService> logger)
    {
        _logger = logger;
    }

    /// <summary>Call on the WPF UI thread only.</summary>
    public void Start()
    {
        _proc = WinEventCallback;
        _hook = PInvoke.SetWinEventHook(
            PInvoke.EVENT_SYSTEM_MINIMIZESTART,
            PInvoke.EVENT_SYSTEM_MINIMIZEEND,
            default,
            _proc,
            0, 0,
            PInvoke.WINEVENT_OUTOFCONTEXT | PInvoke.WINEVENT_SKIPOWNPROCESS);

        if (_hook is null || _hook.IsInvalid)
            _logger.LogWarning("Failed to install WinEvent hook for SC anchor; error {Code}", Marshal.GetLastWin32Error());
        else
            _logger.LogInformation("SC anchor hook installed — watching for {Process}", ScProcessName);
    }

    public void Dispose()
    {
        _hook?.Dispose();
        _hook = null;
        _logger.LogDebug("SC anchor hook removed");
    }

    private void WinEventCallback(
        HWINEVENTHOOK hWinEventHook,
        uint          @event,
        HWND          hwnd,
        int           idObject,
        int           idChild,
        uint          idEventThread,
        uint          dwmsEventTime)
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

    // Manual import so we can use out uint without an unsafe context.
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
        catch
        {
            // Process may have exited between the GetWindowThreadProcessId call and here.
            return false;
        }
    }
}
