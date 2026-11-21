using System.Globalization;
using System.Text.RegularExpressions;

namespace OneUI.Core.Services;

public sealed record ParsedReminder(
    string Title,
    DateTime? DueAt,
    string? Recurrence,
    int? RepeatIntervalMinutes = null);

public static partial class NaturalLanguageReminderParser
{
    private static readonly Dictionary<string, DayOfWeek> DayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sunday"] = DayOfWeek.Sunday, ["sun"] = DayOfWeek.Sunday,
        ["monday"] = DayOfWeek.Monday, ["mon"] = DayOfWeek.Monday,
        ["tuesday"] = DayOfWeek.Tuesday, ["tue"] = DayOfWeek.Tuesday, ["tues"] = DayOfWeek.Tuesday,
        ["wednesday"] = DayOfWeek.Wednesday, ["wed"] = DayOfWeek.Wednesday,
        ["thursday"] = DayOfWeek.Thursday, ["thu"] = DayOfWeek.Thursday, ["thur"] = DayOfWeek.Thursday, ["thurs"] = DayOfWeek.Thursday,
        ["friday"] = DayOfWeek.Friday, ["fri"] = DayOfWeek.Friday,
        ["saturday"] = DayOfWeek.Saturday, ["sat"] = DayOfWeek.Saturday
    };

    private static readonly Dictionary<string, int> MonthNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["january"] = 1, ["jan"] = 1,
        ["february"] = 2, ["feb"] = 2,
        ["march"] = 3, ["mar"] = 3,
        ["april"] = 4, ["apr"] = 4,
        ["may"] = 5,
        ["june"] = 6, ["jun"] = 6,
        ["july"] = 7, ["jul"] = 7,
        ["august"] = 8, ["aug"] = 8,
        ["september"] = 9, ["sep"] = 9, ["sept"] = 9,
        ["october"] = 10, ["oct"] = 10,
        ["november"] = 11, ["nov"] = 11,
        ["december"] = 12, ["dec"] = 12
    };

    public static ParsedReminder Parse(string input, DateTime? referenceNow = null)
    {
        var now = referenceNow ?? DateTime.Now;
        if (string.IsNullOrWhiteSpace(input))
            return new ParsedReminder(string.Empty, null, null);

        string working = NormalizeSpokenText(input.Trim());
        string? recurrence = null;
        int? repeatIntervalMinutes = null;
        DateTime? explicitDate = null;
        TimeSpan? explicitTime = null;
        bool hasRelativeOffset = false;
        DateTime relativeTarget = now;

        // 1. Strip natural leading filler ("remind me to ...", "continuous reminder to ...", etc.)
        working = LeadingFillerRegex().Replace(working, string.Empty).Trim();

        // 2a. Custom continuous interval recurrence ("every 15 minutes", "in every 2 hours", "every half hour")
        var halfHourRecMatch = EveryHalfHourRegex().Match(working);
        if (halfHourRecMatch.Success)
        {
            repeatIntervalMinutes = 30;
            recurrence = "Every 30m";
            hasRelativeOffset = true;
            relativeTarget = now.AddMinutes(30);
            working = RemoveSpan(working, halfHourRecMatch.Index, halfHourRecMatch.Length);
        }
        else
        {
            var intervalMatch = IntervalRecurrenceRegex().Match(working);
            if (intervalMatch.Success &&
                int.TryParse(intervalMatch.Groups["num"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int num) &&
                num > 0)
            {
                string unit = intervalMatch.Groups["unit"].Value.ToLowerInvariant();
                if (unit.StartsWith("m"))
                {
                    repeatIntervalMinutes = num;
                    recurrence = $"Every {num}m";
                }
                else if (unit.StartsWith("h"))
                {
                    repeatIntervalMinutes = num * 60;
                    recurrence = $"Every {num}h";
                }
                else if (unit.StartsWith("d"))
                {
                    repeatIntervalMinutes = num * 1440;
                    recurrence = $"Every {num}d";
                }

                if (repeatIntervalMinutes.HasValue)
                {
                    hasRelativeOffset = true;
                    relativeTarget = now.AddMinutes(repeatIntervalMinutes.Value);
                    working = RemoveSpan(working, intervalMatch.Index, intervalMatch.Length);
                }
            }
        }

        // 2b. Named recurrence ("every day", "daily", "every morning", "every Monday", "every week", "every hour")
        if (recurrence is null)
        {
            var recMatch = RecurrenceRegex().Match(working);
            if (recMatch.Success)
            {
                string kind = recMatch.Groups["kind"].Value.ToLowerInvariant();
                if (kind is "day" or "daily")
                {
                    recurrence = "Daily";
                }
                else if (kind == "morning")
                {
                    recurrence = "Daily";
                    explicitTime ??= new TimeSpan(9, 0, 0);
                }
                else if (kind == "evening")
                {
                    recurrence = "Daily";
                    explicitTime ??= new TimeSpan(18, 0, 0);
                }
                else if (kind == "night")
                {
                    recurrence = "Daily";
                    explicitTime ??= new TimeSpan(20, 0, 0);
                }
                else if (kind is "week" or "weekly")
                {
                    recurrence = "Weekly";
                }
                else if (kind is "hour" or "hourly")
                {
                    recurrence = "Hourly";
                    repeatIntervalMinutes = 60;
                    hasRelativeOffset = true;
                    relativeTarget = now.AddHours(1);
                }
                else if (DayNames.TryGetValue(kind, out var recDow))
                {
                    recurrence = "Weekly";
                    explicitDate = NextWeekday(now.Date, recDow, forceNextWeek: false, includeToday: true);
                }

                working = RemoveSpan(working, recMatch.Index, recMatch.Length);
            }
        }

        // 3. Relative duration ("in 30 minutes", "in 2 hours 15 mins", "in 3 days", "in half an hour")
        var halfHourMatch = HalfHourRegex().Match(working);
        if (halfHourMatch.Success)
        {
            hasRelativeOffset = true;
            relativeTarget = now.AddMinutes(30);
            working = RemoveSpan(working, halfHourMatch.Index, halfHourMatch.Length);
        }
        else
        {
            var relMatch = RelativeDurationRegex().Match(working);
            if (relMatch.Success)
            {
                var target = now;
                bool matchedAny = false;
                foreach (Match part in DurationPartRegex().Matches(relMatch.Value))
                {
                    if (!double.TryParse(part.Groups["num"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                        continue;
                    string unit = part.Groups["unit"].Value.ToLowerInvariant();
                    if (unit.StartsWith("s")) { target = target.AddSeconds(val); matchedAny = true; }
                    else if (unit.StartsWith("m")) { target = target.AddMinutes(val); matchedAny = true; }
                    else if (unit.StartsWith("h")) { target = target.AddHours(val); matchedAny = true; }
                    else if (unit.StartsWith("d")) { target = target.AddDays(val); matchedAny = true; }
                    else if (unit.StartsWith("w")) { target = target.AddDays(val * 7); matchedAny = true; }
                }

                if (matchedAny)
                {
                    hasRelativeOffset = true;
                    relativeTarget = target;
                    working = RemoveSpan(working, relMatch.Index, relMatch.Length);
                }
            }
        }

        // 4. Explicit clock time ("at 5pm", "at 9:30 am", "14:30", "at noon", "at midnight")
        if (!hasRelativeOffset)
        {
            var namedTimeMatch = NamedTimeRegex().Match(working);
            if (namedTimeMatch.Success)
            {
                string word = namedTimeMatch.Groups["word"].Value.ToLowerInvariant();
                if (word == "noon")
                {
                    explicitTime = new TimeSpan(12, 0, 0);
                }
                else if (word == "midnight")
                {
                    explicitTime = TimeSpan.Zero;
                    explicitDate ??= now.Date.AddDays(1);
                }
                working = RemoveSpan(working, namedTimeMatch.Index, namedTimeMatch.Length);
            }

            if (explicitTime is null)
            {
                var timeMatch = ClockTimeRegex().Match(working);
                if (timeMatch.Success)
                {
                    int hour = int.Parse(timeMatch.Groups["h"].Value, CultureInfo.InvariantCulture);
                    int minute = timeMatch.Groups["m"].Success
                        ? int.Parse(timeMatch.Groups["m"].Value, CultureInfo.InvariantCulture)
                        : 0;
                    string ampm = timeMatch.Groups["ampm"].Value.ToLowerInvariant();

                    if (minute is >= 0 and <= 59 && hour is >= 0 and <= 23)
                    {
                        if (ampm == "pm" && hour < 12) hour += 12;
                        else if (ampm == "am" && hour == 12) hour = 0;

                        if (hour <= 23)
                        {
                            explicitTime = new TimeSpan(hour, minute, 0);
                            working = RemoveSpan(working, timeMatch.Index, timeMatch.Length);
                        }
                    }
                }
            }

            // 5. Part-of-day hints ("tonight", "this morning", "tomorrow morning", "this afternoon", "this evening")
            var podMatch = PartOfDayRegex().Match(working);
            if (podMatch.Success)
            {
                string qualifier = podMatch.Groups["qual"].Value.ToLowerInvariant();
                string part = podMatch.Groups["part"].Value.ToLowerInvariant();

                if (qualifier is "tomorrow" or "tmr" or "tmrw")
                    explicitDate ??= now.Date.AddDays(1);
                else if (qualifier is "today" or "this" || part == "tonight")
                    explicitDate ??= now.Date;

                explicitTime ??= part switch
                {
                    "morning" => new TimeSpan(9, 0, 0),
                    "afternoon" => new TimeSpan(15, 0, 0),
                    "evening" => new TimeSpan(18, 0, 0),
                    "night" or "tonight" => new TimeSpan(20, 0, 0),
                    _ => null
                };

                working = RemoveSpan(working, podMatch.Index, podMatch.Length);
            }

            // 6. Relative day words ("today", "tomorrow", "day after tomorrow")
            var relDayMatch = RelativeDayRegex().Match(working);
            if (relDayMatch.Success)
            {
                string phrase = relDayMatch.Groups["day"].Value.ToLowerInvariant();
                if (phrase.Contains("after"))
                    explicitDate ??= now.Date.AddDays(2);
                else if (phrase.StartsWith("tom") || phrase.StartsWith("tmr"))
                    explicitDate ??= now.Date.AddDays(1);
                else
                    explicitDate ??= now.Date;

                working = RemoveSpan(working, relDayMatch.Index, relDayMatch.Length);
            }

            // 7. Month + Day ("on Dec 1", "November 25", "25 Nov")
            if (explicitDate is null)
            {
                var monthDayMatch = MonthDayRegex().Match(working);
                if (monthDayMatch.Success &&
                    MonthNames.TryGetValue(monthDayMatch.Groups["mon"].Value, out int month) &&
                    int.TryParse(monthDayMatch.Groups["day"].Value, out int day))
                {
                    int year = now.Year;
                    int maxDay = DateTime.DaysInMonth(year, month);
                    if (day >= 1 && day <= maxDay)
                    {
                        var candidate = new DateTime(year, month, day);
                        if (candidate < now.Date) candidate = candidate.AddYears(1);
                        explicitDate = candidate;
                        working = RemoveSpan(working, monthDayMatch.Index, monthDayMatch.Length);
                    }
                }
            }

            // 8. Numeric date ("12/15" or "2026-12-15")
            if (explicitDate is null)
            {
                var numDateMatch = NumericDateRegex().Match(working);
                if (numDateMatch.Success)
                {
                    int month = int.Parse(numDateMatch.Groups["m"].Value, CultureInfo.InvariantCulture);
                    int day = int.Parse(numDateMatch.Groups["d"].Value, CultureInfo.InvariantCulture);
                    int year = numDateMatch.Groups["y"].Success
                        ? int.Parse(numDateMatch.Groups["y"].Value, CultureInfo.InvariantCulture)
                        : now.Year;
                    if (year < 100) year += 2000;

                    if (month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month))
                    {
                        var candidate = new DateTime(year, month, day);
                        if (!numDateMatch.Groups["y"].Success && candidate < now.Date)
                            candidate = candidate.AddYears(1);
                        explicitDate = candidate;
                        working = RemoveSpan(working, numDateMatch.Index, numDateMatch.Length);
                    }
                }
            }

            // 9. Weekday ("Monday", "on Friday", "next Tuesday", "this Saturday")
            if (explicitDate is null)
            {
                var dowMatch = WeekdayRegex().Match(working);
                if (dowMatch.Success && DayNames.TryGetValue(dowMatch.Groups["dow"].Value, out var targetDow))
                {
                    bool forceNext = string.Equals(dowMatch.Groups["mod"].Value, "next", StringComparison.OrdinalIgnoreCase);
                    explicitDate = NextWeekday(now.Date, targetDow, forceNextWeek: forceNext, includeToday: false);
                    working = RemoveSpan(working, dowMatch.Index, dowMatch.Length);
                }
            }
        }

        // Assemble final DueAt
        DateTime? dueAt = null;
        if (hasRelativeOffset)
        {
            dueAt = new DateTime(relativeTarget.Year, relativeTarget.Month, relativeTarget.Day, relativeTarget.Hour, relativeTarget.Minute, 0);
            if (relativeTarget.Second > 0 && (relativeTarget - now).TotalMinutes < 2)
                dueAt = relativeTarget;
        }
        else if (explicitDate is not null || explicitTime is not null)
        {
            var time = explicitTime ?? new TimeSpan(9, 0, 0);
            var date = explicitDate ?? now.Date;
            var candidate = date.Add(time);

            // If user only specified a time (e.g. "at 5pm") and it already passed today, schedule for tomorrow
            if (explicitDate is null && candidate <= now)
                candidate = candidate.AddDays(1);

            dueAt = candidate;
        }
        else if (recurrence is not null)
        {
            dueAt = recurrence switch
            {
                "Hourly" => now.AddHours(1),
                "Weekly" => now.Date.AddDays(7).AddHours(9),
                _ => now.Date.AddDays(1).AddHours(9)
            };
        }

        // Clean up leftover connectors/punctuation in title
        string title = CleanupTitle(working);
        if (string.IsNullOrWhiteSpace(title))
            title = input.Trim();

        return new ParsedReminder(title, dueAt, recurrence, repeatIntervalMinutes);
    }

    public static DateTime? ComputeNextOccurrence(DateTime currentDue, string? recurrence, int? repeatIntervalMinutes = null, DateTime? referenceNow = null)
    {
        var now = referenceNow ?? DateTime.Now;
        if (repeatIntervalMinutes is > 0)
        {
            var next = currentDue.AddMinutes(repeatIntervalMinutes.Value);
            if (next <= now)
                next = now.AddMinutes(repeatIntervalMinutes.Value);
            return next;
        }

        return recurrence switch
        {
            "Hourly" => currentDue.AddHours(1) <= now ? now.AddHours(1) : currentDue.AddHours(1),
            "Daily" => currentDue.AddDays(1),
            "Weekly" => currentDue.AddDays(7),
            _ => null
        };
    }

    public static string FormatDueLabel(DateTime dueAt, DateTime? referenceNow = null, string? recurrence = null)
    {
        var now = referenceNow ?? DateTime.Now;
        var diff = dueAt - now;
        string prefix = string.IsNullOrEmpty(recurrence) ? "" : $"\u21BB {recurrence} \u00B7 ";

        if (diff.TotalSeconds < -30)
        {
            var ago = -diff;
            if (ago.TotalMinutes < 60) return $"{prefix}Overdue \u00B7 {(int)Math.Max(1, Math.Round(ago.TotalMinutes))}m ago";
            if (ago.TotalHours < 24) return $"{prefix}Overdue \u00B7 {(int)Math.Round(ago.TotalHours)}h ago";
            return $"{prefix}Overdue \u00B7 {dueAt:MMM d}";
        }

        if (diff.TotalMinutes < 1)
            return $"{prefix}Due now";

        if (diff.TotalMinutes < 60)
            return $"{prefix}In {(int)Math.Ceiling(diff.TotalMinutes)}m";

        if (diff.TotalHours < 6 && dueAt.Date == now.Date)
        {
            int h = (int)diff.TotalHours;
            int m = diff.Minutes;
            return m > 0 ? $"{prefix}In {h}h {m}m ({dueAt:h:mm tt})" : $"{prefix}In {h}h ({dueAt:h:mm tt})";
        }

        if (dueAt.Date == now.Date)
            return $"{prefix}Today, {dueAt:h:mm tt}";

        if (dueAt.Date == now.Date.AddDays(1))
            return $"{prefix}Tomorrow, {dueAt:h:mm tt}";

        if ((dueAt.Date - now.Date).TotalDays < 7 && dueAt.Date > now.Date)
            return $"{prefix}{dueAt:ddd, h:mm tt}";

        return $"{prefix}{dueAt:MMM d, h:mm tt}";
    }

    private static readonly Dictionary<string, int> SpokenNumbers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5,
        ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10,
        ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14, ["fifteen"] = 15,
        ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19,
        ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["forty-five"] = 45, ["fifty"] = 50
    };

    private static string NormalizeSpokenText(string text)
    {
        string s = text;
        // Normalize "p.m." / "a.m." from speech-to-text
        s = SpokenAmPmRegex().Replace(s, m => m.Groups["ap"].Value.ToLowerInvariant() == "p" ? "pm" : "am");

        // "in an hour" / "in a minute" / "in a day" / "in a week"
        s = SpokenArticleUnitRegex().Replace(s, "in 1 ${unit}");

        // "in twenty five minutes" -> "in 25 minutes", "every fifteen minutes" -> "every 15 minutes"
        s = SpokenCompoundDurationRegex().Replace(s, m =>
        {
            int tens = SpokenNumbers.GetValueOrDefault(m.Groups["tens"].Value, 0);
            int ones = m.Groups["ones"].Success ? SpokenNumbers.GetValueOrDefault(m.Groups["ones"].Value, 0) : 0;
            int total = tens + ones;
            return total > 0 ? $"{m.Groups["prefix"].Value} {total} {m.Groups["unit"].Value}" : m.Value;
        });

        // "at five thirty pm" -> "at 5:30 pm", "at seven o'clock" -> "at 7:00", "at eight pm" -> "at 8 pm"
        s = SpokenClockTimeRegex().Replace(s, m =>
        {
            if (!SpokenNumbers.TryGetValue(m.Groups["h"].Value, out int h) || h > 12)
                return m.Value;
            string prep = m.Groups["prep"].Value;
            string ampm = m.Groups["ampm"].Success ? $" {m.Groups["ampm"].Value}" : string.Empty;
            if (m.Groups["m"].Success && SpokenNumbers.TryGetValue(m.Groups["m"].Value, out int min))
                return $"{prep} {h}:{min:00}{ampm}";
            if (m.Groups["oclock"].Success)
                return $"{prep} {h}:00{ampm}";
            return $"{prep} {h}{ampm}";
        });

        return s;
    }

    private static DateTime NextWeekday(DateTime fromDate, DayOfWeek target, bool forceNextWeek, bool includeToday)
    {
        int diff = ((int)target - (int)fromDate.DayOfWeek + 7) % 7;
        if (diff == 0 && !includeToday) diff = 7;
        if (forceNextWeek && diff < 7) diff += 7;
        return fromDate.AddDays(diff);
    }

    private static string RemoveSpan(string text, int start, int length)
    {
        string before = text[..start];
        string after = text[(start + length)..];
        return MultiSpaceRegex().Replace($"{before} {after}", " ").Trim();
    }

    private static string CleanupTitle(string text)
    {
        string cleaned = text.Trim();
        cleaned = TrailingPrepRegex().Replace(cleaned, string.Empty).Trim();
        cleaned = LeadingPrepRegex().Replace(cleaned, string.Empty).Trim();
        cleaned = cleaned.Trim(',', ';', '-', ':', '.').Trim();
        cleaned = MultiSpaceRegex().Replace(cleaned, " ");

        if (cleaned.Length > 0 && char.IsLower(cleaned[0]))
            cleaned = char.ToUpper(cleaned[0], CultureInfo.CurrentCulture) + cleaned[1..];

        return cleaned;
    }

    [GeneratedRegex(@"^(?:please\s+)?(?:remind\s+me\s+(?:to\s+)?|set\s+(?:a\s+)?(?:continuous\s+|repeating\s+)?reminder\s+(?:to\s+|for\s+)?|(?:continuous|repeating)\s+reminder\s+(?:to\s+|for\s+)?|remember\s+(?:to\s+)?|don'?t\s+forget\s+(?:to\s+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingFillerRegex();

    [GeneratedRegex(@"\b(?:in\s+)?every\s+(?:half\s+an?\s+hour|half\s+hour)\b", RegexOptions.IgnoreCase)]
    private static partial Regex EveryHalfHourRegex();

    [GeneratedRegex(@"\b(?:in\s+)?every\s+(?<num>\d+)\s*(?<unit>minutes?|mins?|m|hours?|hrs?|h|days?|d)\b", RegexOptions.IgnoreCase)]
    private static partial Regex IntervalRecurrenceRegex();

    [GeneratedRegex(@"\b(?:in\s+)?(?:every\s+(?<kind>day|morning|evening|night|week|hour|sunday|sun|monday|mon|tuesday|tues|tue|wednesday|wed|thursday|thurs|thur|thu|friday|fri|saturday|sat)|(?<kind>daily|weekly|hourly))\b", RegexOptions.IgnoreCase)]
    private static partial Regex RecurrenceRegex();

    [GeneratedRegex(@"\bin\s+(?:half\s+an\s+hour|an?\s+half\s+hour)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HalfHourRegex();

    [GeneratedRegex(@"\bin\s+(?:\d+(?:\.\d+)?\s*(?:seconds?|secs?|s|minutes?|mins?|m|hours?|hrs?|h|days?|d|weeks?|wks?|w)(?:\s*(?:and|,)?\s*)?)+", RegexOptions.IgnoreCase)]
    private static partial Regex RelativeDurationRegex();

    [GeneratedRegex(@"(?<num>\d+(?:\.\d+)?)\s*(?<unit>seconds?|secs?|s|minutes?|mins?|m|hours?|hrs?|h|days?|d|weeks?|wks?|w)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DurationPartRegex();

    [GeneratedRegex(@"\b(?:at\s+|by\s+)?(?<word>noon|midnight)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NamedTimeRegex();

    [GeneratedRegex(@"\b(?:(?:at|by|@)\s*(?<h>\d{1,2})(?::(?<m>\d{2}))?\s*(?<ampm>am|pm)?|(?<h>\d{1,2})(?::(?<m>\d{2}))\s*(?<ampm>am|pm)?|(?<h>\d{1,2})\s*(?<ampm>am|pm))\b", RegexOptions.IgnoreCase)]
    private static partial Regex ClockTimeRegex();

    [GeneratedRegex(@"\b(?:(?<qual>tomorrow|tmrw|tmr|today|this)\s+(?<part>morning|afternoon|evening|night)|(?<part>tonight))\b", RegexOptions.IgnoreCase)]
    private static partial Regex PartOfDayRegex();

    [GeneratedRegex(@"\b(?:on\s+)?(?<day>day\s+after\s+tomorrow|tomorrow|tmrw|tmr|today)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RelativeDayRegex();

    [GeneratedRegex(@"\b(?:on\s+)?(?:(?<mon>january|jan|february|feb|march|mar|april|apr|may|june|jun|july|jul|august|aug|september|sept|sep|october|oct|november|nov|december|dec)\s+(?<day>\d{1,2})(?:st|nd|rd|th)?|(?<day>\d{1,2})(?:st|nd|rd|th)?\s+(?<mon>january|jan|february|feb|march|mar|april|apr|may|june|jun|july|jul|august|aug|september|sept|sep|october|oct|november|nov|december|dec))\b", RegexOptions.IgnoreCase)]
    private static partial Regex MonthDayRegex();

    [GeneratedRegex(@"\b(?:on\s+)?(?:(?<y>\d{4})-(?<m>\d{1,2})-(?<d>\d{1,2})|(?<m>\d{1,2})/(?<d>\d{1,2})(?:/(?<y>\d{2,4}))?)\b")]
    private static partial Regex NumericDateRegex();

    [GeneratedRegex(@"\b(?:on\s+)?(?:(?<mod>this|next)\s+)?(?<dow>sunday|sun|monday|mon|tuesday|tues|tue|wednesday|wed|thursday|thurs|thur|thu|friday|fri|saturday|sat)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WeekdayRegex();

    [GeneratedRegex(@"\s+(?:at|on|by|for|in)$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingPrepRegex();

    [GeneratedRegex(@"^(?:to|at|on|by|for)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingPrepRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"\b(?<ap>[ap])\.m\.?", RegexOptions.IgnoreCase)]
    private static partial Regex SpokenAmPmRegex();

    [GeneratedRegex(@"\bin\s+an?\s+(?<unit>seconds?|secs?|minutes?|mins?|hours?|hrs?|days?|weeks?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpokenArticleUnitRegex();

    [GeneratedRegex(@"\b(?<prefix>in|every)\s+(?<tens>one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|thirty|forty|forty-five|fifty)(?:[\s-]+(?<ones>one|two|three|four|five|six|seven|eight|nine))?\s+(?<unit>seconds?|secs?|minutes?|mins?|hours?|hrs?|days?|weeks?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpokenCompoundDurationRegex();

    [GeneratedRegex(@"\b(?<prep>at|by)\s+(?<h>one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)(?:\s+(?<oclock>o'?clock)|\s+(?<m>fifteen|thirty|forty-five|forty|twenty|ten|five))?(?:\s+(?<ampm>am|pm))?\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpokenClockTimeRegex();
}
