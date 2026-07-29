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
    /// <summary>A fortnight's fight cards across every division.</summary>
    private void RunEvent()
    {
        foreach (var wc in AllDivisions) if (DivisionActive(wc)) { _cursor = wc; RunEventCard(); }
        _cursor = Division;
    }

    /// <summary>One division's fortnightly card: an occasional title defence plus showcase undercards.</summary>
    private void RunEventCard()
    {
        // Champions don't fight on undercards — when they fight, it's a title defence (handled below).
        // A man who's already boxed 8 times this year sits the rest of it out.
        var pool = ActiveHere.Where(b => b.Id != Player.Id && !HoldsAnyWorldBelt(b) && !AtYearCap(b) && Available(b))
                         .OrderByDescending(b => b.Overall).ToList();
        if (pool.Count < 2) return;

        if (Champ is not null && !Champ.IsChampion) Champ = null;

        // A rare unification is checked FIRST and, when it fires, is the only world-title bout on this card:
        // the belts merge in one fight rather than each champion ALSO making a separate defence the same
        // fortnight (which produced impossible back-to-back title bouts days apart). Both men must be rested.
        if (!CursorUnified && Champ is not null && Wbc is not null && Champ.Id != Wbc.Id
            && Champ.Id != Player.Id && Wbc.Id != Player.Id
            && DaysSinceLastBout(Champ) >= (int)(112 / CareerMileage.Activity(Champ)) && DaysSinceLastBout(Wbc) >= (int)(112 / CareerMileage.Activity(Wbc))
            && _rng.NextDouble() < UnificationChance(_cursor, 0.006, 0.04))
        {
            Unify();
        }
        else if (CursorUnified)
        {
            var c = Champ!;
            if (c.Id != Player.Id && DaysSinceLastBout(c) >= (int)(112 / CareerMileage.Activity(c)) && _rng.NextDouble() < 0.055 * CareerMileage.Activity(c))   // ~2 defences a year, min 14 weeks apart
            {
                if (_rng.NextDouble() < 0.10) RelinquishBelt(c);   // ~1 in 10: ducks a mandatory and gives up a belt
                else UnifiedDefence(c);
            }
        }
        else
        {
            if (Champ is not null && Champ.Id != Player.Id && DaysSinceLastBout(Champ) >= (int)(112 / CareerMileage.Activity(Champ)) && _rng.NextDouble() < 0.055 * CareerMileage.Activity(Champ))   // ~2 defences a year, min 14 weeks apart
            {
                var ch = PickChallenger(Champ, Wbc);
                if (ch is not null)
                {
                    var res = FastBout(Champ, ch, 12);
                    ApplyOutcome(res, Champ, ch, $"{PrimaryBelt} title");
                    if (!res.IsDraw && res.Winner!.Id == ch.Id) { LogTitle($"{ch.Name} DETHRONES {Champ.Name} for the {PrimaryBelt} title!", RefOf(res)); CrownChampion(ch); }
                    else { Defended(_cursor, "WBA", Champ.Id); LogTitle($"{Champ.Name} retains the {PrimaryBelt} title against {ch.Name}.", RefOf(res)); ConsiderTitleStepUp(Champ); }
                }
            }
            if (Wbc is not null && Wbc.Id != Player.Id && DaysSinceLastBout(Wbc) >= (int)(112 / CareerMileage.Activity(Wbc)) && _rng.NextDouble() < 0.055 * CareerMileage.Activity(Wbc))   // ~2 defences a year, min 14 weeks apart
            {
                var ch = PickChallenger(Wbc, Champ);
                if (ch is not null)
                {
                    var res = FastBout(Wbc, ch, 12);
                    ApplyOutcome(res, Wbc, ch, "WBC title");
                    if (!res.IsDraw && res.Winner!.Id == ch.Id) { LogTitle($"{ch.Name} TAKES the WBC title from {Wbc.Name}!", RefOf(res)); CrownWbc(ch); }
                    else { Defended(_cursor, "WBC", Wbc.Id); LogTitle($"{Wbc.Name} retains the WBC title against {ch.Name}.", RefOf(res)); ConsiderTitleStepUp(Wbc); }
                }
            }
        }

        // IBF title defence — the third belt, contested independently from 1983.
        if (IbfActive && Ibf is not null && Ibf.Id != Player.Id && DaysSinceLastBout(Ibf) >= (int)(112 / CareerMileage.Activity(Ibf)) && _rng.NextDouble() < 0.055 * CareerMileage.Activity(Ibf))
        {
            var ch = PickChallenger(Ibf, null);
            if (ch is not null)
            {
                var res = FastBout(Ibf, ch, 12);
                ApplyOutcome(res, Ibf, ch, "IBF title");
                if (!res.IsDraw && res.Winner!.Id == ch.Id) { LogTitle($"{ch.Name} TAKES the IBF title from {Ibf.Name}!", RefOf(res)); CrownIbf(ch); }
                else { Defended(_cursor, "IBF", Ibf.Id); LogTitle($"{Ibf.Name} retains the IBF title against {ch.Name}.", RefOf(res)); ConsiderTitleStepUp(Ibf); }
            }
        }

        // Regional title defences — a regional champ risks his belt against a fellow regional contender.
        foreach (var region in RegionalBelts)
        {
            if (!_regional.TryGetValue((_cursor, region), out var rc) || rc.Id == Player.Id || rc.Retired) continue;
            // Regional belts are meant to be DEFENDED - that is the whole point of holding one on the way up.
            // At a twentieth per card they mostly sat idle on a man's record.
            if (DaysSinceLastBout(rc) < 84 || _rng.NextDouble() >= 0.11 * CareerMileage.Activity(rc)) continue;
            var candidates = pool.Where(b => RegionOf(b) == region && b.Id != rc.Id && CredibleForRegional(b))
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
            ApplyOutcome(rres, rc, chall, $"{region} title");
            if (!rres.IsDraw && rres.Winner!.Id == chall.Id) { _regional[(_cursor, region)] = chall; LogTitle($"{chall.Name} wins the {region} title from {rc.Name}.", RefOf(rres)); }
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
        var top20 = Top20Ids(_cursor); var top8 = Top8Ids(_cursor);

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
            used.Add(i); used.Add(j);
            var res = FastBout(pool[i], pool[j], 10);
            ApplyOutcome(res, pool[i], pool[j]);
            ReportBout(res);
        }
    }

    private void RunNpcSeason()
    {
        foreach (var wc in AllDivisions) if (DivisionActive(wc)) { _cursor = wc; RunNpcSeasonFor(); }
        _cursor = Division;
    }

    private void RunNpcSeasonFor()
    {
        var fighters = ActiveHere.Where(b => b.Id != Player.Id).ToList();
        if (fighters.Count < 2) return;
        int yr = Date.Year;

        // Title bouts: each champion defends 2–3 times a year (mandatories and voluntary defences),
        // dated across the calendar. The belt is where the elites meet.
        if (Champ is not null && !Champ.IsChampion) Champ = null;

        // A unification (rare) is settled FIRST, early in the year, so the belts merge before the defence
        // campaign runs. The rest of the season is then defended as one undisputed title — never a stray
        // WBC "defence" back-dated after the belts have already come together (which read as a bug).
        if (!CursorUnified && Champ is not null && Wbc is not null && Champ.Id != Wbc.Id
            && Champ.Id != Player.Id && Wbc.Id != Player.Id
            && _rng.NextDouble() < UnificationChance(_cursor, 0.15, 0.80))
        { Date = SpreadDate(yr, 0, 6); Unify(); }

        if (CursorUnified)
        {
            UnifiedDefenceSeason(yr);
        }
        else
        {
            DefendBeltSeason(() => Champ, CrownChampion, () => Wbc, PrimaryBelt, yr, dethrone: true);
            if (WbcActive) DefendBeltSeason(() => Wbc, CrownWbc, () => Champ, "WBC", yr, dethrone: false);
        }
        if (IbfActive) DefendBeltSeason(() => Ibf, CrownIbf, null, "IBF", yr, dethrone: false);

        // Two undercards. Matchmaking is by ability with the better man favoured: each fighter generally
        // meets someone a notch below him (a showcase). Champions sit these out — they only defend.
        var top20 = Top20Ids(_cursor); var top8 = Top8Ids(_cursor);
        for (int pass = 0; pass < 6; pass++)   // several cards a year so a simulated career builds a real record, not a handful of bouts
        {
            // A prospect stays busy on the club circuit; an established (world-ranked) fighter takes fewer, bigger
            // bouts — long camps, ~3–4 a year — so he only appears on some cards.
            var pool = ActiveHere.Where(b => b.Id != Player.Id && !HoldsAnyWorldBelt(b) && !AtYearCap(b) && Available(b)
                                          && (!WorldRanked(b) || _rng.NextDouble() < FightChancePerCard(b)))
                             .OrderByDescending(b => b.Overall).ToList();
            Date = SpreadDate(yr, pass, 6);
            var owed = new HashSet<int>();
            StageDueRematches(pool, owed);

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
                Date = SpreadDate(yr, pass, 6);
                ApplyOutcome(FastBout(pool[i], pool[j], rounds), pool[i], pool[j]);
            }
        }

        // Club circuit: guarantee young prospects stay genuinely busy. The pooled matchmaking above leaves many
        // unpaired, so top up any unranked young fighter who's been idle this year with bouts against lower
        // opposition — so a real prospect actually piles up a record instead of stalling on a handful of fights.
        foreach (var pr in ActiveHere.Where(b => b.Id != Player.Id && !WorldRanked(b) && b.Age <= 26).OrderByDescending(b => b.Potential).ToList())
        {
            int guard = 0;
            while (FightsThisYear(pr) < 5 && !AtYearCap(pr) && guard++ < 6)
            {
                var foe = ActiveHere.Where(b => b.Id != pr.Id && b.Id != Player.Id && ProFights(b) >= 4
                                             && b.Overall <= pr.Overall + 4 && Available(b) && !AtYearCap(b)
                                             && !RecentFoes(pr, 3).Contains(b.Name))
                                    .OrderBy(_ => _rng.Next()).FirstOrDefault();
                if (foe is null) break;
                Date = SpreadDate(yr);
                ApplyOutcome(FastBout(pr, foe, pr.History.Count < 6 ? 6 : 8), pr, foe);
            }
        }
        Date = new DateOnly(yr, 1, 1);   // leave the clock where the warmup loop expects it
    }

    /// <summary>The two world champions meet; the winner unifies both belts. Uses the current date.</summary>
    private void Unify()
    {
        if (Champ is null || Wbc is null || Champ.Id == Wbc.Id) return;
        var res = FastBout(Champ, Wbc, 12);
        ApplyOutcome(res, Champ, Wbc, "unification");
        if (!res.IsDraw)
        {
            var w = res.Winner!;
            LogTitle($"{w.Name} UNIFIES the {PrimaryBelt} and WBC titles!", RefOf(res));
            CrownChampion(w); CrownWbc(w);
            ClaimLinealByUnification(w.WeightClass);
        }
    }

    /// <summary>A unified champion risks BOTH world belts in a single bout — the winner walks away with the lot.</summary>
    private void UnifiedDefence(Boxer champ)
    {
        var ch = PickChallenger(champ, null);
        if (ch is null) return;
        var res = FastBout(champ, ch, 12);
        ApplyOutcome(res, champ, ch, "Undisputed title");
        if (!res.IsDraw && res.Winner!.Id == ch.Id)
        {
            LogTitle($"{ch.Name} DETHRONES {champ.Name} to take the unified {PrimaryBelt} and WBC titles!", RefOf(res));
            CrownChampion(ch); CrownWbc(ch);
        }
        else { Defended(champ.WeightClass, "WBA", champ.Id); Defended(champ.WeightClass, "WBC", champ.Id); LogTitle($"{champ.Name} retains the unified {PrimaryBelt} and WBC titles against {ch.Name}.", RefOf(res)); ConsiderTitleStepUp(champ); }
    }

    /// <summary>Warmup: a unified champion runs a season of 2–3 combined defences, and may vacate a belt.</summary>
    private void UnifiedDefenceSeason(int yr)
    {
        int titleBouts = 2 + _rng.Next(2);
        for (int d = 0; d < titleBouts; d++)
        {
            var c = Champ;
            if (c is null || c.Id == Player.Id || !CursorUnified || !Available(c)) return;
            if (_rng.NextDouble() < 0.10) { RelinquishBelt(c); return; }   // ducks a mandatory, splitting the belts
            var ch = PickChallenger(c, null);
            if (ch is null) return;
            if (NextTitleDate(c, yr, d, titleBouts) is not DateOnly nd) return;
            Date = nd;
            var res = FastBout(c, ch, 12);
            ApplyOutcome(res, c, ch, "Undisputed title");
            if (!res.IsDraw && res.Winner!.Id == ch.Id)
            {
                LogTitle($"{ch.Name} beats {c.Name} to take the unified {PrimaryBelt} and WBC titles.");
                CrownChampion(ch); CrownWbc(ch);
            }
        }
    }

    /// <summary>A unified champion gives up the WBC belt (keeping the senior belt) rather than meet a mandatory;
    /// the vacant WBC is then filled by the leading contender.</summary>
    private void RelinquishBelt(Boxer champ)
    {
        if (!WbcActive || Wbc is null) return;
        Wbc = null;
        LogTitle($"{champ.Name} relinquishes the WBC title rather than face the mandatory, keeping the {PrimaryBelt} belt.");
        UpdateBeltsFor(_cursor);   // the WBC is picked up by the next contender in line
    }

    /// <summary>Run one belt through a season of 2–3 defences, each dated across the year.</summary>
    private void DefendBeltSeason(Func<Boxer?> champ, Action<Boxer> crown, Func<Boxer?>? other, string belt, int yr, bool dethrone)
    {
        int titleBouts = 2 + _rng.Next(2);
        for (int d = 0; d < titleBouts; d++)
        {
            var c = champ();
            if (c is null || c.Id == Player.Id || !Available(c)) return;   // an injured champion doesn't defend while on the shelf
            var challenger = PickChallenger(c, other?.Invoke());
            if (challenger is null)
            {
                // No credible mandatory this slot — rather than sit idle for a year, the champion takes a stay-busy
                // (non-title) fight against the best available gatekeeper he hasn't just met.
                var busy = ActiveIn(c.WeightClass).Where(b => b.Id != c.Id && b.Id != Player.Id && b.Overall is >= 58
                                                          && b.Overall <= c.Overall && Available(b) && !RecentFoes(c, 3).Contains(b.Name))
                                                  .OrderByDescending(RankScore).FirstOrDefault();
                if (busy is null) return;
                if (NextTitleDate(c, yr, d, titleBouts) is not DateOnly bd) return;
                Date = bd;
                ApplyOutcome(FastBout(c, busy, 10), c, busy);
                continue;
            }
            if (NextTitleDate(c, yr, d, titleBouts) is not DateOnly td) return;
            Date = td;
            var res = FastBout(c, challenger, 12);
            ApplyOutcome(res, c, challenger, $"{belt} title");
            if (!res.IsDraw && res.Winner!.Id == challenger.Id)
            {
                LogTitle(dethrone ? $"{challenger.Name} dethrones {c.Name} for the {belt} title."
                                  : $"{challenger.Name} takes the {belt} title from {c.Name}.");
                crown(challenger);
            }
            else { Defended(c.WeightClass, BeltSlot(belt), c.Id); ConsiderTitleStepUp(c); }
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
    private DateOnly? NextTitleDate(Boxer c, int yr, int index, int count)
    {
        var d = SpreadDate(yr, index, count);
        if (c.History.Count > 0)
        {
            var last = c.History.Max(h => h.Date);
            if (d.DayNumber < last.AddDays(72).DayNumber) d = last.AddDays(72 + _rng.Next(24));
        }
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
        if (_everChampion.Contains(b.Id)) basis *= 0.72;
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
    private int DaysSinceLastBout(Boxer b) => b.History.Count == 0 ? 999 : Date.DayNumber - b.History[^1].Date.DayNumber;

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
