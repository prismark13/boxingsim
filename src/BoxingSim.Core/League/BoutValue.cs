using BoxingSim.Core.Model;

namespace BoxingSim.Core.League;

/// <summary>What is at stake in a bout, which is the largest part of what it is worth.</summary>
public enum BoutStakes
{
    None,
    Regional,      // a national or continental strap
    Eliminator,    // the winner is next in line
    WorldTitle,
    Unification,   // two belts in one ring
    Undisputed,    // all of them
}

/// <summary>How big a fight is.
///
/// The sim kept answering this question separately every time it came up, and the answers disagreed. Whether a
/// bout was worth stopping the build-up for was "a title fight, or an eliminator, or anybody in the top 20".
/// Whether a headline was drawn as major was a different rule. Where the player's own fight sat on his card —
/// main event, chief support, opener — was not a judgement at all, but a random roll. Three hand-written
/// answers and a coin, to one question that has a real answer.
///
/// So it is one number, and everything that has an opinion about how big a fight is reads it.
///
/// IT DOES NOT PICK FIGHTS. This orders and selects among bouts that have already been made; it must never
/// decide who meets whom. A matchmaker that maximised this would have every prospect chasing the best
/// available name, and the club shows, the tune-ups and the gentle rebuilding jobs that a real career is
/// mostly made of would stop existing. Who fights whom is <c>BuildOffer</c>'s business. This is worth.
///
/// The scale is arbitrary but the ORDER is the point: roughly 0 for two novices in a six-rounder, ~1,800 for
/// an undisputed heavyweight championship between two unbeaten greats.
///
/// The first version of this got the central thing wrong, and it is worth writing down because it is an easy
/// mistake to make twice. It scored a bout on its WEAKER man, reasoning that a fight is only as good as the
/// lesser fighter in it — which is true of how good the FIGHT is, and false of how big the NIGHT is. It rated
/// Ali against a 28-rated journeyman below a club show between two unknowns. That is not how boxing sells: a
/// great fighter is a draw wherever he appears, and so is a prospect people have come to look at. A star in a
/// mismatch is a moderate night, not a worthless one.
///
/// So the base is STAR POWER — what is on the poster — and an even matchup is a bonus on top of it, not the
/// thing being measured. A mismatch is not punished; it simply does not earn the bonus.</summary>
public static class BoutValue
{
    /// <summary>What the night is for. Stakes dominate everything else on purpose: a flyweight world title
    /// fight is a bigger occasion than a heavyweight six-rounder, and any scoring where poundage can outweigh
    /// a championship leaves a flyweight unable to headline even his own title night.</summary>
    private static double Stakes(BoutStakes s) => s switch
    {
        BoutStakes.Undisputed => 900,
        BoutStakes.Unification => 760,
        BoutStakes.WorldTitle => 620,
        BoutStakes.Eliminator => 260,
        BoutStakes.Regional => 150,
        _ => 0,
    };

    /// <summary>The pull of the division itself.
    ///
    /// Heavyweight is the top of the sport and always has been. But this is deliberately NOT a straight line
    /// down from it: welterweight and middleweight are the glamour divisions and have outdrawn the weights
    /// directly above them for a century — Robinson, Leonard, Hagler, Duran. Cruiserweight, sitting between
    /// light-heavy and heavy, draws less than either. A ladder by poundage would say the opposite of what the
    /// gate receipts say.</summary>
    private static double Division(WeightClass wc) => wc switch
    {
        WeightClass.Heavyweight => 240,
        WeightClass.Welterweight => 165,
        WeightClass.Middleweight => 160,
        WeightClass.LightHeavyweight => 120,
        WeightClass.Lightweight => 115,
        WeightClass.LightMiddleweight => 100,
        WeightClass.LightWelterweight => 95,
        WeightClass.Featherweight => 90,
        WeightClass.Cruiserweight => 75,
        WeightClass.Bantamweight => 65,
        WeightClass.Flyweight => 55,
        _ => 60,
    };

