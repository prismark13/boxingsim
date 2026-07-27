using BoxingSim.Core.Analysis;
using BoxingSim.Core.Model;

namespace BoxingSim.Core.Engine;

/// <summary>
/// Round-by-round bout resolution at 10-second granularity. Punches are jabs or power shots,
/// to the head or the body, accumulating separate damage pools that drive knockdowns, cuts,
/// swelling, hand injuries and stoppages. Each completed round is scored by three judges.
/// Bouts that go the distance are decided on the cards; afterwards, acute injuries and (rarely)
/// permanent wear are resolved for the career simulation. This is the single authoritative engine.
/// </summary>
public sealed class FightEngine
{
    private const int TicksPerRound = 18;   // 18 × 10s = 3:00
    private readonly Random _rng;

    public FightEngine(Random rng) => _rng = rng;

    private static readonly string[] HeadShots =
        { "a straight right", "a left hook", "a right uppercut", "a left uppercut", "a right cross", "an overhand right", "a check hook" };
    private static readonly string[] BodyShots =
        { "a left to the body", "a right to the ribs", "a hook downstairs", "a body uppercut", "a right under the heart" };
    private static readonly (string Loc, bool Eye)[] CutLocs =
    {
        ("over the left eye", true), ("over the right eye", true), ("above the left brow", true), ("above the right brow", true),
        ("on the left eyelid", true), ("on the right eyelid", true), ("under the left eye", true), ("under the right eye", true),
        ("on the bridge of the nose", false), ("across the nose", false), ("along the left cheekbone", false),
        ("along the right cheekbone", false), ("at the corner of the mouth", false), ("on the forehead", false),
        ("inside the lip", false), ("on the hairline", false)
    };
    // Swelling sites — some close the eye and wreck a fighter's vision, others are cosmetic mice.
    private static readonly (string Loc, bool Eye)[] SwellLocs =
    {
        ("the left eye", true), ("the right eye", true), ("under the left eye", true), ("under the right eye", true),
        ("a mouse under the left eye", true), ("a mouse under the right eye", true),
        ("the bridge of the nose", false), ("the left cheek", false), ("the right cheek", false), ("the jaw", false)
    };
    /// <summary>Describe a cut by how bad it is, for commentary.</summary>
    private static string CutType(double cut) =>
        cut >= 0.85 ? "a horrible gash" : cut >= 0.6 ? "a deep cut" : cut >= 0.35 ? "a cut" : "a nick";
    private static readonly string[] FoulTypes = { "a low blow", "a headbutt", "a rabbit punch", "holding and hitting", "a shove" };

    // Punch arsenal. Each shot has a relative chance to land, a force multiplier, a scoring weight,
    // and whether it goes to the body. Jabs land easily but barely hurt; uppercuts rarely land but end nights.
    private enum Punch { Jab, Cross, Hook, Uppercut, Body }
    private readonly record struct PunchDef(double LandMod, double Force, double Score, bool Body);
    private static readonly PunchDef[] PunchTable =
    {
        new(1.35, 0.00, 1.00, false),  // Jab — the light scoring shot (handled outside the table)
        new(1.00, 1.00, 1.00, false),  // Cross
        new(0.84, 1.12, 1.08, false),  // Hook
        new(0.74, 1.22, 1.12, false),  // Uppercut
        new(0.96, 0.92, 1.00, true),   // Body
    };
    private static readonly string[] Crosses = { "a straight right", "a right cross", "an overhand right", "a one-two" };
    private static readonly string[] Hooks = { "a left hook", "a check hook", "a looping hook" };
    private static readonly string[] Uppercuts = { "a right uppercut", "a left uppercut", "an uppercut inside" };

    private static readonly Punch[] PowerPunches = { Punch.Cross, Punch.Hook, Punch.Uppercut, Punch.Body };

    /// <summary>Choose which power punch to throw — weighted by the fighter's attributes and style.</summary>
    private Punch PickPowerPunch(State s)
    {
        var w = PunchProfile.PowerWeights(s.Style, s.R);
        double total = w[0] + w[1] + w[2] + w[3];
        double roll = _rng.NextDouble() * total, acc = 0;
        for (int i = 0; i < 4; i++) { acc += w[i]; if (roll < acc) return PowerPunches[i]; }
        return Punch.Cross;
    }

    /// <summary>Chance a landed power shot is strung into a combination. Fast, busy fighters throw more of
    /// them; swarmers and boxer-punchers live on combinations, sluggers head-hunt with single big shots.</summary>
    private double ComboChance(State s)
    {
        double basis = 0.08 + s.Eff(s.R.Speed) / 500.0 + Math.Max(0, s.R.Aggression - 55) / 400.0;
        double styleAdj = s.Style switch
        {
            FightingStyle.Swarmer => 0.10,
            FightingStyle.BoxerPuncher => 0.06,
            FightingStyle.OutBoxer => 0.02,
            FightingStyle.CounterPuncher => 0.00,
            FightingStyle.Slugger => -0.04,
            _ => 0.0
        };
        return Math.Clamp(basis + styleAdj, 0.04, 0.42);
    }

    private string PunchName(Punch p) => p switch
    {
        Punch.Cross => Pick(Crosses),
        Punch.Hook => Pick(Hooks),
        Punch.Uppercut => Pick(Uppercuts),
        Punch.Body => Pick(BodyShots),
        _ => "a jab"
    };

