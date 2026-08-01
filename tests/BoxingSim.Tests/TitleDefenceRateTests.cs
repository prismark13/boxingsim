using BoxingSim.Core.Career;
using BoxingSim.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace BoxingSim.Tests;

/// <summary>How often a champion is seen.
///
/// A world champion is barred from ordinary cards — when he fights, it is a defence — so one probability
/// decides whether he appears in the sport at all. It was set so low that a reigning champion averaged a
/// defence a year and a bad run of rolls left him idle far longer: Joe Frazier held two belts and did not box
/// for the nine months after January 1975.
///
/// A champion defends one to four times a year, five months apart on average. That is a rate, so it is
/// measured over a world rather than asserted about one man.</summary>
public class TitleDefenceRateTests
{
    private readonly ITestOutputHelper _out;
    public TitleDefenceRateTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void ChampionsDefendAtAHumanRate()
    {
        var u = new Universe(new UniverseSettings
        {
            StartYear = 1965, WarmupYears = 4, Seed = 31,
            Divisions = new[] { WeightClass.Middleweight, WeightClass.Welterweight, WeightClass.Heavyweight },
        }, Fixtures.Roster.ToList());

        var start = u.World.Date;
        for (int w = 0; w < 52 * 6; w++) u.PlayWeek();
        double years = (u.World.Date.DayNumber - start.DayNumber) / 365.25;

        // Per MAN, not per division: a division has two or three belts, so its title nights are several
        // champions' defences added together and say nothing about how often any one of them boxes.
        bool IsWorldTitle(string? note) =>
            note is not null && note.EndsWith(" title", StringComparison.Ordinal)
            && !note.StartsWith("NABF", StringComparison.Ordinal)
            && !note.StartsWith("European", StringComparison.Ordinal)
            && !note.StartsWith("British", StringComparison.Ordinal)
            && !note.StartsWith("Commonwealth", StringComparison.Ordinal);

        var gaps = new List<int>();
        var perYear = new List<double>();
        int championsSeen = 0;

        foreach (var b in u.World.EveryFighter)
        {
            var nights = b.History.Where(h => h.Date >= start && IsWorldTitle(h.Note))
                                  .Select(h => h.Date).Distinct().OrderBy(d => d).ToList();
            if (nights.Count < 2) continue;   // one fight tells us nothing about a rate
            championsSeen++;
            for (int i = 1; i < nights.Count; i++) gaps.Add(nights[i].DayNumber - nights[i - 1].DayNumber);

            double span = (nights[^1].DayNumber - nights[0].DayNumber) / 365.25;
            if (span >= 0.5) perYear.Add((nights.Count - 1) / span);
        }

        Assert.True(championsSeen >= 3,
                    $"only {championsSeen} men made more than one title defence in {years:0} years, so there "
                    + "is no rate here to measure");

        gaps.Sort();
        double meanGap = gaps.Average();
        int medianGap = gaps[gaps.Count / 2];
        double meanPerYear = perYear.Count > 0 ? perYear.Average() : 0;

        _out.WriteLine($"{years:0.0} years; {championsSeen} champions with more than one defence");
        _out.WriteLine($"  defences per champion per year: mean {meanPerYear:0.00}");
        _out.WriteLine($"  gap between a champion's defences: mean {meanGap:0} days, median {medianGap} days");
        _out.WriteLine($"  shortest {gaps[0]}, longest {gaps[^1]}");

        Assert.True(meanPerYear >= 1.0 && meanPerYear <= 4.0,
                    $"champions averaged {meanPerYear:0.00} defences a year; one to four is the range a real "
                    + "champion keeps");
        // The MEDIAN, deliberately. These gaps are between a man's world title fights, and the long ones are
        // not idle champions: they are men who won a belt, lost it, and challenged again years later, which is
        // a career and not a fault. The mean is dragged around by those; the middle of the distribution is
        // what "five months between defences" actually describes.
        Assert.True(medianGap >= 110 && medianGap <= 210,
                    $"the median gap between a champion's title fights was {medianGap} days; the target is "
                    + "about five months, and either side of that is a different sport");
    }
}
