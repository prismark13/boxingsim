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
public sealed partial class CareerGame
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

    /// <summary>Headlines ever written — which is NOT <c>_log.Count</c>.
    ///
    /// The log is capped and drops from the front, so once it is full every add is matched by a remove and its
    /// Count stops moving. A step that asked "how many are there now, minus how many there were before" got
    /// zero from that moment on, for the rest of the career. Fifteen years of a world is about fifteen hundred
    /// headlines, so a long career reached the cap and the build-up feed went permanently silent while the
    /// sport around it carried on making news exactly as loudly as before — a division with three thousand
    /// active fighters reporting a hundred headlines a year, and not one of them shown.
    ///
    /// A position in a stream cannot be the length of a window onto it. Marks are taken against this.</summary>
    private long _logWrites;
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

    /// <summary>A year's honours that the player has not been shown yet.
    ///
    /// The awards were computed the moment the calendar turned and then filed silently on a page he had to
    /// go and look at. A year of the sport ending is an occasion — somebody was fighter of the year, some
    /// night was the fight of the year — and the sim knew all of it and said nothing. The world raises this
    /// as it passes the new year; whoever is watching decides what to do about it.</summary>
    /// <summary>The last card the player actually boxed on, with its results — the whole bill, his own bout
    /// marked. Kept because <see cref="Bill"/> only ever describes the fight that is NEXT.</summary>
    public IReadOnlyList<BillLine> LastNightsCard { get; private set; } = Array.Empty<BillLine>();

    /// <summary>The last calendar year whose turn-of-the-year pass has been run. Nothing else decides when
    /// the world ages: see CatchUpYears.</summary>
    private int _lastYearRun;

    public AwardsYear? UnseenAwards { get; private set; }

    /// <summary>Mark the honours as read.</summary>
    public void AwardsSeen() => UnseenAwards = null;
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
    private FightOffer? _offer;
    /// <summary>The fight on the table. Setting it draws up the card it sits on — the hall, the size of the
    /// night, his billing, and the rest of the bill — because a card is something you read BEFORE you agree
    /// to be on it. Six places in the sim hand the player a new offer; hanging this off the property means
    /// none of them can forget.</summary>
    public FightOffer? Offer
    {
        get => _offer;
        private set
        {
            _offer = value;
            // A universe has no player and shows no card, so it must not draw one — doing so consumed
            // random numbers for a bill nobody would ever see and shifted every result in the world after it.
            if (value is null || Universe is not null || Player.Retired)
            { _billed.Clear(); _undercard.Clear(); Hall = null; }
            else { SetTheCard(); AnnounceUndercard(); }
        }
    }
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
            Date = Date.AddDays(Math.Min(days, target.DayNumber - Date.DayNumber));
            // There used to be an "if this step crossed New Year, run the yearly pass" here as well. Once
            // CatchUpYears started asking which years had not been run, that was a second pass over the same
            // year — but only when the STEP crossed the boundary, not when a card carried the clock over, so
            // it read as a world ageing about a third faster than the calendar rather than twice. Sixteen and
            // a half years passed and every fighter in it aged twenty-one.
            CatchUpYears();
            RunEvent();
            CatchUpYears();
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
                for (int i = 0; i < 24; i++) AddActive(_factory.CreateExisting(wc, GeneratedCap, Year));

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
        _logWrites = 0;
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
        _logWrites = _log.Count;   // a reopened career carries on counting from where the save left off
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

}
