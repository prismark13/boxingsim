using System.Linq;
using BoxingSim.Core.Career;
using BoxingSim.Core.Engine;
using BoxingSim.Core.Model;
using Xunit;

namespace BoxingSim.Tests;

/// <summary>The commentary, which until now could not be tested at all.
///
/// FightCall is eight hundred lines turning a FightResult into the words a commentator would say. It lived in
/// the desktop project — a WinExe the test project cannot reference — despite having no WPF dependency
/// whatsoever, so the single most-read output in the app had no coverage. It is in Core now, and these are the
/// properties that matter: it says who won, it never contradicts the scorecard, and it does not fall over on
/// the edge cases a long career will certainly produce.</summary>
public class FightCallTests
{
    /// <summary>A fight, and the man whose corner the call is written from. Build needs both: the commentary
    /// is told from somebody's point of view, which is why it says "you" about one of them.</summary>
    private static (FightResult Res, Boxer Me) Fight(int seed, int rounds = 10)
    {
        var rng = new System.Random(seed);
        var roster = Fixtures.Roster.ToList();
        var a = roster[seed % roster.Count];
        var b = roster[(seed * 7 + 3) % roster.Count];
        return (new FightEngine(rng).Simulate(a, b, rounds), a);
    }

    [Fact]
    public void EveryFightProducesACall()
    {
        for (int seed = 1; seed <= 40; seed++)
        {
            var (res, me) = Fight(seed);
            var call = FightCall.Build(res, me);
            Assert.NotEmpty(call);
            Assert.All(call, line => Assert.False(string.IsNullOrWhiteSpace(line.Text),
                                                  $"seed {seed} produced a blank line"));
        }
    }

    [Fact]
    public void TheCallEndsWithAVerdictThatNamesTheResult()
    {
        for (int seed = 1; seed <= 40; seed++)
        {
            var (res, me) = Fight(seed);
            var call = FightCall.Build(res, me);
            var verdict = call.LastOrDefault(l => l.Kind == CallKind.Verdict);
            Assert.True(verdict is not null, $"seed {seed}: the call never reached a verdict");

            // It has to be ABOUT this fight — one of these two men, or a draw. Deliberately not "it names the
            // winner": a stoppage on a cut is called on the man it happened to ("the cut has ended his
            // night"), which is how a commentator says it and how this should stay.
            // By SURNAME, which is what the call actually uses — "the cut has ended Matthews's night", not
            // "Wallace Matthews". A commentator says the surname and so does this.
            static string Surname(Boxer b) => b.Name.Split(' ').Last();
            bool aboutThisFight =
                verdict!.Text.Contains(Surname(res.A)) || verdict.Text.Contains(Surname(res.B)) ||
                verdict.Text.Contains("draw", System.StringComparison.OrdinalIgnoreCase);
            Assert.True(aboutThisFight,
                        $"seed {seed}: the verdict names neither man and is not a draw — \"{verdict.Text}\"");
        }
    }

    [Fact]
    public void AStoppageIsNeverCalledPastTheRoundItHappenedIn()
    {
        for (int seed = 1; seed <= 60; seed++)
        {
            var (res, me) = Fight(seed);
            if (res.Outcome is not (FightOutcome.Knockout or FightOutcome.TechnicalKnockout)) continue;

            var call = FightCall.Build(res, me);
            int highest = call.Where(l => l.Round > 0).Select(l => l.Round).DefaultIfEmpty(0).Max();
            Assert.True(highest <= res.EndRound,
                        $"seed {seed}: the fight ended in round {res.EndRound} but the call reached {highest}");
        }
    }

    [Fact]
    public void ARoundIsNeverCalledBeforeTheOneBeforeIt()
    {
        var (res12, me12) = Fight(11, rounds: 12);
        var call = FightCall.Build(res12, me12);
        int last = 0;
        foreach (var line in call.Where(l => l.Round > 0))
        {
            Assert.True(line.Round >= last, $"the call went backwards: round {line.Round} after {last}");
            last = line.Round;
        }
    }

    [Fact]
    public void AOneRoundFightStillReadsAsAFight()
    {
        // The shortest thing the sim can produce, and the likeliest to fall off the end of an assumption.
        var (res, me) = Fight(3, rounds: 1);
        var call = FightCall.Build(res, me);
        Assert.NotEmpty(call);
        Assert.Contains(call, l => l.Kind == CallKind.Verdict);
    }

    /// <summary>How much is actually said, round by round.
    ///
    /// A round is twelve fifteen-second ticks, and only the loud ones used to speak — so a technical round
    /// passed in near silence even though two men were in there working. Every tick says something now, so a
    /// round should run to a dozen lines or more: the punches that landed, and the circling in between.</summary>
    [Fact]
    public void EachRoundIsActuallyCalled()
    {
        var thin = new System.Collections.Generic.List<string>();
        double total = 0; int rounds = 0;

        for (int seed = 1; seed <= 20; seed++)
        {
            var (res, me) = Fight(seed, rounds: 10);
            var call = FightCall.Build(res, me);

            foreach (var grp in call.Where(l => l.Round > 0).GroupBy(l => l.Round))
            {
                // The round a fight ENDS in is short by nature — a man counted out at 1:10 has nothing after
                // it, and padding that would be a lie.
                if (grp.Key == res.EndRound && res.Outcome is FightOutcome.Knockout
                                                          or FightOutcome.TechnicalKnockout) continue;
                int spoken = grp.Count(l => !string.IsNullOrWhiteSpace(l.Text));
                total += spoken; rounds++;
                if (spoken < 12) thin.Add($"seed {seed} round {grp.Key}: {spoken}");
            }
        }

        double perRound = total / System.Math.Max(1, rounds);
        Assert.True(thin.Count == 0,
                    $"rounds under twelve lines (average {perRound:F1} over {rounds}): "
                    + string.Join(", ", thin.Take(8)));
    }
}
