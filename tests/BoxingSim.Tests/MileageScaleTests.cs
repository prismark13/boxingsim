// CareerMileage sits in the League folder but declares the Generation namespace. Noted rather than moved:
// renaming a namespace is a wide, mechanical diff and this is not the pass for it.
using BoxingSim.Core.Generation;
using Xunit;

namespace BoxingSim.Tests;

/// <summary>The mileage dials, and the leak they used to allow.
///
/// LengthScale and ActivityScale are process-wide: a universe sets them to build a harder or a gentler sport,
/// and career mode expects them at 1. They used to be plain settable statics with a separate ResetScales()
/// that every caller had to remember — and a caller that forgot left the next career running on the last
/// universe's rules, silently, with every fighter ageing on the wrong schedule and nothing on screen to say
/// so. These lock in the behaviour that makes forgetting impossible.</summary>
public class MileageScaleTests
{
    [Fact]
    public void TheDialsStartWhereCareerModeExpectsThem()
    {
        Assert.Equal(1.0, CareerMileage.LengthScale);
        Assert.Equal(1.0, CareerMileage.ActivityScale);
    }

    [Fact]
    public void AScopePutsThemBackWhenItIsLetGo()
    {
        using (CareerMileage.Scale(2.5, 0.5))
        {
            Assert.Equal(2.5, CareerMileage.LengthScale);
            Assert.Equal(0.5, CareerMileage.ActivityScale);
        }
        Assert.Equal(1.0, CareerMileage.LengthScale);
        Assert.Equal(1.0, CareerMileage.ActivityScale);
    }

    [Fact]
    public void ScopesRestoreWhatTheyFoundRatherThanAssumingTheDefault()
    {
        using var outer = CareerMileage.Scale(2.0, 2.0);
        using (CareerMileage.Scale(0.4, 0.4))
            Assert.Equal(0.4, CareerMileage.LengthScale);

        // The inner scope must hand back the OUTER world, not the factory default.
        Assert.Equal(2.0, CareerMileage.LengthScale);
    }

    [Fact]
    public void LettingGoTwiceDoesNotUndoSomebodyElsesScope()
    {
        var first = CareerMileage.Scale(2.0, 2.0);
        first.Dispose();
        Assert.Equal(1.0, CareerMileage.LengthScale);

        using var second = CareerMileage.Scale(3.0, 3.0);
        first.Dispose();                              // a stale handle, released again
        Assert.Equal(3.0, CareerMileage.LengthScale); // the live scope survives it
    }

    [Fact]
    public void TheDialsAreClampedToSomethingASportCanSurvive()
    {
        using (CareerMileage.Scale(99.0, 99.0)) Assert.Equal(3.0, CareerMileage.LengthScale);
        using (CareerMileage.Scale(0.0, 0.0)) Assert.Equal(0.3, CareerMileage.LengthScale);
    }
}