    /// <summary>The worth of a fight, to a promoter, a broadcaster and a crowd.</summary>
    /// <param name="at">The weight it is made at — for a bout across two divisions, the heavier man's, which
    /// is where it would be held.</param>
    /// <param name="rankA">1-based place on the divisional board, or 0 for unranked. The caller knows this;
    /// working it out here would mean handing this function the whole world.</param>
    /// <param name="grudge">A return with a question hanging over it — a draw, a split decision, a bad
    /// stoppage. The one fight everybody already wants to see again.</param>
    public static double Of(Boxer a, Boxer b, BoutStakes stakes, WeightClass at,
                            int rankA = 0, int rankB = 0, bool grudge = false)
    {
        double v = Stakes(stakes) + Division(at);

        // The names on the poster. The bigger draw carries the night; the second man adds to it but does not
        // have to match him — one star and a live opponent is a show, which is most of what boxing puts on.
        double starA = Draw(a, rankA), starB = Draw(b, rankB);
        v += Math.Max(starA, starB) + Math.Min(starA, starB) * 0.55;

        // And THEN, on top: is it a real fight? Two good men closely matched is the thing people talk about
        // for years, so it is worth a great deal — but it is a bonus for being competitive, not the base.
        // A mismatch is not penalised here. It just does not earn this.
        int lo = Math.Min(a.Overall, b.Overall);
        int hi = Math.Max(a.Overall, b.Overall);
        if (lo >= 60) v += Math.Max(0, 95 - (hi - lo) * 6.0);

        // Two men near the top of the same board is a fight with the division riding on it. #1 vs #2 is worth
        // a great deal more than #14 vs #15.
        if (rankA > 0 && rankB > 0) v += Math.Max(0, 120 - (rankA + rankB) * 6);

        if (grudge) v += 90;

        return Math.Max(0, v);
    }

    /// <summary>What one man is worth on a poster, whoever he is in with.
    ///
    /// This is the term that makes a showcase a show. Ability first, because a great fighter is a great
    /// fighter; then the things that put a name in the paper — a championship place, an unbeaten record, a run
    /// of wins, and being a prospect people have turned up specifically to look at.</summary>
    private static double Draw(Boxer f, int rank)
    {
        double s = Math.Max(0, f.Overall - 55) * 5.0;   // 55 → 0, 100 → 225
        if (rank == 1) s += 75;
        else if (rank > 0) s += Math.Max(0, 60 - rank * 3);

        // WHAT HE MIGHT BE, as against what he has done — and it is capped for a man the boards have not
        // placed yet. Promise stacked three ways with nothing holding it down: a 10-0 prospect drew 56 for
        // being a prospect, 30 for the streak and, from twelve fights, another 40 for being unbeaten. On a
        // rating of 75 that came to 186 against the 165 of a #10 heavyweight rated 82 — so the kid closed the
        // show over a ranked contender, which is not how a bill is ordered.
        //
        // Uncapped once he IS ranked, because a ranked contender who is also unbeaten is exactly the draw this
        // term is for. The cap sits below what a low ranking is worth, so promise can lift him over journeymen
        // and never over the men who have earned a number.
        double promise = Unbeaten(f) + Streak(f) + Prospect(f);
        if (rank == 0) promise = Math.Min(promise, 45);
        return s + promise;
    }

    /// <summary>A young fighter people have come to see BEFORE he has done anything — the other kind of draw.
    /// A televised prospect sells tickets on what he might become, which is why promoters build them in
    /// public. Needs the talent and the youth together: an ordinary 22-year-old is not an attraction.</summary>
    private static double Prospect(Boxer f)
    {
        if (f.Age > 25 || f.Potential < 78 || f.Record.Losses > 1) return 0;
        int fights = f.Record.Fights;
        if (fights is < 3 or > 22) return 0;            // not yet a name, or no longer a prospect
        return (f.Potential - 74) * 3.5;                // 78 → 14, 95 → 73
    }

    /// <summary>A real unbeaten record. A 3-0 novice is not "unbeaten" in any sense a crowd cares about, so
    /// this asks for enough fights to have meant something, and pays more the longer it has run.</summary>
    private static double Unbeaten(Boxer f)
    {
        if (f.Record.Losses > 0 || f.Record.Fights < 12) return 0;
        return 40 + Math.Min(f.Record.Fights - 12, 20) * 2.5;   // 40 at 12-0, 90 at 32-0
    }

    /// <summary>A long run of wins forces a man into the conversation whatever his record says overall.</summary>
    private static double Streak(Boxer f)
    {
        int n = 0;
        for (int i = f.History.Count - 1; i >= 0 && f.History[i].Result == 'W'; i--) n++;
        return n < 8 ? 0 : Math.Min(n, 20) * 3.0;
    }

    /// <summary>Where a bout sits on a bill, given the values of everything on it. The biggest fight closes
    /// the show; the rest run up to it, smallest first, which is the order a card is actually built in.</summary>
    public static IReadOnlyList<T> RunningOrder<T>(IEnumerable<T> bouts, Func<T, double> value) =>
        bouts.OrderBy(value).ToList();
}
