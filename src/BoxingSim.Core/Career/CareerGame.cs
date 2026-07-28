using BoxingSim.Core.Engine;
using BoxingSim.Core.Generation;
using BoxingSim.Core.League;
using BoxingSim.Core.Model;

namespace BoxingSim.Core.Career;

/// <summary>
/// A single-division career game. You create a fighter and steer their career: take or turn down
/// offers, climb the rankings, win the title. The division lives around you — real fighters debut
/// at their historical years, generated prospects arrive every year, everyone ages on the
/// starter→pre-prime→prime→post-prime→end arc, and matchmaking keeps fighters in their own class
/// (starters vs starters, contenders vs contenders) unless a hot prospect is stepped up.
/// </summary>
public sealed class CareerGame
{
    private readonly Random _rng;
    private readonly BoxerFactory _factory;
    private readonly CareerProgression _careers;
    private readonly FightEngine _engine;
    private readonly NameGenerator _oppNames;

    private readonly List<Boxer> _roster = new();                 // every fighter ever, across all divisions
    private readonly Dictionary<int, (Ratings Prime, int Peak)> _historical = new();
    private readonly List<(int DebutYear, Boxer Proto, int DebutAge, int Peak)> _future = new();
    private readonly List<CareerEvent> _log = new();
    private readonly List<TitleReign> _reigns = new();
    // Successful defences of each belt, keyed by (division, belt slot, current holder) — so a new champion
    // automatically starts at zero. Belt slots are "WBA" (the primary/World belt), "WBC", "IBF".
    private readonly Dictionary<(WeightClass Div, string Belt, int Holder), int> _beltDefenses = new();
    // Hall of Fame + the trackers that decide induction. _everChampion / _peakOverall persist across saves so a
    // fighter who held a belt (or peaked as an elite) in an earlier session still qualifies when he finally retires.
    private readonly List<HallOfFamer> _hof = new();
    private readonly List<AwardsYear> _awards = new();
    private readonly List<YearBout> _yearBouts = new();   // this year's honourable-mention bouts, cleared each year end
    public IReadOnlyList<AwardsYear> Awards => _awards.OrderByDescending(a => a.Year).ToList();
    private sealed record YearBout(int Year, DateOnly Date, string Winner, string Loser, int WinnerId, int LoserId,
                                   string Method, int Round, bool Title, int WinnerOvr, int LoserOvr, int Kds,
                                   bool Draw, bool Close, WeightClass Div, string LoserStanding)
    {
        /// <summary>How to find this night again in either man's record.</summary>
        public BoutRef Ref => new(Winner, Loser, Date);
    }
    private sealed class FoyAcc { public string Name = ""; public WeightClass Div; public double Score; public int Wins, Losses, Titles, Kos; public double BestScore = -1; public YearBout? Best; }
    private readonly HashSet<int> _everChampion = new();
    private readonly Dictionary<int, int> _peakOverall = new();
    private readonly Dictionary<int, int> _peakClass = new();
    private readonly Dictionary<int, HashSet<WeightClass>> _titleDivisions = new();   // id → every division he held a world belt in
    private readonly Dictionary<int, DateOnly> _outUntil = new();   // NPC id → date he's fit again after an injury (KO layoff)

    /// <summary>True if a fighter is fit to be matched — not currently on the shelf recovering from an injury.</summary>
    private bool Available(Boxer b) => !_outUntil.TryGetValue(b.Id, out var d) || Date >= d;

    /// <summary>How well a fighter weathers punishment — the injury model's stand-in for a durable frame.</summary>
    private static int Durability(Ratings r) => (int)Math.Round(r.Chin * 0.5 + r.Heart * 0.3 + r.Conditioning * 0.2);
    public IReadOnlyList<HallOfFamer> HallOfFame => _hof.OrderByDescending(m => m.Prestige).ToList();
    private const string UndisputedBelt = "Undisputed";
    private const int MaxFightsPerYear = 8;   // nobody boxes more than 8 times in a calendar year

    public Boxer Player { get; }

    // Every division runs at once. Belts are held per weight class; the player-facing Champion/WbcChampion
    // views resolve to the player's CURRENT division (which changes if he moves up in weight).
    private readonly Dictionary<WeightClass, Boxer?> _champions = new();   // WBA / "World"
    private readonly Dictionary<WeightClass, Boxer?> _wbc = new();          // WBC (from 1963)
    private readonly Dictionary<WeightClass, Boxer?> _ibf = new();          // IBF (from 1983)
    // The lineal ("Ring") championship: unsanctioned, so it never passes on a relinquishment or a vacant-belt
    // bout. The holder keeps it until he's beaten in the ring, retires, or leaves the division.
    private readonly Dictionary<WeightClass, Boxer?> _lineal = new();

    // The division the world-sim is currently resolving (RunEvent/RunNpcSeason loop over all eight and set
    // this). Cursor-scoped belt accessors let the season logic stay division-agnostic.
    private WeightClass _cursor;
    private Boxer? Champ { get => ChampOf(_cursor); set => _champions[_cursor] = value; }
    private Boxer? Wbc { get => WbcOf(_cursor); set => _wbc[_cursor] = value; }
    private Boxer? Ibf { get => IbfOf(_cursor); set => _ibf[_cursor] = value; }
    private bool CursorUnified => Champ is not null && Wbc is not null && Champ.Id == Wbc.Id;
    private IEnumerable<Boxer> ActiveHere => ActiveIn(_cursor);

    public WeightClass Division => Player.WeightClass;
    public Boxer? Champion { get => _champions.GetValueOrDefault(Division); private set => _champions[Division] = value; }
    public Boxer? WbcChampion { get => _wbc.GetValueOrDefault(Division); private set => _wbc[Division] = value; }
    public Boxer? IbfChampion { get => _ibf.GetValueOrDefault(Division); private set => _ibf[Division] = value; }
    public Boxer? LinealChampion { get => _lineal.GetValueOrDefault(Division); }
    private Boxer? ChampOf(WeightClass wc) => _champions.GetValueOrDefault(wc);
    private Boxer? WbcOf(WeightClass wc) => _wbc.GetValueOrDefault(wc);
    private Boxer? IbfOf(WeightClass wc) => _ibf.GetValueOrDefault(wc);
    /// <summary>The lineal holder, ignoring a stale reference: the line ends the moment he retires or leaves the
    /// division, and a seeded-history warmup can retire a champion without passing through the yearly hooks.</summary>
    private Boxer? LinealOf(WeightClass wc) =>
        _lineal.GetValueOrDefault(wc) is Boxer b && !b.Retired && b.WeightClass == wc ? b : null;
    /// <summary>The Ring began recognising champions in 1922 — before that the lineal title is just "the man".</summary>
    public string LinealBelt => Year >= 1922 ? "Ring" : "Lineal";
    public string PrimaryBelt => Year < 1962 ? "World" : "WBA";
    public bool WbcActive => Year >= 1963;
    public bool IbfActive => Year >= 1983;   // the IBF is the third sanctioning body; no WBO or minor belts
    /// <summary>A division only exists from its founding year (the junior/intermediate classes came later).</summary>
    /// <summary>Whether a division exists in this world at all. Two things can switch one off: the year (a
    /// division cannot run before it was founded), and a universe that was asked for a shorter list. The second
    /// is a real exclusion, not a filter on what is shown - no cards, no seasons, no debuts and no belts happen
    /// outside the chosen divisions, so a one-division universe is one division of boxing and nothing else.</summary>
    private bool DivisionActive(WeightClass wc) =>
        Year >= wc.FoundedYear()
        && (Universe is null || Universe.Divisions.Count == 0 || Universe.Divisions.Contains(wc));
    /// <summary>True when one man holds both world belts in the player's division.</summary>
    public bool Unified => Champion is not null && WbcChampion is not null && Champion.Id == WbcChampion.Id;
    private bool UnifiedIn(WeightClass wc) => ChampOf(wc) is Boxer a && WbcOf(wc) is Boxer b && a.Id == b.Id;
    public DateOnly Date { get; private set; }
    public int Year => Date.Year;
    public FightOffer? Offer { get; private set; }
    public DateOnly OfferDate { get; private set; }
    public Injury? PlayerInjury { get; private set; }   // set while the player is on the shelf recovering
    private int _layoffDays;
    private int _lastTitleShot = -100;   // player's pro-fight count at his last title bout (for a rebuild gap)

    public string DateLabel => Date.ToString("d MMM yyyy");
    public string OfferDateLabel => OfferDate.ToString("d MMM yyyy");

    public IReadOnlyList<CareerEvent> Log => _log;
    public IReadOnlyList<TitleReign> Reigns => _reigns;
    public int TitleDefenses => _reigns.Sum(r => r.Defenses);
    public int DaysAsChampion => _reigns.Sum(r => (r.Lost ?? Date).DayNumber - r.Won.DayNumber);
    // Player-facing views are scoped to the player's current division; the world-sim uses ActiveIn(wc).
    public IEnumerable<Boxer> Active => _roster.Where(b => !b.Retired && b.WeightClass == Division);
    public int ActiveCount => _roster.Count(b => !b.Retired && b.WeightClass == Division);
    public int UniverseSize => _roster.Count(b => b.WeightClass == Division);
    private IEnumerable<Boxer> ActiveIn(WeightClass wc) => _roster.Where(b => !b.Retired && b.WeightClass == wc);

    /// <summary>Set when a universe is driving this world instead of a career. Null in career mode, where the
    /// sim uses its own numbers and nothing here applies.</summary>
    public UniverseSettings? Universe { get; set; }

    // Every bout the world resolves while a universe is watching. Career mode never turns this on, so it costs
    // nothing there; a universe drains it each week to build its cards.
    private List<WorldBout>? _watch;

    /// <summary>Start recording every bout the world resolves, for a universe to read back.</summary>
    public void WatchBouts() => _watch ??= new List<WorldBout>();

    /// <summary>Take everything resolved since the last call.</summary>
    public IReadOnlyList<WorldBout> DrainBouts()
    {
        if (_watch is null) return Array.Empty<WorldBout>();
        var taken = _watch.ToList();
        _watch.Clear();
        return taken;
    }

    /// <summary>Run the world forward with nobody playing it. Career mode steps a fortnight at a time and stops
    /// the moment the player retires; a universe has no player to retire and wants a week at a time, because a
    /// week is what a card is.</summary>
    public void AdvanceWorld(int days = 7)
    {
        var target = Date.AddDays(days);
        while (Date < target)
        {
            var next = Date.AddDays(Math.Min(days, target.DayNumber - Date.DayNumber));
            bool yearTurned = next.Year != Date.Year;
            Date = next;
            if (yearTurned) { ComputeAwardsFor(Date.Year - 1); InjectDebuts(); AgeRetireCrown(); PruneRematches(); StageSuperfights(); }
            RunEvent();
        }
    }
    private static readonly WeightClass[] AllDivisions = WeightClasses.All;

    // ---- read-only views of any division, for the UI's cross-division picture ----
    public IReadOnlyList<WeightClass> Divisions => AllDivisions;
    /// <summary>Divisions that exist in the current year (founded and populated), heaviest first — for the UI.</summary>
    public IReadOnlyList<WeightClass> LiveDivisions => AllDivisions.Where(wc => DivisionActive(wc) && ActiveCountOf(wc) > 0).OrderByDescending(wc => (int)wc).ToList();
    public Boxer? WorldChampionOf(WeightClass wc) => ChampOf(wc);
    public Boxer? WbcChampionOf(WeightClass wc) => WbcOf(wc);
    public Boxer? IbfChampionOf(WeightClass wc) => IbfOf(wc);
    public Boxer? LinealChampionOf(WeightClass wc) => LinealOf(wc);

    /// <summary>Every division's championship picture in one pass — for the champions list. Heaviest first.</summary>
    public IReadOnlyList<DivisionChampions> ChampionsBoard() =>
        LiveDivisions.Select(wc => new DivisionChampions(
            wc,
            ChampOf(wc), DefensesOf(wc, "WBA", ChampOf(wc)?.Id ?? 0),
            WbcActive ? WbcOf(wc) : null, DefensesOf(wc, "WBC", WbcOf(wc)?.Id ?? 0),
            IbfActive ? IbfOf(wc) : null, DefensesOf(wc, "IBF", IbfOf(wc)?.Id ?? 0),
            LinealOf(wc), DefensesOf(wc, "Ring", LinealOf(wc)?.Id ?? 0),
            UndisputedOf(wc))).ToList();

    /// <summary>The man holding every belt going in a division — the true undisputed champion, or null.</summary>
    private Boxer? UndisputedOf(WeightClass wc)
    {
        var a = ChampOf(wc);
        if (a is null) return null;
        if (WbcActive && WbcOf(wc)?.Id != a.Id) return null;
        if (IbfActive && IbfOf(wc)?.Id != a.Id) return null;
        return a;
    }

    private static string BeltSlot(string belt) =>
        belt switch { "WBC" => "WBC", "IBF" => "IBF", "Ring" or "Lineal" => "Ring", _ => "WBA" };
    private void Defended(WeightClass wc, string slot, int holder) =>
        _beltDefenses[(wc, slot, holder)] = _beltDefenses.GetValueOrDefault((wc, slot, holder)) + 1;
    public int DefensesOf(WeightClass wc, string belt, int holderId) => _beltDefenses.GetValueOrDefault((wc, BeltSlot(belt), holderId));

