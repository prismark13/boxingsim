using System.Linq;
using BoxingSim.Core.Career;
using BoxingSim.Core.Engine;
using BoxingSim.Core.Model;
using Xunit;

namespace BoxingSim.Tests;

/// <summary>Nothing the player reads should contain mojibake.
///
/// Moving FightCall into Core was done with a PowerShell Get-Content/Set-Content round trip, and PowerShell
/// 5.1 reads as the system codepage unless told otherwise. Every em dash in eight hundred lines of commentary
/// came out the other side as "a-EUR-quote", and it went out in a release: "There it is again â€" the check
/// hook from Doyle." The compiler has no opinion about the contents of a string literal, so nothing caught it.
/// This does.</summary>
public class EncodingTests
{
    /// <summary>The three-character wreckage of a UTF-8 punctuation mark read as Windows-1252.</summary>
    private const string Mojibake = "\u00e2\u20ac";

    [Fact]
    public void TheCommentaryIsNotMangled()
    {
        var rng = new System.Random(4);
        var roster = Fixtures.Roster.ToList();
        var res = new FightEngine(rng).Simulate(roster[10], roster[40], 10);

        foreach (var line in FightCall.Build(res, roster[10]))
            Assert.False(line.Text.Contains(Mojibake) || line.Text.Contains('\ufffd'),
                         $"mangled text in the call: \"{line.Text}\"");
    }

    [Fact]
    public void TheNewsIsNotMangled()
    {
        // A warmed copy: what is being read here is the TEXT of the news, and mojibake is in the source file
        // rather than in the world. Six fights' worth of headlines is as good a sample from a warm world as
        // from one seeded across sixty-five years, and it does not cost a minute to get.
        var g = Worlds.Fresh(potential: 88, seed: 7);
        for (int i = 0; i < 6 && g.Offer is not null && !g.Player.Retired; i++) g.TakeOffer();

        foreach (var e in g.Log)
            Assert.False(e.Text.Contains(Mojibake) || e.Text.Contains('\ufffd'),
                         $"mangled text in the news: \"{e.Text}\"");
    }
}
