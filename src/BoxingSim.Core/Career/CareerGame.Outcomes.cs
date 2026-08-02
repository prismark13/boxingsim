using BoxingSim.Core.Engine;
using BoxingSim.Core.Generation;
using BoxingSim.Core.League;
using BoxingSim.Core.Model;

namespace BoxingSim.Core.Career;

/// <summary>What a result does to the two men in it: records, ratings, injuries, rankings and the news.</summary>
public sealed partial class CareerGame
{
    // ---- outcomes & ratings ----

    /// <summary>Nudge a bout off a night either man has already boxed on.
    ///
    /// The sport is SIMULATED in pass order but DATED across the year, so a man matched on one card could be
    /// handed a date he had already fought on — Joe Frazier met Roberto Ribeiro and Muhammad Ali on the same
    /// 19 April 1970. Guarded here, at the one point every bout in the game passes through, rather than at the
    /// dozen places a bout is staged: that is a list somebody will add to and forget.
    ///
    /// It moves the fight rather than cancelling it, so the volume of boxing in the world is unchanged. The
    /// player is left alone — his dates come from offers he agreed to, and moving one would put the fight on a
    /// different night from the one the app has been counting down to.</summary>
    private DateOnly LegalNightFor(Boxer a, Boxer b, DateOnly when)
    {
        if (a.Id == Player.Id || b.Id == Player.Id) return when;

        static DateOnly? Clash(Boxer f, DateOnly d)
        {
            foreach (var h in f.History)
                if (Math.Abs(h.Date.DayNumber - d.DayNumber) < 28) return h.Date;
            return null;
        }

        for (int guard = 0; guard < 24; guard++)
        {
            var hit = Clash(a, when) ?? Clash(b, when);
            if (hit is null) return when;
            when = hit.Value.AddDays(28);
        }
        return when;
    }

    /// <summary>Apply a result to both men, on a given night.
    ///
    /// <paramref name="on"/> is the night the bout was made for; left out, it is the day the world is living
    /// in, which is right for the player's own fight and for anything resolved in the present.
    ///
    /// It no longer touches the world clock. It used to open by assigning to it, which meant a bout could not
    /// be resolved without moving the date the whole world was standing on — so the second fight on a card
    /// began from wherever the first one had pushed the clock, and a "week" of the sport could advance a
    /// month. Everything a result produces that carries a date now takes that date as an argument.
    ///
    /// RETURNS the night the bout actually landed on, which is not necessarily the one asked for:
    /// <see cref="LegalNightFor"/> pushes a fight off a date either man has already boxed on. A caller that
    /// reports the bout must date the report by what comes back, not by what it passed in — announcing a new
    /// champion on a night he did not fight is exactly the fault this returns to prevent.</summary>
    /// <summary>The injuries that end a career rather than interrupt one.</summary>
    private static readonly string[] RingEnders =
        { "a detached retina", "a fractured orbital bone", "a broken jaw", "a bleed on the brain", "a shattered hand" };

    /// <summary>Did tonight finish him? Returns what did, or null.
    ///
    /// Rare on purpose — this is the thing that takes a man out of the sport with no warning, and it stops
    /// being a shock if it happens often. Age carries most of the weight: a twenty-four-year-old walks away
    /// from damage that retires a thirty-six-year-old, which is as true of eyes and hands as it is of chins.
    /// Being knocked out repeatedly over a career tells as well, because the men who go this way are usually
    /// the men who have already taken too much.</summary>
    private string? CareerEndingBlow(Boxer f, DateOnly on)
    {
        if (f.Retired) return null;
        // These numbers are small because the sport is large. ApplyOutcome runs for EVERY bout in the world —
        // journeyman six-rounders included — so a rate that reads as rare on one card is a plague across a
        // division: the first pass at 1-in-2,500 produced sixty-two career-ending injuries in three years of a
        // single division and kept the belts permanently vacant, which took unifications to zero. It has to be
        // rare per NIGHT, not rare per career.
        double risk = 0.00003                                    // a young, unmarked fighter: about 1 in 33,000
                    + Math.Max(0, f.Age - 32) * 0.00006          // it is the late thirties that carry this
                    + Math.Min(6, f.Record.KnockoutLosses) * 0.00008;
        if (_rng.NextDouble() >= risk) return null;
        return RingEnders[_rng.Next(RingEnders.Length)];
    }

    /// <summary>Take him out of the sport, and make sure the world notices. A belt he was holding is vacated
    /// the way it is for a retirement, because that is what this is.</summary>
    private void HangThemUpInjured(Boxer f, string injury, DateOnly on)
    {
        f.Retired = true;
        f.IsChampion = false;
        _titles.VacateWorldBelts(f.WeightClass, f);
        VacateLineal(f.WeightClass, f, "is forced out of the sport");
        bool inducted = MaybeInductHoF(f);
        if (!inducted)
            LogEvent($"{f.Name} ({f.Record}) is forced to retire with {injury}.",
                     f.Id == Player.Id, kind: "retire", div: f.WeightClass, on: on);
        UpdateBeltsFor(f.WeightClass);
    }