    /// <summary>The world belts a fighter currently holds in his division, with the defence count of each —
    /// for the card's championship line and to show all straps on a unified champion.</summary>
    public IEnumerable<(string Belt, int Defenses)> BeltsHeld(Boxer b)
    {
        var wc = b.WeightClass;
        if (ChampOf(wc)?.Id == b.Id) yield return (PrimaryBelt, DefensesOf(wc, "WBA", b.Id));
        if (WbcOf(wc)?.Id == b.Id) yield return ("WBC", DefensesOf(wc, "WBC", b.Id));
        if (IbfOf(wc)?.Id == b.Id) yield return ("IBF", DefensesOf(wc, "IBF", b.Id));
        if (LinealOf(wc)?.Id == b.Id) yield return (LinealBelt, DefensesOf(wc, "Ring", b.Id));
        foreach (var kv in _regional.Where(kv => kv.Key.Div == wc && kv.Value.Id == b.Id))
            yield return (kv.Key.Region, 0);
    }
    public int ActiveCountOf(WeightClass wc) => _roster.Count(b => !b.Retired && b.WeightClass == wc);

    /// <summary>Every fighter this world has ever held, retired or not — for reading the sport as a whole
    /// rather than one division's standings.</summary>
    public IReadOnlyList<Boxer> EveryFighter => _roster;
    /// <summary>The top world-ranked fighters in a division (for a rankings view).</summary>
    public IReadOnlyList<Boxer> RankingOf(WeightClass wc, int take = 15) =>
        ActiveIn(wc).Where(RankedContender).OrderByDescending(RankScore).Take(take).ToList();

    /// <summary>The division's ranking as a BOARD reads it: the champions first — the convention every real
    /// sanctioning body follows — then the contenders behind them in ranking order. A champion appears whether
    /// or not his record clears the contender bar, because holding the belt is what puts him there.
    /// Matchmaking deliberately keeps using <see cref="RankingOf"/>, which stays ordered purely on merit: who
    /// the sim protects a prospect from shouldn't shift just because a board lists champions differently.</summary>
    public IReadOnlyList<Boxer> RankingBoard(WeightClass wc, int take = 15)
    {
        var champions = ActiveIn(wc).Where(IsWorldChampion).OrderByDescending(RankScore).ToList();
        var champIds = champions.Select(b => b.Id).ToHashSet();
        var contenders = ActiveIn(wc).Where(b => RankedContender(b) && !champIds.Contains(b.Id))
                                     .OrderByDescending(RankScore).ToList();
        var board = champions.Concat(contenders).Take(take).ToList();

        // A board is never half empty. To be a RANKED contender a man needs twenty bouts and a 65% win rate,
        // and there are stretches - a young world, a generation retiring together - where a division of two
        // hundred active fighters has only a handful who clear both. Real bodies rank someone regardless, so
        // the rest of the list is topped up with the best of who is actually there.
        if (board.Count < take)
        {
            var have = board.Select(b => b.Id).ToHashSet();
            board.AddRange(ActiveIn(wc).Where(b => !have.Contains(b.Id) && ProFights(b) >= 8)
                                       .OrderByDescending(RankScore)
                                       .Take(take - board.Count));
        }
        return board;
    }

    /// <summary>True if the fighter currently holds any world belt (WBA/WBC/IBF) in his division.</summary>
    public bool IsWorldChampion(Boxer b) =>
        ChampOf(b.WeightClass)?.Id == b.Id || WbcOf(b.WeightClass)?.Id == b.Id || IbfOf(b.WeightClass)?.Id == b.Id;

    /// <summary>Pound-for-pound: the best fighters across every division, ranked by ability tempered by record.
    /// Reigning world champions are strongly favoured, so the list reads like a real P4P board (champions on top).</summary>
    public IReadOnlyList<Boxer> PoundForPound(int take = 15) =>
        _roster.Where(b => !b.Retired && (RankedContender(b) || IsWorldChampion(b) || LinealOf(b.WeightClass)?.Id == b.Id))
               .OrderByDescending(P4PScore)
               .Take(take).ToList();

    /// <summary>P4P standing is an ACHIEVEMENT board, not a ratings list: what a man has actually won, not how
    /// good he might become. A long reign is the strongest credential there is, then belts held, the lineal title,
    /// titles in more than one division, and world title bouts won. Ability enters only as a tiebreaker between
    /// men with comparable résumés — so a high-rated prospect with a regional strap can't sit above a champion.</summary>
    private double P4PScore(Boxer b)
    {
        double score = 0;

        // What he holds right now. A regional strap is not a P4P credential.
        int belts = 0, bestDef = 0;
        bool lineal = false;
        foreach (var (belt, def) in BeltsHeld(b))
        {
            if (belt is "Ring" or "Lineal") { lineal = true; continue; }
            if (RegionalBelts.Contains(belt)) continue;
            belts++;
            bestDef = Math.Max(bestDef, def);
        }
        if (belts > 0) score += 30 + belts * 10;          // a reigning world champion, more for holding several
        if (lineal) score += 15;                          // the man who beat the man
        score += Math.Min(bestDef, 15) * 4;               // the length of the reign matters most of all

        // Achievement that outlasts the current belt: title bouts won, and belts won in more than one division.
        score += WorldTitleWins(b) * 3;
        int divs = _titleDivisions.TryGetValue(b.Id, out var td) ? td.Count : 0;
        score += Math.Max(0, divs - 1) * 18;              // a two- or three-weight champion

        // Form: staying unbeaten is itself an achievement; defeats undo one.
        int fights = b.Record.Wins + b.Record.Losses + b.Record.Draws;
        double winRate = fights > 0 ? (b.Record.Wins + 0.5 * b.Record.Draws) / fights : 0;
        score += winRate * 20 - b.Record.Losses;

        return score + b.Overall * 0.15;                  // ability breaks ties, nothing more
    }

    /// <summary>World title bouts a fighter has WON, from his ledger — the hard record of what he's achieved.</summary>
    private static int WorldTitleWins(Boxer b) =>
        b.History.Count(h => h.Result == 'W' && IsWorldTitleNote(h.Note));

    /// <summary>A fighter's championship credentials — exactly what the P4P order is built on, so the board can
    /// show its own reasoning instead of a bare "champ" tag.</summary>
    public Achievements AchievementsOf(Boxer b)
    {
        var belts = new List<string>();
        bool lineal = false;
        int defences = 0;
        foreach (var (belt, def) in BeltsHeld(b))
        {
            if (belt is "Ring" or "Lineal") { lineal = true; defences = Math.Max(defences, def); continue; }
            if (RegionalBelts.Contains(belt)) continue;
            belts.Add(belt);
            defences = Math.Max(defences, def);
        }
        int weightTitles = _titleDivisions.TryGetValue(b.Id, out var td) ? td.Count : 0;
        return new Achievements(belts, lineal, UndisputedOf(b.WeightClass)?.Id == b.Id, defences, weightTitles, WorldTitleWins(b));
    }

    /// <summary>The brightest prospects in a division — promising young fighters not yet world-ranked.</summary>
    public IReadOnlyList<Boxer> ProspectsOf(WeightClass wc, int take = 12) =>
        ActiveIn(wc).Where(b => b.Id != Player.Id && IsProspect(b) && ProFights(b) >= 3)
                    .OrderByDescending(b => b.Potential).ThenByDescending(b => b.Overall)
                    .Take(take).ToList();
    public IReadOnlyList<CareerEvent> RecentLog(int n) => _log.Skip(Math.Max(0, _log.Count - n)).ToList();

    public CareerGame(int startYear, Boxer player, IEnumerable<Boxer> historicalProtos, Random rng,
                      WeightClass division = WeightClass.Heavyweight, int warmupYears = 10, bool seedHistory = false,
                      UniverseSettings? universe = null)
    {
        // Assigned before anything else: the seeding and the warm-up years below both read it, so a universe
        // set afterwards would build a world under the sim's rules and only then change them.
        Universe = universe;
        _rng = rng;
        _factory = new BoxerFactory(rng);
        _careers = new CareerProgression(rng);
        _engine = new FightEngine(rng);
        _oppNames = new NameGenerator(rng);
        Player = player;

        // Reserve every real fighter's name (and the player's) so generated filler can never be born as a
        // second "Carlos Ortiz". Materialise the protos once since we enumerate them again when seeding.
        var protos = historicalProtos as IReadOnlyList<Boxer> ?? historicalProtos.ToList();
        var reserved = protos.Select(p => p.Name).Append(player.Name).ToList();
        _factory.Reserve(reserved);
        _oppNames.Reserve(reserved);
        // Generated fighters must get ids above every historical fighter (and the player) — both id spaces
        // otherwise start at 1, and a collision makes the engine treat filler as a historical great (and mixes
        // every id-keyed map: _historical, _peakOverall, belts, ...).
        _factory.StartIdsAt(protos.Select(p => p.Id).Append(player.Id).DefaultIfEmpty(0).Max() + 1);

        // Stand the whole sport up a decade earlier and let all eight divisions run, so that by the
        // player's debut year everyone has a real record, the rankings have settled and there are
        // established champions in every weight class.
        warmupYears = Math.Max(0, warmupYears);
        if (seedHistory) warmupYears = Math.Clamp(startYear - 1898, 30, 65);   // run most of the sport's history for a full Hall of Fame (opt-in accepts a longer setup)
        int seedYear = startYear - warmupYears;
        Date = new DateOnly(seedYear, 1, 1);

        // Seed each division that exists yet with a base of journeymen, alongside the real fighters.
        foreach (var wc in AllDivisions)
            if (DivisionActive(wc))
                for (int i = 0; i < 24; i++) AddActive(_factory.CreateExisting(wc, GeneratedCap));

        foreach (var proto in protos)
        {
            if (proto.DebutYear is not int debutYear) continue;
            if (Universe is { Divisions.Count: > 0 } uni && !uni.Divisions.Contains(proto.WeightClass)) continue;
            int birth = FirstYear(proto.DateOfBirth);
            int debutAge = birth > 0 ? Math.Clamp(debutYear - birth, 16, 30) : 19;
            int peak = PeakOf(proto, birth);

            if (debutYear > seedYear)
            {
                _future.Add((debutYear, proto, debutAge, peak));
            }
            else
            {
                int ageNow = debutAge + (seedYear - debutYear);
                if (ageNow > 39) continue;                         // already retired before the seed year
                InjectHistorical(proto, ageNow, debutAge, peak, announce: false);
            }
        }

        foreach (var wc in AllDivisions)
        {
            if (!DivisionActive(wc)) continue;
            var champ = ActiveIn(wc).OrderByDescending(b => RankScore(b)).FirstOrDefault();
            if (champ is not null) { _champions[wc] = champ; champ.IsChampion = true; }
        }

        // Run the world forward to the player's debut year — a full season each year.
        for (int y = seedYear; y < startYear; y++)
        {
            Date = new DateOnly(y, 1, 1);
            InjectDebuts();
            RunNpcSeason();
            AgeRetireCrown();
            ComputeAwardsFor(y);
        }
        Date = new DateOnly(startYear, 1, 1);
        InjectDebuts();
        // A division founded exactly in the start year is seeded and crowned now (the yearly loop stopped at
        // startYear-1), so it opens with a real champion rather than sitting vacant on day one.
        SeedNewlyFoundedDivisions();
        foreach (var wc in AllDivisions)
        {
            if (!DivisionActive(wc)) continue;
            if (ChampOf(wc) is null || ChampOf(wc)!.Retired)
            {
                var champ = ActiveIn(wc).Where(b => b.Id != Player.Id && !RecentlyMovedUp(b)).OrderByDescending(RankScore).FirstOrDefault();
                if (champ is not null) { _champions[wc] = champ; champ.IsChampion = true; }
            }
            UpdateBeltsFor(wc);
        }

        // The decade of build-up isn't the player's story — start his timeline (and the Hall of Fame) clean, so
        // the Hall fills with fighters who retire during his career rather than a generation he never saw.
        _log.Clear();
        _yearBouts.Clear();
        if (!seedHistory) { _hof.Clear(); _awards.Clear(); }   // when seeding history, keep the past greats + their era's awards
        Date = new DateOnly(startYear, 3, 1);
        if (Champion is not null) LogEvent($"{Champion.Name} reigns as {PrimaryBelt} champion as {player.Name} turns pro.", kind: "title");

        Player.WeightClass = division;
        AddActive(Player);
        CapStarter(Player);
        Offer = BuildOffer();
    }

