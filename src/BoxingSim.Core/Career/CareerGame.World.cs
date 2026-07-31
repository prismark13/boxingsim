using BoxingSim.Core.Engine;
using BoxingSim.Core.Generation;
using BoxingSim.Core.League;
using BoxingSim.Core.Model;

namespace BoxingSim.Core.Career;

/// <summary>The calendar, and everything the sport does while the player is not fighting: cards, seasons,
/// debuts, retirements and the turn of the year.</summary>
public sealed partial class CareerGame
{
    // ---- world calendar ----

    /// <summary>Roll the calendar forward to a date, running a fight card every fortnight and doing the
    /// yearly bookkeeping (debuts, aging, retirements) each time the year turns over.</summary>
    private void AdvanceTo(DateOnly target)
    {
        while (Date < target)
        {
            if (AdvanceSome(target, 14).Count == 0 && Date >= target) return;
            if (Player.Retired) return;
        }
    }

    /// <summary>Run the turn of the year for every year that has gone by since the last one was run.
    ///
    /// This used to ask whether THIS STEP crossed New Year — and the step is not the only thing that moves the
    /// clock. Every staged bout sets Date to put itself on a night, the yearly pass moves it while it works,
    /// and a fight can carry the calendar months forward on its own. Miss the boundary once and the year was
    /// never noticed again: nobody aged, nobody retired, nobody debuted, and no superfight was ever made. A
    /// fighter could be nine fights into his career and still eighteen, and a world fifteen years old could
    /// go silent because none of the things that make news were happening any more.
    ///
    /// Asking "which years have I not run yet" instead cannot be missed, whatever moved the clock or by how
    /// much. It is a loop rather than an if because a fight can jump more than a year.</summary>
    private void CatchUpYears()
    {
        // First call of a world: adopt the current year rather than running every year since the calendar
        // began. A loaded save does the same, so reopening a career never re-runs a year it already had.
        if (_lastYearRun == 0) { _lastYearRun = Date.Year; return; }

        while (Date.Year > _lastYearRun)
        {
            _lastYearRun++;
            ComputeAwardsFor(_lastYearRun - 1);
            // A year of the sport just ended. Hand its honours to whoever is watching — but not in a
            // universe, which has no player to hand them to.
            if (Universe is null && !Player.Retired)
                UnseenAwards = _awards.FirstOrDefault(a => a.Year == _lastYearRun - 1);
            YearlyPass();
        }
    }

    /// <summary>Move the world forward by one step and hand back what happened in it.
    ///
    /// Everything the sport does between the player's fights used to happen inside a single call that ran
    /// three months of boxing before his bout and returned nothing. He clicked "take the fight", the world
    /// silently caught up, and the results were waiting for him in a list afterwards. Nothing ever happened
    /// WHILE HE WAITED - the fight he was anticipating and the world around it arrived in the same frame.
    ///
    /// This is the same machinery with a door in it: step the calendar, run the cards, and return the
    /// headlines from that step so they can be read as they land. The fortnight is unchanged, so a career
    /// played straight through behaves exactly as it did.</summary>
    private IReadOnlyList<CareerEvent> AdvanceSome(DateOnly target, int days)
    {
        if (Date >= target) return Array.Empty<CareerEvent>();
        long mark = _logWrites;   // not _log.Count — see the field. The log is capped; its length is not a position.
        var next = Date.AddDays(days);
        if (next > target) next = target;
        AdvanceClockTo(next);
        CatchUpYears();
        RunEvent();
        return NewsSince(mark);
    }

    /// <summary>The headlines written since a mark. Clamped to what the log still holds, because a single step
    /// of a busy world can write more than the whole window keeps.</summary>
    private IReadOnlyList<CareerEvent> NewsSince(long mark)
    {
        int added = (int)Math.Min(_logWrites - mark, _log.Count);
        return added <= 0 ? Array.Empty<CareerEvent>() : _log.Skip(_log.Count - added).ToList();
    }

    /// <summary>Run the sport forward one week toward fight night and report what happened, so the wait can
    /// be watched rather than skipped. Null once there is nothing left to wait for.</summary>
    public IReadOnlyList<CareerEvent>? WaitAWeek()
    {
        if (Player.Retired || Offer is null || Date >= OfferDate) return null;
        return AdvanceSome(OfferDate, 7);
    }

