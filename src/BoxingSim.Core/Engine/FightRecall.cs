using BoxingSim.Core.Model;

namespace BoxingSim.Core.Engine;

/// <summary>Rebuilds a real, punch-by-punch fight for a bout that was resolved statistically.
///
/// Most bouts in the world are decided by the fast resolver, which produces a result and a plausible card but
/// no punches — so there is nothing to call. Simulating every bout with the full engine instead would be
/// affordable (0.086 ms a bout) but it would replace the model that decides who wins, and with it every piece
/// of balance tuned into the sport: the edge young prospects carry, the floor under an elite underdog, the
/// stoppage mix. That is not a price worth paying to read a commentary.
///
/// So the record stands and the fight is reconstructed to fit it. The engine is run over and over from a seed
/// derived from the bout itself until it produces a night that ends the way the history says it ended — same
/// winner, same manner, near enough the same round. What comes back is a genuine simulated fight, not invented
/// prose: the punches landed, the man was hurt, the referee stepped in. It simply happens to be the version of
/// that night which agrees with the record.
///
/// Because the seed comes from the bout's own identity, reopening a fight always replays the same night.
///
/// One honest limit: fighters are replayed at their CURRENT ratings, since what they were on the night is not
/// stored. For a recent bout that is accurate; for one fifteen years and one retirement ago it is a reconstruction
/// of the men as they are now, fitted to what they did then.</summary>
public static class FightRecall
{
    /// <summary>How hard to look for a night that matches before giving up. Each attempt is ~0.09 ms, so even
    /// the ceiling costs well under a tenth of a second — and it is only ever paid when somebody actually opens
    /// a fight to watch it.</summary>
    private const int MaxAttempts = 900;

    /// <param name="owner">The fighter whose record this bout line belongs to; the line reads from his side.</param>
    /// <param name="preferDrama">Keep looking past the first night that fits and return the best of several.
    /// A fight that won an award should reward being watched, so the version of it that gets replayed is the
    /// one with the knockdowns and the late finish rather than whichever turned up first.</param>
    /// <returns>A fight that ends as recorded, or null if no version of the night could be made to fit.</returns>
    public static FightResult? Rebuild(Boxer owner, Boxer foe, BoutLine line, bool preferDrama = false)
    {
        int rounds = ScheduledRounds(line);
        int seed = StableSeed(owner.Name, foe.Name, line.Date);
        bool stopped = IsStoppage(line.Method);

        FightResult? best = null;
        double bestScore = double.NegativeInfinity;
        int found = 0, want = preferDrama ? 20 : 1;

        for (int k = 0; k < MaxAttempts && found < want; k++)
        {
            // An upset is a man having a night he had no right to have. If the record says the lesser fighter
            // won, the engine will rarely produce it unprompted, so the longer the search runs the more it
            // grants him — up to a quarter better than he is. That is the reconstruction saying "he was superb
            // that night", which is exactly what the result already claims.
            double lift = 1.0 + 0.25 * (k / (double)MaxAttempts);
            var (a, b) = Favoured(owner, foe, line.Result, lift);
            var res = new FightEngine(new Random(seed + k)).Simulate(a, b, rounds);
            if (!Matches(res, a, line, stopped, RoundSlack(k))) continue;

            found++;
            double score = preferDrama ? Drama(res) : 0;
            if (score > bestScore) { bestScore = score; best = res; }
        }
        return best is null ? null : Reface(best, owner, foe);
    }

    /// <summary>How exactly the finishing round has to match. Held to the letter at first, because the round a
    /// man went out in is part of the story, and loosened only once it is clear no night gets it exactly —
    /// better a stoppage a round late than no fight to watch at all.</summary>
    private static int RoundSlack(int attempt) =>
        attempt < MaxAttempts / 3 ? 0 : attempt < MaxAttempts * 2 / 3 ? 1 : 3;

