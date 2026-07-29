using BoxingSim.Core.Engine;
using BoxingSim.Core.Generation;
using BoxingSim.Core.League;
using BoxingSim.Core.Model;

namespace BoxingSim.Core.Career;

/// <summary>Moving up: whether a fighter may, whether he will, and what it costs him when he does.</summary>
/// <summary>The fights people want: returns for the ones that were never settled, superfights between the
/// best men in the world, and eliminators that mean something.</summary>
public sealed partial class CareerGame
{
    // ================================ THE FIGHTS PEOPLE WANT ================================
    //
    // Everything above this point is a matchmaker trying to keep fights sensible: the elite are kept apart
    // outside title fights, and nobody meets the same man twice in a row. Left alone that produces a tidy
    // sport with no events in it - a split decision is never settled, a champion caught cold never gets his
    // night back, and the two best men in the world spend their careers in different halves of the draw.
    //
    // These two rules push the other way. A fight that ended badly or wrongly creates a DEMAND, which the
    // matchmaker then honours ahead of its usual caution; and the best fighters are deliberately steered
    // into each other rather than merely permitted to meet.

    /// <summary>A fight the public wants back, and the window it is wanted in. Held by the world rather than
    /// by either man, because it is not a plan - it is what the sport is asking for.</summary>
    private sealed record Rematch(int A, int B, DateOnly Wanted, DateOnly Expires, string Why, bool WasTitle);

    private readonly Dictionary<(int, int), Rematch> _rematch = new();

    /// <summary>The man who won the eliminator, and the date his claim runs out. A champion honours it before
    /// he picks his own challenger - otherwise "he is next for the title" is a line in the news that the
    /// matchmaker never reads.</summary>
    private readonly Dictionary<WeightClass, (int Id, DateOnly Until)> _mandatory = new();

    private static (int, int) PairKey(Boxer x, Boxer y) => x.Id < y.Id ? (x.Id, y.Id) : (y.Id, x.Id);

    /// <summary>Did this fight leave a question? Draws and split decisions leave the obvious one; so does a
    /// champion beaten by a man nobody rated, and a fight cut short by a cut or a foul before it had run.
    ///
    /// The gate is deliberately narrow. A rematch is only wanted when at least one of them is somebody - a
    /// world-ranked man, or a night with a belt on it. Two club fighters drawing is not an event, and letting
    /// it register would fill the calendar with returns nobody asked for.</summary>
    private void NoteRematchDemand(FightResult res, Boxer a, Boxer b, string? note)
    {
        if (a.Id == b.Id) return;

        // What happened, before who it happened to - the verdict is free to read and rules out nineteen fights
        // in twenty, so the expensive question of whether these two are anybody is asked last.
        bool title = note is not null;
        string? why = null;
        double want = 0;

        // A night with a belt on it is the one people actually want back, so an unsettled title fight is
        // almost always returned and an unsettled club fight usually is not.
        if (res.IsDraw) { why = "a draw"; want = title ? 0.95 : 0.70; }
        else if (res.Method is "SD") { why = "a split decision"; want = title ? 0.85 : 0.45; }
        else if (res.Method is "MD") { why = "a majority decision"; want = title ? 0.55 : 0.22; }
        else if (res.Method is "DQ") { why = "a disqualification"; want = title ? 0.85 : 0.55; }
        // A stoppage with nobody knocked down and rounds still to run is a cut or a stopped-too-soon: the
        // fight was taken away rather than won.
        else if (res.Outcome is FightOutcome.TechnicalKnockout
                 && res.KnockdownsA + res.KnockdownsB == 0
                 && res.EndRound <= res.ScheduledRounds - 3) { why = "an unsatisfactory ending"; want = title ? 0.70 : 0.35; }
        // An upset: the beaten man was the ranked one, and by a distance. This is the one the public wants
        // most - it is the fight where somebody has to prove it was not a fluke.
        else if (res.Winner is Boxer w && res.Loser is Boxer l
                 && (IsWorldChampion(l) || l.Overall - w.Overall >= 6)) { why = "an upset"; want = title ? 0.85 : 0.60; }

        if (why is null || _rng.NextDouble() >= want) return;

        // Three fights is a trilogy and a trilogy is enough; a fourth is a road show.
        if (a.History.Count(h => h.Opponent == b.Name) >= 3) return;

        // Now who they are. A belt or a superfight is its own justification; otherwise BOTH men have to be
        // top-fifteen in the division. This is the line the first cut of this got wrong: it asked whether
        // either man was "world-ranked", which in this sim only means twenty fights - so every seasoned club
        // fighter qualified, and the sport produced 367 rematches a year against 73 title fights.
        if (!title)
        {
            var top = Top20Ids(a.WeightClass);
            if (!(top.Contains(a.Id) && top.Contains(b.Id))) return;
        }

        // A title return comes back quickly - a contractual rematch clause, in effect. An ordinary one waits
        // for both men to have a fight in between.
        int soonest = title ? 100 : 150;
        var wanted = Date.AddDays(soonest + _rng.Next(title ? 130 : 190));
        _rematch[PairKey(a, b)] = new Rematch(a.Id, b.Id, wanted, wanted.AddDays(420 + _rng.Next(200)), why, title);
    }

