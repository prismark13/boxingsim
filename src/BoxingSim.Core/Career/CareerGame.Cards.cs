using BoxingSim.Core.Engine;
using BoxingSim.Core.Generation;
using BoxingSim.Core.League;
using BoxingSim.Core.Model;

namespace BoxingSim.Core.Career;

/// <summary>Moving up: whether a fighter may, whether he will, and what it costs him when he does.</summary>
/// <summary>The cards the world puts on: the fortnightly shows, the NPC season, title defences and
/// unifications. This is where most of the sport actually happens.</summary>
public sealed partial class CareerGame
{
    /// <summary>When each division last put a card on. A fortnight is measured in calendar days, not in
    /// calls.</summary>
    private readonly Dictionary<WeightClass, DateOnly> _lastCard = new();

    /// <summary>How often a division boxes. The method below has always called itself a fortnightly card and
    /// this is what makes it one.</summary>
    private const int DaysBetweenCards = 14;

    /// <summary>A fortnight's fight cards across every division — for the divisions that are due one.
    ///
    /// This used to run a card for every division on EVERY step, whatever the step was: fourteen days when
    /// the world was catching up, seven when the player was waiting out a camp, seven for a universe week. So
    /// a "fortnightly" card was fortnightly only when the caller happened to step a fortnight.
    ///
    /// It was hidden while resolving a bout dragged the world clock along with it — each card pushed the date
    /// forward, so the next one landed a fortnight or more later in calendar terms whatever the step size.
    /// Take the dragging away (which is what a clock is for) and the cards fire twice as often as intended,
    /// men are matched twice as often as they can be rested, and the scheduler ends up shoving bouts weeks
    /// into the future to find them a legal night.
    ///
    /// Asking the calendar how long it has been makes the cadence independent of who is stepping and by how
    /// much, which is what it always meant.</summary>
    private void RunEvent()
    {
        foreach (var wc in AllDivisions)
        {
            if (!DivisionActive(wc)) continue;
            if (_lastCard.TryGetValue(wc, out var last) && Date.DayNumber - last.DayNumber < DaysBetweenCards)
                continue;
            _lastCard[wc] = Date;
            RunEventCard(wc);
        }
    }

    /// <summary>One division's fortnightly card: an occasional title defence plus showcase undercards.</summary>

    /// <summary>Whether a champion puts his belt up on this card.
    ///
    /// A world champion cannot box on an ordinary card — when he fights, it is a defence — so this alone
    /// decides how often he is seen at all, and it used to leave him idle for a year. The gate was a
    /// sixteen-week rest and then a 5.5% roll per fortnightly card: sixteen weeks plus a mean wait of
    /// eighteen cards is a gap of about twelve months, so a reigning champion averaged ONE defence a year and
    /// a bad run of rolls buried him. Joe Frazier held the WBC and the Ring belt and did not box for the nine
    /// months after January 1975, which is not a champion, it is a man with a belt in a drawer.
    ///
    /// A real champion defends between one and four times a year, five months apart on average. That is the
    /// rest floor plus roughly three cards of waiting, so the roll is a third rather than a twentieth:
    ///
    ///   112 days rest  +  14 / 0.33 ≈ 42 days waiting  ≈  154 days  ≈  five months
    ///
    /// The floor caps him at under four a year and the backstop stops the tail: ten months idle and he is
    /// matched, roll or no roll. Activity scales both ends, so a busy champion of a busy era fights more.</summary>
    private bool DefendsOnThisCard(Boxer champ)
    {
        if (BookedWithThePlayer(champ)) return false;   // he is already booked — against the player
        double activity = CareerMileage.Activity(champ);
        int idle = DaysSinceLastBout(champ);
        if (idle < (int)(112 / activity)) return false;   // a title camp is a real camp
        if (idle >= 300) return true;                     // nobody sits on a belt for ten months
        return _rng.NextDouble() < 0.33 * activity;
    }