    /// <summary>How long until he fights, in days. Zero once fight night has arrived.</summary>
    public int DaysToFight => Offer is null ? 0 : Math.Max(0, OfferDate.DayNumber - Date.DayNumber);

    /// <summary>A fight in the player's own division worth stopping to watch, from the events just logged:
    /// a title fight, an eliminator, or a night involving somebody ranked around him. This is what turns a
    /// list of results into an invitation - the sim already stores the round-by-round for exactly these
    /// bouts, so every one of them can be watched rather than merely read.</summary>
    public BoutRef? WorthWatching(IEnumerable<CareerEvent> events)
    {
        foreach (var e in events)
        {
            if (e.Bout is not BoutRef b || e.Div != Player.WeightClass) continue;
            if (e.Kind is "title" || e.Text.Contains("eliminator", StringComparison.OrdinalIgnoreCase)) return b;
            if (FindByName(b.Winner) is Boxer w && FindByName(b.Loser) is Boxer l
                && (Top20Ids(Player.WeightClass).Contains(w.Id) || Top20Ids(Player.WeightClass).Contains(l.Id)))
                return b;
        }
        return null;
    }

    /// <summary>New blood for the year: real fighters on their historical debut year, plus generated prospects.</summary>
    private void InjectDebuts()
    {
        foreach (var e in _future.Where(f => f.DebutYear == Date.Year).ToList())
        {
            InjectHistorical(e.Proto, e.DebutAge, e.DebutAge, e.Peak, announce: true);
            _future.Remove(e);
        }
        // Fresh prospects turn pro in every division that exists yet.
        foreach (var wc in AllDivisions)
        {
            if (!DivisionActive(wc)) continue;
            int debuts = Universe is { } u
                ? Math.Max(0, u.EntrantsPerYear + _rng.Next(-3, 4))
                : 14 + _rng.Next(10);
            for (int i = 0; i < debuts; i++) AddActive(_factory.CreateProspect(wc, GeneratedCap, Year));
        }
    }

    /// <summary>Yearly aging and retirements across every division; weight-moves; re-crown vacant belts.</summary>
    private void AgeRetireCrown()
    {
        SeedNewlyFoundedDivisions();   // a division founded this year opens with contenders moving up into it
        foreach (var b in _roster.Where(x => !x.Retired).ToList())
        {
            if (b.Id == Player.Id) { _careers.AdvanceOneYear(b); }
            else if (_historical.TryGetValue(b.Id, out var h)) { b.Age++; AgeHistorical(b, h.Prime, h.Peak); }  // advance a year, THEN re-point ratings on their arc (AgeHistorical only sets ratings, never the age)
            else _careers.AdvanceOneYear(b);
            CapStarter(b);

            // Ranking points drift back toward what the man can actually do NOW. Without this the ratings only ever
            // ratchet up, so a padded record compounds forever and a faded former great never slides down the list.
            // A real run still lifts a fighter well clear of his anchor — it just can't outrun ability indefinitely.
            b.RankPoints += (World.AbilityAnchor(b.Overall) - b.RankPoints) * 0.28;

            // Track Hall-of-Fame credentials: best rating ever reached, and whether he ever held a world belt.
            // The player's arc has to be recorded as it happens; his year-to-year development is random and
            // there is no way to reconstruct what he was at 22 once he is 30.
            if (b.Id == Player.Id && _playerArc.All(x => x.Age != b.Age))
                _playerArc.Add((CareerMileage.Fights(b), b.Age, b.Ratings.Clone()));
            _peakOverall[b.Id] = Math.Max(_peakOverall.GetValueOrDefault(b.Id), b.Overall);
            _peakClass[b.Id] = Math.Max(_peakClass.GetValueOrDefault(b.Id), b.Class);
            if (ChampOf(b.WeightClass)?.Id == b.Id || WbcOf(b.WeightClass)?.Id == b.Id || IbfOf(b.WeightClass)?.Id == b.Id)
            {
                _everChampion.Add(b.Id);
                if (!_titleDivisions.TryGetValue(b.Id, out var divs)) _titleDivisions[b.Id] = divs = new();
                divs.Add(b.WeightClass);   // he campaigned up and won here too → a multi-weight champion
            }

            // Fight regularly or hang them up: a generated fighter who's been idle for ~2 years drifts out
            // of the sport, so the rankings stay full of active men rather than ghosts.
            bool inactive = b.Id != Player.Id && !_historical.ContainsKey(b.Id)
                            && ProFights(b) > 0 && DaysSinceLastBout(b) > 730;
            if (_careers.ShouldRetire(b) || inactive)
            {
                b.Retired = true;
                if (b.IsChampion) b.IsChampion = false;
                if (ChampOf(b.WeightClass)?.Id == b.Id) _champions[b.WeightClass] = null;
                if (WbcOf(b.WeightClass)?.Id == b.Id) _wbc[b.WeightClass] = null;
                if (IbfOf(b.WeightClass)?.Id == b.Id) _ibf[b.WeightClass] = null;
                VacateLineal(b.WeightClass, b, "retires as champion");
                bool inducted = MaybeInductHoF(b);
                if (b.Id == Player.Id) { if (!inducted) LogEvent($"{Player.Name} retires from boxing.", true, kind: "retire"); }
                else if (!inducted && b.Overall >= 80) LogEvent($"{b.Name} ({b.Record}) hangs them up after a fine career.", kind: "retire", div: b.WeightClass);
            }
        }

        FlushStepUps();   // fighters queued over the year now campaign up in weight

        // Re-crown any vacant primary belt in every division that exists.
        foreach (var wc in AllDivisions)
        {
            if (!DivisionActive(wc)) continue;
            var champ = ChampOf(wc);
            if (champ is null || champ.Retired)
            {
                _champions[wc] = null;
                var winner = ContestVacantTitle(wc, PrimaryBelt, WbcOf(wc)?.Id ?? 0, IbfOf(wc)?.Id ?? 0);
                if (winner is not null)   // announced by ContestVacantTitle, dated to fight night
                {
                    _champions[wc] = winner; winner.IsChampion = true;
                }
            }
        }

        foreach (var wc in AllDivisions) UpdateBeltsFor(wc);
        // Divisions aren't capped — their sizes settle naturally as fighters age, retire and move up.
    }

