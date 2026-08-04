using BoxingSim.Core.Career;
using BoxingSim.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace BoxingSim.Tests;

/// <summary>Whatever is in the ring is on the line.
///
/// A title bout used to settle exactly ONE belt — the one the offer happened to be billed as — and the man
/// picking the challenger excluded one named rival rather than asking who held what. Between them that made
/// the fight boxing never makes: two reigning champions meeting with only one man's belt at stake. Measured
/// across six worlds before this, 857 of 14,450 title challengers already held a world belt, and the player
/// could beat a champion and walk away with nothing.
///
/// These pin the rule rather than the numbers: a belt-holder is not somebody else's challenger, and when two
/// of them do meet it is a unification and everything moves.</summary>
public class ChampionMeetsChampionTests
{
    private readonly ITestOutputHelper _out;
    public ChampionMeetsChampionTests(ITestOutputHelper o) => _out = o;

    private static bool IsWorldTitle(string? note) =>
        note is not null && note != "unification"
        && note.EndsWith(" title", StringComparison.Ordinal)
        && !note.StartsWith("NABF", StringComparison.Ordinal)
        && !note.StartsWith("European", StringComparison.Ordinal)
        && !note.StartsWith("Commonwealth", StringComparison.Ordinal);

    /// <summary>The one the player actually sees: he beats a reigning champion in a title fight, and what that
    /// man was carrying is now his. Checked against the belts held BEFORE the bell, because a fight that ends
    /// a career vacates them and the winner still has to end up with them.</summary>
    [Fact]
    public void BeatingAChampionTakesWhatHeWasHolding()
    {
        int checked_ = 0;

        for (int seed = 1; seed <= 10; seed++)
        {
            var g = Worlds.Fresh(potential: 92, seed: seed);
            for (int i = 0; i < 120 && !g.Player.Retired; i++)
            {
                if (g.Offer is not { } offer) { if (g.WaitAWeek() is null) break; continue; }

                // TO FIGHT NIGHT FIRST. Taking an offer runs the world forward to the date it was made for,
                // and the sport keeps moving in between: read the opponent's belts when the offer arrives and
                // he may have lost them to somebody else by the time the bell goes, so the test would demand
                // a strap that was no longer his to lose.
                while (g.DaysToFight > 0 && g.WaitAWeek() is not null) { }
                if (g.Player.Retired || g.Offer is null) break;

                var opp = offer.Opponent;
                var his = g.BeltsHeld(opp)
                           .Where(x => !CareerGame.IsRegionalBelt(x.Belt) && x.Belt is not ("Ring" or "Lineal"))
                           .Select(x => x.Belt).ToList();

                var res = g.TakeOffer();
                if (res is null) continue;
                if (res.IsDraw || res.Winner!.Id != g.Player.Id) continue;
                if (his.Count == 0) continue;

                // He just beat a man who held world belts. Either it was a title fight and he has them, or
                // the fight had no belt on it at all — and that second case must not exist: the matchmaker is
                // not allowed to put a reigning champion in front of him with nothing at stake.
                Assert.True(offer.TitleFight,
                            $"seed {seed}: he was matched with {opp.Name}, who held the "
                            + $"{string.Join(" and ", his)}, and there was no belt on the fight");

                var now = g.BeltsHeld(g.Player).Select(x => x.Belt).ToHashSet();
                foreach (var belt in his)
                    Assert.True(now.Contains(belt),
                                $"seed {seed}: he beat {opp.Name} for the {string.Join(" and ", his)} "
                                + $"and does not hold the {belt} — he holds {string.Join(", ", now)}");
                checked_++;
            }
        }

        _out.WriteLine($"{checked_} wins over a reigning champion, every belt accounted for");
        Assert.True(checked_ >= 5,
                    $"only {checked_} careers beat a reigning champion, which is too few to have tested anything");
    }

    /// <summary>And the world does it too. Two men holding belts in the same division is the sport's biggest
    /// fight sitting unmade; it used to sit unmade for a decade at a time because only the WBA and the WBC
    /// could ever be merged, and the IBF was in nobody's way.</summary>
    [Fact]
    public void TheChampionsOfADivisionActuallyMeet()
    {
        var u = new Universe(new UniverseSettings
        {
            StartYear = 1980, WarmupYears = 12, Seed = 77,
            Divisions = new[] { WeightClass.Middleweight, WeightClass.Welterweight, WeightClass.Heavyweight },
        }, Fixtures.Roster.ToList());

        var start = u.World.Date;
        for (int w = 0; w < 52 * 20; w++) u.PlayWeek();
        double years = (u.World.Date.DayNumber - start.DayNumber) / 365.25;

        int unifications = u.World.EveryFighter
            .SelectMany(b => b.History)
            .Where(h => h.Date >= start && h.Note == "unification")
            .Select(h => (h.Date, h.Opponent))
            .Distinct().Count() / 2;   // both men carry the bout

        // Measured at 14 across sixty division-years. The bar is half of that, because this is a rate and
        // a rate wanders: what it has to catch is the RULE going back, and the rule going back means two or
        // three — which is where this sat when only the WBA and the WBC could ever be merged.
        _out.WriteLine($"{unifications} unifications across 3 divisions in {years:0} years");
        Universe.Release();
        Assert.True(unifications >= 7,
                    $"only {unifications} unifications in {years:0} years across three divisions: the "
                    + "champions of a division are not meeting");
    }
}
