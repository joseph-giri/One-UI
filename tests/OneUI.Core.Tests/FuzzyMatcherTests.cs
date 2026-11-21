using OneUI.Core.Services;
using Xunit;

namespace OneUI.Core.Tests;

public class FuzzyMatcherTests
{
    [Fact]
    public void ExactAndPrefixMatches_OutrankSubstringAndSubsequence()
    {
        int exact = FuzzyMatcher.Score("Notepad", "Notepad");
        int prefix = FuzzyMatcher.Score("Note", "Notepad++");
        int wordMatch = FuzzyMatcher.Score("Code", "Visual Studio Code");
        int subseq = FuzzyMatcher.Score("vsc", "Visual Studio Code");

        Assert.True(exact > prefix);
        Assert.True(prefix > wordMatch);
        Assert.True(wordMatch > subseq);
        Assert.True(subseq > 0);
    }

    [Fact]
    public void NonMatchingQuery_ReturnsZero()
    {
        Assert.Equal(0, FuzzyMatcher.Score("xyz", "Calculator"));
        Assert.Equal(0, FuzzyMatcher.Score("", "Calculator"));
    }
}