    private void RunEventCard(WeightClass wc)
    {
        // Champions don't fight on undercards — when they fight, it's a title defence (handled below).
        // A man who's already boxed 8 times this year sits the rest of it out.
        var pool = ActiveIn(wc).Where(b => b.Id != Player.Id && !HoldsAnyWorldBelt(b) && !AtYearCap(b) && _medical.Available(b)
                                        && Rested(b) && !BookedWithThePlayer(b))
                         .OrderByDescending(b => b.Overall).ToList();
        if (pool.Count < 2) return;

        if (ChampOf(wc) is Boxer stale && !stale.IsChampion) _titles.SetChamp(wc, null);

        // A rare unification is checked FIRST and, when it fires, is the only world-title bout on this card:
        // the belts merge in one fight rather than each champion ALSO making a separate defence the same
        // fortnight (which produced impossible back-to-back title bouts days apart). Both men must be rested.
        if (!UnifiedIn(wc) && ChampOf(wc) is Boxer wba && WbcOf(wc) is Boxer wbc && wba.Id != wbc.Id
            && wba.Id != Player.Id && wbc.Id != Player.Id
            && !BookedWithThePlayer(wba) && !BookedWithThePlayer(wbc)   // one of them owes the player a night
            && DaysSinceLastBout(wba) >= (int)(112 / CareerMileage.Activity(wba)) && DaysSinceLastBout(wbc) >= (int)(112 / CareerMileage.Activity(wbc))
            && _rng.NextDouble() < UnificationChance(wc, 0.006, 0.04))
        {
            Unify(wc);
        }
        else if (UnifiedIn(wc))
        {
            var c = ChampOf(wc)!;
            if (c.Id != Player.Id && DefendsOnThisCard(c))   // ~2 defences a year, min 14 weeks apart
            {
                if (_rng.NextDouble() < 0.10) RelinquishBelt(c);   // ~1 in 10: ducks a mandatory and gives up a belt
                else UnifiedDefence(c);
            }
        }
        else
        {
            if (ChampOf(wc) is Boxer champ && champ.Id != Player.Id && DefendsOnThisCard(champ))   // ~2 defences a year, min 14 weeks apart
            {
                var ch = PickChallenger(champ, WbcOf(wc));
                if (ch is not null)
                {
                    var res = FastBout(champ, ch, 12);
                    var on = ApplyOutcome(res, champ, ch, $"{PrimaryBelt} title");
                    if (!res.IsDraw && res.Winner!.Id == ch.Id) { LogTitle($"{ch.Name} DETHRONES {champ.Name} for the {PrimaryBelt} title!", wc, RefOf(res, on), on); CrownChampion(ch, on); }
                    else { Defended(wc, "WBA", champ.Id); LogTitle($"{champ.Name} retains the {PrimaryBelt} title against {ch.Name}.", wc, RefOf(res, on), on); ConsiderTitleStepUp(champ); }
                }
            }
            if (WbcOf(wc) is Boxer wbcC && wbcC.Id != Player.Id && DefendsOnThisCard(wbcC))   // ~2 defences a year, min 14 weeks apart
            {
                var ch = PickChallenger(wbcC, ChampOf(wc));
                if (ch is not null)
                {
                    var res = FastBout(wbcC, ch, 12);
                    var on = ApplyOutcome(res, wbcC, ch, "WBC title");
                    if (!res.IsDraw && res.Winner!.Id == ch.Id) { LogTitle($"{ch.Name} TAKES the WBC title from {wbcC.Name}!", wc, RefOf(res, on), on); CrownWbc(ch, on); }
                    else { Defended(wc, "WBC", wbcC.Id); LogTitle($"{wbcC.Name} retains the WBC title against {ch.Name}.", wc, RefOf(res, on), on); ConsiderTitleStepUp(wbcC); }
                }
            }
        }

        // IBF title defence — the third belt, contested independently from 1983.
        if (IbfActive && IbfOf(wc) is Boxer ibf && ibf.Id != Player.Id && DefendsOnThisCard(ibf))
        {
            var ch = PickChallenger(ibf, null);
            if (ch is not null)
            {
                var res = FastBout(ibf, ch, 12);
                var on = ApplyOutcome(res, ibf, ch, "IBF title");
                if (!res.IsDraw && res.Winner!.Id == ch.Id) { LogTitle($"{ch.Name} TAKES the IBF title from {ibf.Name}!", wc, RefOf(res, on), on); CrownIbf(ch, on); }
                else { Defended(wc, "IBF", ibf.Id); LogTitle($"{ibf.Name} retains the IBF title against {ch.Name}.", wc, RefOf(res, on), on); ConsiderTitleStepUp(ibf); }
            }
        }

        // Regional title defences — a regional champ risks his belt against a fellow regional contender.
        foreach (var region in RegionalBelts)
        {
            if (!_titles.TryRegional(wc, region, out var rc) || rc.Id == Player.Id || rc.Retired) continue;
            // Regional belts are meant to be DEFENDED - that is the whole point of holding one on the way up.
            // At a twentieth per card they mostly sat idle on a man's record.
            if (DaysSinceLastBout(rc) < 84 || _rng.NextDouble() >= 0.11 * CareerMileage.Activity(rc)) continue;
            // Rest is re-checked here rather than trusted from the pool: the pool was built at the top of the
            // card and men have boxed on it since. Nobody boxes twice on the same night.
            var candidates = pool.Where(b => RegionOf(b) == region && b.Id != rc.Id && CredibleForRegional(b) && Rested(b))
                                 .OrderByDescending(RankScore).ToList();
            // Picking straight down the ranking order meant an established contender always sat above a young
            // man and the belt only ever passed between men who had already arrived. Roughly one defence in
            // three now goes to somebody still coming through, which is what these titles are for.
            var comers = candidates.Where(b => !WorldRanked(b)).ToList();
            var chall = comers.Count > 0 && _rng.Next(3) == 0
                ? comers[_rng.Next(comers.Count)]
                : candidates.Skip(_rng.Next(4)).FirstOrDefault() ?? candidates.FirstOrDefault();
            if (chall is null) continue;
            var rres = FastBout(rc, chall, 12);
            var ron = ApplyOutcome(rres, rc, chall, $"{region} title");
            if (!rres.IsDraw && rres.Winner!.Id == chall.Id) { _titles.SetRegional(wc, region, chall); LogTitle($"{chall.Name} wins the {region} title from {rc.Name}.", wc, RefOf(rres, ron), ron); }
        }

        // A fortnight's cards across a whole division, scaled to how many men are in it. This used to be a
        // flat two to four bouts however large the division was, which in a roster of two hundred active
        // fighters gave each man about ONE fight a year - a contender should be out three or four times.
        // Sized so a typical fighter gets that, with world-ranked men thinned out separately by
        // FightChancePerCard because they take fewer, bigger nights.
        // A universe's activity dial has to move this number, not just each man's willingness. How busy the
        // sport is IS how many bouts get staged; a fighter who wants to be out more often cannot be if there
        // are no cards for him, which is why turning the dial on its own changed nothing.
        int bouts = Math.Clamp((int)Math.Round(pool.Count * 0.105 * CareerMileage.ActivityScale), 3, 90);
        var top20 = Top20Ids(wc); var top8 = Top8Ids(wc);

        // Anything the sport is owed goes on first.
        var staged = new HashSet<int>();
        StageDueRematches(pool, staged);
        var used = new HashSet<int>();
        for (int k = 0; k < pool.Count; k++) if (staged.Contains(pool[k].Id)) used.Add(k);
        for (int b = 0; b < bouts; b++)
        {
            int i = _rng.Next(pool.Count);
            if (used.Contains(i)) continue;
            int span = _rng.NextDouble() < 0.35 ? 40 + _rng.Next(70) : 12;   // ~35% a wide-gap tune-up
            int j = -1;
            for (int k = i + 1; k < Math.Min(pool.Count, i + span); k++)
                if (!used.Contains(k) && !BadMatch(pool[i], pool[k], top20, top8) && (j < 0 || _rng.NextDouble() < 0.5)) j = k;
            if (j < 0) for (int k = i + 1; k < pool.Count; k++) if (!used.Contains(k) && !BadMatch(pool[i], pool[k], top20, top8)) { j = k; break; }
            if (j < 0) continue;
            // Live, not from the pool snapshot: a title defence or a regional earlier on this same card may
            // have put one of these two in the ring already.
            if (!Rested(pool[i]) || !Rested(pool[j])) continue;
            used.Add(i); used.Add(j);
            var res = FastBout(pool[i], pool[j], 10);
            var on = ApplyOutcome(res, pool[i], pool[j]);
            ReportBout(res, on);
        }
    }

