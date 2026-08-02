using System.Linq;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace BoxingSim.Tests;

/// <summary>A champion who moves up vacates the division he LEAVES.
///
/// Everything in MoveUpTo happens either side of one line — b.WeightClass = to — and the vacating has to be
/// on the far side of it, using the weight he is leaving rather than the property. Read the property one line
/// too late and a champion moving up would strip the division he is walking into: the man upstairs would lose
/// a belt he had just defended, to somebody who had never boxed in his weight.</summary>
public class MovingUpVacateTests
{
    private readonly ITestOutputHelper _out;
    public MovingUpVacateTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void HeVacatesTheWeightHeLeavesAndNotTheOneHeEnters()
    {
        int checkedMoves = 0;

        for (int seed = 1; seed <= 8; seed++)
        {
            var g = Worlds.Fresh(potential: 95, seed: seed);
            for (int i = 0; i < 55 && g.Offer is not null && !g.Player.Retired; i++)
            {
                if (g.BeltsHeld(g.Player).Any(x => x.Belt is "WBA" or "WBC" or "IBF" or "World") && g.CanMoveUp) break;
                g.TakeOffer();
            }
            if (g.NextDivision is not WeightClass to || g.Player.Retired) continue;
            var mine = g.BeltsHeld(g.Player).Where(x => x.Belt is "WBA" or "WBC" or "IBF" or "World").ToList();
            if (mine.Count == 0) continue;

            var from = g.Player.WeightClass;
            // Who holds what UPSTAIRS, before he gets there.
            var beforeUp = new[] { g.WorldChampionOf(to)?.Name, g.WbcChampionOf(to)?.Name, g.IbfChampionOf(to)?.Name };

            g.MoveUp();
            checkedMoves++;

            var afterUp = new[] { g.WorldChampionOf(to)?.Name, g.WbcChampionOf(to)?.Name, g.IbfChampionOf(to)?.Name };
            Assert.True(beforeUp.SequenceEqual(afterUp),
                        $"seed {seed}: moving up to {to.DisplayName()} changed its champions from "
                        + $"[{string.Join(", ", beforeUp.Select(x => x ?? "vacant"))}] to "
                        + $"[{string.Join(", ", afterUp.Select(x => x ?? "vacant"))}] — he vacated the weight he "
                        + "walked INTO rather than the one he left");

            // And he is not still holding the belts he left behind.
            Assert.True(!g.BeltsHeld(g.Player).Any(x => x.Belt is "WBA" or "WBC" or "IBF" or "World"),
                        $"seed {seed}: he moved up to {to.DisplayName()} still holding a {from.DisplayName()} belt");
            _out.WriteLine($"seed {seed}: {string.Join("/", mine.Select(x => x.Belt))} at {from.DisplayName()} "
                           + $"→ {to.DisplayName()}, upstairs untouched");
        }

        Assert.True(checkedMoves >= 3, $"only {checkedMoves} champions moved up, too few to have tested anything");
    }
}