    private DateOnly ApplyOutcome(FightResult res, Boxer a, Boxer b, string? note = null, DateOnly? on = null)
    {
        var night = LegalNightFor(a, b, on ?? Date);
        if (_watch is not null)
        {
            var w = res.Winner ?? a; var l = res.Loser ?? b;
            _watch.Add(new WorldBout(night, a.WeightClass, RegionOf(a) ?? "Rest of the world", a.Country ?? "",
                                     w.Name, l.Name, res.Method, res.EndRound, res.IsDraw, note));
        }
        // Giving up a regional strap belongs to WINNING a world belt, not to turning up for the fight.
        // Both men used to drop theirs the moment a world title bout happened — so a challenger who lost was
        // stripped of the national title he still held, for a belt he did not win.
        if (IsWorldTitleNote(note) && !res.IsDraw && res.Winner is not null) DropRegionals(res.Winner, night);

        bool ko = res.Outcome is FightOutcome.Knockout or FightOutcome.TechnicalKnockout;
        if (res.IsDraw) { a.Record.Draws++; b.Record.Draws++; }
        else
        {
            res.Winner!.Record.Wins++;
            res.Loser!.Record.Losses++;
            if (ko)
            {
                res.Winner.Record.KnockoutWins++;
                res.Loser.Record.KnockoutLosses++;
                _careers.RegisterKnockoutLoss(res.Loser);
                // A knockout means a medical suspension — a fragile fighter (low durability: chin/heart/conditioning)
                // is hurt worse and sits out far longer; a granite-chinned man is back in a month or two.
                int dura = MedicalRoom.Durability(res.Loser.Ratings);
                _medical.Suspend(res.Loser.Id, night.AddDays(35 + Math.Max(0, 85 - dura) * 2 + _rng.Next(45)));
            }
        }
        // Cuts and hand injuries can sideline either man, win or lose — a fighter with poor cut resistance is far
        // more injury-prone, so brittle fighters miss real time while durable ones almost never do.
        foreach (var f in new[] { a, b })
        {
            if (ko && f.Id == res.Loser?.Id) continue;   // the KO'd man is already on the shelf
            double proneness = 0.012 + (1.0 - f.Ratings.CutResistance / 100.0) * 0.06;
            if (_rng.NextDouble() < proneness)
                _medical.Suspend(f.Id, night.AddDays(28 + _rng.Next(63)));
        }

        // A CAREER CAN END IN THE RING, and for everyone. The engine has always produced these — a detached
        // retina, a fractured orbital, a broken jaw — and exactly one line of code read the flag, for the
        // player. So the sport had a hazard that only ever befell one man in it, and the NPC resolver did not
        // generate them at all: every other fighter in the world was immortal until his mileage ran out.
        // Rolled here instead, which is the one place every bout in the world goes through, fast model or full.
        string? endedA = CareerEndingBlow(a, night), endedB = CareerEndingBlow(b, night);

        // Each fighter's ledger: date, result, method, round, knockdowns scored / suffered.
        char ra = res.IsDraw ? 'D' : res.Winner!.Id == a.Id ? 'W' : 'L';
        char rb = res.IsDraw ? 'D' : res.Winner!.Id == b.Id ? 'W' : 'L';
        string? cardsA = null, cardsB = null;
        if (res.Scorecards.Count > 0)
        {
            cardsA = ScoreCards.Write(res.Scorecards.Select(c => (c.A, c.B)));
            cardsB = ScoreCards.Write(res.Scorecards.Select(c => (c.B, c.A)));
        }

        // Full-engine bouts carry a round-by-round breakdown; the fast NPC resolver has none.
        List<BoutRound>? roundsA = null, roundsB = null;
        if (res.Rounds.Count > 0)
        {
            roundsA = res.Rounds.Select(r => new BoutRound { Round = r.Round, LandedFor = r.LandedA, LandedAgainst = r.LandedB, KdFor = r.KnockdownsB, KdAgainst = r.KnockdownsA, ScoreFor = r.ScoreA, ScoreAgainst = r.ScoreB }).ToList();
            roundsB = res.Rounds.Select(r => new BoutRound { Round = r.Round, LandedFor = r.LandedB, LandedAgainst = r.LandedA, KdFor = r.KnockdownsA, KdAgainst = r.KnockdownsB, ScoreFor = r.ScoreB, ScoreAgainst = r.ScoreA }).ToList();
        }
        // The full per-round grid is kept only for bouts a player is likely to inspect — his own fights,
        // title fights, and any involving a world-ranked fighter. The rest keep just the (cheap) card
        // string, so a long career's save doesn't balloon with round data for journeyman undercards.
        bool keepRounds = a.Id == Player.Id || b.Id == Player.Id || note is not null || WorldRanked(a) || WorldRanked(b);
        var commentary = ExtractHighlights(res);   // null for the fast NPC resolver (no tick detail)
        // The weight it was made at. Equal for an ordinary bout; for a superfight between two divisions it is
        // the heavier man's, because that is the weight the lighter man came up to.
        var at = (WeightClass)Math.Max((int)a.WeightClass, (int)b.WeightClass);
        Record(a, b.Name, ra, res.Method, res.EndRound, res.KnockdownsB, res.KnockdownsA, note, cardsA, keepRounds ? roundsA : null, commentary, at, night, endedA);
        Record(b, a.Name, rb, res.Method, res.EndRound, res.KnockdownsA, res.KnockdownsB, note, cardsB, keepRounds ? roundsB : null, commentary, at, night, endedB);

        // Retired AFTER both ledgers are written, so the fight that finished him is the last line on his
        // record rather than a bout he was already retired for.
        if (endedA is not null) HangThemUpInjured(a, endedA, night);
        if (endedB is not null) HangThemUpInjured(b, endedB, night);

        // Where the two men stood BEFORE this result moved them — the year-end awards judge an upset on what
        // was expected going in, and everything below rewrites it.
        double aPtsBefore = a.RankPoints, bPtsBefore = b.RankPoints;

        double scoreA = res.IsDraw ? 0.5 : res.Winner!.Id == a.Id ? 1.0 : 0.0;
        const double k = 32.0;
        double ea = 1.0 / (1.0 + Math.Pow(10, (b.RankPoints - a.RankPoints) / 400.0));
        a.RankPoints += k * (scoreA - ea);
        b.RankPoints += k * ((1 - scoreA) - (1 - ea));
        // Momentum matters — a win run forces a fighter into contention — but ONLY against real opposition, and
        // only in capped amounts. These bonuses are the one part of the rating that isn't zero-sum, so paying them
        // for every win turned the ratings into a fight counter: a busy journeyman out-earned an elite simply by
        // boxing more often, and a 60-fight record beat a 23-0 champion.
        if (res.Winner is Boxer wn && res.Loser is Boxer ls && WorldRanked(ls) && ls.Overall >= wn.Overall - 10)
        {
            if (ko) wn.RankPoints += 4;
            int ws = WinStreak(wn);   // includes the bout just recorded
            if (ws >= 3) wn.RankPoints += Math.Min(ws, 10) * 1.2;
        }
        if (res.Loser is not null) res.Loser.RankPoints -= 12;   // a defeat that ends a run stings the standing

        UpdateLineal(res, a, b, note, night);

        // Rare permanent wear carries forward (only matters for non-historical fighters, whose ratings
        // are recomputed from their prime each year — so apply to the player and generated fighters).
        foreach (var le in res.Lasting)
        {
            var f = le.Name == a.Name ? a : b;
            if (_historical.ContainsKey(f.Id)) continue;
            ApplyLasting(f.Ratings, le);
        }

        // Every bout is a chance for either man, from his prime on, to decide he's outgrowing the weight.
        ConsiderStepUp(a);
        ConsiderStepUp(b);

        NoteRematchDemand(res, a, b, note);   // did this one leave a question?
        CaptureBout(res, a, b, note, night, aPtsBefore, bPtsBefore);  // a candidate for the year-end awards
        return night;
    }