    /// <summary>Is a return between these two due now? Used by the matchmaker to override its own caution -
    /// the recent-opponent rule and the elite-stay-apart rule both have to give way to a fight the sport is
    /// actually asking for.</summary>
    private bool RematchDue(Boxer x, Boxer y) =>
        _rematch.TryGetValue(PairKey(x, y), out var r) && Date >= r.Wanted && Date <= r.Expires;

    private bool RematchPending(Boxer x, Boxer y) => _rematch.ContainsKey(PairKey(x, y));

    /// <summary>The man a fighter owes a return to, if one is due and he can be got in the ring.</summary>
    private Boxer? RematchFoeFor(Boxer f)
    {
        foreach (var (key, r) in _rematch)
        {
            if (r.A != f.Id && r.B != f.Id) continue;
            if (Date < r.Wanted || Date > r.Expires) continue;
            var other = _roster.FirstOrDefault(b => b.Id == (r.A == f.Id ? r.B : r.A));
            if (other is null || other.Retired || !Available(other) || AtYearCap(other)) continue;
            if (other.WeightClass != f.WeightClass) continue;   // one of them has moved; it is a different fight now
            return other;
        }
        return null;
    }

    /// <summary>Why the return is being made - for the record, the news and the offer text.</summary>
    private string RematchWhy(Boxer x, Boxer y) =>
        _rematch.TryGetValue(PairKey(x, y), out var r) ? r.Why : "the first fight";

    private void ClearRematch(Boxer x, Boxer y) => _rematch.Remove(PairKey(x, y));

    /// <summary>Forget the returns that will never happen: expired, or one of the men gone. Run with the
    /// year, so the dictionary cannot grow for the life of a world.</summary>
    private void PruneRematches()
    {
        var dead = _rematch.Where(kv => Date > kv.Value.Expires
                                     || _roster.FirstOrDefault(b => b.Id == kv.Value.A)?.Retired != false
                                     || _roster.FirstOrDefault(b => b.Id == kv.Value.B)?.Retired != false)
                           .Select(kv => kv.Key).ToList();
        foreach (var k in dead) _rematch.Remove(k);

        var lapsed = _mandatory.Where(kv => Date > kv.Value.Until
                                         || _roster.FirstOrDefault(b => b.Id == kv.Value.Id)?.Retired != false)
                               .Select(kv => kv.Key).ToList();
        foreach (var k in lapsed) _mandatory.Remove(k);
    }