    /// <summary>Land a power punch (or a counter) and apply its damage, cuts and knockdown chance.</summary>
    private Blow ApplyPower(State puncher, State target, Punch punch, double wcKo, bool isCounter, double forceScale = 1.0)
    {
        var pd = PunchTable[(int)punch];
        double powerEff = puncher.Eff(puncher.R.Power) * (1 - puncher.HandPen);
        double chin = target.EffChin();   // a chin already worn by knockdowns offers less protection
        double clean = (isCounter ? 0.72 : 0.6) + _rng.NextDouble() * 0.7;   // counters land a touch cleaner
        double force = powerEff / Math.Max(20.0, chin) * wcKo * clean * pd.Force * forceScale;
        if (isCounter) force *= 0.45;   // a quick counter scores and stings, but isn't a loaded haymaker
        puncher.RoundWeighted += 1.6 * (0.5 + powerEff / 200.0) * pd.Score;

        bool handHurt = false;
        if (!puncher.HandHurt && !pd.Body && _rng.NextDouble() < puncher.R.Power / 100.0 * 0.012 * Math.Max(0, force - 1.0))
        {
            puncher.HandHurt = true; puncher.HandPen = 0.18 + _rng.NextDouble() * 0.16; handHurt = true;
        }

        bool countedOut = false, toBodyKd = false;
        if (pd.Body)
        {
            target.BodyDmg = Math.Min(1.3, target.BodyDmg + force * 0.028);
            target.Fatigue = Math.Min(1.0, target.Fatigue + 0.01 + force * 0.004);
            if (_rng.NextDouble() < force * 0.013 * (0.4 + target.BodyDmg)) { countedOut = Knockdown(target, body: true); toBodyKd = true; }
        }
        else
        {
            target.Damage = Math.Min(1.3, target.Damage + force * 0.029);
            target.Swell = Math.Min(1.0, target.Swell + 0.02 + force * 0.005);
            if (target.SwellLoc is null && target.Swell >= 0.16) { var sw = Pick(SwellLocs); target.SwellLoc = sw.Loc; target.SwellEye = sw.Eye; }
            if (_rng.NextDouble() < force * 0.019 * (0.5 + target.Damage)) countedOut = Knockdown(target, body: false);
        }
        bool staggered = !pd.Body && !countedOut && force >= 1.6 && _rng.NextDouble() < Math.Clamp((force - 1.5) * 0.17, 0, 0.24);
        return new Blow(true, pd.Body, handHurt, countedOut, toBodyKd, staggered, PunchName(punch));
    }

    /// <summary>The ring effect of a reach differential (in inches). The longer man controls distance and
    /// lands the jab more freely — but an aggressive pressure fighter or slugger who gets inside erases much
    /// of that edge. Expressed on the same ~[-0.7, 0.7] scale as the style edge, so both fold into landProb.</summary>
    private static double ReachEdge(int myReach, int oppReach, FightingStyle oppStyle)
    {
        if (myReach <= 0 || oppReach <= 0) return 0;   // unknown reach → no effect
        double e = (myReach - oppReach) * 0.05;        // ~6" edge ≈ 0.30
        if (e > 0 && (oppStyle == FightingStyle.Swarmer || oppStyle == FightingStyle.Slugger)) e *= 0.55; // he closes the gap
        return Math.Clamp(e, -0.7, 0.7);
    }

    /// <summary>How readily a fighter catches a missing opponent with a counter.</summary>
    private double CounterChance(State def)
    {
        double skill = def.Eff(def.R.Defense) * 0.3 + def.Eff(def.R.Speed) * 0.4 + def.Eff(def.R.Accuracy) * 0.3;
        double styleBonus = def.Style switch
        {
            FightingStyle.CounterPuncher => 30,
            FightingStyle.OutBoxer => 12,
            FightingStyle.BoxerPuncher => 6,
            _ => 0
        };
        return Math.Clamp((skill + styleBonus) / 820.0, 0.02, 0.04);
    }

    private sealed class State
    {
        public required Boxer Boxer { get; init; }
        public double Fatigue, Damage, BodyDmg, Cut, Swell;
        public string? CutLoc, SwellLoc;
        public bool CutEye, SwellEye, HandHurt;
        public double HandPen;
        public int RoundKnockdowns, TotalKnockdowns, RoundLanded;
        /// <summary>Where we are in the fight, 0 at the opening bell and 1 at the last round. Read by the
        /// starter trait so a slow starter is genuinely muted early and comes on late.</summary>
        public double Phase;
        public double RoundWeighted;
        public FightingStyle Style;
        public Ratings R => Boxer.Ratings;

        public double Eff(int rating)
        {
            double heart = (R.Heart - 50) / 250.0;
            double vision = (CutEye && Cut >= 0.5) ? (Cut - 0.4) * 0.10 : 0.0;
            if (SwellEye && Swell >= 0.5) vision += (Swell - 0.4) * 0.12;   // an eye swelling shut blinds a fighter
            double mult = 1.0 - Fatigue * 0.30 - Math.Max(0, Damage - heart) * 0.35 - Swell * 0.08 - BodyDmg * 0.10 - vision;
            // A fast starter is sharper out of the gate and fades; a slow starter warms into it. Symmetric
            // about the midpoint so the trait costs a man nothing over a full fight — it only moves WHEN his
            // best work happens, which is what decides close rounds.
            mult *= 1.0 + Boxer.StartSpeed * 0.07 * (1.0 - 2.0 * Phase);
            return rating * Math.Max(0.36, mult);
        }