    /// <summary>Fill a vacant belt by matching the two leading eligible contenders in a real title bout, so the new
    /// champion actually WON it (the fight lands in his ledger) instead of being handed the strap. Returns the new
    /// champion — the lone credible contender unopposed if there's only one — or null if the division is bare.</summary>
    private Boxer? ContestVacantTitle(WeightClass wc, string belt, params int[] excludeIds)
    {
        var exclude = excludeIds.Where(id => id != 0).ToHashSet();
        bool Eligible(Boxer b) => (b.Id != Player.Id || Player.IsChampion) && !exclude.Contains(b.Id)
                               && !RecentlyMovedUp(b) && _medical.Available(b) && Rested(b);
        var field = ActiveIn(wc).Where(b => Eligible(b) && WorldRanked(b)).OrderByDescending(RankScore).Take(2).ToList();
        if (field.Count == 0) return null;
        if (field.Count == 1)
        {
            // Only one ranked contender — bring in the best available challenger so the belt is still fought for,
            // never simply handed over (a great shouldn't become champion without a title-winning bout).
            var next = ActiveIn(wc).Where(b => Eligible(b) && b.Id != field[0].Id).OrderByDescending(RankScore).FirstOrDefault();
            if (next is null) return field[0];   // a truly bare division — he takes it unopposed
            field.Add(next);
        }

        // Nothing to save and restore any more: neither the clock nor the division is ambient.
        // Spread forward through the rest of the year while a year of history is being laid out, but never past
        // the day the player is living in. Crowning a vacant belt is settled here and now — the result is
        // applied to records immediately — so dating it months ahead announced a champion the player could not
        // yet have watched being crowned, and put the headline below older news in a feed sorted by date.
        var wanted = NoLaterThanToday(SpreadDateFrom(Date));
        var res = FastBout(field[0], field[1], 12);
        // What comes back, not what went in: if either man boxed too recently the bout is pushed to a later
        // night, and the headline has to follow the fight rather than the intention.
        var night = ApplyOutcome(res, field[0], field[1], $"{belt} title", on: wanted);
        var winner = res.IsDraw ? field[0] : res.Winner!;   // a draw leaves the belt with the higher-ranked man
        // Dated to fight night rather than to whenever the caller happens to be. It used to depend on the
        // announcement happening BEFORE the clock was put back, which is a rule about statement order that
        // nothing enforces: move this line down three and the headline reads months before the bout that
        // decided it. It carries its own date now, so it cannot be moved into the wrong one.
        _everChampion.Add(winner.Id);
        LogEvent($"{winner.Name} wins the vacant {belt} title.", winner.Id == Player.Id, kind: "title", div: wc,
                 on: night);
        return winner;
    }

