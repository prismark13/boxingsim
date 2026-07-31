using System;
using System.Collections.Generic;
using System.Linq;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;
using Xunit;

namespace BoxingSim.Tests;

/// <summary>Who the player is allowed to be offered.
///
/// BuildOffer picks one man and then rewrites that choice through ten sequential guard passes, and the rules
/// those passes encode were asserted NOWHERE — MatchmakingTests covers when a man may box, not who he may box.
/// The only thing standing behind any of it was the golden master, which says a career changed without saying
/// which rule broke.
///
/// So these are written against the behaviour as it stands, before the passes are replaced by scoring. They
/// are what "the refactor preserved the intent" has to mean, because the fingerprint is expected to move and
/// therefore cannot answer the question.
///
/// Each walks whole careers and judges every offer, rather than sampling one, because a guard that holds
/// ninety-nine times in a hundred is a guard that fails in every career anybody plays.</summary>
public class OpponentTests
{
    private sealed record Seen(int Seed, FightOffer Offer, int PlayerFights, bool PlayerRanked, int OpponentPlace);

    /// <summary>Walked once and shared. Standing a world up takes decades of warm-up and every offer taken
    /// runs a fortnight of cards in every division, so a net that built a career per assertion took nine
    /// minutes — which is a net nobody runs, and therefore not a net. Two careers seeded the cheap way still
    /// puts a few hundred offers through every guard.</summary>
    private static readonly Lazy<List<Seen>> Walk = new(() => Offers(2, 22));

    /// <summary>Walk several careers and collect every offer the player was shown, with the facts that decide
    /// whether it was legal at the time it was made.</summary>
    private static List<Seen> Offers(int careers, int fights)
    {
        var all = new List<Seen>();
        for (int seed = 1; seed <= careers; seed++)
        {
            var rng = new Random(seed);
            var player = CareerGame.CreatePlayer(rng, "Probe Man", "USA", WeightClass.Middleweight,
                                                 potential: 70 + seed * 6);
            var g = new CareerGame(1972, player, Fixtures.Roster.ToList(), rng, WeightClass.Middleweight,
                                   warmupYears: 12);

            for (int i = 0; i < fights && g.Offer is not null && !g.Player.Retired; i++)
            {
                var board = g.RankingOf(g.Player.WeightClass, 40).Select(b => b.Id).ToList();
                int mine = board.IndexOf(g.Player.Id);
                int his = board.IndexOf(g.Offer.Opponent.Id);
                all.Add(new Seen(seed, g.Offer, g.Player.History.Count, mine >= 0 && mine < 20, his));
                g.TakeOffer();
            }
        }
        return all;
    }

    private static bool IsChampion(CareerGame g, Boxer b) =>
        g.WorldChampionOf(b.WeightClass)?.Id == b.Id
        || g.WbcChampionOf(b.WeightClass)?.Id == b.Id
        || g.IbfChampionOf(b.WeightClass)?.Id == b.Id;

    [Fact]
    public void EveryOfferHasSomebodyInTheOtherCorner()
    {
        var seen = Walk.Value;
        Assert.NotEmpty(seen);
        Assert.All(seen, s => Assert.NotEqual(0, s.Offer.Opponent.Id));
    }

    /// <summary>A reigning world champion only ever meets the player with his belt on the line. Without this
    /// the player could beat the WBA champion in a stay-busy bout and walk away with nothing.</summary>
    [Fact]
    public void AWorldChampionIsOnlyMetForHisBelt()
    {
        var rng = new Random(3);
        var player = CareerGame.CreatePlayer(rng, "Probe Man", "USA", WeightClass.Middleweight, potential: 88);
        var g = new CareerGame(1972, player, Fixtures.Roster.ToList(), rng, WeightClass.Middleweight,
                               warmupYears: 12);

        var bad = new List<string>();
        for (int i = 0; i < 30 && g.Offer is not null && !g.Player.Retired; i++)
        {
            var o = g.Offer;
            if (!o.TitleFight && IsChampion(g, o.Opponent))
                bad.Add($"{o.Opponent.Name} ({o.Context}) at {g.Date:yyyy}");
            g.TakeOffer();
        }
        Assert.True(bad.Count == 0,
                    "a world champion was offered in a non-title bout: " + string.Join("; ", bad.Take(5)));
    }