        // Every knockdown leaves a man more fragile for the rest of the night: his chin and his
        // defence are diminished (a within-fight "DEF2 / chin-worn" secondary value).
        public double EffChin() => Eff(R.Chin) * (1.0 - Math.Min(0.26, TotalKnockdowns * 0.07));
        public double EffDef() => Eff(R.Defense) * (1.0 - Math.Min(0.18, TotalKnockdowns * 0.05));
    }

    private struct Hit
    {
        public bool Landed, Big, Body, HandHurt, CountedOut, ToBodyKd, Staggered;
        public string? Type;
        public int ComboLen;      // >1 when the landing power shot was strung into a combination
        public bool ComboBody;    // the combination went to the body as well as the head
        // The defender's counter, fired when the attacker misses.
        public bool CounterLanded, CounterBig, CounterBody, CounterCountedOut, CounterToBodyKd;
        public string? CounterType;
    }

    private readonly record struct Blow(bool Big, bool Body, bool HandHurt, bool CountedOut, bool ToBodyKd, bool Staggered, string? Type);

    public FightResult Simulate(Boxer a, Boxer b, int scheduledRounds)
    {
        var sa = new State { Boxer = a };
        var sb = new State { Boxer = b };
        var rounds = new List<RoundResult>(scheduledRounds);
        double wcKo = a.WeightClass.KnockoutFactor();

        var styleA = StyleClassifier.Of(a);
        var styleB = StyleClassifier.Of(b);
        sa.Style = styleA; sb.Style = styleB;
        // The total edge each man carries into every exchange = the style rock-paper-scissors plus his reach.
        double edgeA = FightingStyles.Advantage(styleA, styleB) + ReachEdge(a.Reach, b.Reach, styleB);
        double edgeB = FightingStyles.Advantage(styleB, styleA) + ReachEdge(b.Reach, a.Reach, styleA);

        // Running judge totals (three judges), accumulated as each round is scored.
        var jtA = new int[3];
        var jtB = new int[3];

        FightOutcome? stoppage = null;
        Boxer? stopWinner = null, stopLoser = null;
        string stopMethod = "";
        bool bodyStop = false;
        FoulEvent? dqFoul = null;
        int endRound = scheduledRounds;
        int hurtRounds = 0;

        for (int round = 1; round <= scheduledRounds; round++)
        {
            sa.RoundKnockdowns = sb.RoundKnockdowns = 0;
            sa.RoundLanded = sb.RoundLanded = 0;
            sa.RoundWeighted = sb.RoundWeighted = 0;
            // 0 at the opening bell, 1 in the last round — drives the fast/slow starter trait.
            sa.Phase = sb.Phase = scheduledRounds <= 1 ? 0.5 : (round - 1) / (double)(scheduledRounds - 1);
            double cutBeforeA = sa.Cut, cutBeforeB = sb.Cut, swellBeforeA = sa.Swell, swellBeforeB = sb.Swell;

            int attemptsA = PunchVolume(sa);
            int attemptsB = PunchVolume(sb);
            int exchanges = Math.Max(attemptsA, attemptsB);

            var ticks = new FightTick[TicksPerRound];
            for (int i = 0; i < TicksPerRound; i++)
                ticks[i] = new FightTick { Clock = ClockLabel(i) };
            var touched = new bool[TicksPerRound];

            int stopSeg = -1;

            for (int e = 0; e < exchanges && stopSeg < 0; e++)
            {
                int seg = Math.Min(TicksPerRound - 1, e * TicksPerRound / Math.Max(1, exchanges));
                var t = ticks[seg];
                touched[seg] = true;

                if (e < attemptsA)
                {
                    var hit = Throw(sa, sb, wcKo, edgeA);
                    FoldHit(t, hit, attacker: true, sa, sb);
                    if (hit.CountedOut) { stoppage = FightOutcome.Knockout; stopWinner = a; stopLoser = b; stopMethod = "KO"; bodyStop = hit.ToBodyKd; stopSeg = seg; }
                    else if (hit.CounterCountedOut) { stoppage = FightOutcome.Knockout; stopWinner = b; stopLoser = a; stopMethod = "KO"; bodyStop = hit.CounterToBodyKd; stopSeg = seg; }
                    else if (sb.Damage >= 1.0 || sb.BodyDmg >= 1.0) { stoppage = FightOutcome.TechnicalKnockout; stopWinner = a; stopLoser = b; stopMethod = "TKO"; bodyStop = sb.BodyDmg >= 1.0; stopSeg = seg; }
                    else if (sa.Damage >= 1.0 || sa.BodyDmg >= 1.0) { stoppage = FightOutcome.TechnicalKnockout; stopWinner = b; stopLoser = a; stopMethod = "TKO"; bodyStop = sa.BodyDmg >= 1.0; stopSeg = seg; }
                }
                if (stopSeg < 0 && e < attemptsB)
                {
                    var hit = Throw(sb, sa, wcKo, edgeB);
                    FoldHit(t, hit, attacker: false, sa, sb);
                    if (hit.CountedOut) { stoppage = FightOutcome.Knockout; stopWinner = b; stopLoser = a; stopMethod = "KO"; bodyStop = hit.ToBodyKd; stopSeg = seg; }
                    else if (hit.CounterCountedOut) { stoppage = FightOutcome.Knockout; stopWinner = a; stopLoser = b; stopMethod = "KO"; bodyStop = hit.CounterToBodyKd; stopSeg = seg; }
                    else if (sa.Damage >= 1.0 || sa.BodyDmg >= 1.0) { stoppage = FightOutcome.TechnicalKnockout; stopWinner = b; stopLoser = a; stopMethod = "TKO"; bodyStop = sa.BodyDmg >= 1.0; stopSeg = seg; }
                    else if (sb.Damage >= 1.0 || sb.BodyDmg >= 1.0) { stoppage = FightOutcome.TechnicalKnockout; stopWinner = a; stopLoser = b; stopMethod = "TKO"; bodyStop = sb.BodyDmg >= 1.0; stopSeg = seg; }
                }
                Snapshot(t, sa, sb);
            }

            FoulEvent? roundFoul = null;
            int deductA = 0, deductB = 0;

            if (stoppage is null)
            {
                ApplyCuts(sa, sb);
                ApplyCuts(sb, sa);
                roundFoul = ResolveFouls(sa, sb, ref deductA, ref deductB, out bool dq, out int dqWinner);
                if (roundFoul is not null)
                {
                    int fseg = Math.Min(TicksPerRound - 1, exchanges == 0 ? 0 : (exchanges - 1) * TicksPerRound / Math.Max(1, exchanges));
                    ticks[fseg].Foul = roundFoul;
                    touched[fseg] = true;
                }
                if (dq)
                {
                    stoppage = FightOutcome.TechnicalKnockout;
                    stopMethod = "DQ"; dqFoul = roundFoul;
                    stopWinner = dqWinner == 0 ? a : b; stopLoser = dqWinner == 0 ? b : a;
                    stopSeg = Math.Min(TicksPerRound - 1, exchanges == 0 ? 0 : exchanges - 1);
                }
                else if (TryCutStoppage(sa, round)) { stoppage = FightOutcome.TechnicalKnockout; stopWinner = b; stopLoser = a; stopMethod = "cut"; stopSeg = TicksPerRound - 1; }
                else if (TryCutStoppage(sb, round)) { stoppage = FightOutcome.TechnicalKnockout; stopWinner = a; stopLoser = b; stopMethod = "cut"; stopSeg = TicksPerRound - 1; }
            }

            // Forward-fill cumulative state into any untouched ticks, then trim on a stoppage.
            FillTicks(ticks, touched, sa, sb, round, exchanges);
            int lastSeg = stopSeg >= 0 ? stopSeg : TicksPerRound - 1;
            var roundTicks = new List<FightTick>(lastSeg + 1);
            for (int i = 0; i <= lastSeg; i++) roundTicks.Add(ticks[i]);
            if (stopSeg >= 0)
                roundTicks[stopSeg].Fin = new StopInfo
                {
                    Method = stopMethod, Winner = stopWinner == a ? 0 : 1, Body = bodyStop, Foul = dqFoul
                };

            var (baseA, baseB, closeness) = ScoreRound(sa, sb);
            baseA = Math.Max(6, baseA - deductA);
            baseB = Math.Max(6, baseB - deductB);

            (int A, int B)[]? judges = null;
            if (stoppage is null)
            {
                judges = new (int, int)[3];
                for (int j = 0; j < 3; j++)
                {
                    int ca = baseA, cb = baseB;
                    if (ca != cb && _rng.NextDouble() < closeness * 0.35) (ca, cb) = (cb, ca);
                    judges[j] = (ca, cb);
                    jtA[j] += ca; jtB[j] += cb;
                }
            }

            bool hurtEndA = sa.Damage >= 0.8 || sa.BodyDmg >= 0.85;
            bool hurtEndB = sb.Damage >= 0.8 || sb.BodyDmg >= 0.85;
            if (hurtEndA || hurtEndB) hurtRounds++;

            // A fresh cut vs an existing one that opened up further this round.
            bool cutNewA = cutBeforeA < 0.05 && sa.Cut >= 0.1, cutNewB = cutBeforeB < 0.05 && sb.Cut >= 0.1;
            bool cutWorseA = cutBeforeA >= 0.15 && sa.Cut > cutBeforeA + 0.05;
            bool cutWorseB = cutBeforeB >= 0.15 && sb.Cut > cutBeforeB + 0.05;
            // End-of-round severity is what the fighter shows; then the corner works it for the next round.
            double cutShownA = sa.Cut, cutShownB = sb.Cut;
            bool cutClosedA = stoppage is null && CutmanWork(sa);
            bool cutClosedB = stoppage is null && CutmanWork(sb);

            rounds.Add(new RoundResult
            {
                Round = round,
                LandedA = sa.RoundLanded, LandedB = sb.RoundLanded,
                KnockdownsA = sa.RoundKnockdowns, KnockdownsB = sb.RoundKnockdowns,
                ScoreA = baseA, ScoreB = baseB,
                Ticks = roundTicks, Judges = judges,
                CutA = cutShownA, CutB = cutShownB,
                CutNewA = cutNewA, CutNewB = cutNewB,
                CutWorseA = cutWorseA, CutWorseB = cutWorseB,
                CutClosedA = cutClosedA, CutClosedB = cutClosedB,
                CutLocA = sa.CutLoc, CutLocB = sb.CutLoc,
                SwellNewA = sa.Swell > swellBeforeA + 0.03, SwellNewB = sb.Swell > swellBeforeB + 0.03,
                SwellLocA = sa.SwellLoc, SwellLocB = sb.SwellLoc,
                SwellEyeA = sa.SwellEye, SwellEyeB = sb.SwellEye,
                HurtEndA = hurtEndA, HurtEndB = hurtEndB
            });

            if (stoppage is not null) { endRound = round; break; }
            Recover(sa);
            Recover(sb);
        }

        // ---- decide the bout ----
        Boxer? winner = stopWinner, loser = stopLoser;
        FightOutcome outcome;
        string method;
        IReadOnlyList<(int A, int B)> cards = Array.Empty<(int, int)>();

        if (stoppage is not null)
        {
            outcome = stoppage.Value;
            method = stopMethod == "cut" ? "TKO" : stopMethod; // "KO", "TKO" (incl. cut), or "DQ"
        }
        else
        {
            cards = new[] { (jtA[0], jtB[0]), (jtA[1], jtB[1]), (jtA[2], jtB[2]) };
            int forA = 0, forB = 0;
            for (int j = 0; j < 3; j++) { if (jtA[j] > jtB[j]) forA++; else if (jtB[j] > jtA[j]) forB++; }
            if (forA >= 2) { winner = a; loser = b; outcome = FightOutcome.Decision; method = forA == 3 ? "UD" : forB == 1 ? "SD" : "MD"; }
            else if (forB >= 2) { winner = b; loser = a; outcome = FightOutcome.Decision; method = forB == 3 ? "UD" : forA == 1 ? "SD" : "MD"; }
            else { winner = null; loser = null; outcome = FightOutcome.Draw; method = "D"; }
        }

        bool war = (sa.TotalKnockdowns + sb.TotalKnockdowns >= 3)
                   || (sa.TotalKnockdowns >= 1 && sb.TotalKnockdowns >= 1)
                   || hurtRounds >= 4;

        var injuries = DetermineInjuries(a, b, sa, sb, winner, stopMethod);
        var lasting = LastingEffects(a, b, sa, sb, winner, stopMethod, bodyStop, war);

        return new FightResult
        {
            A = a, B = b,
            Winner = winner, Loser = loser,
            Outcome = outcome,
            ScheduledRounds = scheduledRounds, EndRound = endRound,
            KnockdownsA = sa.TotalKnockdowns, KnockdownsB = sb.TotalKnockdowns,
            Scorecards = cards, Rounds = rounds,
            Injuries = injuries, Lasting = lasting,
            War = war, BodyStop = bodyStop,
            Method = method
        };
    }

