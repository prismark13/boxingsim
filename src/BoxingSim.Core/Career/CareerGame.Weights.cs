using BoxingSim.Core.Engine;
using BoxingSim.Core.Generation;
using BoxingSim.Core.League;
using BoxingSim.Core.Model;

namespace BoxingSim.Core.Career;

/// <summary>Moving up: whether a fighter may, whether he will, and what it costs him when he does.</summary>
/// <summary>Moving up: whether a fighter may, whether he will, and what it costs him when he does.</summary>
public sealed partial class CareerGame
{
    // ---- weight movement ----

    private static WeightClass? NextUp(WeightClass wc) =>
        wc == WeightClass.Heavyweight ? null : (WeightClass)((int)wc + 1);

    /// <summary>The next division up that actually exists in the current year (skips not-yet-founded classes).</summary>
    private WeightClass? NextActiveUp(WeightClass wc)
    {
        var n = NextUp(wc);
        while (n is WeightClass w && !DivisionActive(w)) n = NextUp(w);
        return n;
    }

    /// <summary>Moving up a class: skill carries, but a fighter is relatively lighter-hitting and less
    /// durable against bigger men, so power and chin ease off (and a touch of speed).</summary>
    private void RebalanceRatings(Ratings r)
    {
        r.Power = Ratings.Clamp(r.Power - (4 + _rng.Next(4)));
        r.Chin = Ratings.Clamp(r.Chin - (3 + _rng.Next(3)));
        r.Speed = Ratings.Clamp(r.Speed - _rng.Next(3));
    }

    private readonly Dictionary<int, int> _warmupUntil = new();   // fighter id → pro-fight count he must reach before a title shot in the new class
    /// <summary>A fighter who just moved up needs a few tune-ups before he can contest a title in the new class.</summary>
    private bool RecentlyMovedUp(Boxer b) => _warmupUntil.TryGetValue(b.Id, out var t) && ProFights(b) < t;

    /// <summary>Send a fighter up to the next division: he relinquishes any belts he held, is rebalanced, keeps
    /// his record, and (unless he's seeding a brand-new division) needs 1–4 warm-up fights before a title shot.</summary>
    private void MoveUpTo(Boxer b, WeightClass to, bool warmup = true)
    {
        var from = b.WeightClass;
        b.DebutWeight ??= from;   // captured on the first move up — the floor the two-division climb cap measures from
        var vacated = BeltsHeld(b).Select(x => x.Belt).ToList();
        if (b.IsChampion) b.IsChampion = false;
        if (ChampOf(from)?.Id == b.Id) _champions[from] = null;
        if (WbcOf(from)?.Id == b.Id) _wbc[from] = null;
        if (IbfOf(from)?.Id == b.Id) _ibf[from] = null;
        VacateLineal(from, b, $"moves up to {to.DisplayName()}");
        foreach (var region in RegionalBelts) if (_regional.GetValueOrDefault((from, region))?.Id == b.Id) _regional.Remove((from, region));
        if (vacated.Count > 0 && (b.Id == Player.Id || from == Division))
            LogEvent($"{b.Name} relinquishes the {string.Join(", ", vacated)} title{(vacated.Count > 1 ? "s" : "")} to move up to {to.DisplayName()}.", b.Id == Player.Id, kind: "title", div: from);
        b.WeightClass = to;
        if (b.Id != Player.Id && (WorldRanked(b) || b.Class >= 8))
            LogEvent($"{b.Name} campaigns up to {to.DisplayName()}{(vacated.Count > 0 ? $", vacating the {string.Join(", ", vacated)}" : "")}.", false, kind: "title", div: to);
        RebalanceRatings(b.Ratings);
        b.Potential = b.Overall;
        if (_historical.TryGetValue(b.Id, out var h)) { var prime = h.Prime.Clone(); RebalanceRatings(prime); _historical[b.Id] = (prime, h.Peak); }
        if (warmup) { _warmupUntil[b.Id] = ProFights(b) + 1 + _rng.Next(4); StepUpsPerformed++; }
    }

    /// <summary>Count of organic career move-ups performed (excludes new-division seeding). Diagnostics/tests.</summary>
    public int StepUpsPerformed { get; private set; }

    /// <summary>The escalating champion step-up hazard, exposed for verification/tuning.</summary>
    public static double DefenceStepUpHazardAt(int defenceNumber) => DefenceStepUpHazard(defenceNumber);

    /// <summary>The defence-driven unification curve, exposed for verification/tuning.</summary>
    public static double UnificationChanceAt(int defencesA, int defencesB, double baseChance, double cap) =>
        baseChance + (cap - baseChance) * Math.Min(1.0, (Math.Min(defencesA, defencesB) * 3 + Math.Max(defencesA, defencesB)) / 18.0);