    /// <summary>Before he is world-ranked, the player is kept away from the division's leading men BY RANKING,
    /// not merely by rating — an unproven #1 is often a young fighter whose rating has not caught up.
    ///
    /// A rematch is exempt, deliberately and by design: a return the sport is asking for beats every guard
    /// below it, which is the point of the demand existing at all.</summary>
    [Fact]
    public void AnUnrankedFighterIsKeptOffTheTopOfTheDivision()
    {
        var bad = Walk.Value
            .Where(s => !s.PlayerRanked && !s.Offer.TitleFight && !s.Offer.Context.StartsWith("rematch")
                     && s.OpponentPlace >= 0 && s.OpponentPlace < 15)
            .ToList();

        Assert.True(bad.Count == 0,
                    $"{bad.Count} offers put an unranked player in with the division's top 15: "
                    + string.Join("; ", bad.Take(5).Select(b => $"#{b.OpponentPlace + 1} {b.Offer.Opponent.Name} ({b.Offer.Context})")));
    }

    /// <summary>The same man is not served up again and again. A rematch the sport is asking for is exempt —
    /// it says so in its own context line.</summary>
    [Fact]
    public void TheSameManIsNotOfferedStraightBack()
    {
        var rng = new Random(5);
        var player = CareerGame.CreatePlayer(rng, "Probe Man", "USA", WeightClass.Middleweight, potential: 84);
        var g = new CareerGame(1972, player, Fixtures.Roster.ToList(), rng, WeightClass.Middleweight,
                               warmupYears: 12);

        var bad = new List<string>();
        for (int i = 0; i < 30 && g.Offer is not null && !g.Player.Retired; i++)
        {
            var o = g.Offer;
            var last3 = g.Player.History.TakeLast(3).Select(h => h.Opponent).ToList();
            if (!o.Context.StartsWith("rematch") && last3.Contains(o.Opponent.Name))
                bad.Add($"{o.Opponent.Name} again after {string.Join(", ", last3)}");
            g.TakeOffer();
        }
        Assert.True(bad.Count == 0, "a man was re-offered inside three fights: " + string.Join("; ", bad.Take(4)));
    }

    /// <summary>Nobody is met four times. Three fights is a trilogy; a fourth is a road show.</summary>
    [Fact]
    public void NobodyIsMetMoreThanThreeTimes()
    {
        var rng = new Random(6);
        var player = CareerGame.CreatePlayer(rng, "Probe Man", "USA", WeightClass.Middleweight, potential: 84);
        var g = new CareerGame(1972, player, Fixtures.Roster.ToList(), rng, WeightClass.Middleweight,
                               warmupYears: 12);
        for (int i = 0; i < 34 && g.Offer is not null && !g.Player.Retired; i++) g.TakeOffer();

        var worst = g.Player.History.GroupBy(h => h.Opponent).OrderByDescending(x => x.Count()).FirstOrDefault();
        Assert.True(worst is null || worst.Count() <= 3,
                    $"{worst?.Key} was met {worst?.Count()} times");
    }

    /// <summary>A debutant does not fight another debutant. The man in the other corner early on is a
    /// PROFESSIONAL OPPONENT: fights behind him, and a record that says he loses. This was measured and found
    /// broken once — 79% of a player's first fourteen opponents carried under a dozen bouts, and only 17% had
    /// a losing record, which is a parade of other people's prospects rather than a record being built.</summary>
    [Fact]
    public void TheEarlyOppositionAreProfessionalOpponents()
    {
        var seen = Walk.Value.Where(s => s.PlayerFights < 12 && !s.Offer.TitleFight).ToList();
        Assert.NotEmpty(seen);

        int seasoned = seen.Count(s => s.Offer.Opponent.Record.Fights >= 8);
        int losing = seen.Count(s =>
        {
            var r = s.Offer.Opponent.Record;
            int decided = r.Wins + r.Losses;
            return decided > 0 && r.Wins / (double)decided <= 0.70;
        });

        Assert.True(seasoned >= seen.Count * 0.7,
                    $"only {seasoned} of {seen.Count} early opponents had 8+ fights");
        Assert.True(losing >= seen.Count * 0.4,
                    $"only {losing} of {seen.Count} early opponents had a losing-ish record");
    }