    private int PunchVolume(State s)
    {
        double basis = 14 + s.R.Aggression * 0.22 + s.Eff(s.R.Stamina) * 0.10;
        return Math.Max(6, (int)Math.Round(basis * (0.85 + _rng.NextDouble() * 0.30)));
    }

    private Hit Throw(State att, State def, double wcKo, double styleEdge)
    {
        var hit = new Hit();
        // Landing blends accuracy, speed and power: a feared puncher backs opponents up and forces
        // openings, so power is part of offense — not just damage once a shot lands. Weights sum to 1
        // so an evenly-matched bout is unchanged; only the style balance shifts.
        double offense = att.Eff(att.R.Accuracy) * 0.5 + att.Eff(att.R.Speed) * 0.3 + att.Eff(att.R.Power) * 0.2;
        double pressure = 1.0 - Math.Max(0, att.R.Aggression - 60) / 350.0;   // a pressure fighter crowds his man, cutting his defence
        double defense = (def.EffDef() * 0.6 + def.Eff(def.R.Speed) * 0.4) * pressure;
        double landProb = 0.55 / (1.0 + Math.Exp(-(offense - defense) / 14.0));
        landProb = Math.Clamp(landProb * (1.0 + 0.10 * styleEdge), 0.03, 0.95);

        bool power = _rng.NextDouble() < PunchProfile.PowerFraction(att.Style, att.R);  // jab or load up? (style-driven)

        if (_rng.NextDouble() >= landProb)
        {
            // Missed. Loading up on a power shot leaves him open — a sharp defender counters.
            if (power && _rng.NextDouble() < CounterChance(def))
            {
                def.RoundLanded++;
                var cp = _rng.NextDouble() < 0.6 ? Punch.Cross : Punch.Hook;
                var c = ApplyPower(def, att, cp, wcKo, isCounter: true);
                hit.CounterLanded = true; hit.CounterBig = c.Big; hit.CounterBody = c.Body;
                hit.CounterType = c.Type; hit.CounterCountedOut = c.CountedOut; hit.CounterToBodyKd = c.ToBodyKd;
            }
            return hit;
        }

        hit.Landed = true;
        att.RoundLanded++;

        if (!power)
        {
            att.RoundWeighted += 1.0 * (0.5 + att.Eff(att.R.Accuracy) / 220.0);   // a jab / pawing shot
            return hit;
        }

        var blow = ApplyPower(att, def, PickPowerPunch(att), wcKo, isCounter: false);
        hit.Big = blow.Big; hit.Body = blow.Body; hit.Type = blow.Type;
        hit.HandHurt = blow.HandHurt; hit.CountedOut = blow.CountedOut; hit.ToBodyKd = blow.ToBodyKd;

        // In rhythm and in range — a fast, busy fighter strings the shot into a short combination.
        // Follow-up punches land cleaner (he's set) but carry less than a loaded single shot.
        if (!hit.CountedOut && !blow.Staggered)
        {
            int shots = 1;
            bool comboBody = blow.Body;
            double cc = ComboChance(att);
            while (shots < 3 && _rng.NextDouble() < cc)
            {
                var extra = ApplyPower(att, def, PickPowerPunch(att), wcKo, isCounter: false, forceScale: 0.7);
                shots++; att.RoundLanded++;
                if (extra.Body) comboBody = true;
                if (extra.HandHurt) hit.HandHurt = true;
                if (extra.CountedOut) { hit.CountedOut = true; break; }
                if (extra.Staggered) { blow = extra; break; }
                cc *= 0.5;   // each additional punch is less likely than the last
            }
            if (shots > 1) { hit.ComboLen = shots; hit.ComboBody = comboBody; }
        }

        // Wobbled him — a finisher jumps on a hurt man and tries to take him out right now.
        if (!hit.CountedOut && blow.Staggered)
        {
            hit.Staggered = true;
            if (Flurry(att, def, wcKo)) hit.CountedOut = true;
        }
        return hit;
    }