    /// <summary>Enshrine a retiring great: a world champion with a real body of work, or a genuinely elite talent.
    /// The snapshot is self-contained so it survives the roster being pruned on save. Returns true if inducted.</summary>
    private bool MaybeInductHoF(Boxer b)
    {
        if (_hof.Any(x => x.Id == b.Id)) return false;
        int peak = _peakOverall.GetValueOrDefault(b.Id, b.Overall);
        int peakClass = Math.Max(_peakClass.GetValueOrDefault(b.Id), b.Class);
        if (_historical.TryGetValue(b.Id, out var h)) { peak = Math.Max(peak, h.Prime.Overall); peakClass = Math.Max(peakClass, h.Prime.Class); }
        bool wasChamp = _everChampion.Contains(b.Id)
                        || ChampOf(b.WeightClass)?.Id == b.Id || WbcOf(b.WeightClass)?.Id == b.Id || IbfOf(b.WeightClass)?.Id == b.Id;
        // The lineal line is not a fourth belt to defend — a Ring defence IS one of the sanctioned defences, so
        // it's tracked for the champions board but must never be double-counted into a career total.
        int defenses = _beltDefenses.Where(kv => kv.Key.Holder == b.Id && kv.Key.Belt != "Ring").Sum(kv => kv.Value);
        int weightTitles = _titleDivisions.TryGetValue(b.Id, out var tds) ? tds.Count : (wasChamp ? 1 : 0);
        // A real champion with a genuine reign (3+ defences) or a multi-weight champion — but only a true top-tier
        // fighter (peakClass floor keeps journeyman champions of a thin division out) — or an outright elite talent.
        // A Hall of Famer needs a real body of work, not a handful of bouts — plus either a genuine title reign,
        // a multi-weight title, or an elite career-long talent.
        int pf = ProFights(b);
        bool worthy = pf >= 15 && ((((wasChamp && defenses >= 3) || weightTitles >= 2) && peakClass >= 8) || (peak >= 88 && pf >= 25));
        if (!worthy) return false;

        _hof.Add(new HallOfFamer
        {
            Id = b.Id, Name = b.Name, Nickname = b.Nickname, Country = b.Country, Division = b.WeightClass,
            Record = b.Record.ToString(), PeakOverall = peak, PeakClass = peakClass, Defenses = defenses, WasChampion = wasChamp,
            WeightTitles = weightTitles, TitleDivisions = tds?.OrderBy(d => (int)d).ToList() ?? new(), Age = b.Age, Year = Date.Year,
            // Snapshot the ledger (drop the heavy per-round grid/commentary) so the Hall keeps his fight history.
            History = b.History.Select(h => new BoutLine
            {
                Date = h.Date, Opponent = h.Opponent, Result = h.Result, Method = h.Method,
                Round = h.Round, KdFor = h.KdFor, KdAgainst = h.KdAgainst, Note = h.Note, Cards = h.Cards
            }).ToList()
        });
        LogEvent($"{b.Name} ({b.Record}) retires and enters the Hall of Fame.", b.Id == Player.Id, kind: "hof", div: b.WeightClass);
        return true;
    }

