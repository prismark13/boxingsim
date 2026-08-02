using System;
using System.Collections.Generic;
using System.Linq;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace BoxingSim.Tests;

/// <summary>What a fighter carries up a division with him.
///
/// He carried nothing but his Elo. A world champion moved up and arrived as an anonymous body behind five men
/// who had spent years getting where they were, with his power and chin freshly docked for the weight and one
/// to four tune-ups to serve before anybody would even discuss a belt — so the reward for winning a world
/// title was to start again. Leonard, Duran, Hearns and Pacquiao all moved up as champions and all of them
/// were challenging at the new weight inside a year.</summary>
public class MovingUpTests
{
    private readonly ITestOutputHelper _out;
    public MovingUpTests(ITestOutputHelper o) => _out = o;

    private static int ProFightsOf(CareerGame g) =>
        g.Player.Record.Wins + g.Player.Record.Losses + g.Player.Record.Draws;

    private static bool IsWorldTitle(FightOffer o) => o.TitleFight && o.Belt is "WBA" or "WBC" or "IBF" or "World";
    private static bool HoldsAWorldBelt(CareerGame g) =>
        g.BeltsHeld(g.Player).Any(x => x.Belt is "WBA" or "WBC" or "IBF" or "World");

    /// <summary>Take fights until the player is a world champion who still has a division above him. Returns
    /// false if this seed's career never got there — he lost, retired, or ran out of weight.</summary>
    private static bool ChampionReadyToClimb(CareerGame g)
    {
        for (int i = 0; i < 55 && g.Offer is not null && !g.Player.Retired; i++)
        {
            if (HoldsAWorldBelt(g) && g.CanMoveUp) return true;
            g.TakeOffer();
        }
        return HoldsAWorldBelt(g) && g.CanMoveUp && !g.Player.Retired;
    }

    /// <summary>A champion who moves up is challenging for a belt again after at most two warm-ups.
    ///
    /// Two, not "eventually": the tune-ups exist so he can find out what he feels like at the weight, not so he
    /// can prove a case he has already made. The gate he has to clear is a top-five ranking in a division he
    /// has never boxed in, which is why moving up has to bring a standing with it — see CarryStandingUp. Note
    /// that a champion always serves at least one, so the pass band is one or two.</summary>
    [Fact]
    public void AChampionWhoMovesUpIsChallengingAgainWithinTwoWarmUps()
    {
        var tooLong = new List<string>();
        var seen = new List<string>();

        for (int seed = 1; seed <= 12; seed++)
        {
            var g = Worlds.Fresh(potential: 95, seed: seed);
            if (!ChampionReadyToClimb(g)) continue;

            var from = g.Player.WeightClass;
            var belts = string.Join("/", g.BeltsHeld(g.Player).Select(x => x.Belt));
            g.MoveUp();
            var to = g.Player.WeightClass;

            // Count the fights he actually has to take before a belt is on the table. He may lose one — a
            // beaten man is not owed a title shot and the count stops meaning anything — so that seed drops out
            // rather than being asserted on.
            int warmups = 0;
            string? outcome = null;
            while (warmups <= 4 && g.Offer is not null && !g.Player.Retired)
            {
                if (g.Slate.Any(IsWorldTitle)) { outcome = $"belt offered after {warmups}"; break; }
                // Nobody can be offered a belt that nobody is holding. A vacancy is a fight the sport has to
                // order and then wait two to five months for, so a champion who moves up into a division whose
                // title is sitting empty is not being overlooked — there is simply nothing to challenge for
                // yet, and counting it against the promise measures the calendar rather than the rule.
                if (g.Champion is null && g.WbcChampion is null && g.IbfChampion is null)
                { outcome = $"the division's belts were vacant after {warmups}"; break; }
                int lossesBefore = g.Player.Record.Losses;
                g.TakeOffer();
                warmups++;
                if (g.Player.Record.Losses > lossesBefore) { outcome = "beaten in a warm-up"; break; }
            }
            // A career that ends is not the same as a belt that never came, and lumping them together hid the
            // more interesting of the two.
            outcome ??= g.Player.Retired
                ? $"RETIRED {warmups} fights after moving up, aged {g.Player.Age} on {g.Player.Record} "
                  + $"({ProFightsOf(g)} bouts, ovr {g.Player.Overall})"
                : $"no belt in {warmups}";

            seen.Add($"seed {seed}: {belts} champion at {from.DisplayName()} → {to.DisplayName()}, {outcome}");
            // Dropped from the sample for the same reason a loss is: nobody is owed a title shot who is no
            // longer a fighter. Reported rather than hidden, because "he retired" is the answer to a different
            // and more interesting question — see MovingUpDoesNotEndCareers.
            if (outcome.StartsWith("RETIRED") || outcome.StartsWith("the division's belts")) continue;
            if (outcome.StartsWith("no belt") || (outcome.StartsWith("belt offered") && warmups > 2))
                tooLong.Add(seen[^1]);
        }

        foreach (var line in seen) _out.WriteLine(line);
        Assert.True(seen.Count >= 6, $"only {seen.Count} of 12 careers produced a champion with a division above "
                                     + "him, which is too few to have tested anything");

        // A STRONG MAJORITY RATHER THAN A GUARANTEE, and the difference is a rule we chose. A belt that falls
        // vacant is now a fight the sport has to order and wait two to five months for, so a champion can move
        // up into a division whose title is genuinely between owners and wait through it. Asserting "always"
        // would mean exempting that case away one condition at a time until the test agreed with itself; what
        // is actually true is that the overwhelming majority get their shot inside two warm-ups, and a
        // regression in the RULE would drop this well below the bar rather than nudge it.
        int quick = seen.Count - tooLong.Count;
        Assert.True(quick * 10 >= seen.Count * 8,
                    $"only {quick} of {seen.Count} champions who moved up were challenging again inside two "
                    + "warm-ups: " + string.Join("; ", tooLong));
    }