    /// <summary>A follow-up flurry on a staggered opponent. How hard the attacker pours it on is his
    /// killer instinct; whether the hurt man survives is his durability (chin, heart, conditioning).</summary>
    private bool Flurry(State att, State def, double wcKo)
    {
        double instinct = SecondaryStats.KillerInstinct(att.R) / 100.0;
        double survive = SecondaryStats.Durability(def.R) / 104.0;   // a durable man shuts the storm down fast
        int shots = 1 + (int)Math.Round(instinct * 2.0);            // 1..3 follow-up shots
        for (int s = 0; s < shots; s++)
        {
            if (_rng.NextDouble() < survive) break;                 // ties up, covers, holds on before taking more
            var b = ApplyPower(att, def, PickPowerPunch(att), wcKo, isCounter: false);
            if (b.CountedOut || def.Damage >= 1.0 || def.BodyDmg >= 1.0) return true;
        }
        return false;
    }

    private bool Knockdown(State def, bool body)
    {
        def.RoundKnockdowns++;
        def.TotalKnockdowns++;
        if (body) def.BodyDmg = Math.Min(1.3, def.BodyDmg + 0.15);
        else def.Damage = Math.Min(1.3, def.Damage + 0.18);

        double riseBase = body ? 0.85 - def.BodyDmg * 0.50 : 0.95 - def.Damage * 0.55;
        double riseChance = Math.Clamp(riseBase + (def.R.Conditioning + def.R.Heart - 100) / 300.0, 0.12, 0.98);
        if (_rng.NextDouble() > riseChance) return true; // counted out

        if (def.RoundKnockdowns >= 3) { if (body) def.BodyDmg = 1.0; else def.Damage = 1.0; return false; }

        if (body) def.BodyDmg = Math.Max(def.BodyDmg - 0.10, 0.40);
        else def.Damage = Math.Max(def.Damage - 0.10, 0.45);
        return false;
    }