    /// <summary>Log a completed bout as a candidate for the year-end awards — only fights worth honouring
    /// (a world title bout, two decent men, or a knockout of a decent fighter).</summary>
    private void CaptureBout(FightResult res, Boxer a, Boxer b, string? note, DateOnly on)
    {
        bool title = IsWorldTitleNote(note);
        bool ko = res.Outcome is FightOutcome.Knockout or FightOutcome.TechnicalKnockout;
        int lo = Math.Min(a.Overall, b.Overall);
        if (!title && lo < 66 && !(ko && (res.Loser?.Overall ?? 0) >= 66)) return;
        var w = res.Winner; var l = res.Loser;
        bool close = res.IsDraw || res.Method is "SD" or "MD"
                     || (res.Scorecards.Count > 0 && res.Scorecards.All(c => Math.Abs(c.A - c.B) <= 4));
        _yearBouts.Add(new YearBout(on.Year, on, w?.Name ?? a.Name, l?.Name ?? b.Name, w?.Id ?? a.Id, l?.Id ?? b.Id,
            res.Method, res.EndRound, title, w?.Overall ?? a.Overall, l?.Overall ?? b.Overall,
            res.KnockdownsA + res.KnockdownsB, res.IsDraw, close, (w ?? a).WeightClass, l is not null ? Standing(l) : ""));
    }

    /// <summary>A short description of where a fighter stands — a reigning champion (with defences), a ranked
    /// contender, or nobody in particular — for colour in the award commentary. Kept cheap (no ranking sort).</summary>
    private string Standing(Boxer b)
    {
        var belts = BeltsHeld(b).Where(x => x.Belt is "WBA" or "WBC" or "IBF").ToList();
        if (belts.Count > 0)
        {
            int def = belts.Max(x => x.Defenses);
            return def >= 1 ? $"the reigning champion with {def} defence{(def == 1 ? "" : "s")}" : "the reigning champion";
        }
        return WorldRanked(b) && b.Class >= 8 ? "a top contender" : WorldRanked(b) ? "a ranked contender" : "";
    }

    /// <summary>Expand a method abbreviation into words for award commentary.</summary>
    private static string Long(string method) => method switch
    {
        "KO" => "knockout", "TKO" => "stoppage", "UD" => "a unanimous decision", "SD" => "a split decision",
        "MD" => "a majority decision", "DQ" => "disqualification", "D" => "a draw", _ => method
    };

