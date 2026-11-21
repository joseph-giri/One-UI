using OneUI.Core.Services;
using Xunit;

namespace OneUI.Core.Tests;

public class MathEvaluatorTests
{
    [Theory]
    [InlineData("2 + 3 * 4", 14)]
    [InlineData("(2 + 3) * 4", 20)]
    [InlineData("100 / 4 - 5", 20)]
    [InlineData("2 ^ 8", 256)]
    [InlineData("-5 + 12.5", 7.5)]
    [InlineData("10 % 3", 1)]
    public void TryEvaluate_ValidExpressions_ReturnsExpectedResult(string expr, double expected)
    {
        Assert.True(MathEvaluator.TryEvaluate(expr, out var actual));
        Assert.Equal(expected, actual, precision: 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("42")]
    [InlineData("notepad")]
    [InlineData("10 / 0")]
    [InlineData("(2 + 3")]
    public void TryEvaluate_InvalidOrPlainInputs_ReturnsFalse(string expr)
    {
        Assert.False(MathEvaluator.TryEvaluate(expr, out _));
    }

    [Fact]
    public void FormatResult_WholeNumbersUseGrouping()
    {
        Assert.Equal("1,024", MathEvaluator.FormatResult(1024));
    }
}