    private void ApplyCuts(State victim, State attacker)
    {
        double exposure = attacker.RoundWeighted / 40.0;
        double cutChance = exposure * (1.0 - victim.R.CutResistance / 130.0);
        if (_rng.NextDouble() < cutChance)
        {
            if (victim.CutLoc is null) { var c = Pick(CutLocs); victim.CutLoc = c.Loc; victim.CutEye = c.Eye; }
            victim.Cut = Math.Min(1.0, victim.Cut + 0.15 + _rng.NextDouble() * 0.25);
        }
        else if (victim.Cut >= 0.3 && _rng.NextDouble() < exposure * 0.5)
        {
            victim.Cut = Math.Min(1.0, victim.Cut + 0.08 + _rng.NextDouble() * 0.12); // an existing cut worsens
        }
    }

    /// <summary>Between rounds the cutman works a cut and swelling down — a fighter can come out looking
    /// better than he went back to the corner. Returns true if a cut was meaningfully closed.</summary>
    private bool CutmanWork(State s)
    {
        if (s.Cut < 0.1 && s.Swell < 0.05) return false;
        double before = s.Cut;
        double skill = 0.5 + s.R.CutResistance / 200.0;   // tougher skin / a better corner closes cuts faster
        if (s.Cut > 0.1) s.Cut = Math.Max(0.05, s.Cut - (0.05 + _rng.NextDouble() * 0.12) * skill);
        if (s.Swell > 0.05) s.Swell = Math.Max(0.0, s.Swell - 0.03 * skill);
        return before - s.Cut > 0.06;
    }

    private bool TryCutStoppage(State s, int round)
    {
        if (s.Cut < 0.6) return false;
        double chance = (s.Cut - 0.5) * 0.25 * (round / 12.0);
        return _rng.NextDouble() < chance;
    }