    /// <summary>The distance follows his mileage: six-rounders to begin with, then eight, ten once he is
    /// established, and the championship distance only at the top.
    ///
    /// Bounded loosely on purpose. The rule is written against the sim's own count of professional bouts and
    /// this can only see the ledger, which is not the same number, so a fight either side of a boundary is
    /// not evidence of anything. A novice put in over twelve is.</summary>
    [Fact]
    public void TheDistanceFollowsHisMileage()
    {
        var bad = Walk.Value
            .Where(s => !s.Offer.TitleFight && s.Offer.Context != "eliminator")
            .Where(s => s.PlayerFights <= 5 && s.Offer.Rounds > 6
                     || s.PlayerFights <= 11 && s.Offer.Rounds > 8
                     || s.PlayerFights <= 16 && s.Offer.Rounds > 10)
            .ToList();

        Assert.True(bad.Count == 0,
                    $"{bad.Count} offers were over the distance for his experience: "
                    + string.Join("; ", bad.Take(5).Select(b => $"{b.PlayerFights} fights, {b.Offer.Rounds} rounds")));
    }

    /// <summary>A title shot is EARNED. It never appears before the apprenticeship is served, and never
    /// back-to-back — after a title bout he rebuilds before the next one.</summary>
    [Fact]
    public void ATitleShotIsEarnedAndNeverBackToBack()
    {
        var early = new List<string>();
        var backToBack = new List<string>();

        foreach (var career in Walk.Value.GroupBy(s => s.Seed))
        {
            int lastWorldTitleAt = -100;
            foreach (var s in career)
            {
                bool world = s.Offer.TitleFight && s.Offer.Belt is "WBA" or "WBC" or "IBF";
                if (!world) continue;
                // A DEFENCE is not a shot: a champion is expected to be back with his belt on the line, and
                // the rebuild gap is about challenging, not about holding.
                if (s.Offer.Context.Contains("defence")) { lastWorldTitleAt = s.PlayerFights; continue; }
                if (s.PlayerFights < 14) early.Add($"seed {s.Seed}: {s.Offer.Belt} shot at {s.PlayerFights} fights");
                if (s.PlayerFights - lastWorldTitleAt < 3)
                    backToBack.Add($"seed {s.Seed}: {s.Offer.Belt} shot {s.PlayerFights - lastWorldTitleAt} fights after the last ({s.Offer.Context})");
                lastWorldTitleAt = s.PlayerFights;
            }
        }

        Assert.True(early.Count == 0, "a world title shot came too early: " + string.Join("; ", early.Take(4)));
        Assert.True(backToBack.Count == 0, "world title shots came back to back: " + string.Join("; ", backToBack.Take(4)));
    }

    /// <summary>Only ever one fight on the table. This is the invariant a slate of offers would have to keep,
    /// and it is written now, while it is trivially true, so that it is already there to break.</summary>
    [Fact]
    public void OnlyOneFightIsEverOnTheTable()
    {
        var rng = new Random(8);
        var player = CareerGame.CreatePlayer(rng, "Probe Man", "USA", WeightClass.Middleweight, potential: 86);
        var g = new CareerGame(1972, player, Fixtures.Roster.ToList(), rng, WeightClass.Middleweight,
                               warmupYears: 12);
        for (int i = 0; i < 20 && !g.Player.Retired; i++)
        {
            Assert.NotNull(g.Offer);
            g.TakeOffer();
        }
    }
}