    private void RunNpcSeason()
    {
        foreach (var wc in AllDivisions) if (DivisionActive(wc)) RunNpcSeasonFor(wc);
    }

    private void RunNpcSeasonFor(WeightClass wc)
    {
        var fighters = ActiveIn(wc).Where(b => b.Id != Player.Id).ToList();
        if (fighters.Count < 2) return;
        int yr = Date.Year;

        // Title bouts: each champion defends 2–3 times a year (mandatories and voluntary defences),
        // dated across the calendar. The belt is where the elites meet.
        if (ChampOf(wc) is Boxer stale && !stale.IsChampion) _titles.SetChamp(wc, null);

        // A unification (rare) is settled FIRST, early in the year, so the belts merge before the defence
        // campaign runs. The rest of the season is then defended as one undisputed title — never a stray
        // WBC "defence" back-dated after the belts have already come together (which read as a bug).
        if (!UnifiedIn(wc) && ChampOf(wc) is Boxer wba && WbcOf(wc) is Boxer wbc && wba.Id != wbc.Id
            && wba.Id != Player.Id && wbc.Id != Player.Id
            && _rng.NextDouble() < UnificationChance(wc, 0.15, 0.80))
        { Date = SpreadDate(yr, 0, 6); Unify(wc); }

        if (UnifiedIn(wc))
        {
            UnifiedDefenceSeason(wc, yr);
        }
        else
        {
            DefendBeltSeason(wc, () => ChampOf(wc), (b, on) => CrownChampion(b, on), () => WbcOf(wc), PrimaryBelt, yr, dethrone: true);
            if (WbcActive) DefendBeltSeason(wc, () => WbcOf(wc), (b, on) => CrownWbc(b, on), () => ChampOf(wc), "WBC", yr, dethrone: false);
        }
        if (IbfActive) DefendBeltSeason(wc, () => IbfOf(wc), (b, on) => CrownIbf(b, on), null, "IBF", yr, dethrone: false);

        // Two undercards. Matchmaking is by ability with the better man favoured: each fighter generally
        // meets someone a notch below him (a showcase). Champions sit these out — they only defend.
        var top20 = Top20Ids(wc); var top8 = Top8Ids(wc);
        for (int pass = 0; pass < 6; pass++)   // several cards a year so a simulated career builds a real record, not a handful of bouts
        {
            // A prospect stays busy on the club circuit; an established (world-ranked) fighter takes fewer, bigger
            // bouts — long camps, ~3–4 a year — so he only appears on some cards.
            var pool = ActiveIn(wc).Where(b => b.Id != Player.Id && !HoldsAnyWorldBelt(b) && !AtYearCap(b) && _medical.Available(b)
                                          && (!WorldRanked(b) || _rng.NextDouble() < FightChancePerCard(b)))
                             .OrderByDescending(b => b.Overall).ToList();
            var cardNight = SpreadDate(yr, pass, 6);
            var owed = new HashSet<int>();
            StageDueRematches(pool, owed, on: cardNight);

            int n = pool.Count;
            var used = new bool[n];
            for (int i = 0; i < n; i++) if (owed.Contains(pool[i].Id)) used[i] = true;
            for (int i = 0; i < n; i++)
            {
                if (used[i]) continue;
                // usually a competitive fight (a notch below); ~30% a wide-gap tune-up vs a journeyman.
                int hi = _rng.NextDouble() < 0.30 ? Math.Min(n - 1, i + 30 + _rng.Next(80))
                                                  : Math.Min(n - 1, i + 4 + _rng.Next(8));
                int j = -1;
                for (int k = i + 1; k <= hi; k++) if (!used[k] && !BadMatch(pool[i], pool[k], top20, top8) && (j < 0 || _rng.NextDouble() < 0.5)) j = k;
                if (j < 0) for (int k = i + 1; k < n; k++) if (!used[k] && !BadMatch(pool[i], pool[k], top20, top8)) { j = k; break; }
                if (j < 0) continue;   // this man has no valid opponent on this card — skip HIM, don't halt the whole card
                used[i] = used[j] = true;
                int rounds = i < 6 ? 10 : 8;
                var night = SpreadDate(yr, pass, 6);   // ApplyOutcome moves it if either man boxed too recently
                ApplyOutcome(FastBout(pool[i], pool[j], rounds), pool[i], pool[j], on: night);
            }
        }

        // Club circuit: guarantee young prospects stay genuinely busy. The pooled matchmaking above leaves many
        // unpaired, so top up any unranked young fighter who's been idle this year with bouts against lower
        // opposition — so a real prospect actually piles up a record instead of stalling on a handful of fights.
        foreach (var pr in ActiveIn(wc).Where(b => b.Id != Player.Id && !WorldRanked(b) && b.Age <= 26).OrderByDescending(b => b.Potential).ToList())
        {
            int guard = 0;
            while (FightsThisYear(pr) < 5 && !AtYearCap(pr) && guard++ < 6)
            {
                var foe = ActiveIn(wc).Where(b => b.Id != pr.Id && b.Id != Player.Id && ProFights(b) >= 4
                                             && b.Overall <= pr.Overall + 4 && _medical.Available(b) && !AtYearCap(b)
                                             && !RecentFoes(pr, 3).Contains(b.Name))
                                    .OrderBy(_ => _rng.Next()).FirstOrDefault();
                if (foe is null) break;
                var night = SpreadDate(yr);
                ApplyOutcome(FastBout(pr, foe, pr.History.Count < 6 ? 6 : 8), pr, foe, on: night);
            }
        }
        // The clock used to be put back to 1 January here, because laying out a season moved it. It does not
        // any more: a season dates its own bouts and leaves the world where it found it.
    }