    /// <summary>Stage the returns that have come due in this division, ahead of the ordinary card. A rematch
    /// is not squeezed in around the undercard - it IS the card, so it is made first and the men are then
    /// unavailable for anything else that night.</summary>
    private void StageDueRematches(List<Boxer> pool, HashSet<int>? used = null)
    {
        // Materialised first: the fights below write to _rematch through ApplyOutcome.
        var due = _rematch.Values
            .Where(r => Date >= r.Wanted && Date <= r.Expires)
            .OrderBy(r => r.Wanted)
            .ToList();

        foreach (var r in due)
        {
            var x = pool.FirstOrDefault(b => b.Id == r.A);
            var y = pool.FirstOrDefault(b => b.Id == r.B);
            if (x is null || y is null) continue;
            if (used is not null && (used.Contains(x.Id) || used.Contains(y.Id))) continue;
            if (x.Retired || y.Retired || !Available(x) || !Available(y) || AtYearCap(x) || AtYearCap(y)) continue;
            if (x.Id == Player.Id || y.Id == Player.Id) continue;   // the player is offered his own, never given it

            ClearRematch(x, y);
            used?.Add(x.Id); used?.Add(y.Id);
            // A return for a belt goes the championship distance; so does one between two ranked men.
            int rounds = r.WasTitle || (WorldRanked(x) && WorldRanked(y)) ? 12 : 10;
            var res = FastBout(x, y, rounds);
            ApplyOutcome(res, x, y, r.WasTitle ? null : "rematch");
            ReportBout(res);
            if (WorldRanked(x) || WorldRanked(y))
                LogEvent($"{(res.IsDraw ? $"{x.Name} and {y.Name} drew their rematch" : $"{res.Winner!.Name} settles it with {res.Loser!.Name}")} — the return after {r.Why}.",
                         kind: "fight", div: x.WeightClass, bout: RefOf(res));
        }
    }

    /// <summary>The best men in the world, steered into each other.
    ///
    /// Pound-for-pound is the one board in boxing that crosses the divisions, and until now it was purely a
    /// list: two men could sit at one and two for five years and never be in the same building, because every
    /// pairing in the sim happens inside a weight class. Meanwhile the elite-stay-apart rule kept the best of
    /// a single division apart as well, so the ONLY way two great fighters ever met was if one happened to
    /// hold a belt the other was ranked for.
    ///
    /// Once a year the sport now makes the fight instead of waiting for it. Two kinds:
    ///
    ///   A superfight - two of the top pound-for-pound men, within two divisions of each other, meeting at
    ///   the heavier man's weight. No belt: this is the fight for its own sake, and the lighter man is not
    ///   moving up permanently, he is coming up for one night.
    ///
    ///   An eliminator - the two best contenders in a division who are not champions, fought over the
    ///   championship distance, with the winner in line. This is the one the sim was structurally incapable
    ///   of making, since two top-eight men were forbidden to meet outside a title fight.
    ///
    /// Both are rationed. One or two superfights a year across the whole sport, and an eliminator in perhaps
    /// half the divisions - any more and being great stops meaning anything, because everyone has beaten
    /// everyone.</summary>
    private void StageSuperfights()
    {
        // Two looks a year. Most come to nothing - the same two men are rarely both free, both in form and
        // both a division apart - but one attempt a year left the very best meeting once every other year,
        // which is not a sport with events in it.
        StageP4PSuperfight();
        StageP4PSuperfight();
        foreach (var wc in AllDivisions)
            if (DivisionActive(wc) && _rng.NextDouble() < 0.45)
                StageEliminator(wc);
    }

    /// <summary>Can this man be put in a big fight at all - fit, not at his cap, and not the player, whose
    /// nights are his own to accept or turn down.</summary>
    private bool FreeForABigNight(Boxer b) =>
        !b.Retired && b.Id != Player.Id && Available(b) && !AtYearCap(b) && !RecentlyMovedUp(b);

