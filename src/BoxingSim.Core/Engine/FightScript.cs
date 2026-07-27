using BoxingSim.Core.Analysis;
using BoxingSim.Core.Model;

namespace BoxingSim.Core.Engine;

/// <summary>Writes a blow-by-blow for a bout that was resolved statistically, instead of hunting for a
/// simulation that happens to match it.
///
/// The alternative was to re-run the engine from seed after seed until it produced a night ending the way the
/// record said. That worked, but it could only reconstruct about nine fights in ten — a first-round knockout by
/// a heavy underdog is a night the engine will not produce however long you look — and every one it did find
/// cost a seed search. Worse, it replayed men at their CURRENT ratings, so a fight from fifteen years and one
/// retirement ago was a reconstruction of two other men.
///
/// This does the opposite. The record already knows what happened in each round: how many punches each man
/// landed, who went down and when, how the round was scored. That is the truth of the fight. What is missing is
/// only the texture — which punch, at what point in the round — and that can be written to fit rather than
/// searched for. Every fight gets a call, instantly, and the call agrees with the record exactly, because the
/// record is what it is generated FROM.
///
/// The punches a man throws come from his own style and ratings, so a swarmer's round reads like a swarmer's
/// round. Everything is keyed off the bout's own identity, so a fight always replays the same way.</summary>
public static class FightScript
{
    private const int TicksPerRound = 18;   // 18 x 10s = 3:00

    /// <param name="notable">A fight that earned its billing — a title bout, or one that won an award. The
    /// facts are fixed either way: the punch counts, the knockdowns and the scores all come from the record and
    /// do not move. What this changes is how the round is TOLD — more of the landing is named and called as a
    /// real shot, more of it strung into combinations, so a fight that mattered reads like one instead of like
    /// a Tuesday. Excitement by emphasis, not by invention.</param>
    public static FightResult Compose(Boxer owner, Boxer foe, BoutLine line, bool notable = false)
    {
        var rng = new Random(Seed(owner.Name, foe.Name, line.Date));
        bool ownerWon = line.Result == 'W';
        bool draw = line.Result == 'D';
        bool stopped = line.Method is "KO" or "TKO" or "cut" or "RTD";

        var styleO = StyleClassifier.Of(owner);
        var styleF = StyleClassifier.Of(foe);

        var rounds = RoundsFor(line, rng, stopped);
        var built = new List<RoundResult>(rounds.Count);

        foreach (var r in rounds)
        {
            bool last = r.Round == rounds[^1].Round;
            var ticks = Round(r, owner, foe, styleO, styleF, rng, last && stopped, line, ownerWon, notable);
            built.Add(new RoundResult
            {
                Round = r.Round,
                LandedA = r.LandedFor, LandedB = r.LandedAgainst,
                KnockdownsA = r.KdAgainst, KnockdownsB = r.KdFor,   // A's knockdowns SUFFERED, engine convention
                ScoreA = r.ScoreFor, ScoreB = r.ScoreAgainst,
                Ticks = ticks
            });
        }

        return new FightResult
        {
            A = owner, B = foe,
            Winner = draw ? null : ownerWon ? owner : foe,
            Loser = draw ? null : ownerWon ? foe : owner,
            Outcome = draw ? FightOutcome.Draw
                    : line.Method == "KO" ? FightOutcome.Knockout
                    : stopped ? FightOutcome.TechnicalKnockout
                    : FightOutcome.Decision,
            ScheduledRounds = Scheduled(line, rounds.Count),
            EndRound = rounds.Count,
            KnockdownsA = rounds.Sum(x => x.KdAgainst),
            KnockdownsB = rounds.Sum(x => x.KdFor),
            Rounds = built,
            Scorecards = Cards(line),
            Method = line.Method
        };
    }