    /// <summary>The two world champions meet; the winner unifies both belts. Uses the current date.</summary>
    private void Unify(WeightClass wc)
    {
        if (ChampOf(wc) is not Boxer wba || WbcOf(wc) is not Boxer wbc || wba.Id == wbc.Id) return;
        var res = FastBout(wba, wbc, 12);
        var on = ApplyOutcome(res, wba, wbc, "unification");
        if (!res.IsDraw)
        {
            var w = res.Winner!;
            LogTitle($"{w.Name} UNIFIES the {PrimaryBelt} and WBC titles!", wc, RefOf(res, on), on);
            CrownChampion(w, on); CrownWbc(w, on);
            ClaimLinealByUnification(w.WeightClass, on);
        }
    }

    /// <summary>A unified champion risks BOTH world belts in a single bout — the winner walks away with the lot.</summary>
    private void UnifiedDefence(Boxer champ)
    {
        var ch = PickChallenger(champ, null);
        if (ch is null) return;
        var res = FastBout(champ, ch, 12);
        var on = ApplyOutcome(res, champ, ch, "Undisputed title");
        if (!res.IsDraw && res.Winner!.Id == ch.Id)
        {
            LogTitle($"{ch.Name} DETHRONES {champ.Name} to take the unified {PrimaryBelt} and WBC titles!", champ.WeightClass, RefOf(res, on), on);
            CrownChampion(ch, on); CrownWbc(ch, on);
        }
        else { Defended(champ.WeightClass, "WBA", champ.Id); Defended(champ.WeightClass, "WBC", champ.Id); LogTitle($"{champ.Name} retains the unified {PrimaryBelt} and WBC titles against {ch.Name}.", champ.WeightClass, RefOf(res, on), on); ConsiderTitleStepUp(champ); }
    }

