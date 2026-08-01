using BoxingSim.Core.Career;
using BoxingSim.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace BoxingSim.Tests;

/// <summary>The line of succession: who has held each belt, and in what order.
///
/// The world knew who held every belt today and nothing about who had held them before. A lineage is only
/// worth having if it is COHERENT — one holder at a time, each man taking it from the one the record says he
/// took it from — so that is what these assert, rather than that it is merely non-empty.</summary>
public class BeltLineageTests
{
    private readonly ITestOutputHelper _out;
    public BeltLineageTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void ABeltHasOneHolderAtATimeAndTheLineJoinsUp()
    {
        var u = new Universe(new UniverseSettings
        {
            StartYear = 1965, WarmupYears = 4, Seed = 17,
            Divisions = new[] { WeightClass.Middleweight, WeightClass.Heavyweight },
        }, Fixtures.Roster.ToList());
        for (int w = 0; w < 52 * 8; w++) u.PlayWeek();
        var g = u.World;

        int reignsChecked = 0;
        foreach (var wc in new[] { WeightClass.Middleweight, WeightClass.Heavyweight })
        foreach (var belt in g.BeltsOf(wc))
        {
            var line = g.LineageOf(wc, belt == g.LinealBelt ? "Ring" : belt);
            if (line.Count == 0) continue;

            _out.WriteLine($"{wc} {belt}: {line.Count} reigns, {line[0].Won:yyyy} to " +
                           $"{(line[^1].Lost?.ToString("yyyy") ?? "present")}");

            BeltReign? previous = null;
            foreach (var r in line)
            {
                Assert.False(string.IsNullOrWhiteSpace(r.Holder), $"a {wc} {belt} reign has no holder");
                Assert.True(r.Lost is null || r.Lost >= r.Won,
                            $"{r.Holder} lost the {belt} on {r.Lost} but won it on {r.Won}");

                if (previous is not null)
                {
                    // One at a time: the previous reign must have ended before this one began.
                    Assert.True(previous.Lost is not null,
                                $"{previous.Holder}'s {wc} {belt} reign never closed, yet {r.Holder} won it after him");
                    Assert.True(previous.Lost <= r.Won,
                                $"{previous.Holder} held the {wc} {belt} until {previous.Lost} but {r.Holder} "
                                + $"won it on {r.Won} — two men held one belt at once");

                    // The line joins up: whoever he took it from is whoever lost it, when it changed hands
                    // directly rather than passing through vacant.
                    if (r.TookFrom is not null)
                        Assert.True(r.TookFrom == previous.Holder,
                                    $"{r.Holder} is recorded as taking the {wc} {belt} from {r.TookFrom}, but the "
                                    + $"man before him in the line is {previous.Holder}");
                }
                previous = r;
                reignsChecked++;
            }

            // Only the last reign of a belt may still be running.
            for (int i = 0; i < line.Count - 1; i++)
                Assert.True(line[i].Lost is not null, $"{line[i].Holder} has an open {wc} {belt} reign in the middle of the line");
        }

        Assert.True(reignsChecked >= 10,
                    $"only {reignsChecked} reigns were recorded in eight years, which is too few to have "
                    + "tested anything");
        _out.WriteLine($"{reignsChecked} reigns checked");
        Universe.Release();
    }

    /// <summary>A lineage outlives the men in it. Retired fighters are pruned from a save, so a reign holds
    /// the name rather than a reference — and has to come back whole.</summary>
    [Fact]
    public void TheLineSurvivesASave()
    {
        var g = Worlds.Fresh(potential: 90, seed: 6);
        for (int i = 0; i < 15 && g.Offer is not null && !g.Player.Retired; i++) g.TakeOffer();

        var wc = g.Player.WeightClass;
        var before = g.BeltsOf(wc).SelectMany(b => g.LineageOf(wc, b == g.LinealBelt ? "Ring" : b)).ToList();
        Assert.True(before.Count > 0, "no belt changed hands, so there is no line to round-trip");

        var reloaded = CareerGame.Load(g.ToSave(), new Random(1));
        var after = reloaded.BeltsOf(wc).SelectMany(b => reloaded.LineageOf(wc, b == reloaded.LinealBelt ? "Ring" : b)).ToList();

        Assert.Equal(before.Count, after.Count);
        foreach (var (b, a) in before.Zip(after))
        {
            Assert.Equal(b.Holder, a.Holder);
            Assert.Equal(b.Belt, a.Belt);
            Assert.Equal(b.Won, a.Won);
            Assert.Equal(b.Lost, a.Lost);
            Assert.Equal(b.TookFrom, a.TookFrom);
        }
        _out.WriteLine($"{before.Count} reigns round-tripped intact");
    }
}
