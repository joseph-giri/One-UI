namespace OneUI.Core.Services;

public enum FocusPhase
{
    Focus,
    Break
}

public sealed class FocusTimerEngine
{
    public FocusPhase Phase { get; private set; } = FocusPhase.Focus;
    public bool IsRunning { get; private set; }
    public int RemainingSeconds { get; private set; }
    public int TotalPhaseSeconds { get; private set; }
    public int FocusDurationMinutes { get; private set; }
    public int BreakDurationMinutes { get; private set; }
    public int CompletedFocusSessions { get; private set; }

    public FocusTimerEngine(int focusMinutes = 25, int breakMinutes = 5)
    {
        FocusDurationMinutes = Math.Clamp(focusMinutes, 1, 180);
        BreakDurationMinutes = Math.Clamp(breakMinutes, 1, 60);
        TotalPhaseSeconds = FocusDurationMinutes * 60;
        RemainingSeconds = TotalPhaseSeconds;
    }

    public void Start() => IsRunning = true;
    public void Pause() => IsRunning = false;
    public void ToggleStartPause() => IsRunning = !IsRunning;

    public void Reset()
    {
        IsRunning = false;
        TotalPhaseSeconds = (Phase == FocusPhase.Focus ? FocusDurationMinutes : BreakDurationMinutes) * 60;
        RemainingSeconds = TotalPhaseSeconds;
    }

    public void SetPreset(FocusPhase phase, int minutes, bool autoStart = true)
    {
        Phase = phase;
        int clamped = Math.Clamp(minutes, 1, 180);
        if (phase == FocusPhase.Focus)
            FocusDurationMinutes = clamped;
        else
            BreakDurationMinutes = clamped;

        TotalPhaseSeconds = clamped * 60;
        RemainingSeconds = TotalPhaseSeconds;
        IsRunning = autoStart;
    }

    /// <summary>
    /// Advances the timer by 1 second when running. Returns the completed phase if it just hit 0.
    /// </summary>
    public FocusPhase? TickOneSecond()
    {
        if (!IsRunning) return null;

        if (RemainingSeconds > 1)
        {
            RemainingSeconds--;
            return null;
        }

        // Phase completed
        var completed = Phase;
        if (completed == FocusPhase.Focus)
        {
            CompletedFocusSessions++;
            Phase = FocusPhase.Break;
            TotalPhaseSeconds = BreakDurationMinutes * 60;
        }
        else
        {
            Phase = FocusPhase.Focus;
            TotalPhaseSeconds = FocusDurationMinutes * 60;
        }

        RemainingSeconds = TotalPhaseSeconds;
        IsRunning = false;
        return completed;
    }

    public string FormattedRemaining
    {
        get
        {
            int mins = RemainingSeconds / 60;
            int secs = RemainingSeconds % 60;
            return $"{mins:00}:{secs:00}";
        }
    }
}
