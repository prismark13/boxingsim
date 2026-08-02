using System.Linq;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace BoxingSim.Tests;

/// <summary>The man on your poster does not box somebody else first.
///
/// A fight is agreed weeks or months before it happens, and the world went on matching the opponent in the
/// meantime — so he could be beaten, cut, stopped or suspended between the handshake and the first bell, and
/// the player walked out to face a man whose record no longer matched the one he had studied. Now that a
/// career can end in the ring he could also simply be gone.</summary>
public class BookedOpponentTests
{
    private readonly ITestOutputHelper _out;
    public BookedOpponentTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void ABookedOpponentDoesNotFightInTheMeantime()
    {
        var bad = new System.Collections.Generic.List<string>();
        int watched = 0;

        for (int seed = 1; seed <= 4; seed++)
        {
            var g = Worlds.Fresh(potential: 88, seed: seed);
            for (int fight = 0; fight < 10 && g.Offer is not null && !g.Player.Retired; fight++)
            {
                var opp = g.Offer.Opponent;
                // What his record and his last night look like the day the fight is made.
                int before = opp.Record.Wins + opp.Record.Losses + opp.Record.Draws;
                var lastBefore = opp.History.Count > 0 ? opp.History.Max(h => h.Date) : default;

                // Walk the calendar to fight night the way the camp screen does.
                int guard = 0;
                while (g.WaitAWeek() is not null && guard++ < 60) { }
                watched++;

                int after = opp.Record.Wins + opp.Record.Losses + opp.Record.Draws;
                var lastAfter = opp.History.Count > 0 ? opp.History.Max(h => h.Date) : default;
                if (after != before)
                    bad.Add($"seed {seed}: {opp.Name} was booked against the player and boxed "
                            + $"{after - before} time(s) first — {lastBefore:d MMM yyyy} to {lastAfter:d MMM yyyy}");

                g.TakeOffer();
            }
        }

        _out.WriteLine($"{watched} booked fights watched from agreement to first bell");
        Assert.True(watched >= 20, $"only {watched} fights were followed, too few to have tested anything");
        Assert.True(bad.Count == 0, string.Join("; ", bad.Take(5)));
    }
}