    /// <summary>Warmup: a unified champion runs a season of 2–3 combined defences, and may vacate a belt.</summary>
    private void UnifiedDefenceSeason(WeightClass wc, int yr)
    {
        int titleBouts = 2 + _rng.Next(2);
        for (int d = 0; d < titleBouts; d++)
        {
            var c = ChampOf(wc);
            if (c is null || c.Id == Player.Id || !UnifiedIn(wc) || !_medical.Available(c)) return;
            if (_rng.NextDouble() < 0.10) { RelinquishBelt(c); return; }   // ducks a mandatory, splitting the belts
            var ch = PickChallenger(c, null);
            if (ch is null) return;
            if (NextTitleDate(c, ch, yr, d, titleBouts) is not DateOnly nd) return;
            var res = FastBout(c, ch, 12);
            var on = ApplyOutcome(res, c, ch, "Undisputed title", on: nd);
            if (!res.IsDraw && res.Winner!.Id == ch.Id)
            {
                LogTitle($"{ch.Name} beats {c.Name} to take the unified {PrimaryBelt} and WBC titles.", wc, on: on);
                CrownChampion(ch, on); CrownWbc(ch, on);
            }
        }
    }

    /// <summary>A unified champion gives up the WBC belt (keeping the senior belt) rather than meet a mandatory;
    /// the vacant WBC is then filled by the leading contender.</summary>
    private void RelinquishBelt(Boxer champ)
    {
        var wc = champ.WeightClass;
        if (!WbcActive || WbcOf(wc) is null) return;
        _titles.SetWbc(wc, null);
        LogTitle($"{champ.Name} relinquishes the WBC title rather than face the mandatory, keeping the {PrimaryBelt} belt.", wc);
        UpdateBeltsFor(wc);   // the WBC is picked up by the next contender in line
    }