    /// <summary>Rehydrate a saved career.</summary>
    private CareerGame(CareerSave s, Random rng)
    {
        _rng = rng;
        _factory = new BoxerFactory(rng);
        _careers = new CareerProgression(rng);
        _engine = new FightEngine(rng);
        _oppNames = new NameGenerator(rng);
        Date = ParseDate(s.Date, new DateOnly(2000, 1, 1));

        var byId = new Dictionary<int, Boxer>();
        foreach (var bs in s.Roster) { var b = bs.ToBoxer(); _roster.Add(b); byId[b.Id] = b; }
        Player = byId[s.PlayerId];
        // Don't let fighters generated during continued play collide with anyone already on the roster.
        var reserved = _roster.Select(b => b.Name).ToList();
        _factory.Reserve(reserved);
        _oppNames.Reserve(reserved);
        _factory.StartIdsAt(_roster.Select(b => b.Id).Append(Player.Id).DefaultIfEmpty(0).Max() + 1);
        _cursor = Player.WeightClass;
        foreach (var kv in s.Champions) if (Enum.TryParse<WeightClass>(kv.Key, out var wc) && byId.TryGetValue(kv.Value, out var c)) _champions[wc] = c;
        foreach (var kv in s.WbcChampions) if (Enum.TryParse<WeightClass>(kv.Key, out var wc) && byId.TryGetValue(kv.Value, out var c)) _wbc[wc] = c;
        foreach (var kv in s.IbfChampions) if (Enum.TryParse<WeightClass>(kv.Key, out var wc) && byId.TryGetValue(kv.Value, out var c)) _ibf[wc] = c;
        foreach (var kv in s.LinealChampions) if (Enum.TryParse<WeightClass>(kv.Key, out var wc) && byId.TryGetValue(kv.Value, out var c)) _lineal[wc] = c;
        _lastTitleShot = s.LastTitleShot;
        foreach (var h in s.Historical) _historical[h.Id] = (h.Prime.ToRatings(), h.Peak);
        foreach (var a in s.PlayerArc) _playerArc.Add((a.Fights, a.Age, a.R.ToRatings()));
        foreach (var f in s.Future) _future.Add((f.DebutYear, f.Proto.ToBoxer(), f.DebutAge, f.Peak));
        foreach (var e in s.Log)
        {
            var on = ParseDate(e.On, Date);
            _log.Add(new CareerEvent
            {
                On = on, Text = e.Text, PlayerBout = e.PlayerBout, Kind = e.Kind,
                Div = Enum.TryParse<WeightClass>(e.Div, out var ed) ? ed : null,
                Bout = e.BoutWinner is not null && e.BoutLoser is not null
                       ? new BoutRef(e.BoutWinner, e.BoutLoser, on) : null
            });
        }
        foreach (var r in s.Reigns) _reigns.Add(new TitleReign { Belt = r.Belt, Won = ParseDate(r.Won, Date), Lost = string.IsNullOrEmpty(r.Lost) ? null : ParseDate(r.Lost, Date), Defenses = r.Defenses });
        foreach (var kv in s.Regional)
        {
            var parts = kv.Key.Split('|');
            if (parts.Length == 2 && Enum.TryParse<WeightClass>(parts[0], out var wc) && byId.TryGetValue(kv.Value, out var rb))
                _regional[(wc, parts[1])] = rb;
        }
        foreach (var m in s.HallOfFame)
            _hof.Add(new HallOfFamer
            {
                Id = m.Id, Name = m.Name, Nickname = m.Nickname, Country = m.Country,
                Division = Enum.TryParse<WeightClass>(m.Division, out var md) ? md : WeightClass.Heavyweight,
                Record = m.Record, PeakOverall = m.PeakOverall, PeakClass = m.PeakClass, Defenses = m.Defenses, WasChampion = m.WasChampion, WeightTitles = m.WeightTitles,
                TitleDivisions = m.TitleDivisions.Select(s => Enum.TryParse<WeightClass>(s, out var d) ? (WeightClass?)d : null).Where(x => x is not null).Select(x => x!.Value).ToList(),
                Age = m.Age, Year = m.Year,
                History = m.History.Select(h => new BoutLine
                {
                    Date = ParseDate(h.Date, Date), Opponent = h.Opponent, Result = h.Result.Length > 0 ? h.Result[0] : 'D',
                    Method = h.Method, Round = h.Round, KdFor = h.KdFor, KdAgainst = h.KdAgainst, Note = h.Note, Cards = h.Cards
                }).ToList()
            });
        AwardWinner AwLoad(AwardWinnerSave w) => new()
        {
            Name = w.Name, Detail = w.Detail,
            Div = Enum.TryParse<WeightClass>(w.Div, out var wd) ? wd : WeightClass.Heavyweight,
            Commentary = w.Commentary,
            // Saves written before awards carried their fight simply have none; the citation still reads.
            Bout = w.BoutWinner is not null && w.BoutLoser is not null && DateOnly.TryParse(w.BoutDate, out var bd)
                   ? new BoutRef(w.BoutWinner, w.BoutLoser, bd) : null
        };
        foreach (var a in s.Awards) _awards.Add(new AwardsYear
        {
            Year = a.Year,
            FighterOfYear = a.FighterOfYear.Select(AwLoad).ToList(),
            UpsetOfYear = a.UpsetOfYear.Select(AwLoad).ToList(),
            KnockoutOfYear = a.KnockoutOfYear.Select(AwLoad).ToList(),
            FightOfYear = a.FightOfYear.Select(AwLoad).ToList(),
        });
        foreach (var id in s.EverChampion) _everChampion.Add(id);
        foreach (var kv in s.PeakOverall) if (int.TryParse(kv.Key, out var id)) _peakOverall[id] = kv.Value;
        foreach (var kv in s.PeakClass) if (int.TryParse(kv.Key, out var id)) _peakClass[id] = kv.Value;
        foreach (var kv in s.TitleDivisions)
            if (int.TryParse(kv.Key, out var id))
                _titleDivisions[id] = kv.Value.Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => Enum.TryParse<WeightClass>(x, out var w) ? (WeightClass?)w : null)
                    .Where(w => w is not null).Select(w => w!.Value).ToHashSet();
        foreach (var kv in s.BeltDefenses)
        {
            var parts = kv.Key.Split('|');
            if (parts.Length == 3 && Enum.TryParse<WeightClass>(parts[0], out var wc) && int.TryParse(parts[2], out var hid))
                _beltDefenses[(wc, parts[1], hid)] = kv.Value;
        }