    /// <summary>The per-round record if one was kept, or a believable one if not. Club fights between two
    /// unranked men do not store a card, and they should still be watchable.</summary>
    private static List<BoutRound> RoundsFor(BoutLine line, Random rng, bool stopped)
    {
        if (line.Rounds is { Count: > 0 } have) return have.OrderBy(r => r.Round).ToList();

        int n = Math.Max(1, line.Round > 0 ? line.Round : 8);
        bool won = line.Result == 'W';
        var made = new List<BoutRound>(n);
        for (int i = 1; i <= n; i++)
        {
            bool mine = line.Result == 'D' ? rng.NextDouble() < 0.5 : rng.NextDouble() < (won ? 0.66 : 0.34);
            int lf = 8 + rng.Next(12) + (mine ? 4 : 0);
            int la = 8 + rng.Next(12) + (mine ? 0 : 4);
            bool finish = stopped && i == n;
            made.Add(new BoutRound
            {
                Round = i, LandedFor = lf, LandedAgainst = la,
                KdFor = finish && won ? 1 : 0, KdAgainst = finish && !won ? 1 : 0,
                ScoreFor = finish ? (won ? 10 : 8) : mine ? 10 : 9,
                ScoreAgainst = finish ? (won ? 8 : 10) : mine ? 9 : 10
            });
        }
        return made;
    }

    /// <summary>Lay a round's punches out over its three minutes. The totals are fixed by the record; what is
    /// invented is only when each one lands and which punch it was.</summary>
    private static FightTick[] Round(BoutRound r, Boxer a, Boxer b, FightingStyle sa, FightingStyle sb,
                                     Random rng, bool endsHere, BoutLine line, bool ownerWon, bool notable)
    {
        // A knockdown ends the action where it falls, so a round that finishes the fight is cut short.
        int segs = endsHere ? Math.Max(4, 6 + rng.Next(TicksPerRound - 8)) : TicksPerRound;
        var ticks = new FightTick[segs];
        for (int i = 0; i < segs; i++) ticks[i] = new FightTick { Clock = Clock(i) };

        var forWhen = Spread(r.LandedFor, segs, rng);
        var againstWhen = Spread(r.LandedAgainst, segs, rng);

        // Where the fight is being fought drifts toward whoever is landing more.
        double ring = 0;
        int cumFor = 0, cumAgainst = 0;

        for (int i = 0; i < segs; i++)
        {
            var t = ticks[i];
            cumFor += forWhen[i];
            cumAgainst += againstWhen[i];
            t.LandedA = cumFor;
            t.LandedB = cumAgainst;

            if (forWhen[i] > 0) Shot(t, a, sa, rng, mine: true, forWhen[i], notable);
            if (againstWhen[i] > 0) Shot(t, b, sb, rng, mine: false, againstWhen[i], notable);

            ring += (forWhen[i] - againstWhen[i]) * 0.06 + (rng.NextDouble() - 0.5) * 0.05;
            ring = Math.Clamp(ring * 0.92, -1, 1);
            t.Ring = ring;
        }

        // Knockdowns land late in the round, and a man is visibly hurt in the seconds before he goes over.
        PlaceDowns(ticks, r.KdFor, forHim: true, rng);
        PlaceDowns(ticks, r.KdAgainst, forHim: false, rng);

        if (endsHere)
        {
            var t = ticks[^1];
            bool winnerIsOwner = ownerWon;
            t.Fin = new StopInfo
            {
                Method = line.Method, Winner = winnerIsOwner ? 0 : 1,
                Body = false, Foul = line.Method == "DQ" ? null : null
            };
            // Whoever lost is on the floor or being saved, so he reads as badly hurt at the end.
            if (winnerIsOwner) t.RockB = 2; else t.RockA = 2;
        }
        return ticks;
    }