    private void StageP4PSuperfight()
    {
        var board = PoundForPound(10).Where(FreeForABigNight).ToList();
        if (board.Count < 2) return;

        // Every pairing worth making, scored. Closeness on the P4P board matters most - one against two is
        // the fight, one against nine is not - and weight is a real obstacle: a division apart is a story, two
        // is a stretch, three is a different sport.
        (Boxer A, Boxer B, double Score)? best = null;
        for (int i = 0; i < board.Count; i++)
            for (int j = i + 1; j < board.Count; j++)
            {
                var x = board[i]; var y = board[j];
                int weightGap = Math.Abs((int)x.WeightClass - (int)y.WeightClass);
                if (weightGap > 2) continue;
                if (RecentFoes(x, 3).Contains(y.Name)) continue;
                if (x.History.Count(h => h.Opponent == y.Name) >= 2) continue;   // twice is enough
                // Both have to be somebody: a champion, or the best of his division.
                if (!(IsWorldChampion(x) || Top8Ids(x.WeightClass).Contains(x.Id))) continue;
                if (!(IsWorldChampion(y) || Top8Ids(y.WeightClass).Contains(y.Id))) continue;

                double score = (10 - i) + (10 - j) - weightGap * 3.5 + _rng.NextDouble() * 3;
                if (best is null || score > best.Value.Score) best = (x, y, score);
            }

        if (best is null) return;
        // Even a made fight falls through - purses, promoters, the wrong month.
        if (_rng.NextDouble() > 0.70) return;

        var (a, b, _) = best.Value;
        // The heavier man's weight, and the championship distance, because that is what these are.
        // Who is the bigger man — and when they are the same size, the champion, because it is his belt that
        // is at stake and the other man who is challenging for it. Taking whichever happened to be argument
        // `a` meant that a superfight between two middleweights, one of them the champion, was fought for
        // nothing half the time: the sim asked whether the "heavier" man held a belt, and half the time the
        // heavier man was the challenger.
        var heavier = (int)a.WeightClass != (int)b.WeightClass
                    ? ((int)a.WeightClass > (int)b.WeightClass ? a : b)
                    : (IsWorldChampion(a) ? a : IsWorldChampion(b) ? b : a);
        var lighter = ReferenceEquals(heavier, a) ? b : a;
        _cursor = heavier.WeightClass;

        // And his belt, if he has one. These were being fought for nothing, which is not what a superfight is:
        // the lighter man comes up to challenge, and what he is challenging for is the title. Leonard went up
        // to middleweight for Hagler's belt, not for an exhibition. Only the heavier man's strap can be on the
        // line — the lighter man's is at a weight the other could not make.
        // ...but only if the lighter man could actually campaign there. A career climbs two divisions from
        // where it started and no further, and a belt on the line here means he MOVES if he wins it — so a
        // superfight that would carry him past that cap is fought at a catchweight for nothing instead.
        // (The integrity test caught this: without the check a man could be walked up the scale by superfights.)
        string? belt = !StepUpAllowed(lighter, heavier.WeightClass) ? null
                     : ChampOf(heavier.WeightClass)?.Id == heavier.Id ? PrimaryBelt
                     : WbcOf(heavier.WeightClass)?.Id == heavier.Id ? "WBC"
                     : IbfOf(heavier.WeightClass)?.Id == heavier.Id ? "IBF" : null;
        string note = belt is not null ? $"{belt} title" : "superfight";

        Date = SpreadDate(Date.Year, 1 + _rng.Next(4), 6);
        var res = FastBout(a, b, 12);
        ApplyOutcome(res, a, b, note);
        ReportBout(res);
        // A belt changing hands here has to actually change hands, or the champions board still shows the
        // beaten man holding it.
        if (belt is not null && !res.IsDraw && res.Winner!.Id == lighter.Id)
        {
            // He came up and took it, so he campaigns there now — the way a man who wins a belt two divisions
            // north does. MoveUpTo strips the belts he leaves behind and rebalances him for the new weight;
            // no warm-up, because he has just beaten the champion of it.
            var won = heavier.WeightClass;
            MoveUpTo(lighter, won, warmup: false);
            if (belt == PrimaryBelt) CrownChampion(lighter);
            else if (belt == "WBC") CrownWbc(lighter);
            else CrownIbf(lighter);
            _everChampion.Add(lighter.Id);
        }
        LogEvent(res.IsDraw
                    ? $"{a.Name} and {b.Name} draw the superfight — the two best in the world settle nothing."
                    : belt is not null
                        ? $"{res.Winner!.Name} beats {res.Loser!.Name} for the {belt} {heavier.WeightClass.DisplayName()} title — the best against the best, with a belt on it."
                        : $"{res.Winner!.Name} beats {res.Loser!.Name} in the superfight — the best against the best, at {heavier.WeightClass.DisplayName()}.",
                 kind: "fight", div: heavier.WeightClass, bout: RefOf(res));
        _cursor = Division;
    }