    /// <summary>The year a division is founded, seed it by moving a tier of established contenders up from the
    /// division just below — so it opens with ranked fighters and crowns a champion straight away, rather than
    /// sitting empty while debutants slowly mature.</summary>
    private void SeedNewlyFoundedDivisions()
    {
        foreach (var wc in AllDivisions)
        {
            if (wc.FoundedYear() != Year || (int)wc == 0) continue;
            var below = (WeightClass)((int)wc - 1);
            // The next tier of established contenders below (not the very top two, who stay) move up to the new class.
            var movers = ActiveIn(below)
                .Where(b => b.Id != Player.Id && !_historical.ContainsKey(b.Id) && WorldRanked(b))
                .OrderByDescending(RankScore).Skip(2).Take(16).ToList();
            foreach (var b in movers) MoveUpTo(b, wc, warmup: false);   // founding contenders are eligible at once
        }
    }

    // Fighters queued to campaign up a division; applied together at year's end so no one changes weight
    // mid-card. Populated per-bout (see ConsiderStepUp) rather than by a single yearly roll.
    private readonly HashSet<int> _stepUpQueued = new();

    /// <summary>Can this fighter move up to <paramref name="to"/>?
    ///
    /// This used to be a flat count: two divisions above where he started, and no further. That rule wrote
    /// the sport's best careers out of existence. Sugar Ray Leonard went from welterweight to light
    /// heavyweight and won titles at five weights; Duran went lightweight to middleweight; Hearns went
    /// welterweight to cruiserweight; Pacquiao went flyweight to light middleweight. Every one of them is a
    /// four-to-eight division climb, and the sim forbade all of it.
    ///
    /// The real limit is not a number of divisions — it is a body. What a man cannot do is put on unlimited
    /// weight and still be himself, and how many DIVISIONS that buys him depends entirely on where he
    /// started, because the scale is not evenly spaced. Six pounds separate flyweight from bantamweight;
    /// twenty-five separate light heavyweight from cruiserweight. So a flyweight can climb six divisions on
    /// the same forty-odd pounds that takes a welterweight three.
    ///
    /// The limit is therefore weight gained, capped at about 40% above his debut division. Measured against
    /// the men who actually did it: Leonard +19%, Duran +19%, De La Hoya +23%, Hearns +36%, Pacquiao +37%.
    /// Forty per cent admits all of them and still stops a flyweight becoming a heavyweight.</summary>
    private bool StepUpAllowed(Boxer b, WeightClass to)
    {
        // A real fighter with a documented ceiling never climbs past the top weight he actually campaigned at.
        if (_historical.ContainsKey(b.Id) && b.TopWeight is WeightClass top) return (int)to <= (int)top;

        var from = b.DebutWeight ?? b.WeightClass;
        return ScaleWeight(to) <= ScaleWeight(from) * 1.40;
    }

    /// <summary>A division's weight for comparison. Heavyweight has no limit, so it is read as the weight a
    /// heavyweight actually is rather than as infinity — otherwise nothing could ever climb into it.</summary>
    private static double ScaleWeight(WeightClass wc) =>
        wc == WeightClass.Heavyweight ? 215 : wc.WeightLimitLbs();

    /// <summary>The flat per-fight chance any fighter drifts up a weight, from his prime onward (bodies fill out).
    /// Zero before the prime — a kid isn't outgrowing his division yet.</summary>
    private static double PerFightStepUpBase(CareerStage stage) => stage switch
    {
        CareerStage.Prime => 0.008,       // ~0.8%/fight
        CareerStage.PostPrime => 0.015,   // ~1.5%/fight — most likely to outgrow the weight late
        CareerStage.End => 0.010,
        _ => 0.0                          // Starter / PrePrime: too early
    };

    /// <summary>The extra, escalating chance a champion vacates to move up on the back of a title defence.
    /// The hazard grows with the number of defences so a long reign almost forces a step up — calibrated so
    /// the cumulative chance of having moved by the 10th defence is ~65%.</summary>
    private static double DefenceStepUpHazard(int defenceNumber)
    {
        if (defenceNumber < 1) return 0;
        // Survival S(n) = exp(-k·n²) with k≈0.0105 gives S(10)≈0.35 (→65% moved). Conditional per-defence
        // hazard = 1 − S(n)/S(n−1) = 1 − exp(−k·(2n−1)) — ~1% at the first defence rising to ~18% by the tenth.
        const double k = 0.0105;
        return 1.0 - Math.Exp(-k * (2 * defenceNumber - 1));
    }