    /// <summary>Run one belt through a season of 2–3 defences, each dated across the year.</summary>
    private void DefendBeltSeason(WeightClass wc, Func<Boxer?> champ, Action<Boxer, DateOnly> crown, Func<Boxer?>? other, string belt, int yr, bool dethrone)
    {
        int titleBouts = 2 + _rng.Next(2);
        for (int d = 0; d < titleBouts; d++)
        {
            var c = champ();
            if (c is null || c.Id == Player.Id || !_medical.Available(c)) return;   // an injured champion doesn't defend while on the shelf
            var challenger = PickChallenger(c, other?.Invoke());
            if (challenger is null)
            {
                // No credible mandatory this slot — rather than sit idle for a year, the champion takes a stay-busy
                // (non-title) fight against the best available gatekeeper he hasn't just met.
                var busy = ActiveIn(c.WeightClass).Where(b => b.Id != c.Id && b.Id != Player.Id && b.Overall is >= 58
                                                          && b.Overall <= c.Overall && _medical.Available(b) && !RecentFoes(c, 3).Contains(b.Name))
                                                  .OrderByDescending(RankScore).FirstOrDefault();
                if (busy is null) return;
                if (NextTitleDate(c, busy, yr, d, titleBouts) is not DateOnly bd) return;
                ApplyOutcome(FastBout(c, busy, 10), c, busy, on: bd);
                continue;
            }
            if (NextTitleDate(c, challenger, yr, d, titleBouts) is not DateOnly td) return;
            var res = FastBout(c, challenger, 12);
            var on = ApplyOutcome(res, c, challenger, $"{belt} title", on: td);
            if (!res.IsDraw && res.Winner!.Id == challenger.Id)
            {
                LogTitle(dethrone ? $"{challenger.Name} dethrones {c.Name} for the {belt} title."
                                  : $"{challenger.Name} takes the {belt} title from {c.Name}.", wc, on: on);
                crown(challenger, on);   // the night, so the reign is dated by the fight that won it
            }
            else { Defended(c.WeightClass, belt, c.Id); ConsiderTitleStepUp(c); }
        }
    }

    /// <summary>A random calendar date within a year — spreads warmup bouts off 1 January.</summary>
    private DateOnly SpreadDate(int yr) => new(yr, 1 + _rng.Next(12), 1 + _rng.Next(28));

    /// <summary>A date somewhere later in the same year, never earlier than <paramref name="from"/>. A bout the
    /// world resolves mid-season must not be stamped with a day that has already gone by: doing so put a
    /// "relinquishes his title" line months BEFORE the fight that caused it, and left the news feed telling a
    /// story out of order.</summary>
    private DateOnly SpreadDateFrom(DateOnly from)
    {
        int last = new DateOnly(from.Year, 12, 28).DayNumber;
        return from.DayNumber >= last ? from : DateOnly.FromDayNumber(from.DayNumber + _rng.Next(last - from.DayNumber + 1));
    }

    /// <summary>Date bout <paramref name="index"/> of <paramref name="count"/> within its own slice of the year,
    /// so a fighter's bouts in a season land a couple of months apart instead of clustering days apart.</summary>
    private DateOnly SpreadDate(int yr, int index, int count)
    {
        int slice = 365 / Math.Max(1, count);
        int day = Math.Clamp(index * slice + _rng.Next(Math.Max(1, slice - 30)), 0, 364);
        return new DateOnly(yr, 1, 1).AddDays(day);
    }

    /// <summary>A within-year date for a champion's next title bout that always sits at least ~10 weeks AFTER his
    /// previous bout — so a season's title bouts (and a unification plus the defences that follow it) never land
    /// days apart or out of order. Null when there's no room left in the year: the bout is then skipped, not crammed.</summary>
    /// <summary>When a title bout can be made, respecting BOTH men's rest.
    ///
    /// The gap used to be measured from the champion's last night only, which is fine for him and useless for
    /// the other man. Each belt runs its own season, so a contender could take the WBA in September and be
    /// pulled into the WBC season twelve days later — a date computed from that champion's history, with
    /// nothing anywhere asking when the challenger had last fought. It reads exactly as a bug in a record:
    /// a man wins a world title and is back in the ring a fortnight afterwards.</summary>
    private DateOnly? NextTitleDate(Boxer c, Boxer? opponent, int yr, int index, int count)
    {
        var d = SpreadDate(yr, index, count);
        var last = DateOnly.MinValue;
        foreach (var man in new[] { c, opponent })
            if (man is not null && man.History.Count > 0)
            {
                var his = man.History.Max(h => h.Date);
                if (his > last) last = his;
            }
        if (last > DateOnly.MinValue && d.DayNumber < last.AddDays(72).DayNumber)
            d = last.AddDays(72 + _rng.Next(24));
        return d.Year == yr ? d : null;
    }

    private static int ProFights(Boxer b) => b.Record.Wins + b.Record.Losses + b.Record.Draws;

    /// <summary>How many times the player has already fought a given opponent (by name).</summary>
    private int TimesFaced(string name) => Player.History.Count(h => h.Opponent == name);

    /// <summary>How many bouts a fighter has had in the current calendar year — for the 8-a-year cap.</summary>
    private int FightsThisYear(Boxer b) => b.History.Count(h => h.Date.Year == Date.Year);
    private bool AtYearCap(Boxer b) => FightsThisYear(b) >= MaxFightsPerYear;

