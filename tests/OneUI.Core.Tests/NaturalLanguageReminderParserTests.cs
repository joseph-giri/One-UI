using OneUI.Core.Services;
using Xunit;

namespace OneUI.Core.Tests;

public class NaturalLanguageReminderParserTests
{
    // Saturday, Nov 21, 2026 at 1:00 PM
    private static readonly DateTime RefNow = new(2026, 11, 21, 13, 0, 0);

    [Fact]
    public void Parses_RemindMe_Tomorrow_AtTime()
    {
        var result = NaturalLanguageReminderParser.Parse("Remind me to call Mom tomorrow at 5pm", RefNow);

        Assert.Equal("Call Mom", result.Title);
        Assert.Equal(new DateTime(2026, 11, 22, 17, 0, 0), result.DueAt);
        Assert.Null(result.Recurrence);
    }

    [Fact]
    public void Parses_RelativeDuration_MinutesAndHours()
    {
        var r1 = NaturalLanguageReminderParser.Parse("Stretch breaks in 30 minutes", RefNow);
        Assert.Equal("Stretch breaks", r1.Title);
        Assert.Equal(new DateTime(2026, 11, 21, 13, 30, 0), r1.DueAt);

        var r2 = NaturalLanguageReminderParser.Parse("Take oven out in 1 hour 15 mins", RefNow);
        Assert.Equal("Take oven out", r2.Title);
        Assert.Equal(new DateTime(2026, 11, 21, 14, 15, 0), r2.DueAt);
    }

    [Fact]
    public void Parses_Weekday_AndTime()
    {
        var result = NaturalLanguageReminderParser.Parse("Dentist appointment on Monday at 9:30 am", RefNow);

        Assert.Equal("Dentist appointment", result.Title);
        Assert.Equal(new DateTime(2026, 11, 23, 9, 30, 0), result.DueAt);
    }

    [Fact]
    public void Parses_MonthAndDay()
    {
        var result = NaturalLanguageReminderParser.Parse("Pay rent Dec 1 at 10am", RefNow);

        Assert.Equal("Pay rent", result.Title);
        Assert.Equal(new DateTime(2026, 12, 1, 10, 0, 0), result.DueAt);
    }

    [Fact]
    public void Parses_TimeOnly_RollsToTomorrowIfPassed()
    {
        var laterToday = NaturalLanguageReminderParser.Parse("Gym at 6pm", RefNow);
        Assert.Equal(new DateTime(2026, 11, 21, 18, 0, 0), laterToday.DueAt);

        var alreadyPassed = NaturalLanguageReminderParser.Parse("Standup at 9am", RefNow);
        Assert.Equal(new DateTime(2026, 11, 22, 9, 0, 0), alreadyPassed.DueAt);
    }

    [Fact]
    public void Parses_Recurrence_DailyAndWeekly()
    {
        var daily = NaturalLanguageReminderParser.Parse("Take vitamins every morning", RefNow);
        Assert.Equal("Take vitamins", daily.Title);
        Assert.Equal("Daily", daily.Recurrence);
        Assert.Equal(new DateTime(2026, 11, 22, 9, 0, 0), daily.DueAt);

        var weekly = NaturalLanguageReminderParser.Parse("Team sync every Monday at 10am", RefNow);
        Assert.Equal("Team sync", weekly.Title);
        Assert.Equal("Weekly", weekly.Recurrence);
        Assert.Equal(new DateTime(2026, 11, 23, 10, 0, 0), weekly.DueAt);
    }

    [Fact]
    public void Parses_PartOfDay_Tonight()
    {
        var result = NaturalLanguageReminderParser.Parse("Watch movie tonight", RefNow);
        Assert.Equal("Watch movie", result.Title);
        Assert.Equal(new DateTime(2026, 11, 21, 20, 0, 0), result.DueAt);
    }

    [Fact]
    public void PlainText_WithoutDate_ReturnsNullDueAt()
    {
        var result = NaturalLanguageReminderParser.Parse("Buy milk and eggs", RefNow);
        Assert.Equal("Buy milk and eggs", result.Title);
        Assert.Null(result.DueAt);
        Assert.Null(result.Recurrence);
    }

    [Fact]
    public void Parses_SpokenVoicePhrases()
    {
        var r1 = NaturalLanguageReminderParser.Parse("Remind me to check the laundry in twenty five minutes", RefNow);
        Assert.Equal("Check the laundry", r1.Title);
        Assert.Equal(new DateTime(2026, 11, 21, 13, 25, 0), r1.DueAt);

        var r2 = NaturalLanguageReminderParser.Parse("Remind me to call Dad tomorrow at five thirty p.m.", RefNow);
        Assert.Equal("Call Dad", r2.Title);
        Assert.Equal(new DateTime(2026, 11, 22, 17, 30, 0), r2.DueAt);

        var r3 = NaturalLanguageReminderParser.Parse("Drink water in an hour", RefNow);
        Assert.Equal("Drink water", r3.Title);
        Assert.Equal(new DateTime(2026, 11, 21, 14, 0, 0), r3.DueAt);
    }

    [Fact]
    public void Parses_ContinuousIntervalReminders()
    {
        var r1 = NaturalLanguageReminderParser.Parse("Drink water in every 15 minutes", RefNow);
        Assert.Equal("Drink water", r1.Title);
        Assert.Equal(15, r1.RepeatIntervalMinutes);
        Assert.Equal("Every 15m", r1.Recurrence);
        Assert.Equal(new DateTime(2026, 11, 21, 13, 15, 0), r1.DueAt);

        var r2 = NaturalLanguageReminderParser.Parse("Continuous reminder to stretch every two hours", RefNow);
        Assert.Equal("Stretch", r2.Title);
        Assert.Equal(120, r2.RepeatIntervalMinutes);
        Assert.Equal("Every 2h", r2.Recurrence);
        Assert.Equal(new DateTime(2026, 11, 21, 15, 0, 0), r2.DueAt);

        var r3 = NaturalLanguageReminderParser.Parse("Rest eyes every half hour", RefNow);
        Assert.Equal("Rest eyes", r3.Title);
        Assert.Equal(30, r3.RepeatIntervalMinutes);
        Assert.Equal(new DateTime(2026, 11, 21, 13, 30, 0), r3.DueAt);
    }
}