        OfferDate = ParseDate(s.OfferDate, Date.AddDays(42));
        if (s.Offer is OfferSave o && byId.TryGetValue(o.OpponentId, out var opp))
            Offer = new FightOffer { Opponent = opp, Rounds = o.Rounds, TitleFight = o.TitleFight, Belt = o.Belt, Context = o.Context };
        else
            Offer = Player.Retired ? null : BuildOffer();
    }

    public static CareerGame Load(CareerSave save, Random rng) => new(save, rng);

    /// <summary>Snapshot the whole career for serialization.</summary>
    public CareerSave ToSave()
    {
        var s = new CareerSave
        {
            Division = Division, Date = Date.ToString("yyyy-MM-dd"), OfferDate = OfferDate.ToString("yyyy-MM-dd"),
            PlayerId = Player.Id, LastTitleShot = _lastTitleShot
        };
        foreach (var kv in _champions) if (kv.Value is Boxer c) s.Champions[kv.Key.ToString()] = c.Id;
        foreach (var kv in _wbc) if (kv.Value is Boxer c) s.WbcChampions[kv.Key.ToString()] = c.Id;
        foreach (var kv in _ibf) if (kv.Value is Boxer c) s.IbfChampions[kv.Key.ToString()] = c.Id;
        foreach (var kv in _lineal) if (kv.Value is Boxer c) s.LinealChampions[kv.Key.ToString()] = c.Id;
        // Keep the save lean: only active fighters are persisted (retired journeymen across eight divisions
        // would balloon localStorage). The fight ledger is kept for the player and for anyone above class 9
        // (the contenders and champions you'd actually inspect); their bouts drop the heavy round-by-round
        // grid though. Everyone below class 10 keeps just their record.
        foreach (var b in _roster)
        {
            if (b.Retired && b.Id != Player.Id) continue;
            var bs = BoxerSave.From(b);
            if (b.Id != Player.Id)
            {
                if (b.Class >= 10)
                    foreach (var h in bs.History) { h.Rounds = null; h.Commentary = null; }
                else
                    bs.History.Clear();
            }
            s.Roster.Add(bs);
        }
        foreach (var kv in _historical) s.Historical.Add(new HistoricalSave { Id = kv.Key, Peak = kv.Value.Peak, Prime = RatingsSave.From(kv.Value.Prime) });
        foreach (var a in _playerArc) s.PlayerArc.Add(new ArcPointSave { Fights = a.Fights, Age = a.Age, R = RatingsSave.From(a.R) });
        foreach (var f in _future) s.Future.Add(new FutureSave { DebutYear = f.DebutYear, DebutAge = f.DebutAge, Peak = f.Peak, Proto = BoxerSave.From(f.Proto) });
        foreach (var e in _log) s.Log.Add(new CareerEventSave { On = e.On.ToString("yyyy-MM-dd"), Text = e.Text, PlayerBout = e.PlayerBout, Kind = e.Kind, Div = e.Div?.ToString(), BoutWinner = e.Bout?.Winner, BoutLoser = e.Bout?.Loser });
        foreach (var r in _reigns) s.Reigns.Add(new TitleReignSave { Belt = r.Belt, Won = r.Won.ToString("yyyy-MM-dd"), Lost = r.Lost?.ToString("yyyy-MM-dd"), Defenses = r.Defenses });
        foreach (var kv in _regional) s.Regional[$"{kv.Key.Div}|{kv.Key.Region}"] = kv.Value.Id;
        foreach (var m in _hof) s.HallOfFame.Add(new HallOfFamerSave
        {
            Id = m.Id, Name = m.Name, Nickname = m.Nickname, Country = m.Country, Division = m.Division.ToString(),
            Record = m.Record, PeakOverall = m.PeakOverall, PeakClass = m.PeakClass, Defenses = m.Defenses,
            WasChampion = m.WasChampion, WeightTitles = m.WeightTitles, TitleDivisions = m.TitleDivisions.Select(d => d.ToString()).ToList(), Age = m.Age, Year = m.Year,
            History = m.History.Select(h => new BoutLineSave
            {
                Date = h.Date.ToString("yyyy-MM-dd"), Opponent = h.Opponent, Result = h.Result.ToString(),
                Method = h.Method, Round = h.Round, KdFor = h.KdFor, KdAgainst = h.KdAgainst, Note = h.Note, Cards = h.Cards
            }).ToList()
        });
        AwardWinnerSave AwSave(AwardWinner w) => new()
        {
            Name = w.Name, Detail = w.Detail, Div = w.Div.ToString(), Commentary = w.Commentary,
            BoutWinner = w.Bout?.Winner, BoutLoser = w.Bout?.Loser,
            BoutDate = w.Bout?.Date.ToString("yyyy-MM-dd")
        };
        foreach (var a in _awards) s.Awards.Add(new AwardsYearSave
        {
            Year = a.Year,
            FighterOfYear = a.FighterOfYear.Select(AwSave).ToList(),
            UpsetOfYear = a.UpsetOfYear.Select(AwSave).ToList(),
            KnockoutOfYear = a.KnockoutOfYear.Select(AwSave).ToList(),
            FightOfYear = a.FightOfYear.Select(AwSave).ToList(),
        });
        s.EverChampion.AddRange(_everChampion);
        foreach (var kv in _peakOverall) s.PeakOverall[kv.Key.ToString()] = kv.Value;
        foreach (var kv in _peakClass) s.PeakClass[kv.Key.ToString()] = kv.Value;
        foreach (var kv in _titleDivisions) s.TitleDivisions[kv.Key.ToString()] = string.Join("|", kv.Value);
        foreach (var kv in _beltDefenses) s.BeltDefenses[$"{kv.Key.Div}|{kv.Key.Belt}|{kv.Key.Holder}"] = kv.Value;
        if (Offer is not null) s.Offer = new OfferSave { OpponentId = Offer.Opponent.Id, Rounds = Offer.Rounds, TitleFight = Offer.TitleFight, Belt = Offer.Belt, Context = Offer.Context };
        return s;
    }

    // ---- player turn ----

    /// <summary>Take the current offer: the calendar rolls forward to fight night, then the bout is fought.</summary>
    public FightResult? TakeOffer()
    {
        if (Offer is null || Player.Retired) return null;
        PlayerInjury = null;                         // he's fit again by fight night
        AdvanceTo(OfferDate);                       // run the world's fortnightly cards up to fight night
        if (Player.Retired) { Offer = null; return null; }

        var opp = Offer.Opponent;
        string? belt = Offer.Belt;
        string? note = belt is not null ? $"{belt} title"
                     : Offer.Context is "eliminator" ? "eliminator"
                     : Offer.Context.StartsWith("rematch") ? "rematch"
                     : null;
        var res = _engine.Simulate(Player, opp, Offer.Rounds);
        _declined.Clear();
        ClearRematch(Player, opp);          // whatever it was, it has now been settled once more
        ApplyOutcome(res, Player, opp, note);

        string verb = res.IsDraw ? "drew with" : (res.Winner!.Id == Player.Id ? "beat" : "lost to");
        string how = res.IsDraw ? Offer.Rounds + "-round draw" : res.Method;
        LogEvent($"{Player.Name} {verb} {opp.Name} ({how}){(belt is not null ? $" — {belt} TITLE" : "")}", playerBout: true,
                 bout: res.Winner is null ? null : new BoutRef(res.Winner.Name, res.Loser!.Name, Date));

        if (belt == UndisputedBelt && !res.IsDraw)
        {
            // Both world belts rode on this one. Win = defend both; loss = the challenger takes the lot.
            bool playerWon = res.Winner!.Id == Player.Id;
            if (playerWon)
            {
                Defended(Player.WeightClass, "WBA", Player.Id); Defended(Player.WeightClass, "WBC", Player.Id);
                foreach (var bl in new[] { PrimaryBelt, "WBC" }) { var r = OpenReign(bl); if (r is not null) r.Defenses++; }
            }
            else
            {
                SetBeltHolder(PrimaryBelt, opp); SetBeltHolder("WBC", opp);
                foreach (var bl in new[] { PrimaryBelt, "WBC" }) { var r = OpenReign(bl); if (r is not null) r.Lost = Date; }
                LogEvent($"{Player.Name} loses the unified {PrimaryBelt} and WBC titles to {opp.Name}.", true);
            }
        }
        else if (belt is not null && !res.IsDraw)
        {
            bool playerWon = res.Winner!.Id == Player.Id;
            bool held = PlayerHolds(belt);
            if (playerWon && !held)
            {
                SetBeltHolder(belt, Player);
                _reigns.Add(new TitleReign { Belt = belt, Won = Date });
                LogEvent($"{Player.Name} WINS THE {belt} TITLE, beating {opp.Name}!", true);
            }
            else if (playerWon && held)
            {
                Defended(Player.WeightClass, BeltSlot(belt), Player.Id);
                var r = OpenReign(belt); if (r is not null) r.Defenses++;   // successful defence
            }
            else if (!playerWon && held)
            {
                SetBeltHolder(belt, opp);
                var r = OpenReign(belt); if (r is not null) r.Lost = Date;
                LogEvent($"{Player.Name} loses the {belt} title to {opp.Name}.", true);
            }
        }
        if (belt is not null) _lastTitleShot = ProFights(Player);   // start the rebuild clock before the next title bout

        // A serious injury keeps him on the shelf — his next fight is pushed out to recovery.
        var inj = res.Injuries.Where(i => i.Name == Player.Name).OrderByDescending(i => i.LayoffDays).FirstOrDefault();
        if (inj is not null)
        {
            LogEvent($"{Player.Name} suffered {inj.Type} — {LayoffText(inj.LayoffDays)}.", playerBout: true);
            if (inj.Retires) { Player.Retired = true; LogEvent($"{Player.Name} is forced to retire on medical advice.", playerBout: true); }
            else { PlayerInjury = inj; _layoffDays = inj.LayoffDays; }
        }

        Offer = Player.Retired ? null : BuildOffer();
        return res;
    }

    /// <summary>Turn the offer down and wait — the calendar still moves and a new offer comes in.</summary>
    /// <summary>Turn a fight down. The man you passed on is remembered, so holding out gets you a DIFFERENT
    /// name rather than the same one again: a fighter who has said no does not keep getting the same offer
    /// from the same matchmaker week after week.</summary>
    public void DeclineOffer()
    {
        if (Player.Retired) return;
        if (Offer is { } turned)
        {
            _declined.Add(turned.Opponent.Id);
            while (_declined.Count > 4) _declined.RemoveAt(0);   // he comes back round eventually
        }
        AdvanceTo(Date.AddDays(21 + _rng.Next(21)));
        Offer = Player.Retired ? null : BuildOffer();
    }

    // Men the player has recently turned down. Cleared when he actually takes a fight - once he is boxing
    // again the matchmaker has no reason to keep steering round them.
    private readonly List<int> _declined = new();

    /// <summary>Give up the WBC belt rather than defend it — the senior belt (and that reign) stays intact.
    /// Only meaningful for a unified champion; the vacant WBC passes to the leading contender.</summary>
    public void RelinquishWbc()
    {
        if (WbcChampion?.Id != Player.Id) return;
        var r = OpenReign("WBC"); if (r is not null) r.Lost = Date;
        WbcChampion = null;
        LogEvent($"{Player.Name} relinquishes the WBC title.", true);
        UpdateBeltsFor(Division);
        if (!Player.Retired) Offer = BuildOffer();   // the offer is no longer a unified defence
    }

    /// <summary>The division the player could move up to (null at heavyweight; skips not-yet-founded classes).</summary>
    public WeightClass? NextDivision => NextActiveUp(Player.WeightClass);
    public bool CanMoveUp => NextDivision is not null && !Player.Retired;

    /// <summary>Campaign up a weight: keep the record, rebalance for bigger men, vacate belts and reigns in
    /// the old division, and start fresh (unranked) in the new one.</summary>
    public void MoveUp()
    {
        if (NextDivision is not WeightClass to || Player.Retired) return;
        var from = Player.WeightClass;
        foreach (var r in _reigns.Where(r => r.Lost is null)) r.Lost = Date;   // old-division reigns end
        MoveUpTo(Player, to);
        Player.IsChampion = false;
        _lastTitleShot = -100;
        UpdateBeltsFor(from);   // the belts he vacated pass on
        LogEvent($"{Player.Name} moves up to the {to.DisplayName()} division.", true, kind: "title");
        Offer = BuildOffer();
    }

    // ---- world calendar ----

    /// <summary>Roll the calendar forward to a date, running a fight card every fortnight and doing the
    /// yearly bookkeeping (debuts, aging, retirements) each time the year turns over.</summary>
    private void AdvanceTo(DateOnly target)
    {
        while (Date < target)
        {
            var next = Date.AddDays(14);
            if (next > target) next = target;
            bool yearTurned = next.Year != Date.Year;
            Date = next;
            if (yearTurned) { ComputeAwardsFor(Date.Year - 1); InjectDebuts(); AgeRetireCrown(); PruneRematches(); StageSuperfights(); }
            RunEvent();
            if (Player.Retired) return;
        }
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
            for (int i = 0; i < debuts; i++) AddActive(_factory.CreateProspect(wc, GeneratedCap));
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
        bool Eligible(Boxer b) => (b.Id != Player.Id || Player.IsChampion) && !exclude.Contains(b.Id) && !RecentlyMovedUp(b) && Available(b);
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

        var (savedDate, savedCursor) = (Date, _cursor);
        _cursor = wc;
        Date = SpreadDateFrom(Date);
        var res = FastBout(field[0], field[1], 12);
        ApplyOutcome(res, field[0], field[1], $"{belt} title");
        var winner = res.IsDraw ? field[0] : res.Winner!;   // a draw leaves the belt with the higher-ranked man
        // Announce it HERE, while the clock still reads fight night. Reporting it after the restore below stamped
        // the headline with the caller's date — so "wins the vacant title" could appear months before the bout
        // that decided it, which is the same broken ordering read as a bug in the news feed.
        _everChampion.Add(winner.Id);
        LogEvent($"{winner.Name} wins the vacant {belt} title.", winner.Id == Player.Id, kind: "title", div: wc);
        (Date, _cursor) = (savedDate, savedCursor);          // don't disturb the caller's clock/cursor
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
    private void CaptureBout(FightResult res, Boxer a, Boxer b, string? note)
    {
        bool title = IsWorldTitleNote(note);
        bool ko = res.Outcome is FightOutcome.Knockout or FightOutcome.TechnicalKnockout;
        int lo = Math.Min(a.Overall, b.Overall);
        if (!title && lo < 66 && !(ko && (res.Loser?.Overall ?? 0) >= 66)) return;
        var w = res.Winner; var l = res.Loser;
        bool close = res.IsDraw || res.Method is "SD" or "MD"
                     || (res.Scorecards.Count > 0 && res.Scorecards.All(c => Math.Abs(c.A - c.B) <= 4));
        _yearBouts.Add(new YearBout(Date.Year, Date, w?.Name ?? a.Name, l?.Name ?? b.Name, w?.Id ?? a.Id, l?.Id ?? b.Id,
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
    private void LogTitle(string text, BoutRef? bout = null) => LogEvent(text, kind: "title", div: _cursor, bout: bout);

    /// <summary>How to find a just-fought bout again. A draw has no winner to key on, so it gets no link.</summary>
    private BoutRef? RefOf(FightResult res) =>
        res.Winner is null || res.Loser is null ? null : new BoutRef(res.Winner.Name, res.Loser.Name, Date);

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

    /// <summary>Can this fighter move up to <paramref name="to"/>? Real fighters never climb past the top weight
    /// they actually campaigned at; generated fighters (and multi-weight greats below their ceiling) can.</summary>
    private bool StepUpAllowed(Boxer b, WeightClass to)
    {
        // A real fighter with a documented ceiling never climbs past the top weight he actually campaigned at.
        if (_historical.ContainsKey(b.Id) && b.TopWeight is WeightClass top) return (int)to <= (int)top;
        // Otherwise he can thicken out and chase belts up the scale, but only so far: two divisions above where
        // he started is already a rare career (a three-weight champion). A welterweight has no business ending
        // up at heavyweight — and if he does, the division's ratings are nonsense.
        return (int)to - (int)(b.DebutWeight ?? b.WeightClass) <= 2;
    }

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
        if (_rng.NextDouble() < p * greatness) _stepUpQueued.Add(b.Id);
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
        var pool = ActiveHere.Where(b => b.Id != Player.Id && b.Id != Champ?.Id && b.Id != Wbc?.Id && !AtYearCap(b) && Available(b))
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
            var pool = ActiveHere.Where(b => b.Id != Player.Id && b.Id != Champ?.Id && b.Id != Wbc?.Id && !AtYearCap(b) && Available(b)
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
        var heavier = (int)a.WeightClass >= (int)b.WeightClass ? a : b;
        _cursor = heavier.WeightClass;
        Date = SpreadDate(Date.Year, 1 + _rng.Next(4), 6);
        var res = FastBout(a, b, 12);
        ApplyOutcome(res, a, b, "superfight");
        ReportBout(res);
        LogEvent(res.IsDraw
                    ? $"{a.Name} and {b.Name} draw the superfight — the two best in the world settle nothing."
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

    // ---- matchmaking for the player ----

    private FightOffer BuildOffer()
    {
        int gap = Math.Max(DaysForFights(ProFights(Player)), _layoffDays);   // recovery pushes the next bout out
        // A world champion fights on a title schedule — long camps, ~3 defences a year — not a
        // club-show frequency, however young he is.
        // A champion fights on a title schedule; a former champion still picks his nights. Both wait longer
        // between bouts than a contender does.
        if (Player.IsChampion || WbcChampion?.Id == Player.Id || IbfChampion?.Id == Player.Id)
            gap = Math.Max((int)Math.Round(gap * 1.35), (int)Math.Round(112 * (0.5 + _rng.NextDouble())));
        else if (_everChampion.Contains(Player.Id))
            gap = (int)Math.Round(gap * 1.18);
        _layoffDays = 0;
        OfferDate = Date.AddDays(gap);
        // Hard cap: no more than 8 bouts in a calendar year. Once he's had his eight, the next one waits for the new year.
        if (FightsThisYear(Player) >= MaxFightsPerYear && OfferDate.Year == Date.Year)
            OfferDate = new DateOnly(Date.Year + 1, 1, 12 + _rng.Next(24));
        var ranked = Active.OrderByDescending(b => RankScore(b)).ToList();   // index 0 = strongest
        int idx = ranked.FindIndex(b => b.Id == Player.Id);
        if (ranked.Count <= 1) return new FightOffer { Opponent = _factory.CreateProspect(Player.WeightClass, GeneratedCap), Rounds = 6, Context = "stay-busy" };

        int proFights = ProFights(Player);
        // Build a career properly: journeyman fodder (class 1–3) for the first stretch, then a step-up phase that
        // MIXES gatekeepers with journeyman tune-ups (a stepping-up prospect still stays busy against tomato cans
        // between the tougher fights), and only the elite once he's served his apprenticeship (short for a wonder
        // kid). So a green fighter spends a dozen bouts on tomato cans before real opposition, like a real prospect.
        // The ceiling RAMPS with experience and never jumps straight to "anyone". Graduating the apprenticeship
        // earns a prospect real contenders, not an all-time great: a 14-fight novice offered a class-14 champion
        // isn't a step up, it's a mismatch. (Title shots bypass this cap entirely — they're earned by ranking.)
        int maxOvr = !ReadyForContenders(Player)
                   ? (proFights < 12 ? 55
                      : _rng.Next(2) == 0 ? 55                           // ~half the step-up bouts are jman tune-ups
                                          : Math.Min(78, 55 + (proFights - 12) * 3))   // gatekeeper, not contender
                   : Math.Min(99, 70 + (proFights - ContenderApprenticeship(Player)) * 3);

        bool holdsWba = Player.IsChampion;
        bool holdsWbc = WbcChampion?.Id == Player.Id;
        bool holdsIbf = IbfChampion?.Id == Player.Id;

        // Defend a belt I already hold. Holding both senior belts means a single unified defence.
        if (holdsWba && holdsWbc)
        {
            var chall = PickChallenger(Player, null)   // a top-10 contender he hasn't just fought
                     ?? ranked.FirstOrDefault(b => b.Id != Player.Id);
            if (chall is not null)
                return new FightOffer { Opponent = chall, Rounds = 12, TitleFight = true, Belt = UndisputedBelt, Context = "undisputed title defence" };
        }
        else if (holdsWba || holdsWbc || holdsIbf)
        {
            string belt = holdsWba ? PrimaryBelt : holdsWbc ? "WBC" : "IBF";
            var chall = PickChallenger(Player, null)   // a top-10 contender he hasn't just fought
                     ?? ranked.FirstOrDefault(b => b.Id != Player.Id);
            if (chall is not null)
                return new FightOffer { Opponent = chall, Rounds = 12, TitleFight = true, Belt = belt, Context = $"{belt} title defence" };
        }

        // The fight he is owed, or owes. A draw, a split card, a cut that stopped it, a night he was beaten by
        // somebody nobody rated - the public wants it settled, and it is offered to him before the matchmaker
        // goes back to building his record. He can still turn it down; it stays wanted.
        if (RematchFoeFor(Player) is Boxer again)
        {
            // A rematch with a champion is for the belt - it is the same fight, and the belt was on it.
            string? belt = Champion?.Id == again.Id ? PrimaryBelt
                         : WbcChampion?.Id == again.Id ? "WBC"
                         : IbfChampion?.Id == again.Id ? "IBF" : null;
            return new FightOffer
            {
                Opponent = again,
                Rounds = belt is not null || WorldRanked(again) ? 12 : 10,
                TitleFight = belt is not null,
                Belt = belt,
                Context = $"rematch — {RematchWhy(Player, again)}"
            };
        }

        // A title shot is earned by being world-ranked (top 5) with a real body of work behind you:
        // an exceptional talent breaks in after 20 fights, an ordinary fighter needs 24. (Champions are
        // 80+, so this never lets a novice near the belt inside his first 20 bouts.)
        int fightsToRank = ContenderApprenticeship(Player) switch { 12 => 14, 15 => 17, _ => Player.Potential >= 85 ? 20 : 24 };
        // After a title bout, rebuild with a few wins before the next shot — no back-to-back challenges.
        bool titleCooldownOk = proFights - _lastTitleShot >= 3;
        if (idx >= 0 && idx <= 4 && proFights >= fightsToRank && titleCooldownOk && !RecentlyMovedUp(Player))
        {
            if (!holdsWba && Champion is not null && Champion.Id != Player.Id)
                return new FightOffer { Opponent = Champion, Rounds = 12, TitleFight = true, Belt = PrimaryBelt, Context = $"{PrimaryBelt} title shot" };
            if (!holdsWbc && WbcChampion is not null && WbcChampion.Id != Player.Id)
                return new FightOffer { Opponent = WbcChampion, Rounds = 12, TitleFight = true, Belt = "WBC", Context = "WBC title shot" };
            if (IbfActive && !holdsIbf && IbfChampion is not null && IbfChampion.Id != Player.Id)
                return new FightOffer { Opponent = IbfChampion, Rounds = 12, TitleFight = true, Belt = "IBF", Context = "IBF title shot" };
        }

        // Match by career stage. A prospect (starter/pre-prime) is fed beatable opposition so he can
        // build a record — a higher target index is a LOWER-ranked, weaker opponent. Once he matures,
        // he fights the men ranked above him.
        var stage = CareerStages.Of(Player);

        // Regional title — a stepping stone before world level. A ranked contender who isn't yet a
        // top-5 world man goes after (or defends) his region's belt.
        var region = RegionOf(Player);
        if (region is not null && proFights >= fightsToRank && idx > 4 && idx <= 25
            && titleCooldownOk && stage is CareerStage.PrePrime or CareerStage.Prime && _rng.Next(2) == 0)
        {
            if (PlayerHolds(region))   // defend the regional belt against a fellow regional contender
            {
                var chall = ranked.FirstOrDefault(b => b.Id != Player.Id && RegionOf(b) == region && !IsWorldChampion(b));
                if (chall is not null)
                    return new FightOffer { Opponent = chall, Rounds = 12, TitleFight = true, Belt = region, Context = $"{region} title defence" };
            }
            else                       // or challenge for it as a stepping stone to world level
            {
                var rc = BeltHolder(region);
                if (rc is not null && rc.Id != Player.Id && RegionOf(rc) == region)
                    return new FightOffer { Opponent = rc, Rounds = 12, TitleFight = true, Belt = region, Context = $"{region} title shot" };
            }
        }

        int target = stage switch
        {
            CareerStage.Starter => idx + 3 + _rng.Next(5),   // clearly weaker — build the record
            CareerStage.PrePrime => idx + 1 + _rng.Next(3),  // a touch below — keep winning, learn
            // Top contenders don't meet each other every time out — roughly 1 in 3 is a real top-20
            // clash, the rest are stay-busy tune-ups against lower opposition.
            CareerStage.Prime => (idx < 20 && _rng.Next(3) != 0) ? idx + 3 + _rng.Next(8) : idx - 1 - _rng.Next(4),
            _ => idx - _rng.Next(3)                          // around his level late in the career
        };
        target = Math.Clamp(target, 0, ranked.Count - 1);
        if (target == idx) target = idx + 1 <= ranked.Count - 1 ? idx + 1 : idx - 1;
        var opp = ranked[Math.Clamp(target, 0, ranked.Count - 1)];
        if (opp.Id == Player.Id) opp = ranked[Math.Max(0, idx - 1)];

        // Enforce the experience ceiling — a hot prospect who's ranked high won't be fed to the elite
        // before he's ready; instead he gets the best opponent his record has earned.
        bool capped = false;
        if (opp.Overall > maxOvr)
        {
            var pool = ranked.Where(b => b.Id != Player.Id && b.Overall <= maxOvr).ToList();
            if (pool.Count > 0) { opp = pool[_rng.Next(Math.Min(pool.Count, 5))]; capped = true; }
        }

        // Avoid a stale rematch — don't keep serving up a man he's just fought or has met many times.
        if (RecentFoes(Player, 3).Contains(opp.Name) || TimesFaced(opp.Name) >= 3)
        {
            var fresh = ranked.Where(b => b.Id != Player.Id && b.Overall <= maxOvr
                                       && !RecentFoes(Player, 3).Contains(b.Name) && TimesFaced(b.Name) < 3).ToList();
            if (fresh.Count > 0) opp = NearOne(fresh, opp.Overall);
        }

        // A gatekeeper is a SEASONED fighter (15+ bouts) — a mid-rated man with a thin record is a rising prospect,
        // not a test. If a gatekeeper-tier opponent (not an elite contender, and not a ranked man) is green, swap
        // him for an experienced fighter at a similar level, else drop back to a journeyman tune-up.
        if (opp.Overall is > 55 and < 78 && ProFights(opp) < 15 && !Top20Ids(Player.WeightClass).Contains(opp.Id))
        {
            var seasoned = ranked.Where(b => b.Id != Player.Id && b.Id != opp.Id && b.Overall <= maxOvr
                                          && (ProFights(b) >= 15 || b.Overall <= 55)
                                          && !RecentFoes(Player, 3).Contains(b.Name) && TimesFaced(b.Name) < 3).ToList();
            if (seasoned.Count > 0) opp = NearOne(seasoned, opp.Overall);
        }

        // A ranked contender's schedule: NOT a top-10 war every time out, and NEVER fed raw prospects. Most dates
        // are seasoned gatekeeper tune-ups; roughly one in three is a genuine clash with a fellow contender.
        if (WorldRanked(Player) && idx >= 0 && idx <= 20)
        {
            var seasoned = ranked.Where(b => b.Id != Player.Id && !IsProspect(b) && ProFights(b) >= 15
                                          && !RecentFoes(Player, 3).Contains(b.Name) && TimesFaced(b.Name) < 3).ToList();
            bool clash = _rng.Next(3) == 0;
            var pick = clash ? seasoned.Where(b => b.Overall >= 74).OrderByDescending(RankScore).Take(10).ToList()
                             : seasoned.Where(b => b.Overall is >= 58 and <= 76).OrderByDescending(RankScore).Take(10).ToList();
            if (pick.Count == 0) pick = seasoned;
            if (pick.Count > 0) opp = pick[_rng.Next(pick.Count)];
        }

        // Before he is world-ranked, the OPPOSITION'S experience climbs with his own — and the man in the other
        // corner is a PROFESSIONAL OPPONENT, not another kid starting out.
        //
        // This used to select by career stage, and asked for a "Starter" in the first half-dozen bouts on the
        // reasoning that you begin against green boys who cannot hurt you. That is not how a record is built.
        // A debutant does not fight another debutant - somebody has to lose, and the sport does not throw away
        // two prospects to find out which. He fights a designated opponent: a man with twenty fights and
        // fifteen losses whose trade is losing competitively in somebody else's home town.
        //
        // Measured, the old rule had 79% of a player's first fourteen opponents carrying under a dozen fights
        // and only 17% carrying a losing record - a parade of 0-0 and 3-2 boys, some of whom were other
        // people's prospects. So the band is now drawn on EXPERIENCE AND RECORD, which is what makes a man an
        // opponent, and the career-stage question does not come into it.
        if (!WorldRanked(Player))
        {
            int sofar = ProFights(Player);
            // What he needs in the other corner: fights behind him, and a record that says he loses.
            int wantFights = sofar <= 6 ? 8 : sofar <= 13 ? 10 : 12;
            double wantWinRate = sofar <= 6 ? 0.58 : sofar <= 13 ? 0.65 : 1.00;

            bool Opponent(Boxer b)
            {
                int f = ProFights(b);
                if (f < wantFights) return false;
                int decided = b.Record.Wins + b.Record.Losses;
                return decided == 0 || b.Record.Wins / (double)decided <= wantWinRate;
            }

            var pool = ranked.Where(b => b.Id != Player.Id && b.Overall <= maxOvr
                                      && !DangerousProspect(b)
                                      && !RecentFoes(Player, 3).Contains(b.Name) && TimesFaced(b.Name) < 3).ToList();

            // The men who are genuinely opponents; failing that, anyone seasoned; failing that, leave it be.
            var band = pool.Where(Opponent).ToList();
            if (band.Count == 0) band = pool.Where(b => ProFights(b) >= wantFights).ToList();
            if (band.Count > 0) opp = NearOne(band, opp.Overall);
        }

        // ABSOLUTE final guard, enforcing BOTH hard rules at once. Run as separate passes they each undid the
        // other — the top-15 guard picked a champion, and the champion guard picked a top-15 man.
        //   1. A reigning world champion only ever meets the player with his belt on the line. The NPC world
        //      already keeps champions off undercards; without the same rule here the player could beat the WBA
        //      champion in a stay-busy bout and walk away with nothing.
        //   2. Until he's world-ranked (20 bouts) the player is kept away from the division's top men BY RANKING,
        //      not just by rating — an unproven #1 is often a young fighter whose rating hasn't caught up. This
        //      holds for a fast-tracked wonder kid too: graduating early earns him ranked opposition (#16 and
        //      below), not the very best.
        // Title bouts return far above this, so a genuinely earned challenge is unaffected.
        var offLimits = WorldRanked(Player)
            ? new HashSet<int>()
            : ActiveIn(Player.WeightClass).Where(RankedContender)
                  .OrderByDescending(RankScore).Take(15).Select(b => b.Id).ToHashSet();
        bool Barred(Boxer b) => IsWorldChampion(b) || offLimits.Contains(b.Id);
        if (Barred(opp))
        {
            var ok = ranked.Where(b => b.Id != Player.Id && !Barred(b) && b.Overall <= maxOvr
                                    && !RecentFoes(Player, 3).Contains(b.Name) && TimesFaced(b.Name) < 3).ToList();
            if (ok.Count == 0) ok = ranked.Where(b => b.Id != Player.Id && !Barred(b)).ToList();
            if (ok.Count > 0) opp = NearOne(ok, opp.Overall);
        }

        // The distance a man is trusted with follows his mileage, the way a real career does: four-rounders on
        // debut, then six, eight, and ten once he is established. It used to come off the career STAGE, which
        // gave six rounds until his ninth fight and then eight for everything up to about thirty - so a
        // twenty-one fight professional was still being matched over eight, with no way to a ten-rounder
        // unless he was already ranked in the top five, and no four-rounders for a debutant at all.
        int had = ProFights(Player);
        int rounds = had <= 4 ? 4
                   : had <= 10 ? 6
                   : had <= 18 ? 8
                   : 10;
        string ctx = capped ? "building a record"
                   : target < idx ? (idx <= 5 ? "eliminator" : "step-up")
                   : stage == CareerStage.Starter || stage == CareerStage.PrePrime ? "building a record"
                   : "stay-busy";
        // A final eliminator at the top of the division is fought over the championship distance — the winner
        // is going straight to a title shot and has to prove he can last it.
        if (ctx == "eliminator") rounds = 12;
        return new FightOffer { Opponent = opp, Rounds = rounds, Context = ctx };
    }

    // ---- outcomes & ratings ----

    private void ApplyOutcome(FightResult res, Boxer a, Boxer b, string? note = null)
    {
        if (_watch is not null)
        {
            var w = res.Winner ?? a; var l = res.Loser ?? b;
            _watch.Add(new WorldBout(Date, a.WeightClass, RegionOf(a) ?? "Rest of the world", a.Country ?? "",
                                     w.Name, l.Name, res.Method, res.EndRound, res.IsDraw, note));
        }
        // Stepping up to the world stage means giving up any national/regional strap you were carrying.
        if (IsWorldTitleNote(note)) { DropRegionals(a); DropRegionals(b); }

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
                int dura = Durability(res.Loser.Ratings);
                _outUntil[res.Loser.Id] = Date.AddDays(35 + Math.Max(0, 85 - dura) * 2 + _rng.Next(45));
            }
        }
        // Cuts and hand injuries can sideline either man, win or lose — a fighter with poor cut resistance is far
        // more injury-prone, so brittle fighters miss real time while durable ones almost never do.
        foreach (var f in new[] { a, b })
        {
            if (ko && f.Id == res.Loser?.Id) continue;   // the KO'd man is already on the shelf
            double proneness = 0.012 + (1.0 - f.Ratings.CutResistance / 100.0) * 0.06;
            if (_rng.NextDouble() < proneness)
                _outUntil[f.Id] = Date.AddDays(28 + _rng.Next(63));
        }

        // Each fighter's ledger: date, result, method, round, knockdowns scored / suffered.
        char ra = res.IsDraw ? 'D' : res.Winner!.Id == a.Id ? 'W' : 'L';
        char rb = res.IsDraw ? 'D' : res.Winner!.Id == b.Id ? 'W' : 'L';
        string? cardsA = null, cardsB = null;
        if (res.Scorecards.Count > 0)
        {
            cardsA = string.Join(" · ", res.Scorecards.Select(c => $"{c.A}-{c.B}"));
            cardsB = string.Join(" · ", res.Scorecards.Select(c => $"{c.B}-{c.A}"));
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
        Record(a, b.Name, ra, res.Method, res.EndRound, res.KnockdownsB, res.KnockdownsA, note, cardsA, keepRounds ? roundsA : null, commentary);
        Record(b, a.Name, rb, res.Method, res.EndRound, res.KnockdownsA, res.KnockdownsB, note, cardsB, keepRounds ? roundsB : null, commentary);

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

        UpdateLineal(res, a, b, note);

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
        CaptureBout(res, a, b, note);         // a candidate for the year-end awards
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

    private void CrownChampion(Boxer b)
    {
        _cursor = b.WeightClass;
        if (Champ is not null) Champ.IsChampion = false;
        Champ = b;
        b.IsChampion = true;
    }

    private void CrownWbc(Boxer b) { _cursor = b.WeightClass; Wbc = b; }
    private void CrownIbf(Boxer b) { _cursor = b.WeightClass; Ibf = b; }

    // ---- the lineal ("Ring") championship ----

    /// <summary>Move the lineal title, applying "the man who beat the man". It is NOT sanctioned, so unlike the
    /// alphabet belts it never changes hands on a relinquishment, a stripping, or a vacant-title bout — only in
    /// the ring. A draw leaves it where it is. When it's vacant it's filled the way The Ring fills it: by the
    /// division's two leading men meeting for a world title, or by a man unifying the belts.</summary>
    private void UpdateLineal(FightResult res, Boxer a, Boxer b, string? note)
    {
        var wc = a.WeightClass;
        if (wc != b.WeightClass || res.IsDraw || res.Winner is null || res.Loser is null) return;
        var champ = LinealOf(wc);

        if (champ is not null)
        {
            if (res.Loser.Id == champ.Id)
            {
                _lineal[wc] = res.Winner;
                _everChampion.Add(res.Winner.Id);
                LogEvent($"{res.Winner.Name} beats the man who beat the man — {res.Loser.Name}'s {LinealBelt} championship changes hands.",
                         res.Winner.Id == Player.Id, kind: "title", div: wc);
            }
            else if (res.Winner.Id == champ.Id && IsWorldTitleNote(note))
                Defended(wc, "Ring", champ.Id);
            return;
        }

        // Vacant — only a genuine championship bout between the two leading men can establish a new line.
        if (!IsWorldTitleNote(note)) return;
        var top2 = ActiveIn(wc).Where(RankedContender).OrderByDescending(RankScore).Take(2).Select(x => x.Id).ToHashSet();
        if (!(top2.Contains(a.Id) && top2.Contains(b.Id))) return;
        _lineal[wc] = res.Winner;
        _everChampion.Add(res.Winner.Id);
        LogEvent($"{res.Winner.Name} beats {res.Loser.Name} to establish himself as the {LinealBelt} champion at {wc.DisplayName()}.",
                 res.Winner.Id == Player.Id, kind: "title", div: wc);
    }

    /// <summary>A unified champion holds every belt going, so he IS the man — he takes a vacant lineal title.</summary>
    private void ClaimLinealByUnification(WeightClass wc)
    {
        if (LinealOf(wc) is not null || UndisputedOf(wc) is not Boxer u) return;
        _lineal[wc] = u;
        LogEvent($"{u.Name} holds every belt at {wc.DisplayName()} and is recognised as {LinealBelt} champion.",
                 u.Id == Player.Id, kind: "title", div: wc);
    }

    /// <summary>The lineal title can't be inherited: when the champion retires or leaves the division the line
    /// simply ends, and the next two leading men have to start a new one.</summary>
    private void VacateLineal(WeightClass wc, Boxer who, string why)
    {
        if (LinealOf(wc)?.Id != who.Id) return;
        _lineal[wc] = null;
        LogEvent($"The {LinealBelt} championship at {wc.DisplayName()} falls vacant — {who.Name} {why}.",
                 who.Id == Player.Id, kind: "title", div: wc);
    }

    // ---- regional belts ----

    private readonly Dictionary<(WeightClass Div, string Region), Boxer> _regional = new();   // (division, region) → belt holder
    private static readonly string[] RegionalBelts = { "NABF", "European", "Commonwealth" };

    /// <summary>Who is worth putting in for a regional title. A world-ranked contender obviously, but a good
    /// unbeaten prospect too - that is exactly what these belts are for, and holding a man back until he has
    /// twenty bouts means the belt only ever changes hands between established fighters. It is not everybody
    /// though: a credible challenger has a dozen fights behind him, is rated, and has been winning.</summary>
    private bool CredibleForRegional(Boxer b) =>
        ChasesRegional(b)
        && (WorldRanked(b)
            || (ProFights(b) >= 9 && b.Class >= 5
                && b.Record.Wins * 100 >= Math.Max(1, b.Record.Wins + b.Record.Losses) * 70));

    /// <summary>Whether a fighter would realistically campaign for a regional belt.
    ///
    /// These are a rung on the way UP - a man wins the NABF or the European to prove he belongs, and then goes
    /// after a world title. They were being handed to whoever stood highest in the rankings, which meant former
    /// world champions kept turning up to contest them, and that is not how the sport works. A man who has held
    /// a world title does not go back for a regional one unless his career has genuinely collapsed, and even
    /// then it is rare - it is a rebuilding job, not an ambition.
    ///
    /// A reigning world champion never does it at all.</summary>
    private bool ChasesRegional(Boxer b)
    {
        if (IsWorldChampion(b)) return false;
        if (!_everChampion.Contains(b.Id)) return true;
        return !WorldRanked(b) && _rng.NextDouble() < 0.10;
    }

    /// <summary>The regional belts the player currently holds (for the UI header).</summary>
    public IEnumerable<string> PlayerRegionalBelts => _regional.Where(kv => kv.Key.Div == Division && kv.Value.Id == Player.Id).Select(kv => kv.Key.Region);
    public Boxer? RegionalChampion(string region) => _regional.GetValueOrDefault((Division, region));

    /// <summary>Which regional belt a fighter's nationality makes him eligible for (null = none).</summary>
    private static string? RegionOf(Boxer b) => b.Country switch
    {
        "USA" or "United States" or "Canada" or "Mexico" or "Puerto Rico" or "Cuba" or "Argentina"
            or "Brazil" or "Venezuela" or "Colombia" or "Panama" or "Dominican Republic" => "NABF",
        "England" or "Scotland" or "Wales" or "Ireland" or "Northern Ireland" or "Australia"
            or "New Zealand" or "Nigeria" or "Ghana" or "South Africa" or "Jamaica" or "Canada (CW)" => "Commonwealth",
        "Germany" or "Italy" or "France" or "Spain" or "Russia" or "Soviet Union" or "Ukraine"
            or "Poland" or "Sweden" or "Denmark" or "Netherlands" or "Kazakhstan" or "Romania" or "Croatia" or "Finland" => "European",
        _ => null
    };

    /// <summary>Uniform belt access — routes world belts to their fields, regional belts to the map.</summary>
    private Boxer? BeltHolder(string belt) =>
        belt == "WBC" ? WbcChampion :
        belt == "IBF" ? IbfChampion :
        (belt == PrimaryBelt || belt == "WBA" || belt == "World") ? Champion :
        _regional.GetValueOrDefault((Division, belt));

    private bool PlayerHolds(string belt) => BeltHolder(belt)?.Id == Player.Id;

    /// <summary>Is this bout note a WORLD title (not a regional strap)?</summary>
    private static bool IsWorldTitleNote(string? note) =>
        note is not null && (note == "unification" || (note.EndsWith(" title") && !RegionalBelts.Any(rb => note.StartsWith(rb))));

    /// <summary>Give up any regional belts a fighter holds — used when he contests a world title.</summary>
    private void DropRegionals(Boxer b)
    {
        foreach (var region in RegionalBelts)
            if (_regional.GetValueOrDefault((b.WeightClass, region))?.Id == b.Id)
            {
                _regional.Remove((b.WeightClass, region));
                if (b.WeightClass == Division) LogEvent($"{b.Name} relinquishes the {region} title to campaign for a world belt.", b.Id == Player.Id, kind: "title");
            }
    }

    private void SetBeltHolder(string belt, Boxer holder)
    {
        if (belt == "WBC") CrownWbc(holder);
        else if (belt == "IBF") CrownIbf(holder);
        else if (belt == PrimaryBelt || belt == "WBA" || belt == "World") CrownChampion(holder);
        else _regional[(holder.WeightClass, belt)] = holder;
    }

    /// <summary>Brings the WBC belt into being in 1963 and re-crowns vacant world/regional belts in a division.</summary>
    private void UpdateBeltsFor(WeightClass wc)
    {
        if (!DivisionActive(wc)) return;   // the division doesn't exist yet — no belts to fill
        if (WbcOf(wc) is Boxer w && w.Retired) _wbc[wc] = null;
        if (WbcActive && WbcOf(wc) is null)
        {
            var winner = ContestVacantTitle(wc, "WBC", ChampOf(wc)?.Id ?? 0, IbfOf(wc)?.Id ?? 0);
            if (winner is not null) _wbc[wc] = winner;   // announced by ContestVacantTitle, dated to fight night
        }
        // The IBF is established in 1983; fill it from the leading contender who isn't already a world champ.
        if (IbfOf(wc) is Boxer iw && iw.Retired) _ibf[wc] = null;
        if (IbfActive && IbfOf(wc) is null)
        {
            var winner = ContestVacantTitle(wc, "IBF", ChampOf(wc)?.Id ?? 0, WbcOf(wc)?.Id ?? 0);
            if (winner is not null) _ibf[wc] = winner;   // announced by ContestVacantTitle, dated to fight night
        }

        // A line that has ended (its holder retired or moved) is cleared, and a man who now holds every belt
        // going is recognised as the lineal champion — otherwise a division can show an "undisputed" champion
        // while the Ring title sits with someone else, which reads as a bug even though the rules allow it.
        if (_lineal.GetValueOrDefault(wc) is Boxer lc && (lc.Retired || lc.WeightClass != wc)) _lineal[wc] = null;
        ClaimLinealByUnification(wc);

        // Regional belts: each region's title goes to its best fighter in this division who isn't a world champion.
        foreach (var region in RegionalBelts)
        {
            var champ = ChampOf(wc); var wbc = WbcOf(wc); var ibf = IbfOf(wc);
            if (_regional.TryGetValue((wc, region), out var cur) && (cur.Retired || cur.WeightClass != wc || RegionOf(cur) != region)) _regional.Remove((wc, region));
            if (!_regional.ContainsKey((wc, region)))
            {
                var contenders = ActiveIn(wc).Where(b => b.Id != Player.Id && RegionOf(b) == region
                                          && b.Id != champ?.Id && b.Id != wbc?.Id && b.Id != ibf?.Id
                                          && WorldRanked(b) && ChasesRegional(b))
                                 .OrderByDescending(RankScore).ToList();
                // Skip the very top of the list where possible: the best contender in a division is already
                // fighting for a world title, not collecting a regional one on his way past.
                var pick = contenders.Skip(2).FirstOrDefault() ?? contenders.FirstOrDefault();
                if (pick is not null)
                {
                    _regional[(wc, region)] = pick;
                    // Say so. A vacant regional belt used to change hands in silence, so the next holder's
                    // "relinquishes the title" line arrived with no explanation of how he came to have it.
                    LogEvent($"{pick.Name} takes the vacant {region} title.", kind: "title", div: wc);
                }
            }
        }
    }

    private TitleReign? OpenReign(string belt) => _reigns.LastOrDefault(r => r.Belt == belt && r.Lost is null);

    // ---- historical seeding & aging ----

    private void InjectHistorical(Boxer proto, int ageNow, int debutAge, int peak, bool announce)
    {
        var prime = proto.Ratings.Clone();
        var b = new Boxer
        {
            Id = proto.Id,
            Name = proto.Name,
            Nickname = proto.Nickname,
            WeightClass = proto.WeightClass,
            TopWeight = proto.TopWeight,
            Country = proto.Country,
            DateOfBirth = proto.DateOfBirth,
            DebutYear = proto.DebutYear,
            Ratings = prime.Clone(),
            Age = ageNow,
            PeakAge = peak,
            Potential = proto.Overall
        };
        AgeHistorical(b, prime, peak);                              // set ratings to the right point on their arc
        SeedRecordFor(b, Math.Max(0, ageNow - debutAge));
        CapStarter(b);                                              // a debuting great is still just a starter
        World.SeedRankPoints(b);
        _historical[b.Id] = (prime, peak);
        AddActive(b);
        if (announce) LogEvent($"{b.Name} ({b.Country}) turns pro.", kind: "debut", div: b.WeightClass);
    }

    /// <summary>What a fighter was at each point of his career, so a card can show the arc rather than only
    /// today's snapshot. A 34-year-old ex-champion's current ratings say nothing about the fighter who won the
    /// title at 26, and that man is the one worth looking at.
    ///
    /// For anyone drawn from the real roster this costs nothing to produce: their ratings are a pure function
    /// of age against a stored prime, so any age on the arc can simply be evaluated. Fighters invented inside
    /// the save develop randomly year to year and cannot be rewound, so they have no arc to show - the player
    /// is the exception, because his own is recorded as he lives it.</summary>
    public IReadOnlyList<StagePoint> CareerArc(Boxer b)
    {
        int now = CareerMileage.Fights(b);
        var points = new List<StagePoint>();

        if (b.Id == Player.Id)
        {
            foreach (var (fights, age, r) in _playerArc.OrderBy(x => x.Fights))
                points.Add(new StagePoint(StageName(StageAtFights(b, fights)), fights, age, r, false));
        }
        else if (_historical.TryGetValue(b.Id, out var h))
        {
            // Probe his arc at the end of each stage. The curve is a pure function of mileage, so any point on
            // it can simply be evaluated - no history has to be stored for anyone off the real roster.
            foreach (int at in new[] { CareerMileage.StarterUntil(b), CareerMileage.PrePrimeUntil(b),
                                       (CareerMileage.PrePrimeUntil(b) + CareerMileage.PrimeUntil(b)) / 2,
                                       CareerMileage.PrimeUntil(b), CareerMileage.PostPrimeUntil(b) })
            {
                if (at <= 0 || at >= now) continue;   // not reached yet
                var was = new Ratings();
                PlaceOnArc(was, h.Prime, DevelopmentAt(b, at), at <= CareerMileage.PrimeUntil(b));
                points.Add(new StagePoint(StageName(StageAtFights(b, at)), at, 0, was, false));
            }
        }
        else return points;   // invented inside the save and not the player: nothing to reconstruct

        // Where he is today always closes the arc.
        points.Add(new StagePoint(StageName(CareerStages.Of(b)), now, b.Age, b.Ratings, true));
        return points.GroupBy(p => p.Fights).Select(g => g.Last()).OrderBy(p => p.Fights).ToList();
    }

    /// <summary>The stage a given fighter was in at a given fight count, using HIS boundaries.</summary>
    private static CareerStage StageAtFights(Boxer b, int fights) =>
        fights <= CareerMileage.StarterUntil(b) ? CareerStage.Starter :
        fights <= CareerMileage.PrePrimeUntil(b) ? CareerStage.PrePrime :
        fights <= CareerMileage.PrimeUntil(b) ? CareerStage.Prime :
        fights <= CareerMileage.PostPrimeUntil(b) ? CareerStage.PostPrime : CareerStage.End;

    private static string StageName(CareerStage s) => s switch
    {
        CareerStage.Starter => "Starter",
        CareerStage.PrePrime => "Pre-prime",
        CareerStage.Prime => "Prime",
        CareerStage.PostPrime => "Post-prime",
        _ => "Veteran"
    };

    // The player's own arc. His development is random year to year and cannot be recomputed, so it is recorded
    // as he lives it - keyed on the mileage he had at the time, which is what the stages are measured in.
    private readonly List<(int Fights, int Age, Ratings R)> _playerArc = new();

    /// <param name="peak">Kept for the seeding path, which positions a man on his arc before he has a record.
    /// Once he is in the world his place on it is set by his mileage like everybody else's.</param>
    private static void AgeHistorical(Boxer b, Ratings prime, int peak)
    {
        // Fights, not birthdays. A roster fighter who is not being matched does not decay on the calendar.
        double dev = CareerMileage.Fights(b) > 0
            ? CareerMileage.Development(b)
            : BoxerFactory.Development(b.Age, peak);
        PlaceOnArc(b.Ratings, prime, dev, CareerMileage.PastPrime(b) <= 0);
    }

    /// <summary>Write a fighter's ratings for a given point on his arc. Split out from <see cref="AgeHistorical"/>
    /// so the arc can be evaluated at a mileage the man is not currently at, WITHOUT building a stand-in boxer:
    /// a shallow clone shares his record object, and writing a probe mileage into it would corrupt the real
    /// fighter's record.</summary>
    private static void PlaceOnArc(Ratings r, Ratings prime, double dev, bool young)
    {
        // Young: power/defence/speed are near their ceiling already. Old: they decline normally.
        r.Power = Scale(prime.Power, young ? Lerp(dev, 0.85) : dev);
        r.Speed = Scale(prime.Speed, young ? Lerp(dev, 0.82) : dev);
        r.Defense = Scale(prime.Defense, Lerp(dev, young ? 0.72 : 0.55));
        r.Accuracy = Scale(prime.Accuracy, Lerp(dev, young ? 0.58 : 0.6));
        r.Stamina = Scale(prime.Stamina, dev);
        r.Conditioning = Scale(prime.Conditioning, dev);
        r.Chin = Scale(prime.Chin, Lerp(dev, 0.78));
        r.CutResistance = prime.CutResistance;
        r.Aggression = prime.Aggression;
        r.Heart = prime.Heart;
    }

    /// <summary>The development factor a fighter would have at a given mileage, without touching him.</summary>
    private static double DevelopmentAt(Boxer b, int fights)
    {
        int primeAt = CareerMileage.PrePrimeUntil(b);
        if (fights <= primeAt)
        {
            double t = primeAt <= 0 ? 1 : fights / (double)primeAt;
            return 0.55 + 0.45 * Math.Clamp(t, 0, 1);
        }
        return Math.Max(0.45, 1.0 - Math.Max(0, fights - CareerMileage.PrimeUntil(b)) * 0.010);
    }

    private void SeedRecordFor(Boxer b, int yearsActive)
    {
        int fights = (int)Math.Round(yearsActive * (2.0 + _rng.NextDouble() * 2.0));
        // Win rate reflects his true class (ceiling), not his half-formed current rating — so a future
        // great's early record is a run of wins, not a string of upsets against journeymen.
        int cls = Math.Max(b.Overall, b.Potential);
        double winRate = Math.Clamp(0.5 + (cls - 60) / 90.0, 0.4, 0.97);
        int span = Math.Max(30, (int)(yearsActive * 365));   // his pre-sim years, so bouts can be dated
        for (int i = 0; i < fights; i++)
        {
            // Record the result AND a dated ledger line vs a journeyman, oldest first, so the fight
            // history matches the win-loss record instead of starting blank at his sim debut.
            var when = b.DebutYear is int dy
                ? new DateOnly(Math.Clamp(dy + i / 3, dy, Date.Year - 1), 1 + _rng.Next(12), 1 + _rng.Next(28))
                : Date.AddDays(-span + (int)((i + 1.0) / (fights + 1) * span));
            string opp = _oppNames.Next();
            double roll = _rng.NextDouble();
            char rc; string method; int round = 0;
            if (roll < winRate)
            {
                b.Record.Wins++; rc = 'W';
                if (_rng.NextDouble() < Ratings.KnockoutChance(b.Ratings.Power, 72, b.Overall - 64)) { b.Record.KnockoutWins++; method = _rng.NextDouble() < 0.5 ? "KO" : "TKO"; round = 1 + _rng.Next(8); }
                else method = _rng.NextDouble() < 0.75 ? "UD" : "SD";
            }
            else if (roll < winRate + 0.08) { b.Record.Draws++; rc = 'D'; method = "D"; }
            else
            {
                b.Record.Losses++; rc = 'L';
                if (_rng.NextDouble() < 0.2) { b.Record.KnockoutLosses++; method = "TKO"; round = 1 + _rng.Next(8); }
                else method = _rng.NextDouble() < 0.75 ? "UD" : "SD";
            }
            b.History.Add(new BoutLine { Date = when, Opponent = opp, Result = rc, Method = method, Round = round });
            if (b.History.Count > 60) b.History.RemoveAt(0);
        }
    }

    // ---- helpers ----

    private void AddActive(Boxer b) => _roster.Add(b);

    /// <summary>No starter is rated above 75 — a green fighter, however talented, hasn't proven it yet.
    /// His ratings scale down proportionally (keeping his profile) and the cap lifts once he's seasoned.</summary>
    private static void CapStarter(Boxer b)
    {
        if (CareerStages.Of(b) != CareerStage.Starter) return;
        var r = b.Ratings;
        int guard = 0;
        while (b.Overall > 75 && guard++ < 12)
        {
            r.Power = Dn(r.Power); r.Chin = Dn(r.Chin); r.Speed = Dn(r.Speed); r.Defense = Dn(r.Defense);
            r.Stamina = Dn(r.Stamina); r.Accuracy = Dn(r.Accuracy); r.Conditioning = Dn(r.Conditioning);
            r.Aggression = Dn(r.Aggression); r.Heart = Dn(r.Heart); r.CutResistance = Dn(r.CutResistance);
        }
        static int Dn(int v) => Ratings.Clamp((int)Math.Round(v * 0.96));
    }

    private void Record(Boxer f, string opp, char result, string method, int round, int kdFor, int kdAgainst, string? note, string? cards, IReadOnlyList<BoutRound>? rounds, IReadOnlyList<string>? commentary)
    {
        f.History.Add(new BoutLine { Date = Date, Opponent = opp, Result = result, Method = method, Round = round, KdFor = kdFor, KdAgainst = kdAgainst, Note = note, Cards = cards, Rounds = rounds, Commentary = commentary });
        if (f.History.Count > 60) f.History.RemoveAt(0);   // keep the ledger bounded
    }

    /// <summary>Pull the key play-by-play moments from a full-engine bout (knockdowns, cuts, big shots,
    /// the finish, and a one-line recap per round) so a stored fight can be read back later. Null when the
    /// fight was resolved by the fast NPC model (no tick detail).</summary>
    private static List<string>? ExtractHighlights(FightResult res)
    {
        if (res.Rounds.Count == 0 || res.Rounds[0].Ticks.Count == 0) return null;
        string A = res.A.Name, B = res.B.Name;
        var lines = new List<string>();
        foreach (var rd in res.Rounds)
        {
            int kdA = 0, kdB = 0; bool cutA = false, cutB = false, hurt = false;
            foreach (var t in rd.Ticks)
            {
                if (t.KnockdownsA > kdA) { kdA = t.KnockdownsA; lines.Add($"R{rd.Round} — {A} is DOWN{(t.DownBodyA ? " from a body shot" : "")}!"); }
                if (t.KnockdownsB > kdB) { kdB = t.KnockdownsB; lines.Add($"R{rd.Round} — {B} is DOWN{(t.DownBodyB ? " from a body shot" : "")}!"); }
                if (!cutA && t.CutA >= 0.4) { cutA = true; lines.Add($"R{rd.Round} — {A} is cut."); }
                if (!cutB && t.CutB >= 0.4) { cutB = true; lines.Add($"R{rd.Round} — {B} is cut."); }
                if (!hurt && t.RockB >= 2) { hurt = true; lines.Add($"R{rd.Round} — {A} has {B} badly hurt!"); }
                else if (!hurt && t.RockA >= 2) { hurt = true; lines.Add($"R{rd.Round} — {B} has {A} badly hurt!"); }
                if (t.Fin is StopInfo fin)
                {
                    string w = fin.Winner == 0 ? A : B, l = fin.Winner == 0 ? B : A;
                    lines.Add($"R{rd.Round} — {(fin.Method == "KO" ? $"{w} KNOCKS OUT {l}!" : fin.Method == "DQ" ? $"{l} is disqualified." : $"{w} STOPS {l}!")}");
                }
            }
            // A short recap of who edged the round.
            string recap = rd.LandedA > rd.LandedB + 2 ? $"{A}'s round" : rd.LandedB > rd.LandedA + 2 ? $"{B}'s round" : "even round";
            lines.Add($"R{rd.Round} · {recap} ({rd.LandedA}-{rd.LandedB} landed)");
        }
        return lines.Count > 0 ? lines : null;
    }

    /// <summary>A fast, statistical resolution of an NPC-vs-NPC bout (no round-by-round sim) so a big
    /// division can be simulated cheaply. The player's own fights always use the full engine.</summary>
    private FightResult FastBout(Boxer a, Boxer b, int rounds)
    {
        // Effective rating favours a young fighter still climbing toward a high ceiling, so a genuine
        // prospect very rarely loses to a journeyman who's already maxed out.
        double gap = (a.Overall + YouthEdge(a)) - (b.Overall + YouthEdge(b));
        double pa = 1.0 / (1.0 + Math.Pow(10, -gap / 8.0));   // a's win probability by effective rating
        // Any given night: between two genuine world-class men, even a dominant champion can be caught — so the
        // underdog always keeps a real puncher's chance. This caps ultra-long unbeaten reigns without letting a
        // prospect get upset by a journeyman (the floor only applies when BOTH are top-tier).
        if (a.Overall >= 66 && b.Overall >= 66) pa = Math.Clamp(pa, 0.07, 0.93);
        double drawP = 0.05 * (1 - Math.Abs(pa - 0.5) * 2);
        Boxer? winner = null, loser = null;
        FightOutcome outcome = FightOutcome.Draw;
        string method = "D";
        int endRound = rounds, kdA = 0, kdB = 0;
        bool draw = _rng.NextDouble() < drawP;
        bool aWins = false, ko = false, stopNoKd = false;   // stopNoKd: a cut/DQ stoppage — trims the card, no knockdown

        if (!draw)
        {
            aWins = _rng.NextDouble() < pa;                       // provisional result, by rating
            var pw = aWins ? a : b; var pl = aWins ? b : a;
            double koP = Ratings.KnockoutChance(pw.Ratings.Power, pl.Ratings.Chin, pw.Overall - pl.Overall);
            if (_rng.NextDouble() < koP)
            {
                ko = true; outcome = FightOutcome.Knockout; method = "KO"; endRound = 1 + _rng.Next(rounds);
                if (aWins) kdB = 1; else kdA = 1;
            }
            else
            {
                // Either man can be cut and pulled out — far more often if he's a bleeder — and the cut man LOSES
                // even if he was ahead (a bleeder's curse). If both cut, the more cut-prone one goes.
                double CutRisk(Boxer f) => 0.005 + Math.Max(0, 80 - f.Ratings.CutResistance) / 100.0 * 0.12;
                bool aCut = _rng.NextDouble() < CutRisk(a);
                bool bCut = _rng.NextDouble() < CutRisk(b);
                if (aCut || bCut)
                {
                    bool aStopped = aCut && (!bCut || a.Ratings.CutResistance <= b.Ratings.CutResistance);
                    aWins = !aStopped;
                    outcome = FightOutcome.TechnicalKnockout; method = "TKO"; stopNoKd = true;
                    endRound = Math.Max(3, rounds - _rng.Next(4));
                }
                // A rare disqualification (~1 in 500 fights) — not a knockout; the fouler is thrown out.
                else if (_rng.NextDouble() < 0.0042)
                {
                    outcome = FightOutcome.Decision; method = "DQ"; stopNoKd = true;
                    endRound = 2 + _rng.Next(Math.Max(1, rounds - 2));
                }
                else outcome = FightOutcome.Decision;
            }
            winner = aWins ? a : b; loser = aWins ? b : a;
        }

        // Sketch a believable card: the winner takes most rounds, punch output tracks ability, and a
        // stoppage is a 10-8 final round. Cheap enough to run for the whole division, so even quick
        // NPC undercards are inspectable round by round.
        int lastRound = (ko || stopNoKd) ? endRound : rounds;
        var rr = new List<RoundResult>(lastRound);
        for (int r = 1; r <= lastRound; r++)
        {
            int landA = FastLanded(a, b), landB = FastLanded(b, a);
            int sA = 10, sB = 10, kA = 0, kB = 0;
            if (ko && r == lastRound)
            {
                if (aWins) { sB = 8; kB = 1; landA += 3; } else { sA = 8; kA = 1; landB += 3; }
            }
            else
            {
                bool aRound = draw ? _rng.NextDouble() < 0.5 : _rng.NextDouble() < (aWins ? 0.66 : 0.34);
                if (aRound) { sB = 9; landA += 2; } else { sA = 9; landB += 2; }
            }
            rr.Add(new RoundResult { Round = r, LandedA = landA, LandedB = landB, ScoreA = sA, ScoreB = sB, KnockdownsA = kA, KnockdownsB = kB });
        }

        IReadOnlyList<(int A, int B)> cards = Array.Empty<(int, int)>();
        if (!ko && !stopNoKd)   // went to the cards (a decision or a draw); a cut/DQ stoppage has none
        {
            method = draw ? "D" : (Math.Abs(pa - 0.5) > 0.14 ? "UD" : _rng.NextDouble() < 0.5 ? "SD" : "MD");
            cards = BuildCards(aWins, rounds, method, draw);
        }

        return new FightResult
        {
            A = a, B = b, Winner = winner, Loser = loser, Outcome = outcome,
            ScheduledRounds = rounds, EndRound = endRound, KnockdownsA = kdA, KnockdownsB = kdB,
            Rounds = rr, Scorecards = cards, Method = method
        };
    }

    /// <summary>Clean punches a fighter lands in a round: his output (accuracy + work-rate) blunted by
    /// the other man's defence, so an out-boxer smothers a slugger's numbers and vice-versa.</summary>
    private int FastLanded(Boxer x, Boxer opp)
    {
        double output = 4 + x.Ratings.Accuracy * 0.08 + x.Ratings.Aggression * 0.05;
        double defended = opp.Ratings.Defense * 0.06;
        return Math.Clamp((int)Math.Round(output - defended + _rng.Next(-2, 3)), 1, 22);
    }

    /// <summary>Three judges' final tallies, oriented to the winner and consistent with the verdict.</summary>
    private (int A, int B)[] BuildCards(bool aWins, int rounds, string method, bool draw)
    {
        int bt = rounds * 10;
        int[] margins = draw ? new[] { 0, 0, 0 }
            : method switch
            {
                "UD" => new[] { _rng.Next(3, 8), _rng.Next(2, 6), _rng.Next(1, 5) },   // all three for the winner
                "MD" => new[] { _rng.Next(2, 6), _rng.Next(1, 4), 0 },                 // two clear, one even
                "SD" => new[] { _rng.Next(2, 5), _rng.Next(1, 4), -1 },                // two for, one against by a hair
                _    => new[] { 0, 0, 0 }
            };
        var res = new (int A, int B)[3];
        for (int j = 0; j < 3; j++)
        {
            int w = bt - _rng.Next(0, 4);        // a couple of dropped/shared rounds
            int l = w - margins[j];
            res[j] = aWins ? (w, l) : (l, w);
        }
        return res;
    }
    private void ReRank() { /* RankPoints are the ranking; nothing to precompute */ }

    /// <summary>Turn a notable NPC undercard result into a news headline — upsets, KO streaks, big stoppages.</summary>
    private void ReportBout(FightResult res)
    {
        if (res.IsDraw || res.Winner is null || res.Loser is null) return;
        var w = res.Winner; var l = res.Loser;
        bool ko = res.Outcome is FightOutcome.Knockout or FightOutcome.TechnicalKnockout;

        var div = w.WeightClass;   // tag every headline with the division so the news feed filters by weight
        if (WorldRanked(l) && l.Overall - w.Overall >= 8 && _rng.NextDouble() < 0.7)
        {
            LogEvent(Pick($"UPSET! {w.Name} shocks {l.Name}{(ko ? $", stopped in {res.EndRound}" : "")}.",
                          $"Boilover — {w.Name} outpoints the fancied {l.Name}.",
                          $"{l.Name} is stunned by {w.Name} in a major upset."), kind: "upset", div: div,
                     bout: new BoutRef(w.Name, l.Name, Date));
            return;
        }
        // A long unbeaten run is news in itself — reported once it hits 10, then every 5 (15, 20, …).
        int wins = WinStreak(w);
        if (wins >= 10 && wins % 5 == 0 && _rng.NextDouble() < 0.7)
        {
            LogEvent(Pick($"{w.Name} extends his unbeaten run to {wins} straight.",
                          $"Still perfect — {w.Name} makes it {wins} wins in a row and is knocking on the door.",
                          $"{w.Name} runs his streak to {wins} in a row, forcing his way into the picture."), kind: "streak", div: div,
                     bout: new BoutRef(w.Name, l.Name, Date));
            return;
        }
        if (ko)
        {
            int streak = KoStreak(w);
            if (streak >= 10 && streak % 5 == 0 && _rng.NextDouble() < 0.7)
            {
                LogEvent(Pick($"{w.Name} rolls on — {streak} straight inside the distance.",
                              $"{w.Name} keeps the KO streak going, now {streak} in a row.",
                              $"Frightening — {w.Name} up to {streak} consecutive knockouts."), kind: "streak", div: div,
                         bout: new BoutRef(w.Name, l.Name, Date));
                return;
            }
            if (w.Overall >= 76 && WorldRanked(l) && _rng.NextDouble() < 0.4)
                LogEvent(Pick($"{w.Name} halts {l.Name} in {res.EndRound}.",
                              $"{w.Name} takes out {l.Name} inside the distance."), kind: "ko", div: div,
                         bout: new BoutRef(w.Name, l.Name, Date));
            return;
        }
    }

    /// <summary>Count of a fighter's most recent bouts that were consecutive knockout wins.</summary>
    private static int KoStreak(Boxer b)
    {
        int n = 0;
        for (int i = b.History.Count - 1; i >= 0; i--)
        {
            var h = b.History[i];
            if (h.Result == 'W' && (h.Method == "KO" || h.Method == "TKO")) n++;
            else break;
        }
        return n;
    }

    /// <summary>Count of a fighter's most recent consecutive wins (any method).</summary>
    private static int WinStreak(Boxer b)
    {
        int n = 0;
        for (int i = b.History.Count - 1; i >= 0 && b.History[i].Result == 'W'; i--) n++;
        return n;
    }

    private string Pick(params string[] opts) => opts[_rng.Next(opts.Length)];
    private void LogEvent(string text, bool playerBout = false, string? kind = null, WeightClass? div = null,
                          BoutRef? bout = null)
    {
        _log.Add(new CareerEvent { On = Date, Text = text, PlayerBout = playerBout, Kind = kind,
                                   Div = div ?? Division, Bout = bout });
        if (_log.Count > 1500) _log.RemoveAt(0);   // bounded; eight divisions produce more news
    }

    private static DateOnly ParseDate(string s, DateOnly fallback) => DateOnly.TryParse(s, out var d) ? d : fallback;

    private static string LayoffText(int days) => days >= 60 ? $"out ~{Math.Max(2, days / 30)} months" : $"out ~{Math.Max(1, days / 7)} weeks";

    /// <summary>How long until the next fight, from a man's MILEAGE.
    ///
    /// This used to be picked off his career stage, which meant the gap jumped the moment he crossed a
    /// boundary — a fighter was out every nine weeks and then, on his ninth bout, suddenly every eleven. Wear
    /// does not arrive in steps. The wait now lengthens smoothly with every fight he has had: a novice boxes
    /// every couple of months, a man with sixty bouts on him needs a real camp and a real rest between them.
    ///
    /// Nobody's schedule is metronomic either. Opponents pull out, purse bids drag, a cut needs six weeks, and
    /// sometimes a fight comes up at three weeks' notice — so the actual wait is half to one and a half times
    /// the typical, floored at three weeks because nobody boxes again inside that.</summary>
    private int DaysForFights(int fights)
    {
        // Shaped to real activity rather than a straight line: a young fighter boxes five or six times a year,
        // that falls to three or four once he is twenty fights in and matched properly, and it keeps easing off
        // as the mileage tells. The curve is steepest through the twenties, which is where a career actually
        // changes from a club schedule to a campaign.
        double typical =
            fights <= 10 ? 66                                    // 5–6 a year
            : fights <= 20 ? 66 + (fights - 10) * 3.0            // ramping toward a real camp
            : fights <= 40 ? 96 + (fights - 20) * 0.7            // 3–4 a year
            : 110 + Math.Min(fights - 40, 50) * 0.9;             // winding down
        double spread = 0.5 + _rng.NextDouble();
        return Math.Max(21, (int)Math.Round(typical * spread));
    }

    /// <summary>The ceiling a generated fighter may not pass. It sits just below the all-time-great band, so
    /// a generated man can become a contender or a champion — the sport needs him to, or it has nobody left
    /// once the real fighters retire — but the very top of the scale stays the preserve of the real roster
    /// and the player. It was 56, which capped the entire generated population below the MEDIAN real fighter
    /// and is what left a career with no credible opponents after a dozen bouts.</summary>
    private const int GeneratedCap = 91;

    /// <summary>A fighter is only "world-ranked" once he's built a real body of work — 20 pro bouts.</summary>
    public static bool WorldRanked(Boxer b) => b.Record.Wins + b.Record.Losses + b.Record.Draws >= 20;

    /// <summary>A ranked contender: a real body of work AND a genuinely winning record (65%+). A man hovering
    /// around .500 is a gatekeeper however long he's been around — he belongs on the undercard, not the top 15.</summary>
    public static bool RankedContender(Boxer b) =>
        WorldRanked(b) && b.Record.Wins * 100 >= (b.Record.Wins + b.Record.Losses) * 65;

    /// <summary>How many pro bouts a fighter must build before he'll be matched with ranked contenders. Most
    /// serve the full apprenticeship (20); a rare phenom (a "wonder kid" — a very high ceiling) is fast-tracked
    /// into contention far sooner, the way the odd real great wins a belt inside 12–15 fights. Roughly the top
    /// ~1% (Potential ≥ 93) go at 12, the top ~5% (≥ 87) at 15.</summary>
    public static int ContenderApprenticeship(Boxer b) => b.Potential >= 92 ? 12 : b.Potential >= 87 ? 15 : 20;

    /// <summary>True once a fighter has served his apprenticeship and is ready to share the ring with contenders.</summary>
    public static bool ReadyForContenders(Boxer b) => (b.Record.Wins + b.Record.Losses + b.Record.Draws) >= ContenderApprenticeship(b);

    /// <summary>The current top-20 of a division (world-ranked fighters by ranking score).</summary>
    private HashSet<int> Top20Ids(WeightClass wc) => ActiveIn(wc).Where(RankedContender).OrderByDescending(RankScore).Take(20).Select(b => b.Id).ToHashSet();
    private HashSet<int> Top8Ids(WeightClass wc) => ActiveIn(wc).Where(RankedContender).OrderByDescending(RankScore).Take(8).Select(b => b.Id).ToHashSet();

    /// <summary>Ranking score: ability-anchored Elo, nudged by the fighter's win/loss margin so a padded Elo can't
    /// keep a losing record near the top. The margin is deliberately a light touch — weighted any heavier it just
    /// rewards volume, and a 60-fight journeyman outranks an unbeaten champion.</summary>
    public static double RankScore(Boxer b) => b.RankPoints + (b.Record.Wins - b.Record.Losses) * 2.5;

    private static int Scale(int v, double dev) => Ratings.Clamp((int)Math.Round(v * dev));
    private static double Lerp(double dev, double floor) => floor + (1.0 - floor) * dev;

    private static int PeakOf(Boxer proto, int birth)
    {
        if (proto.PrimeYears is string py && birth > 0)
        {
            int y0 = FirstYear(py);
            if (y0 > 0) return Math.Clamp((y0 + 2) - birth, 25, 34);
        }
        return 28;
    }

    private static int FirstYear(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        for (int i = 0; i + 4 <= s.Length; i++)
            if (char.IsDigit(s[i]) && char.IsDigit(s[i + 1]) && char.IsDigit(s[i + 2]) && char.IsDigit(s[i + 3]))
                return int.Parse(s.Substring(i, 4));
        return 0;
    }

    /// <summary>Build a fresh player fighter: a green starter with the ceiling still ahead of them.</summary>
    public static Boxer CreatePlayer(Random rng, string name, string country, WeightClass wc, int potential, int startAge = 18)
    {
        int peak = 28;
        double dev = BoxerFactory.Development(startAge, peak);
        int Ceil(int spread) => Ratings.Clamp(potential + rng.Next(-spread, spread + 1));
        int Sc(int c, double d) => Ratings.Clamp((int)Math.Round(c * d));
        var r = new Ratings
        {
            // Power, defence, speed and the punch arsenal are there from the start...
            Power = Sc(Ceil(12), Lerp(dev, 0.85)),
            Speed = Sc(Ceil(12), Lerp(dev, 0.82)),
            Defense = Sc(Ceil(13), Lerp(dev, 0.72)),
            Accuracy = Sc(Ceil(12), Lerp(dev, 0.58)),
            // ...stamina, conditioning and the finer timing are what experience builds.
            Stamina = Sc(Ceil(12), dev),
            Conditioning = Sc(Ceil(12), dev),
            Chin = Sc(Ceil(14), Lerp(dev, 0.7)),
            CutResistance = Ceil(18),
            Aggression = Ceil(20),
            Heart = Ceil(16)
        };
        var player = new Boxer
        {
            Id = -1, Name = name, Country = country, WeightClass = wc,
            Ratings = r, Age = startAge, PeakAge = peak, Potential = potential, RankPoints = 1000
        };
        // Every other fighter gets a reach; the player was left on zero, and ReachEdge treats zero as "unknown"
        // and returns no effect — so the range battle simply never applied to the player's fights, in either
        // direction. Give him a frame like anyone else's.
        player.Reach = Physique.ReachInchesFor(wc, name);
        return player;
    }
}