    /// <summary>Hand out the end-of-year honours (top three per category) from the year's captured bouts.</summary>
    private void ComputeAwardsFor(int year)
    {
        var bouts = _yearBouts.Where(x => x.Year == year).ToList();
        _yearBouts.RemoveAll(x => x.Year <= year);
        if (bouts.Count == 0) return;

        // Fighter of the Year rewards the QUALITY of results — beating high-rated men, winning titles, pulling
        // upsets — not volume, and a loss that year is a heavy negative (a Fighter of the Year rarely loses).
        var acc = new Dictionary<int, FoyAcc>();
        FoyAcc Get(int id, string name, WeightClass div)
        {
            if (!acc.TryGetValue(id, out var a)) acc[id] = a = new FoyAcc { Name = name, Div = div };
            return a;
        }
        foreach (var x in bouts)
        {
            if (x.Draw) continue;
            bool inside = x.Method is "KO" or "TKO";
            var w = Get(x.WinnerId, x.Winner, x.Div);
            w.Score += 6 + x.LoserOvr * 0.4 + (x.Title ? 45 : 0) + Math.Max(0, x.LoserOvr - x.WinnerOvr) * 0.9 + (inside ? 5 : 0);
            w.Wins++; if (x.Title) w.Titles++; if (inside) w.Kos++;
            double q = x.LoserOvr + (x.Title ? 25 : 0);
            if (q > w.BestScore) { w.BestScore = q; w.Best = x; }
            var l = Get(x.LoserId, x.Loser, x.Div);
            l.Score -= 32 + Math.Max(0, x.WinnerOvr - x.LoserOvr) * 0.7 + (x.Title ? 8 : 0);   // a defeat sinks his case
            l.Losses++;
        }
        var foy = acc.Values.Where(a => a.Wins > a.Losses && a.Best is not null)   // must have had a winning year
            .OrderByDescending(a => a.Score).Take(3)
            .Select(a => new AwardWinner { Name = a.Name, Div = a.Div, Bout = a.Best!.Ref,
                Detail = $"{a.Wins}-{a.Losses}{(a.Titles > 0 ? $", {a.Titles} title win{(a.Titles == 1 ? "" : "s")}" : "")}",
                Commentary = $"A standout {year} in {a.Div.DisplayName()} — {a.Wins}-{a.Losses} with {a.Kos} inside the distance{(a.Titles > 0 ? $", including {a.Titles} world-title win{(a.Titles == 1 ? "" : "s")}" : "")}. His best: beating {a.Best!.Loser}{(string.IsNullOrEmpty(a.Best.LoserStanding) ? $" (rated {a.Best.LoserOvr})" : $", {a.Best.LoserStanding},")}{(a.Best.Title ? " for the belt" : "")}." }).ToList();

        var upset = bouts.Where(x => !x.Draw && x.WinnerOvr < x.LoserOvr)
            .OrderByDescending(x => (x.LoserOvr - x.WinnerOvr) + (x.Title ? 15 : 0)).Take(3)
            .Select(x => new AwardWinner { Name = x.Winner, Div = x.Div, Bout = x.Ref,
                Detail = $"beat {x.Loser} ({x.WinnerOvr} vs {x.LoserOvr}){(x.Title ? " · title" : "")}",
                Commentary = $"Nobody saw it coming: {x.Winner} (rated {x.WinnerOvr}) upset {x.Loser}{(string.IsNullOrEmpty(x.LoserStanding) ? $" (rated {x.LoserOvr})" : $", {x.LoserStanding},")} by {Long(x.Method)}{(x.Title ? " to rip away the world title" : "")} in {x.Div.DisplayName()}." }).ToList();

        var ko = bouts.Where(x => x.Method is "KO" or "TKO")
            .OrderByDescending(x => x.LoserOvr + (x.Title ? 12 : 0) + Math.Max(0, 9 - x.Round) * 2 + x.Kds * 3).Take(3)
            .Select(x => new AwardWinner { Name = x.Winner, Div = x.Div, Bout = x.Ref,
                Detail = $"KO{(x.Round > 0 ? $" rd{x.Round}" : "")} {x.Loser}{(x.Title ? " · title" : "")}",
                Commentary = $"{x.Winner} flattened {x.Loser}{(string.IsNullOrEmpty(x.LoserStanding) ? "" : $", {x.LoserStanding},")}{(x.Round > 0 ? $" in round {x.Round}" : "")}{(x.Title ? " in a world-title fight" : "")} — the year's most emphatic knockout in {x.Div.DisplayName()}." }).ToList();

        var foty = bouts.OrderByDescending(x => Math.Min(x.WinnerOvr, x.LoserOvr) + (x.Title ? 15 : 0) + (x.Close ? 12 : 0) + x.Kds * 4).Take(3)
            .Select(x => new AwardWinner { Name = $"{x.Winner} vs {x.Loser}", Div = x.Div, Bout = x.Ref,
                Detail = $"{(x.Draw ? "draw" : x.Method)}{(x.Title ? " · title" : "")}{(x.Kds > 0 ? $" · {x.Kds} KD" : "")}",
                Commentary = $"{x.Winner} and {x.Loser} went to war in {x.Div.DisplayName()}{(x.Title ? " with the world title on the line" : "")}{(x.Kds > 0 ? $", trading {x.Kds} knockdown{(x.Kds == 1 ? "" : "s")}" : "")} — settled by {(x.Draw ? "a draw" : Long(x.Method))}." }).ToList();

        _awards.Add(new AwardsYear { Year = year, FighterOfYear = foy, UpsetOfYear = upset, KnockoutOfYear = ko, FightOfYear = foty });

        // The headline honours crop up in the news feed.
        if (foy.Count > 0) LogEvent($"{year} Fighter of the Year: {foy[0].Name} ({foy[0].Detail}).", foy[0].Name == Player.Name, kind: "award", div: foy[0].Div);
        if (foty.Count > 0) LogEvent($"{year} Fight of the Year: {foty[0].Name}.", false, kind: "award", div: foty[0].Div);
        if (ko.Count > 0) LogEvent($"{year} Knockout of the Year: {ko[0].Name} — {ko[0].Detail}.", ko[0].Name == Player.Name, kind: "award", div: ko[0].Div);
        if (upset.Count > 0) LogEvent($"{year} Upset of the Year: {upset[0].Name} {upset[0].Detail}.", upset[0].Name == Player.Name, kind: "award", div: upset[0].Div);
    }