    private FoulEvent? ResolveFouls(State sa, State sb, ref int deductA, ref int deductB, out bool dq, out int dqWinner)
    {
        dq = false; dqWinner = 0;
        for (int who = 0; who < 2; who++)
        {
            if (_rng.NextDouble() >= 0.02) continue;
            var foulerS = who == 0 ? sa : sb;
            var victimS = who == 0 ? sb : sa;
            string type = Pick(FoulTypes);
            var foul = new FoulEvent { Who = who, Type = type };

            if (type == "a headbutt" && _rng.NextDouble() < 0.6)
            {
                foul.Butt = true;
                if (victimS.CutLoc is null) { var c = Pick(CutLocs); victimS.CutLoc = c.Loc; victimS.CutEye = c.Eye; }
                victimS.Cut = Math.Min(1.0, victimS.Cut + 0.12 + _rng.NextDouble() * 0.2);
            }

            if (_rng.NextDouble() < 0.10) // escalates to a point deduction (~1 in 20 fights)
            {
                foul.Deduct = true;
                if (who == 0) deductA = 1; else deductB = 1;
                if (_rng.NextDouble() < 0.02) { foul.Dq = true; dq = true; dqWinner = who == 0 ? 1 : 0; }
            }
            return foul; // at most one foul resolved per round
        }
        return null;
    }

    private (int a, int b, double closeness) ScoreRound(State sa, State sb)
    {
        double marginRaw = sa.RoundWeighted - sb.RoundWeighted;
        int scoreA = 10, scoreB = 10;
        if (marginRaw > 0) scoreB = 9;
        else if (marginRaw < 0) scoreA = 9;

        scoreA = Math.Max(6, scoreA - sa.RoundKnockdowns);
        scoreB = Math.Max(6, scoreB - sb.RoundKnockdowns);

        double total = Math.Abs(sa.RoundWeighted) + Math.Abs(sb.RoundWeighted) + 1;
        double closeness = 1.0 - Math.Min(1.0, Math.Abs(marginRaw) / total);
        return (scoreA, scoreB, closeness);
    }

    private void Recover(State s)
    {
        s.Fatigue = Math.Min(1.0, s.Fatigue + 0.06 + (100 - s.R.Stamina) / 900.0);
        double recovery = 0.07 + s.R.Conditioning / 500.0;
        s.Damage = Math.Max(0, s.Damage - recovery);
        s.BodyDmg = Math.Max(0, s.BodyDmg - recovery * 0.7);
    }

    // ---- injuries & lasting wear (career-sim hooks) ----

    private IReadOnlyList<Injury> DetermineInjuries(Boxer a, Boxer b, State sa, State sb, Boxer? winner, string method)
    {
        var list = new List<Injury>();
        foreach (var (st, f) in new[] { (sa, a), (sb, b) })
        {
            bool loser = winner is not null && winner != f;

            // A deep cut that genuinely needed stitching (a nick heals without missing time).
            if (st.Cut >= 0.7 && _rng.NextDouble() < 0.5)
            {
                bool deep = st.Cut >= 0.88;
                list.Add(new Injury
                {
                    Name = f.Name, Type = $"a {(deep ? "bad " : "")}cut {st.CutLoc ?? "above the eye"}",
                    Severity = deep ? "moderate" : "minor",
                    // A bad cut needs real time for the scar tissue to knit; a nick still costs a few weeks.
                    LayoffDays = deep ? 49 + _rng.Next(35) : 21 + _rng.Next(21)
                });
            }

            // A badly swollen or fully closed eye from sustained head work.
            if (st.SwellEye && st.Swell >= 0.7 && _rng.NextDouble() < 0.45)
            {
                bool closed = st.Swell >= 0.9;
                list.Add(new Injury
                {
                    Name = f.Name, Type = closed ? $"a closed eye ({st.SwellLoc ?? "eye"})" : $"heavy swelling around {st.SwellLoc ?? "the eye"}",
                    Severity = "minor", LayoffDays = (closed ? 21 : 10) + _rng.Next(18)
                });
            }

            // A genuinely broken hand — not just a sore one. Bone plus rehab: a couple of months, often more.
            if (st.HandHurt && _rng.NextDouble() < 0.28)
                list.Add(new Injury { Name = f.Name, Type = "a broken hand", Severity = "moderate", LayoffDays = 63 + _rng.Next(56) });

            // A bad knockout can leave a concussion — mandatory medical suspensions keep him out for months.
            if (loser && method == "KO" && st.Damage >= 0.78 && _rng.NextDouble() < 0.30)
                list.Add(new Injury { Name = f.Name, Type = "a concussion", Severity = "moderate", LayoffDays = 84 + _rng.Next(56) });

            // Very rare, genuinely serious injury — the better part of a year, sometimes career-ending.
            if (_rng.NextDouble() < 0.006)
            {
                string[] majors = { "a detached retina", "a fractured orbital bone", "a broken jaw" };
                bool retires = f.Age >= 35 && _rng.NextDouble() < 0.4;
                list.Add(new Injury
                {
                    Name = f.Name, Type = majors[_rng.Next(majors.Length)], Severity = "serious",
                    LayoffDays = 240 + _rng.Next(300), Retires = retires
                });
            }
        }
        return list;
    }