    /// <summary>Fill in what a landing looked like. Power shots get named and flagged as big so they are called;
    /// the rest are the jabs and workrate that make up a punch count.</summary>
    private static void Shot(FightTick t, Boxer man, FightingStyle style, Random rng, bool mine, int landed,
                             bool notable)
    {
        double named = PunchProfile.PowerFraction(style, man.Ratings) * (notable ? 1.45 : 1.0);
        if (rng.NextDouble() >= named) return;

        var w = PunchProfile.PowerWeights(style, man.Ratings);
        double total = w[0] + w[1] + w[2] + w[3], roll = rng.NextDouble() * total, acc = 0;
        int pick = 3;
        for (int k = 0; k < 4; k++) { acc += w[k]; if (roll < acc) { pick = k; break; } }
        bool body = pick == 3;
        string name = pick switch
        {
            0 => Pick(rng, "a straight right", "a right cross", "an overhand right", "a one-two"),
            1 => Pick(rng, "a left hook", "a check hook", "a looping hook"),
            2 => Pick(rng, "a right uppercut", "a left uppercut", "an uppercut inside"),
            _ => Pick(rng, "a left to the body", "a right to the ribs", "a hook downstairs", "a body uppercut")
        };
        // A busy segment reads as a combination rather than a single shot.
        int combo = landed >= 3 && rng.NextDouble() < (notable ? 0.6 : 0.45) ? 2 + (rng.NextDouble() < 0.3 ? 1 : 0) : 0;

        if (mine) { t.BigA = true; t.PunchA = name; t.BodyShotA = body; t.ComboA = combo; }
        else { t.BigB = true; t.PunchB = name; t.BodyShotB = body; t.ComboB = combo; }
    }

    /// <summary>Put a round's knockdowns on the clock, with the man wobbling before he goes down.</summary>
    private static void PlaceDowns(FightTick[] ticks, int count, bool forHim, Random rng)
    {
        for (int k = 0; k < count; k++)
        {
            int at = Math.Min(ticks.Length - 1, ticks.Length / 2 + rng.Next(Math.Max(1, ticks.Length / 2)));
            for (int i = at; i < ticks.Length; i++)
            {
                if (forHim) ticks[i].KnockdownsB = Math.Max(ticks[i].KnockdownsB, k + 1);
                else ticks[i].KnockdownsA = Math.Max(ticks[i].KnockdownsA, k + 1);
            }
            // Hurt in the moments before, and still shaky just after.
            for (int i = Math.Max(0, at - 1); i <= Math.Min(ticks.Length - 1, at + 1); i++)
            {
                if (forHim) ticks[i].RockB = 2; else ticks[i].RockA = 2;
            }
            if (forHim) { ticks[at].BigA = true; ticks[at].PunchA ??= "a left hook"; }
            else { ticks[at].BigB = true; ticks[at].PunchB ??= "a right cross"; }
        }
    }

    /// <summary>Scatter a round's punches over its segments — bunched, the way exchanges actually happen,
    /// rather than one every ten seconds.</summary>
    private static int[] Spread(int total, int segs, Random rng)
    {
        var o = new int[segs];
        for (int i = 0; i < total; i++)
        {
            // Two rolls and take the busier segment, so activity clusters instead of spreading flat.
            int x = rng.Next(segs), y = rng.Next(segs);
            o[o[x] >= o[y] ? x : y]++;
        }
        return o;
    }

    private static string Pick(Random rng, params string[] xs) => xs[rng.Next(xs.Length)];

    private static string Clock(int i)
    {
        int left = 180 - (i + 1) * 10;
        return $"{left / 60}:{left % 60:00}";
    }

    private static int Scheduled(BoutLine line, int fought)
    {
        if (line.Method is not ("KO" or "TKO" or "cut" or "RTD")) return Math.Max(fought, line.Round > 0 ? line.Round : fought);
        return fought <= 4 ? 6 : fought <= 6 ? 8 : fought <= 8 ? 10 : 12;
    }

    private static IReadOnlyList<(int A, int B)> Cards(BoutLine line)
    {
        if (string.IsNullOrWhiteSpace(line.Cards)) return Array.Empty<(int, int)>();
        var outp = new List<(int, int)>();
        foreach (var part in line.Cards.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var bits = part.Trim().Split('-', '–');
            if (bits.Length == 2 && int.TryParse(bits[0], out int x) && int.TryParse(bits[1], out int y))
                outp.Add((x, y));
        }
        return outp;
    }

    /// <summary>A seed belonging to this bout and no other, so the same fight always reads the same way.</summary>
    private static int Seed(string owner, string foe, DateOnly date)
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
