namespace OneUI.Core.Services;

/// <summary>
/// Fast, allocation-light fuzzy matcher for Spotlight app and command ranking.
/// Returns 0 for no match, higher positive integers for stronger matches.
/// </summary>
public static class FuzzyMatcher
{
    public static int Score(string query, string candidate)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(candidate))
            return 0;

        var q = query.Trim();
        var c = candidate.Trim();

        if (c.Equals(q, StringComparison.OrdinalIgnoreCase))
            return 1000;

        if (c.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            return 850 - Math.Min(200, c.Length - q.Length);

        var containsIdx = c.IndexOf(q, StringComparison.OrdinalIgnoreCase);
        if (containsIdx >= 0)
        {
            bool wordBoundary = containsIdx == 0 || c[containsIdx - 1] is ' ' or '-' or '_' or '.';
            return (wordBoundary ? 700 : 550) - Math.Min(150, containsIdx * 4);
        }

        // Subsequence match with bonuses for consecutive chars & word starts
        int qi = 0;
        int score = 0;
        int consecutive = 0;

        for (int ci = 0; ci < c.Length && qi < q.Length; ci++)
        {
            if (char.ToLowerInvariant(c[ci]) == char.ToLowerInvariant(q[qi]))
            {
                consecutive++;
                score += 18 + (consecutive * 8);

                if (ci == 0 || c[ci - 1] is ' ' or '-' or '_' or '.')
                    score += 25;

                qi++;
            }
            else
            {
                consecutive = 0;
            }
        }

        return qi == q.Length ? Math.Max(1, score - Math.Min(80, c.Length)) : 0;
    }
}