    private IReadOnlyList<LastingEffect> LastingEffects(Boxer a, Boxer b, State sa, State sb, Boxer? winner, string method, bool bodyStop, bool war)
    {
        var list = new List<LastingEffect>();
        foreach (var (st, f) in new[] { (sa, a), (sb, b) })
        {
            bool loser = winner is not null && winner != f;
            if (st.Cut >= 0.78 && st.CutLoc is not null && _rng.NextDouble() < 0.22)
                list.Add(new LastingEffect { Name = f.Name, Note = $"scar tissue {st.CutLoc} — will cut more easily here", Attr = "CutResistance", Delta = -(2 + (int)Math.Round(_rng.NextDouble() * 3)) });
            if (st.HandHurt && _rng.NextDouble() < 0.28)
                list.Add(new LastingEffect { Name = f.Name, Note = "a hand that may never be 100% — power slightly diminished", Attr = "Power", Delta = -(2 + (int)Math.Round(_rng.NextDouble() * 2)) });

            bool brutalKO = loser && method == "KO" && !bodyStop && st.TotalKnockdowns >= 2;
            if ((brutalKO && _rng.NextDouble() < 0.18) || (war && st.TotalKnockdowns >= 1 && _rng.NextDouble() < 0.07))
                list.Add(new LastingEffect
                {
                    Name = f.Name,
                    Note = brutalKO ? "a chin a fraction more fragile after that knockout" : "a little more worn after that war",
                    Attr = "Chin", Delta = -(1 + (int)Math.Round(_rng.NextDouble() * 2))
                });
        }
        return list;
    }

    // ---- tick bookkeeping ----

    private static string ClockLabel(int tickIndex)
    {
        int remaining = 170 - tickIndex * 10; // 2:50 down to 0:00
        if (remaining < 0) remaining = 0;
        return $"{remaining / 60}:{remaining % 60:00}";
    }

    private static void FoldHit(FightTick t, Hit hit, bool attacker, State sa, State sb)
    {
        if (hit.Landed)
        {
            if (attacker)
            {
                if (hit.Big) { t.BigA = true; t.PunchA = hit.Type; t.BodyShotA = hit.Body; }
                if (hit.ComboLen > 1) { t.ComboA = hit.ComboLen; t.ComboBodyA = hit.ComboBody; }
                if (hit.HandHurt) t.HandA = true;
                if (hit.ToBodyKd) t.DownBodyB = true;
                if (hit.Staggered) t.StaggerB = true;       // A wobbled B and teed off
                t.RockB = Math.Max(t.RockB, RockLevel(sb));
            }
            else
            {
                if (hit.Big) { t.BigB = true; t.PunchB = hit.Type; t.BodyShotB = hit.Body; }
                if (hit.ComboLen > 1) { t.ComboB = hit.ComboLen; t.ComboBodyB = hit.ComboBody; }
                if (hit.HandHurt) t.HandB = true;
                if (hit.ToBodyKd) t.DownBodyA = true;
                if (hit.Staggered) t.StaggerA = true;
                t.RockA = Math.Max(t.RockA, RockLevel(sa));
            }
        }

        if (hit.CounterLanded)   // the defender lands the counter, so it shows on the other corner
        {
            if (attacker)
            {
                if (hit.CounterBig) { t.BigB = true; t.PunchB = hit.CounterType; t.BodyShotB = hit.CounterBody; }
                if (hit.CounterToBodyKd) t.DownBodyA = true;
                t.CounterB = true;
                t.RockA = Math.Max(t.RockA, RockLevel(sa));
            }
            else
            {
                if (hit.CounterBig) { t.BigA = true; t.PunchA = hit.CounterType; t.BodyShotA = hit.CounterBody; }
                if (hit.CounterToBodyKd) t.DownBodyB = true;
                t.CounterA = true;
                t.RockB = Math.Max(t.RockB, RockLevel(sb));
            }
        }
    }

    private static int RockLevel(State s)
    {
        double d = Math.Max(s.Damage, s.BodyDmg);
        return d >= 0.9 ? 2 : d >= 0.6 ? 1 : 0;
    }

    private static void Snapshot(FightTick t, State sa, State sb)
    {
        t.LandedA = sa.RoundLanded; t.LandedB = sb.RoundLanded;
        t.KnockdownsA = sa.RoundKnockdowns; t.KnockdownsB = sb.RoundKnockdowns;
        t.DamageA = sa.Damage; t.DamageB = sb.Damage;
        t.BodyA = sa.BodyDmg; t.BodyB = sb.BodyDmg;
        t.CutA = sa.Cut; t.CutB = sb.Cut; t.SwellA = sa.Swell; t.SwellB = sb.Swell;
    }

    private static void FillTicks(FightTick[] ticks, bool[] touched, State sa, State sb, int round, int exchanges)
    {
        FightTick? last = null;
        for (int i = 0; i < ticks.Length; i++)
        {
            if (touched[i]) { last = ticks[i]; continue; }
            var t = ticks[i];
            if (last is null)
            {
                // round-start cumulative: round-local counters at 0, carry fight-wide damage pools
                t.DamageA = sa.Damage; t.DamageB = sb.Damage; t.BodyA = sa.BodyDmg; t.BodyB = sb.BodyDmg;
                t.CutA = sa.Cut; t.CutB = sb.Cut; t.SwellA = sa.Swell; t.SwellB = sb.Swell;
            }
            else
            {
                t.LandedA = last.LandedA; t.LandedB = last.LandedB;
                t.KnockdownsA = last.KnockdownsA; t.KnockdownsB = last.KnockdownsB;
                t.DamageA = last.DamageA; t.DamageB = last.DamageB; t.BodyA = last.BodyA; t.BodyB = last.BodyB;
                t.CutA = last.CutA; t.CutB = last.CutB; t.SwellA = last.SwellA; t.SwellB = last.SwellB;
            }
        }
    }

    private T Pick<T>(T[] arr) => arr[_rng.Next(arr.Length)];
}