    /// <summary>Per-card chance a world-ranked fighter takes the bout — tuned so an established man fights ~3–4
    /// times a year (3–4 month gaps) across the season's six cards, easing off further as he ages.</summary>
    /// <summary>How likely a fighter is to appear on any given card. Champions and former champions box less
    /// than contenders do: a titleholder fights on a title schedule with long camps and a mandatory calendar,
    /// and a man who has held a belt is not taking stay-busy fights for short money. It is the contenders,
    /// chasing a shot, who are out every couple of months.</summary>
    private double FightChancePerCard(Boxer b)
    {
        double basis = CareerStages.Of(b) switch
        {
            CareerStage.Prime => 4.0 / 6,
            CareerStage.PostPrime => 3.0 / 6,
            CareerStage.End => 2.5 / 6,
            _ => 4.5 / 6,   // world-ranked but still pre-prime — fairly active
        };
        // A reigning champion never reaches this pool at all; he is barred from undercards and his year is
        // his defences. A former champion does, and he is not taking stay-busy fights for short money.
        if (_hall.WasEverChampion(b.Id)) basis *= 0.72;
        // And no two men keep the same schedule.
        return basis * CareerMileage.Activity(b);
    }

    /// <summary>One of the closest few by rating, rather than always the single closest. Taking the top match
    /// every time made these passes deterministic: hold a fight out and the same name came straight back,
    /// because "nearest to 62" has one answer. Men recently turned down are stepped over where there is anyone
    /// else to take instead.</summary>
    private Boxer NearOne(List<Boxer> pool, int target)
    {
        var wanted = pool.Where(b => !_declined.Contains(b.Id)).ToList();
        if (wanted.Count == 0) wanted = pool;
        var near = wanted.OrderBy(b => Math.Abs(b.Overall - target)).Take(5).ToList();
        return near[_rng.Next(near.Count)];
    }

    /// <summary>Days since a fighter's most recent bout (large if he has no ledger) — stops a champion
    /// from defending too frequently.</summary>
    /// <summary>Days since a fighter's most recent bout.
    ///
    /// The most recent BY DATE, which is not the same as the last one appended. A ledger is written in the
    /// order the sim resolves fights and each bout carries its own night, so a season laid out across a year
    /// leaves entries that are not in date order. Reading the last element gave a stale answer whenever that
    /// happened, and the rest rules built on it quietly let a man box again too soon.</summary>
    private int DaysSinceLastBout(Boxer b)
    {
        if (b.History.Count == 0) return 999;
        int latest = int.MinValue;
        foreach (var h in b.History) if (h.Date.DayNumber > latest) latest = h.Date.DayNumber;
        return Date.DayNumber - latest;
    }

    /// <summary>Has he had long enough off to box again?
    ///
    /// Four weeks between fights, which is what the sport's own scheduler has always enforced further down:
    /// <see cref="LegalNightFor"/> refuses to date a bout within 28 days of either man's last one. The
    /// difference is WHERE the rule is applied. It used to be applied after the match was made, by shoving
    /// the fight forward until it found a legal night — which, for a card happening tonight, meant announcing
    /// a fight on a date that had not arrived yet. A man who boxed a fortnight ago should not be matched
    /// tonight at all; somebody else takes the slot, and the card is the same size.
    ///
    /// LegalNightFor stays as the backstop it always claimed to be, for the paths that stage a bout without
    /// coming through a card pool.</summary>
    private bool Rested(Boxer b) => DaysSinceLastBout(b) >= 28;

    /// <summary>A man matched with the player is OFF THE MARKET until that night.
    ///
    /// He was not. The fight is agreed weeks or months ahead and the world went on booking him in the
    /// meantime, so the opponent on the poster could be beaten, cut, knocked out, suspended — and, now that a
    /// career can end in the ring, retired outright — between the handshake and the first bell. The player
    /// would walk out to face a man whose record no longer matched the one he had studied, or find the bout
    /// quietly gone. A fighter with a date in the diary does not take another fight before it.
    ///
    /// Lifts by itself: the moment the night passes, or the offer is turned down and replaced, he is back in
    /// the pool with no flag to clear.</summary>
    private bool BookedWithThePlayer(Boxer b) =>
        !Player.Retired && Offer is { } o && o.Opponent.Id == b.Id && Date < OfferDate;


