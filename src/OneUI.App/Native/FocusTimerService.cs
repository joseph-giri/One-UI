using System.Windows.Threading;
using OneUI.Core.Services;

namespace OneUI.App.Native;

public sealed class FocusTimerService : IDisposable
{
    private readonly FocusTimerEngine _engine;
    private readonly DispatcherTimer _timer;

    public event Action? StateChanged;
    public event Action<FocusPhase>? SessionCompleted;

    public FocusPhase Phase => _engine.Phase;
    public bool IsRunning => _engine.IsRunning;
    public int RemainingSeconds => _engine.RemainingSeconds;
    public int CompletedFocusSessions => _engine.CompletedFocusSessions;
    public string FormattedRemaining => _engine.FormattedRemaining;

    public FocusTimerService(int focusMinutes = 25, int breakMinutes = 5)
    {
        _engine = new FocusTimerEngine(focusMinutes, breakMinutes);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            var completed = _engine.TickOneSecond();
            StateChanged?.Invoke();
            if (completed.HasValue)
            {
                _timer.Stop();
                SessionCompleted?.Invoke(completed.Value);
            }
        };
    }

    public void Start()
    {
        _engine.Start();
        if (!_timer.IsEnabled) _timer.Start();
        StateChanged?.Invoke();
    }

    public void Pause()
    {
        _engine.Pause();
        _timer.Stop();
        StateChanged?.Invoke();
    }

    public void ToggleStartPause()
    {
        if (_engine.IsRunning) Pause();
        else Start();
    }

    public void Reset()
    {
        _engine.Reset();
        _timer.Stop();
        StateChanged?.Invoke();
    }

    public void SetPreset(FocusPhase phase, int minutes, bool autoStart = true)
    {
        _engine.SetPreset(phase, minutes, autoStart);
        if (autoStart) _timer.Start();
        else _timer.Stop();
        StateChanged?.Invoke();
    }

    public void Dispose() => _timer.Stop();
}