    /// <summary>Roll a fighter's flat per-fight chance to campaign up a weight — called for both fighters after
    /// every NPC bout, so from his prime on any fighter may gradually outgrow his division.</summary>
    private void ConsiderStepUp(Boxer? b) => TryQueueStepUp(b, PerFightStepUpBase(CareerStages.Of(b!)));

    /// <summary>Roll the escalating champion-only hazard after a successful title defence (on top of the flat
    /// per-fight chance already rolled in <see cref="ApplyOutcome"/>) — a long reign almost forces a move.</summary>
    private void ConsiderTitleStepUp(Boxer? champ)
    {
        if (champ is null) return;
        int defs = BeltsHeld(champ).Select(x => x.Defenses).DefaultIfEmpty(0).Max();
        TryQueueStepUp(champ, DefenceStepUpHazard(defs));
        // Nobody defends forever: a champion piling up defences (who hasn't moved up) increasingly walks away on
        // top rather than fighting on — capping ultra-long reigns, especially in a division he can't climb out of.
        if (defs >= 12 && champ.Id != Player.Id && !champ.Retired)
        {
            double retireOnTop = (defs - 11) * 0.05;   // ~5% at 12 defences, rising past ~40% by 20
            if (_rng.NextDouble() < retireOnTop)
            {
                champ.Retired = true;
                if (ChampOf(champ.WeightClass)?.Id == champ.Id) _champions[champ.WeightClass] = null;
                if (WbcOf(champ.WeightClass)?.Id == champ.Id) _wbc[champ.WeightClass] = null;
                if (IbfOf(champ.WeightClass)?.Id == champ.Id) _ibf[champ.WeightClass] = null;
                champ.IsChampion = false;
                MaybeInductHoF(champ);
                LogEvent($"{champ.Name} retires as champion after {defs} defences, going out on top.", kind: "retire", div: champ.WeightClass);
            }
        }
    }

    private void TryQueueStepUp(Boxer? b, double p)
    {
        if (b is null || b.Id == Player.Id || b.Retired || p <= 0) return;
        if (_stepUpQueued.Contains(b.Id)) return;
        if (NextActiveUp(b.WeightClass) is not WeightClass to || !StepUpAllowed(b, to)) return;
        // The greater the fighter, the more he chases legacy across the weights — an all-time great is far likelier
        // to move up and hunt a second and third belt, so his step-up chance is skewed sharply upward.
        int ceiling = Math.Max(b.Overall, b.Potential);
        double greatness = 1.0 + Math.Max(0, ceiling - 76) / 14.0;   // ~1.0 at contender level, ~2.6 for an ATG
        // And each division he has already climbed makes the next one less likely. With the ceiling now set by
        // his frame rather than by a flat count of two, something has to make a long climb RARE as well as
        // possible — otherwise the whole division drifts upward over a career and everyone ends up a
        // cruiserweight. A second move is two-thirds as likely as the first, a fourth about a fifth.
        int climbed = (int)b.WeightClass - (int)(b.DebutWeight ?? b.WeightClass);
        double weariness = Math.Pow(0.62, climbed);
        if (_rng.NextDouble() < p * greatness * weariness) _stepUpQueued.Add(b.Id);
    }

    /// <summary>Apply every queued move-up. Run once a year (from AgeRetireCrown) so weight changes land between
    /// campaigns, never in the middle of a card.</summary>
    private void FlushStepUps()
    {
        if (_stepUpQueued.Count == 0) return;
        foreach (var id in _stepUpQueued.ToList())
        {
            var b = _roster.FirstOrDefault(x => x.Id == id && !x.Retired);
            if (b is not null && NextActiveUp(b.WeightClass) is WeightClass to && StepUpAllowed(b, to))
                MoveUpTo(b, to);
        }
        _stepUpQueued.Clear();
    }

    /// <summary>How likely the two world champions finally meet. Demand builds with DEFENCES: two established
    /// champions who each keep turning back challengers are the fight the public wants and the one the sanctioning
    /// bodies can't keep apart, while a pair who've only just won their belts have everything to lose and little
    /// to gain. The shorter of the two reigns drives it — it takes two established men to make the fight — with
    /// the longer reign adding a little on top. Roughly five defences apiece maxes the pressure out.</summary>
    private double UnificationChance(WeightClass wc, double baseChance, double cap)
    {
        if (ChampOf(wc) is not Boxer a || WbcOf(wc) is not Boxer b || a.Id == b.Id) return 0;
        int da = DefensesOf(wc, "WBA", a.Id), db = DefensesOf(wc, "WBC", b.Id);
        int pressure = Math.Min(da, db) * 3 + Math.Max(da, db);
        return baseChance + (cap - baseChance) * Math.Min(1.0, pressure / 18.0);
    }

}