    /// <summary>True if pairing these two would be a mismatch a prospect shouldn't be in: a raw fighter
    /// against an elite, or anyone with under 20 pro bouts sharing the ring with a top-20 man.</summary>
    private bool BadMatch(Boxer x, Boxer y, HashSet<int> top20, HashSet<int> top8)
    {
        // A return the sport is asking for beats every rule below it - including the two that would otherwise
        // forbid this exact pairing, since a rematch is by definition a man he has just fought, and the fights
        // worth seeing again are usually between the men who are kept apart.
        if (RematchDue(x, y)) return false;
        // The elite don't waste each other in non-title bouts: two of the division's top 8 only meet with a belt
        // on the line (a title fight or eliminator), never on an ordinary card.
        if (top8.Contains(x.Id) && top8.Contains(y.Id)) return true;
        // No stale rematches: don't pair two men who've met in either's last few bouts.
        if (RecentFoes(x, 4).Contains(y.Name) || RecentFoes(y, 4).Contains(x.Name)) return true;
        // Contenders build a record before facing each other: two genuine contender-calibre fighters (a high
        // ceiling — the real-roster talents) avoid each other until BOTH have served their apprenticeship. Until
        // then they campaign against journeymen and gatekeepers rather than trading losses among themselves.
        // A wonder kid's apprenticeship is short, so a phenom can break in early.
        if (x.Potential >= 72 && y.Potential >= 72 && (!ReadyForContenders(x) || !ReadyForContenders(y))) return true;
        var strong = x.Overall >= y.Overall ? x : y;
        var weak = ReferenceEquals(strong, x) ? y : x;
        if (strong.Overall >= 78 && ProFights(weak) < 12) return true;
        // A ranked contender won't face a fighter who hasn't served his apprenticeship yet (shorter for a phenom).
        if ((top20.Contains(x.Id) && !ReadyForContenders(y)) || (top20.Contains(y.Id) && !ReadyForContenders(x))) return true;
        // Prospects are protected: two unbeaten-ish young hopefuls generally avoid each other, building
        // records against journeymen and gatekeepers rather than risking a blemish early.
        if (IsProspect(x) && IsProspect(y) && _rng.NextDouble() < 0.9) return true;
        return false;
    }

    /// <summary>A prospect: a young fighter still building a record, unranked, with real headroom left.</summary>
    /// <summary>A live unbeaten prospect — young, highly rated, and still climbing. Two of these meeting is a
    /// 50-50 that makes somebody's career, not a night's work for either of them, so one is never offered as a
    /// record-builder or a stay-busy. He is met when it means something: a step-up or an eliminator, which come
    /// through the ranked path above and are unaffected by this.
    ///
    /// The matchmaker already swapped green fighters out for seasoned ones, but the experience band added later
    /// ran AFTER that swap and quietly undid it — a nineteen-fight prospect was being handed an unbeaten 10-0
    /// class-7 man and told he was building a record.</summary>
    private bool DangerousProspect(Boxer b) =>
        IsProspect(b) && b.Record.Losses == 0 && b.Potential >= 70;

    private bool IsProspect(Boxer b) =>
        !WorldRanked(b) && ProFights(b) < 16 && b.Age <= 27 && (b.Potential - b.Overall) >= 3;

    /// <summary>How much a young fighter over-performs his current rating — most of the ceiling he's
    /// still to realise. A can't-miss prospect (huge gap to a high ceiling) already handles lesser men
    /// with ease, so a journeyman almost never upsets a future great. Zero once he's at/past his peak.</summary>
    /// <summary>Turn an award's reference back into something watchable: the two men, and the bout line as it
    /// stands in the winner's record. Returns null if either man has left the world or the night is no longer
    /// on his record.</summary>
    public (Boxer Owner, Boxer Foe, BoutLine Line)? FindBout(BoutRef r)
    {
        var owner = FindByName(r.Winner);
        var foe = FindByName(r.Loser);
        if (owner is null || foe is null) return null;
        var line = owner.History.FirstOrDefault(h => h.Date == r.Date && h.Opponent == r.Loser);
        return line is null ? null : (owner, foe, line);
    }

    /// <summary>Find a fighter anywhere in the world by name — active, retired or enshrined. Bout lines record
    /// only an opponent's name, so replaying an old fight has to look the other man back up.</summary>
    public Boxer? FindByName(string name)
    {
        if (Player.Name == name) return Player;
        return _roster.FirstOrDefault(b => b.Name == name);
    }

    private static double YouthEdge(Boxer b) => b.Age <= b.PeakAge ? Math.Min(16, Math.Max(0, b.Potential - b.Overall) * 0.4) : 0;

}
