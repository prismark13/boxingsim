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


    /// <summary>What a man carries up with him.
    ///
    /// He carried nothing. His ranking points came up with him and that was all, so a world champion arrived in
    /// the division above as an anonymous body with a good Elo, behind five men who had spent years getting
    /// where they were — and had to climb the whole board again against opposition he was plainly better than,
    /// with his power and chin freshly docked for the weight. That is not what happens. A sanctioning body
    /// ranks a champion who moves up the week he arrives, usually in the top few, and it is common to see him
    /// challenging for the belt inside a year: Leonard, Duran, Hearns and Pacquiao all did it, none of them
    /// spent four fights proving they belonged.
    ///
    /// Written as a FLOOR on his standing rather than as a bonus to it, which matters for two reasons. It is
    /// self-calibrating — the credit is worth whatever the new division is worth, so moving into a strong one
    /// gives more ground than moving into a weak one, and no number here needs retuning when the world changes.
    /// And it cannot inflate anybody: a man already above the floor keeps his own points and gains nothing, so
    /// this can only ever rescue a standing that understates him.
    ///
    /// A world champion is floored at third. Not first — he has to beat somebody up here before he is the best
    /// man in the division — but high enough that a couple of tune-ups and a year of the ability anchor pulling
    /// on him still leave him inside the top five the title-shot gate asks for. Floored at fifth he is exactly
    /// eligible and one ordinary night from not being, which makes the promise conditional on nothing moving.
    /// A ranked contender is floored at fifteenth: on the board, in the mix, with work to do.</summary>
    private void CarryStandingUp(Boxer b, WeightClass to, int floorPlace)
    {
        if (floorPlace <= 0) return;
        // The same list, in the same order, that the matchmaker reads to decide whether he is top five — see
        // Active in BuildOffer. Ranked against the contender-filtered board instead, he could clear the floor
        // here and still be sixth where it counts.
        var others = ActiveIn(to).Where(x => x.Id != b.Id).OrderByDescending(RankScore).ToList();
        if (others.Count < floorPlace) return;   // too thin a division to have an Nth place is no floor at all
        double owed = RankScore(others[floorPlace - 1]) + 1 - RankScore(b);   // +1 so he is past that man, not level with him
        if (owed > 0) b.RankPoints += owed;
    }

    /// <summary>Send a fighter up to the next division: he relinquishes any belts he held, is rebalanced, keeps
    /// his record, and (unless he's seeding a brand-new division) needs 1–4 warm-up fights before a title shot.</summary>
    private void MoveUpTo(Boxer b, WeightClass to, bool warmup = true)
    {
        var from = b.WeightClass;
        b.DebutWeight ??= from;   // captured on the first move up — the floor the two-division climb cap measures from
        var vacated = BeltsHeld(b).Select(x => x.Belt).ToList();
        // Read before anything is taken off him: in a moment he holds no belts and stands nowhere, and what he
        // was on the way out is the whole basis of what he is worth on the way in.
        bool wasChampion = IsWorldChampion(b);
        int placeBefore = BoardPlace(b);
        if (b.IsChampion) b.IsChampion = false;
        _titles.VacateWorldBelts(from, b);
        VacateLineal(from, b, $"moves up to {to.DisplayName()}");
        foreach (var region in RegionalBelts) if (_titles.Regional(from, region)?.Id == b.Id) _titles.ClearRegional(from, region);
        if (vacated.Count > 0 && (b.Id == Player.Id || from == Division))
            LogEvent($"{b.Name} relinquishes the {string.Join(", ", vacated)} title{(vacated.Count > 1 ? "s" : "")} to move up to {to.DisplayName()}.", b.Id == Player.Id, kind: "title", div: from);
        b.WeightClass = to;
        if (b.Id != Player.Id && (WorldRanked(b) || b.Class >= 8))
            // "move", not "title". It can vacate belts, which is why it was filed as title news, but a man
            // changing division is not a title fight — and with a title filter on the news feed it was the one
            // thing showing up in a list of championship results that had not been a fight at all. Nothing
            // depended on the old kind: WorthWatching only considers events that carry a bout, and this has none.
            LogEvent($"{b.Name} campaigns up to {to.DisplayName()}{(vacated.Count > 0 ? $", vacating the {string.Join(", ", vacated)}" : "")}.", false, kind: "move", div: to);
        RebalanceRatings(b.Ratings);
        b.Potential = b.Overall;
        if (_historical.TryGetValue(b.Id, out var h)) { var prime = h.Prime.Clone(); RebalanceRatings(prime); _historical[b.Id] = (prime, h.Peak); }
        // After the rebalance, so the credit is not handed out and then docked back off again.
        CarryStandingUp(b, to, wasChampion ? 3 : placeBefore is > 0 and <= 10 ? 15 : 0);
        if (warmup)
        {
            // A champion does not serve the same apprenticeship as a man nobody has heard of. One or two
            // tune-ups to find out what he feels like at the weight, and then the fight everyone wants —
            // rather than the one-to-four a stranger gets, which at the top of its range meant a champion
            // could move up and spend two years being matched with gatekeepers.
            _warmupUntil[b.Id] = ProFights(b) + 1 + (wasChampion ? _rng.Next(2) : _rng.Next(4));
            StepUpsPerformed++;
        }
    }

    /// <summary>Count of organic career move-ups performed (excludes new-division seeding). Diagnostics/tests.</summary>
    public int StepUpsPerformed { get; private set; }


    /// <summary>How many more fights the player has to take before a world title in his new division is open
    /// to him, or 0 if it already is. A man who has just moved up is told nothing about why the belt has gone
    /// quiet, and "one more and you can challenge" is the single most useful thing the camp could tell him.</summary>
    public int TuneUpsLeft =>
        _warmupUntil.TryGetValue(Player.Id, out var t) ? Math.Max(0, t - ProFights(Player)) : 0;

    /// <summary>The per-fight step-up chance by career stage, exposed for verification/tuning.</summary>
    public static double StepUpChanceAt(CareerStage stage) => PerFightStepUpBase(stage);

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

    // Fighters who have decided to campaign up, and the night it takes effect.
    //
    // These were flushed together at the turn of the year, so every move-up in the sport happened on
    // 1 January: a division lost four men and gained six on the same day, and a man who outgrew his weight in
    // February waited eleven months to act on it. A fighter moves up in the weeks AFTER a fight — the decision
    // is made in the dressing room — so each carries his own date, four to twelve weeks out.
    private readonly Dictionary<int, DateOnly> _stepUpQueued = new();

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

    /// <summary>The flat per-fight chance any fighter drifts up a weight.
    ///
    /// This used to peak POST-PRIME, on the reasoning that a man is likeliest to outgrow his weight late. What
    /// it actually produced was a sport where crossing a division was the last thing a fighter ever did.
    /// Measured over twenty years and 435 move-ups: the median man moved at 35 fights and had 8 left, 40% were
    /// gone within SIX fights of arriving, and of those, 161 of 174 hung them up on age and mileage rather than
    /// drifting out for lack of opposition. So it was not a matchmaking failure — they were simply being sent
    /// up with nothing left to spend there, and a division kept filling with men who came to retire.
    ///
    /// Real careers climb through the prime, not out the back of it. Leonard, Duran, Hearns and Pacquiao all
    /// went up while they were the best version of themselves — that is the whole point of moving, to go and
    /// win something else. The weight of it belongs where the career is, so the curve peaks in the prime and
    /// tails off after, and a young man growing out of a light division gets a real chance at it too.</summary>
    private static double PerFightStepUpBase(CareerStage stage) => stage switch
    {
        CareerStage.PrePrime => 0.003,    // a kid outgrowing the division he debuted in — bodies fill out young
        CareerStage.Prime => 0.006,       // the classic move: champion at one weight, off to win a second
        CareerStage.PostPrime => 0.004,   // some still do, but it is no longer the likeliest time
        _ => 0.0                          // Starter: too early. End: see the guard in TryQueueStepUp.
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
                _titles.VacateWorldBelts(champ.WeightClass, champ);
                champ.IsChampion = false;
                MaybeInductHoF(champ);
                LogEvent($"{champ.Name} retires as champion after {defs} defences, going out on top.", kind: "retire", div: champ.WeightClass);
            }
        }
    }

    private void TryQueueStepUp(Boxer? b, double p)
    {
        if (b is null || b.Id == Player.Id || b.Retired || p <= 0) return;
        // NOBODY CROSSES A DIVISION TO RETIRE IN IT. A man in the last stretch has nothing to build up there:
        // he arrives, takes a tune-up, and hangs them up. This catches the champion path as well as the flat
        // one — a twelve-defence reign can put a man in the End stage, and the escalating hazard would happily
        // send him up for two fights and a retirement notice.
        if (CareerStages.Of(b) == CareerStage.End) return;
        if (_stepUpQueued.ContainsKey(b.Id)) return;
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
        // Rolled right after a bout, so the wait is measured from the fight that prompted it.
        if (_rng.NextDouble() < p * greatness * weariness)
            _stepUpQueued[b.Id] = Date.AddDays(28 + _rng.Next(57));   // 4 to 12 weeks
    }

    /// <summary>Move up anyone whose date has come. Called from the world tick rather than the yearly pass,
    /// so a division changes shape through the year instead of all at once on 1 January — and still between
    /// cards rather than during one, which was the only thing the yearly flush was really buying.
    ///
    /// The division he leaves is told at once. The yearly pass ended by putting every division's belts in
    /// order, so a champion who moved up had his vacancy noticed in the same breath; out here nothing is
    /// looking, and a belt vacated in February would sit unordered until the following January.</summary>
    private void SettleDueStepUps()
    {
        if (_stepUpQueued.Count == 0) return;
        foreach (var (id, on) in _stepUpQueued.ToList())
        {
            if (on > Date) continue;
            _stepUpQueued.Remove(id);
            var b = _roster.FirstOrDefault(x => x.Id == id && !x.Retired);
            if (b is not null && NextActiveUp(b.WeightClass) is WeightClass to && StepUpAllowed(b, to))
            {
                var from = b.WeightClass;
                MoveUpTo(b, to);
                UpdateBeltsFor(from);
            }
        }
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