    /// <summary>How good a night this was to watch. Knockdowns first, because that is what people remember;
    /// then a man being hurt and surviving, a finish in the late rounds rather than a early blowout, and a
    /// fight that stayed close. Used only to choose between nights that ALL fit the record equally well.</summary>
    private static double Drama(FightResult res)
    {
        double s = (res.KnockdownsA + res.KnockdownsB) * 10;
        int hurtRounds = 0, swings = 0;
        foreach (var r in res.Rounds)
        {
            foreach (var t in r.Ticks)
                if (t.RockA >= 2 || t.RockB >= 2) { hurtRounds++; break; }
            if (Math.Abs(r.LandedA - r.LandedB) <= 2) swings++;   // a round nobody clearly took
        }
        s += hurtRounds * 6 + swings * 2;
        // A stoppage deep into a fight beats one in the first round; a decision that went the distance close
        // on the cards beats a shut-out.
        if (IsStoppage(res.Method)) s += res.EndRound * 2.5;
        else if (res.Scorecards.Count > 0)
            s += 14 - Math.Min(14, res.Scorecards.Average(c => Math.Abs(c.A - c.B)));
        return s;
    }

    /// <summary>Whether a rebuilt night is close enough to the record to stand in for it.</summary>
    private static bool Matches(FightResult res, Boxer owner, BoutLine line, bool stopped, int slack)
    {
        bool ownerWon = res.Winner is not null && res.Winner.Id == owner.Id;
        char got = res.Winner is null ? 'D' : ownerWon ? 'W' : 'L';
        if (got != line.Result) return false;
        if (IsStoppage(res.Method) != stopped) return false;
        if (!stopped) return true;
        return Math.Abs(res.EndRound - line.Round) <= slack;
    }

    private static bool IsStoppage(string method) =>
        method is "KO" or "TKO" or "cut" or "RTD";

    /// <summary>Hand the engine the two men with the recorded winner lifted, so the night it produces is one
    /// where that man was the better fighter.</summary>
    private static (Boxer A, Boxer B) Favoured(Boxer owner, Boxer foe, char result, double lift)
    {
        if (result == 'D' || Math.Abs(lift - 1.0) < 1e-9) return (owner, foe);
        return result == 'W' ? (Lift(owner, lift), foe) : (owner, Lift(foe, lift));
    }

    /// <summary>A copy of a fighter having a better night than usual. Only the qualities that decide a fight
    /// move; his identity, and everything the rest of the sim reads, is untouched.</summary>
    private static Boxer Lift(Boxer b, double by)
    {
        int Up(int v) => (int)Math.Clamp(Math.Round(v * by), 1, 99);
        var r = b.Ratings;
        return b.WithRatings(new Ratings
        {
            Power = Up(r.Power), Chin = Up(r.Chin), Speed = Up(r.Speed), Defense = Up(r.Defense),
            Stamina = Up(r.Stamina), Accuracy = Up(r.Accuracy), Aggression = r.Aggression,
            Conditioning = Up(r.Conditioning), Heart = Up(r.Heart), CutResistance = r.CutResistance
        });
    }

    /// <summary>Put the real fighters back on the result, so nothing downstream ever sees the lifted copies.</summary>
    private static FightResult Reface(FightResult res, Boxer owner, Boxer foe)
    {
        bool ownerIsA = res.A.Id == owner.Id;
        Boxer a = ownerIsA ? owner : foe, b = ownerIsA ? foe : owner;
        Boxer? Real(Boxer? x) => x is null ? null : x.Id == res.A.Id ? a : b;
        return new FightResult
        {
            A = a, B = b, Winner = Real(res.Winner), Loser = Real(res.Loser),
            Outcome = res.Outcome, ScheduledRounds = res.ScheduledRounds, EndRound = res.EndRound,
            KnockdownsA = res.KnockdownsA, KnockdownsB = res.KnockdownsB,
            Scorecards = res.Scorecards, Rounds = res.Rounds, Injuries = res.Injuries, Method = res.Method
        };
    }

    /// <summary>What the bout was scheduled for. Only the round it ended in is stored, so a stoppage has to be
    /// read back to the nearest plausible distance rather than taken literally.</summary>
    private static int ScheduledRounds(BoutLine line)
    {
        if (!IsStoppage(line.Method)) return line.Round > 0 ? line.Round : 10;
        return line.Round <= 4 ? 6 : line.Round <= 6 ? 8 : line.Round <= 8 ? 10 : 12;
    }

    /// <summary>A seed belonging to this bout and no other, so the same fight always replays the same way
    /// without anything having to be stored alongside the record.</summary>
    private static int StableSeed(string owner, string foe, DateOnly date)
    {
        unchecked
        {
            int h = 17;
            foreach (char c in owner) h = h * 31 + c;
            foreach (char c in foe) h = h * 31 + c;
            h = h * 31 + date.DayNumber;
            return h & 0x7FFFFFFF;
        }
    }
}
