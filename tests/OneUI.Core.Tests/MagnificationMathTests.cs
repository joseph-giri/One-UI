using OneUI.Core.Services;
using Xunit;

namespace OneUI.Core.Tests;

public class MagnificationMathTests
{
    [Fact]
    public void CenterDistanceZero_ReturnsMaxScale()
    {
        var scale = MagnificationMath.ComputeScale(0, 120, 1.6);
        Assert.Equal(1.6, scale, precision: 6);
    }

    [Fact]
    public void OutsideRadius_ReturnsOne()
    {
        Assert.Equal(1.0, MagnificationMath.ComputeScale(120, 120, 1.6), precision: 6);
        Assert.Equal(1.0, MagnificationMath.ComputeScale(250, 120, 1.6), precision: 6);
    }

    [Fact]
    public void CurveIsSymmetricAndMonotonic()
    {
        var left = MagnificationMath.ComputeScale(-40, 120, 1.6);
        var right = MagnificationMath.ComputeScale(40, 120, 1.6);
        var farther = MagnificationMath.ComputeScale(80, 120, 1.6);

        Assert.Equal(left, right, precision: 6);
        Assert.True(left > farther);
        Assert.True(farther > 1.0);
    }
}