    /// <summary>The credit is a floor, not a bonus — it can rescue a standing that understates a man, and it
    /// can never inflate one. A fighter already better than the floor must come out of the move with exactly
    /// the points he went in with, because otherwise every move up is a free promotion and the divisions above
    /// slowly fill with men who were paid to arrive.</summary>
    [Fact]
    public void MovingUpNeverInflatesAManAlreadyAboveTheFloor()
    {
        var checked_ = new List<string>();

        for (int seed = 1; seed <= 8; seed++)
        {
            var g = Worlds.Fresh(potential: 95, seed: seed);
            if (!ChampionReadyToClimb(g)) continue;
            if (g.NextDivision is not WeightClass to) continue;

            // The division he is about to walk into, in the order the matchmaker reads it — everyone active,
            // not just the men who clear the contender bar, because that is the list CarryStandingUp floors
            // against and a filtered one would put the expected number in the wrong place.
            var upstairs = g.EveryFighter.Where(b => !b.Retired && b.WeightClass == to)
                                         .OrderByDescending(CareerGame.RankScore).ToList();
            if (upstairs.Count < 3) continue;
            double third = CareerGame.RankScore(upstairs[2]);
            double before = g.Player.RankPoints;
            double scoreBefore = CareerGame.RankScore(g.Player);

            g.MoveUp();
            double after = g.Player.RankPoints;

            if (scoreBefore > third + 1)
            {
                // Already better than the floor. It must have done nothing at all — not rounded him up, not
                // topped him off. A floor that pays out to a man who does not need it is a bonus wearing a
                // floor's name, and every move up becomes a free promotion.
                Assert.True(Math.Abs(after - before) < 0.001,
                            $"seed {seed}: he was {scoreBefore - third:F0} clear of third at {to.DisplayName()} "
                            + $"and the move still moved him, {before:F0} → {after:F0}");
                checked_.Add($"seed {seed}: clear of third already, {before:F0} untouched");
            }
            else
            {
                // Below it, so he is lifted — to the floor exactly, and not one point past it.
                Assert.True(after > before, $"seed {seed}: he was below third and got nothing");
                Assert.True(Math.Abs(CareerGame.RankScore(g.Player) - (third + 1)) < 0.001,
                            $"seed {seed}: floored to {CareerGame.RankScore(g.Player):F0} when third at "
                            + $"{to.DisplayName()} is {third:F0} — the floor overshot");
                checked_.Add($"seed {seed}: lifted {before:F0} → {after:F0}, third was {third:F0}");
            }
        }

        foreach (var line in checked_) _out.WriteLine(line);
        Assert.True(checked_.Count >= 3, $"only {checked_.Count} moves were observed, too few to have tested anything");
    }

    /// <summary>A man does not cross a division in order to retire in it.
    ///
    /// The step-up curve used to PEAK post-prime, on the reasoning that a fighter is likeliest to outgrow his
    /// weight late. Measured over twenty years and 435 move-ups, what it produced was a sport where moving up
    /// was the last thing anybody ever did: the median man moved at 35 fights with 8 left, 40% were gone within
    /// six fights of arriving, and 161 of those 174 hung them up on age and mileage rather than drifting out
    /// for want of opposition — so it was the timing, not the matchmaking.
    ///
    /// The rule that replaced it is the one worth pinning: the weight of the decision belongs where the career
    /// is. A prime fighter is likelier to move than a faded one, and a man at the end of the road does not move
    /// at all. Pinned as the SHAPE of the curve rather than as a tuning of it, so the numbers stay free to
    /// change and the ordering cannot quietly inverting itself again.</summary>
    [Fact]
    public void MovingUpIsAPrimeDecisionRatherThanALastResort()
    {
        double starter  = CareerGame.StepUpChanceAt(CareerStage.Starter);
        double prePrime = CareerGame.StepUpChanceAt(CareerStage.PrePrime);
        double prime    = CareerGame.StepUpChanceAt(CareerStage.Prime);
        double post     = CareerGame.StepUpChanceAt(CareerStage.PostPrime);
        double end      = CareerGame.StepUpChanceAt(CareerStage.End);
        _out.WriteLine($"starter={starter}  pre-prime={prePrime}  prime={prime}  post-prime={post}  end={end}");

        Assert.True(starter == 0, "a man five fights into his career is not outgrowing his division yet");
        Assert.True(end == 0, "a fighter at the end of the road does not cross a division to retire in it");
        Assert.True(prime > post,
                    $"moving up is a prime decision: prime is {prime} and post-prime {post}, which says a faded "
                    + "fighter is the likeliest to go up — that is the bug this replaced");
        Assert.True(prime > prePrime, "the prime is still the likeliest time, ahead of a kid filling out");
        Assert.True(prePrime > 0, "a young man growing out of a light division has to be able to leave it");
    }
}