    /// <summary>Log a title event, tagged with the division being simulated so the news feed can filter it.</summary>
    /// <summary>Log a title event in a named division. The division is an argument: it used to come from
    /// _cursor, an ambient "which weight am I resolving right now" that every caller had to remember to set
    /// and put back — the same shape of fault as the world clock, one field down.</summary>
    private void LogTitle(string text, WeightClass div, BoutRef? bout = null, DateOnly? on = null) =>
        LogEvent(text, kind: "title", div: div, bout: bout, on: on);

    // The ranked order of the player's division, computed at most once a day. The angle below asks for it on
    // every headline in his weight, and re-sorting two hundred men for each one is not free.
    private DateOnly _rankCachedOn = DateOnly.MinValue;
    private List<string> _rankCache = new();

    private List<string> PlayerDivisionOrder()
    {
        if (_rankCachedOn != Date)
        {
            _rankCache = RankingOf(Player.WeightClass, 15).Select(b => b.Name).ToList();
            _rankCachedOn = Date;
        }
        return _rankCache;
    }

    /// <summary>What this result has to do with the player, in a clause.
    ///
    /// The news feed reported the sport accurately and impersonally: "Hector Ramirez beat Nick Furlano (UD)".
    /// Every fact needed to make that matter was already in hand — that Furlano took the player's unbeaten
    /// record two years ago, that Ramirez is now one place behind him, that the belt just changed hands in
    /// his own division — and none of it was said. A list of other men's results is a scoreboard; the same
    /// list with one clause attached is the story going on around him.
    ///
    /// Only the strongest connection is used, and only one, because a headline carrying three of these is
    /// worse than a headline carrying none.</summary>
    private string? PlayerAngle(BoutRef r, WeightClass? div)
    {
        if (Universe is not null || Player.Retired) return null;         // a universe has nobody to care
        if (r.Winner == Player.Name || r.Loser == Player.Name) return null;   // his own night needs no gloss

        // 1. The man he is about to fight. Nothing outranks this.
        if (Offer is { } o && (o.Opponent.Name == r.Winner || o.Opponent.Name == r.Loser))
            return o.Opponent.Name == r.Winner ? "your next opponent, warming up"
                                               : "your next opponent — beaten going in";

        // 2. A man he has been in with. The record already knows how it went.
        foreach (var (name, them) in new[] { (r.Winner, true), (r.Loser, false) })
        {
            var met = Player.History.LastOrDefault(h => h.Opponent == name);
            if (met is null) continue;
            string when = $"in {met.Date.Year}";
            return met.Result switch
            {
                'W' when them => $"you beat him {when}",
                'W'           => $"you beat him {when} too",
                'L' when them => $"the man who beat you {when}",
                'L'           => $"he beat you {when}",
                _             => $"you drew with him {when}",
            };
        }

        // 3. His own division, while he is in it far enough for any of it to be his business.
        if (div == Player.WeightClass && WorldRanked(Player))
        {
            var order = PlayerDivisionOrder();
            int mine = order.IndexOf(Player.Name);
            if (mine < 0) return null;

            // A belt moving in his own weight is the thing he is working toward, whoever it moved between.
            bool titleNight = IsWorldChampion(FindByName(r.Winner) ?? Player);
            if (titleNight) return mine <= 4 ? "the belt you are ranked for" : "the belt at the end of your road";

            int his = order.IndexOf(r.Winner);
            if (his >= 0 && his != mine)
            {
                int gap = his - mine;
                if (gap == 1) return "he is one place behind you";
                if (gap == -1) return "he is one place ahead of you";
                if (gap < 0) return $"he is #{his + 1} — {-gap} places above you";
                return null;   // somebody ranked below him and not next: not news
            }
            // An unranked man is only closing in on him if he is genuinely closing in. Firing this for every
            // winner in the division made half the feed read "he is coming up behind you", which says nothing.
            if (FindByName(r.Winner) is Boxer up && ProFights(up) >= 15 && WinStreak(up) >= 4)
                return "he is coming up behind you";
        }
        return null;
    }

    /// <summary>How to find a just-fought bout again. A draw has no winner to key on, so it gets no link.</summary>
    private BoutRef? RefOf(FightResult res, DateOnly? on = null) =>
        res.Winner is null || res.Loser is null ? null : new BoutRef(res.Winner.Name, res.Loser.Name, on ?? Date);

}