    /// <summary>The two best contenders in a division meet, with the winner next in line.</summary>
    private void StageEliminator(WeightClass wc)
    {
        var champs = new[] { ChampOf(wc)?.Id, WbcOf(wc)?.Id, IbfOf(wc)?.Id }.Where(i => i is not null).ToHashSet();
        var field = ActiveIn(wc)
            .Where(b => RankedContender(b) && !champs.Contains(b.Id) && FreeForABigNight(b))
            .OrderByDescending(RankScore)
            .Take(4).ToList();
        if (field.Count < 2) return;

        var a = field[0];
        // The second man is usually the next one down, sometimes the one below that - so the same eliminator
        // is not made every year in a division whose top two never change.
        var b = field.Skip(1).Skip(_rng.Next(Math.Min(2, field.Count - 1))).FirstOrDefault() ?? field[1];
        if (RecentFoes(a, 3).Contains(b.Name)) return;

        _cursor = wc;
        Date = SpreadDate(Date.Year, _rng.Next(6), 6);
        var res = FastBout(a, b, 12);
        ApplyOutcome(res, a, b, "eliminator");
        ReportBout(res);
        if (!res.IsDraw)
        {
            _mandatory[wc] = (res.Winner!.Id, Date.AddDays(540));
            LogEvent($"{res.Winner.Name} beats {res.Loser!.Name} in the final eliminator — he is next for the title.",
                     kind: "fight", div: wc, bout: RefOf(res));
        }
        _cursor = Division;
    }

    /// <summary>Opponents a fighter has met in his last few bouts — used to avoid stale rematches.</summary>
    private static HashSet<string> RecentFoes(Boxer b, int n) =>
        b.History.Skip(Math.Max(0, b.History.Count - n)).Select(h => h.Opponent).ToHashSet();

    /// <summary>Pick a title challenger: a top-10 contender the champion hasn't just fought. The other
    /// world champion is never an option here — they only meet in a deliberate unification bout.</summary>
    private Boxer? PickChallenger(Boxer champ, Boxer? otherChamp)
    {
        // The return first. A champion who won on a split card, or was dropped and got up, owes the man the
        // night back before he moves on to somebody new - and the rule below that says "not a man he has just
        // fought" is exactly what would otherwise stop it.
        if (RematchFoeFor(champ) is Boxer owed && owed.Id != Player.Id
            && (otherChamp is null || owed.Id != otherChamp.Id) && !RecentlyMovedUp(owed))
            return owed;

        var recent = RecentFoes(champ, 4);
        var here = ActiveIn(champ.WeightClass);   // challengers come from the champion's own division

        // Then the mandatory. A man who won the eliminator is owed the shot he won, and he keeps that claim
        // for about eighteen months before the division moves on without him.
        if (_mandatory.TryGetValue(champ.WeightClass, out var m) && Date <= m.Until && m.Id != champ.Id)
        {
            var mandatory = here.FirstOrDefault(b => b.Id == m.Id && b.Id != Player.Id && Available(b)
                                                  && !AtYearCap(b) && !recent.Contains(b.Name));
            if (mandatory is not null) { _mandatory.Remove(champ.WeightClass); return mandatory; }
        }
        bool Ok(Boxer b) => b.Id != Player.Id && b.Id != champ.Id
                         && (otherChamp is null || b.Id != otherChamp.Id) && WorldRanked(b) && !RecentlyMovedUp(b) && Available(b);
        // Prefer a contender he hasn't just fought and hasn't already met several times.
        var ranked = here.Where(b => Ok(b) && !recent.Contains(b.Name) && champ.History.Count(h => h.Opponent == b.Name) < 3).ToList();
        if (ranked.Count == 0) ranked = here.Where(b => Ok(b) && !recent.Contains(b.Name)).ToList();
        if (ranked.Count == 0) ranked = here.Where(Ok).ToList();   // fall back if he's fought everyone lately
        // Thin/young division with no ranked contender yet: rather than sit idle for years, the champion defends
        // against the best available REAL contender (a rising fighter, gatekeeper-plus) — never a class-1–3 journeyman.
        if (ranked.Count == 0)
            ranked = here.Where(b => b.Id != Player.Id && b.Id != champ.Id && (otherChamp is null || b.Id != otherChamp.Id)
                                  && !RecentlyMovedUp(b) && Available(b) && b.Potential >= 66 && ProFights(b) >= 15 && !recent.Contains(b.Name))
                         .OrderByDescending(RankScore).ToList();
        if (ranked.Count == 0) return null;
        var top10 = ranked.OrderByDescending(RankScore).Take(10).ToList();
        return top10[_rng.Next(top10.Count)];
    }

}
