using OneUI.Core.Services;
using Xunit;

namespace OneUI.Core.Tests;

public class ProductivityServicesTests
{
    [Fact]
    public void FocusTimer_CountsDownAndCompletesFocusIntoBreak()
    {
        var engine = new FocusTimerEngine(focusMinutes: 1, breakMinutes: 5);
        engine.SetPreset(FocusPhase.Focus, minutes: 1, autoStart: true);

        Assert.Equal("01:00", engine.FormattedRemaining);
        Assert.True(engine.IsRunning);

        FocusPhase? completed = null;
        for (int i = 0; i < 60; i++)
            completed = engine.TickOneSecond();

        Assert.Equal(FocusPhase.Focus, completed);
        Assert.Equal(1, engine.CompletedFocusSessions);
        Assert.Equal(FocusPhase.Break, engine.Phase);
        Assert.Equal("05:00", engine.FormattedRemaining);
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public void FocusTimer_PauseAndReset_WorksCleanly()
    {
        var engine = new FocusTimerEngine(focusMinutes: 25, breakMinutes: 5);
        engine.Start();
        engine.TickOneSecond();
        Assert.Equal("24:59", engine.FormattedRemaining);

        engine.Pause();
        engine.TickOneSecond();
        Assert.Equal("24:59", engine.FormattedRemaining);

        engine.Reset();
        Assert.Equal("25:00", engine.FormattedRemaining);
    }

    [Fact]
    public void ClipboardStore_Deduplicates_CapsAndPreservesPinned()
    {
        var store = new ClipboardHistoryStore(maxEntries: 5);
        var t0 = new DateTime(2026, 11, 21, 14, 0, 0);

        store.Record("first", t0);
        store.Record("second", t0.AddSeconds(1));
        var pinId = store.GetEntries().First(e => e.Text == "first").Id;
        store.TogglePin(pinId);

        store.Record("third", t0.AddSeconds(2));
        store.Record("fourth", t0.AddSeconds(3));
        store.Record("fifth", t0.AddSeconds(4));
        store.Record("sixth", t0.AddSeconds(5));

        var all = store.GetEntries();
        Assert.Equal(5, all.Count);
        Assert.Equal("first", all[0].Text); // pinned stays on top
        Assert.DoesNotContain(all, e => e.Text == "second"); // oldest unpinned evicted

        store.ClearUnpinned();
        Assert.Single(store.GetEntries());
        Assert.Equal("first", store.GetEntries()[0].Text);
    }
}