    private static void ApplyLasting(Ratings r, LastingEffect le)
    {
        switch (le.Attr)
        {
            case "Chin": r.Chin = Ratings.Clamp(r.Chin + le.Delta); break;
            case "Power": r.Power = Ratings.Clamp(r.Power + le.Delta); break;
            case "CutResistance": r.CutResistance = Ratings.Clamp(r.CutResistance + le.Delta); break;
        }
    }

    // Kept as methods rather than inlined at the call sites: three of them are handed to DefendBeltSeason as
    // the "and this is how that belt changes hands" argument, so they have to be method groups.
    // The night is passed wherever the caller knows it — a belt changes hands IN A FIGHT, and that fight has
    // its own date. Callers with no night (a yearly stripping, a relinquishment between cards) fall back to
    // the clock, which for them is the right answer.
    private void CrownChampion(Boxer b, DateOnly? on = null) { _titles.CrownChampion(b, on); Crowned(b); }
    private void CrownWbc(Boxer b, DateOnly? on = null) { _titles.SetWbc(b.WeightClass, b, on); Crowned(b); }
    private void CrownIbf(Boxer b, DateOnly? on = null) { _titles.SetIbf(b.WeightClass, b, on); Crowned(b); }

    /// <summary>He won a world belt. Written down the MOMENT it happens, in the division it happened in.
    ///
    /// The Hall used to learn this by looking around once a year: the turn-of-the-year pass asked who was
    /// champion on 1 January and recorded that. So a man who won a belt in March and lost it in November was
    /// never recorded as having held one at all — and to register as a two-weight champion you had to be
    /// holding a belt on New Year's Day in two different divisions, in two different years. The sport had
    /// none. Every multi-weight champion the Hall was built to honour was invisible to it.</summary>
    private void Crowned(Boxer b)
    {
        _hall.MarkChampion(b.Id);
        _hall.MarkTitleDivision(b.Id, b.WeightClass);
    }

}
