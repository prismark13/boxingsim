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
    /// <summary>What the sport has reported. It owns the cap and the count of headlines ever written, which is
    /// not the same number as how many are kept — see the type.</summary>
    private readonly NewsLog _news = new();
    /// <summary>The Hall, and the long memory that decides who joins it — a man's best rating ever, the
    /// divisions he took belts in, whether he was ever champion. None of it is readable off a 38-year-old.</summary>
    private readonly HallOfFame _hall = new();

    /// <summary>The year's honours — the shortlist of bouts still in contention and every year already decided.</summary>
    private readonly AwardsBoard _awards = new();
    public IReadOnlyList<AwardsYear> Awards => _awards.NewestFirst;

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

    /// <summary>Who is injured. Constructed here and handed the clock; nothing else can reach the layoff dates.</summary>
    private readonly MedicalRoom _medical;
    public IReadOnlyList<HallOfFamer> HallOfFame => _hall.ByPrestige;
    private const string UndisputedBelt = "Undisputed";
    private const int MaxFightsPerYear = 8;   // nobody boxes more than 8 times in a calendar year

    public Boxer Player { get; }

    // Every division runs at once. Belts are held per weight class by the registry; the player-facing
    // Champion/WbcChampion views resolve to the player's CURRENT division (which changes if he moves up in
    // weight), and every one of them is a question for it rather than state kept here.
    private readonly TitleRegistry _titles;

    // The division the world-sim is currently resolving (RunEvent/RunNpcSeason loop over all eight and set
    // this). Cursor-scoped belt accessors let the season logic stay division-agnostic.
    // There was a _cursor here — "the division currently being resolved" — with Champ, Wbc, Ibf, ActiveHere
    // and CursorUnified reading off it, and fourteen places that set it and mostly remembered to put it back.
    // It was the world clock's fault in miniature: ambient context standing in for a parameter, where the
    // only thing keeping a headline in the right division was that nothing had moved the cursor in between.
    // The weight is passed now, so it cannot be wrong.

    public WeightClass Division => Player.WeightClass;
    public Boxer? Champion { get => _titles.Champ(Division); }
    public Boxer? WbcChampion { get => _titles.Wbc(Division); }
    public Boxer? IbfChampion { get => _titles.Ibf(Division); }
    /// <summary>Deliberately the name on the line rather than the guarded reader every other caller uses: the
    /// player-facing view has always shown it unchecked, and the two disagree for exactly as long as it takes
    /// the yearly pass to clear a line whose holder has retired.</summary>
    public Boxer? LinealChampion { get => _titles.LinealOnRecord(Division); }
    private Boxer? ChampOf(WeightClass wc) => _titles.Champ(wc);
    private Boxer? WbcOf(WeightClass wc) => _titles.Wbc(wc);
    private Boxer? IbfOf(WeightClass wc) => _titles.Ibf(wc);
    private Boxer? LinealOf(WeightClass wc) => _titles.Lineal(wc);
    public string LinealBelt => _titles.LinealBelt;
    public string PrimaryBelt => _titles.PrimaryBelt;
    public bool WbcActive => _titles.WbcActive;
    public bool IbfActive => _titles.IbfActive;
    /// <summary>A division only exists from its founding year (the junior/intermediate classes came later).</summary>
    /// <summary>Whether a division exists in this world at all. Two things can switch one off: the year (a
    /// division cannot run before it was founded), and a universe that was asked for a shorter list. The second
    /// is a real exclusion, not a filter on what is shown - no cards, no seasons, no debuts and no belts happen
    /// outside the chosen divisions, so a one-division universe is one division of boxing and nothing else.</summary>
    private bool DivisionActive(WeightClass wc) =>
        Year >= wc.FoundedYear()
        && (Universe is null || Universe.Divisions.Count == 0 || Universe.Divisions.Contains(wc));
    /// <summary>True when one man holds both world belts in the player's division.</summary>
    public bool Unified => _titles.Unified(Division);
    private bool UnifiedIn(WeightClass wc) => _titles.Unified(wc);
    public DateOnly Date { get; private set; }
    public int Year => Date.Year;

    /// <summary>Move the world clock. The ONLY thing that may, outside setting up a world.
    ///
    /// Resolving a bout used to move it, so a step that meant "one week" could advance a month: each fight on
    /// a card pushed the date on, and the next fight started from there. A universe reported WEEK 30 beside a
    /// date two and a bit years along, and a wait for fight night overshot the fight. Every bout carries its
    /// own night now, and the clock belongs to the calendar.
    ///
    /// It does not throw on a backwards move. A clock anomaly should not end a career somebody has played for
    /// hours — the tests assert the guard is never reached, which is the right place for that to be loud.</summary>
    private void AdvanceClockTo(DateOnly d)
    {
        if (d <= Date) return;
        Date = d;
    }
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

    public IReadOnlyList<CareerEvent> Log => _news.All;
    public IReadOnlyList<TitleReign> Reigns => _titles.Reigns;
    public int TitleDefenses => _titles.TitleDefenses;
    public int DaysAsChampion => _titles.DaysAsChampion;
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
            AdvanceClockTo(Date.AddDays(Math.Min(days, target.DayNumber - Date.DayNumber)));
            // There used to be an "if this step crossed New Year, run the yearly pass" here as well. Once
            // CatchUpYears started asking which years had not been run, that was a second pass over the same
            // year — but only when the STEP crossed the boundary, not when a card carried the clock over, so
            // it read as a world ageing about a third faster than the calendar rather than twice. Sixteen and
            // a half years passed and every fighter in it aged twenty-one.
            // There was a second CatchUpYears after RunEvent too, because a card could carry the clock over
            // New Year by itself. Cards do not move the clock any more, so a step crosses at most the boundary
            // it was going to cross, and one pass before the cards run is the whole of it.
            WorldTick();
        }
    }

    /// <summary>Everything the world does at one tick of the clock, whichever path drove it.
    ///
    /// There are two, and there always have been: AdvanceSome for a career, AdvanceWorld for a universe. They
    /// each carried their own copy of this sequence, so a step added to one of them silently did not happen in
    /// the other — and that is not a hypothetical. Vacant-title fights were booked here and never fought,
    /// because the settle went into the career path only: a universe's belts sat empty for a decade, the
    /// welterweight title was vacant ninety per cent of the time, and the WBC never came into existence at
    /// all. One sequence, called from both, so the next thing added to a tick cannot go missing from half of
    /// the world.</summary>
    private void WorldTick()
    {
        CatchUpYears();
        SettleDueVacantTitles();   // a belt ordered months ago is fought for when the night comes round
        SettleDueStepUps();        // and a man who decided to move up does it in the weeks after that fight
        RunEvent();
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
    /// <summary>The line of succession for one belt in one division, oldest first — every man who has held it
    /// since the world began, who he took it from, and who took it off him.</summary>
    public IReadOnlyList<BeltReign> LineageOf(WeightClass wc, string belt) => _titles.LineageOf(wc, belt);

    /// <summary>The belts a division has had a line for, in the order they are always listed.</summary>
    public IReadOnlyList<string> BeltsOf(WeightClass wc) =>
        new[] { PrimaryBelt, "WBC", "IBF", LinealBelt }
            .Where(b => _titles.LineageOf(wc, b == LinealBelt ? "Ring" : b).Count > 0)
            .ToList();

    /// <summary>Every reign the world has recorded, for the save.</summary>
    internal IReadOnlyList<BeltReign> AllReigns => _titles.Lineage;

    public IReadOnlyList<DivisionChampions> ChampionsBoard() =>
        LiveDivisions.Select(wc => new DivisionChampions(
            wc,
            ChampOf(wc), DefensesOf(wc, "WBA", ChampOf(wc)?.Id ?? 0),
            WbcActive ? WbcOf(wc) : null, DefensesOf(wc, "WBC", WbcOf(wc)?.Id ?? 0),
            IbfActive ? IbfOf(wc) : null, DefensesOf(wc, "IBF", IbfOf(wc)?.Id ?? 0),
            LinealOf(wc), DefensesOf(wc, "Ring", LinealOf(wc)?.Id ?? 0),
            UndisputedOf(wc))).ToList();

    private Boxer? UndisputedOf(WeightClass wc) => _titles.Undisputed(wc);

    private void Defended(WeightClass wc, string belt, int holder) => _titles.Defended(wc, belt, holder);
    public int DefensesOf(WeightClass wc, string belt, int holderId) => _titles.Defenses(wc, belt, holderId);

    /// <summary>The world belts a fighter currently holds in his division, with the defence count of each —
    /// for the card's championship line and to show all straps on a unified champion.</summary>
    public IEnumerable<(string Belt, int Defenses)> BeltsHeld(Boxer b) => _titles.BeltsHeld(b);
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
        var (champions, contenders) = BoardOf(wc, take);
        return champions.Concat(contenders).Take(take).ToList();
    }

    /// <summary>A division split the way a board is actually read: the men holding belts, and the contenders
    /// behind them.
    ///
    /// The two were one list, with the champions occupying the first rows and the contenders numbered from one
    /// underneath them. Every number was correct and the page still read as wrong, because with three champions
    /// up top the man labelled #5 is sitting on the eighth row and the eye counts rows. Handing the two out
    /// separately lets the champions be shown as what they are — a block, by belt — and lets #1 be the first
    /// contender row rather than the fourth thing down.
    ///
    /// One source for both halves, because the number printed beside a man ANYWHERE comes from this list (see
    /// BoardPlace). When the page and the fight offer each worked out a place for themselves, they disagreed.</summary>
    public (IReadOnlyList<Boxer> Champions, IReadOnlyList<Boxer> Contenders) BoardOf(WeightClass wc, int take = 15)
    {
        var champions = ActiveIn(wc).Where(IsWorldChampion).OrderByDescending(RankScore).ToList();
        var champIds = champions.Select(b => b.Id).ToHashSet();
        var contenders = ActiveIn(wc).Where(b => RankedContender(b) && !champIds.Contains(b.Id))
                                     .OrderByDescending(RankScore).ToList();

        // A board is never half empty. To be a RANKED contender a man needs twenty bouts and a 65% win rate,
        // and there are stretches - a young world, a generation retiring together - where a division of two
        // hundred active fighters has only a handful who clear both. Real bodies rank someone regardless, so
        // the rest of the list is topped up with the best of who is actually there.
        if (contenders.Count < take)
        {
            var have = contenders.Select(b => b.Id).ToHashSet();
            contenders.AddRange(ActiveIn(wc).Where(b => !have.Contains(b.Id) && !champIds.Contains(b.Id)
                                                        && ProFights(b) >= 8)
                                            .OrderByDescending(RankScore)
                                            .Take(take - contenders.Count));
        }
        return (champions, contenders.Take(take).ToList());
    }

    /// <summary>True if the fighter currently holds any world belt (WBA/WBC/IBF) in his division.</summary>
    public bool IsWorldChampion(Boxer b) => _titles.HoldsAnyWorldBelt(b);

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
        int divs = _hall.TitleDivisionCount(b.Id);
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
        int weightTitles = _hall.TitleDivisionCount(b.Id);
        return new Achievements(belts, lineal, UndisputedOf(b.WeightClass)?.Id == b.Id, defences, weightTitles, WorldTitleWins(b));
    }

    /// <summary>The brightest prospects in a division — promising young fighters not yet world-ranked.</summary>
    public IReadOnlyList<Boxer> ProspectsOf(WeightClass wc, int take = 12) =>
        ActiveIn(wc).Where(b => b.Id != Player.Id && IsProspect(b) && ProFights(b) >= 3)
                    .OrderByDescending(b => b.Potential).ThenByDescending(b => b.Overall)
                    .Take(take).ToList();
    public IReadOnlyList<CareerEvent> RecentLog(int n) => _news.Recent(n);

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
        _medical = new MedicalRoom(() => Date);
        _titles = new TitleRegistry(() => Date);
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
        // Everything from here to the player's first day is history being WRITTEN rather than lived: the clock
        // sits on 1 January and a whole year is laid out across its months at once. That is the only time a
        // bout may be dated ahead of the clock — see NoLaterThanToday.
        _writingHistory = true;
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
            if (champ is not null) { _titles.SetChamp(wc, champ); champ.IsChampion = true; }
        }

        // Run the world forward to the player's debut year — a full season each year.
        for (int y = seedYear; y < startYear; y++)
        {
            Date = new DateOnly(y, 1, 1);
            InjectDebuts();
            RunNpcSeason();
            AgeRetireCrown();
            // The warm-up never ticks the clock, so nothing else would ever fight the vacancies it creates —
            // and a career would then open on belts that had been "ordered" decades ago.
            SettleDueVacantTitles();
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
                if (champ is not null) { _titles.SetChamp(wc, champ); champ.IsChampion = true; }
            }
            UpdateBeltsFor(wc);
        }

        _writingHistory = false;   // from here the world is lived a day at a time

        // The decade of build-up isn't the player's story — start his timeline (and the Hall of Fame) clean, so
        // the Hall fills with fighters who retire during his career rather than a generation he never saw.
        _news.Clear();
        _awards.ClearShortlist();
        if (!seedHistory) { _hall.Clear(); _awards.ClearDecided(); }   // when seeding history, keep the past greats + their era's awards
        Date = new DateOnly(startYear, 3, 1);
        if (Champion is not null) LogEvent($"{Champion.Name} reigns as {PrimaryBelt} champion as {player.Name} turns pro.", kind: "title");

        Player.WeightClass = division;
        AddActive(Player);
        CapStarter(Player);
        // Unconditionally, even for a universe's ghost — who is retired before the first week and will never
        // be offered anything. It still draws its random numbers, and every result in the world sits behind
        // them: skipping it here moved the universe fingerprint.
        NewSlate(evenIfRetired: true);
    }

    /// <summary>Rehydrate a saved career.</summary>
    private CareerGame(CareerSave s, Random rng)
    {
        _rng = rng;
        _factory = new BoxerFactory(rng);
        _careers = new CareerProgression(rng);
        _engine = new FightEngine(rng);
        _oppNames = new NameGenerator(rng);
        _medical = new MedicalRoom(() => Date);
        _titles = new TitleRegistry(() => Date);
        Date = ParseDate(s.Date, new DateOnly(2000, 1, 1));

        var byId = new Dictionary<int, Boxer>();
        foreach (var bs in s.Roster) { var b = bs.ToBoxer(); _roster.Add(b); byId[b.Id] = b; }
        Player = byId[s.PlayerId];
        // Don't let fighters generated during continued play collide with anyone already on the roster.
        var reserved = _roster.Select(b => b.Name).ToList();
        _factory.Reserve(reserved);
        _oppNames.Reserve(reserved);
        _factory.StartIdsAt(_roster.Select(b => b.Id).Append(Player.Id).DefaultIfEmpty(0).Max() + 1);
        foreach (var kv in s.Champions) if (Enum.TryParse<WeightClass>(kv.Key, out var wc) && byId.TryGetValue(kv.Value, out var c)) _titles.LoadBelt(wc, "WBA", c);
        foreach (var kv in s.WbcChampions) if (Enum.TryParse<WeightClass>(kv.Key, out var wc) && byId.TryGetValue(kv.Value, out var c)) _titles.LoadBelt(wc, "WBC", c);
        foreach (var kv in s.IbfChampions) if (Enum.TryParse<WeightClass>(kv.Key, out var wc) && byId.TryGetValue(kv.Value, out var c)) _titles.LoadBelt(wc, "IBF", c);
        foreach (var kv in s.LinealChampions) if (Enum.TryParse<WeightClass>(kv.Key, out var wc) && byId.TryGetValue(kv.Value, out var c)) _titles.LoadBelt(wc, "Ring", c);
        _lastTitleShot = s.LastTitleShot;
        if (s.WarmupUntilFights > 0) _warmupUntil[s.PlayerId] = s.WarmupUntilFights;
        foreach (var v in s.VacantTitleBouts)
            if (Enum.TryParse<WeightClass>(v.Div, out var vd) && DateOnly.TryParse(v.On, out var von))
                _vacantBouts.Add((vd, v.Belt, von));
        if (s.ShotBelt is string sb) _shot = new TitleShot(sb, s.ShotChampionId, s.ShotGrantedAtFights);
        _declined.AddRange(s.Declined);
        foreach (var h in s.Historical) _historical[h.Id] = (h.Prime.ToRatings(), h.Peak);
        foreach (var a in s.PlayerArc) _playerArc.Add((a.Fights, a.Age, a.R.ToRatings()));
        foreach (var f in s.Future) _future.Add((f.DebutYear, f.Proto.ToBoxer(), f.DebutAge, f.Peak));
        _news.Restore(s.Log.Select(e =>
        {
            var on = ParseDate(e.On, Date);
            return new CareerEvent
            {
                On = on, Text = e.Text, PlayerBout = e.PlayerBout, Kind = e.Kind,
                Div = Enum.TryParse<WeightClass>(e.Div, out var ed) ? ed : null,
                Bout = e.BoutWinner is not null && e.BoutLoser is not null
                       ? new BoutRef(e.BoutWinner, e.BoutLoser, on) : null
            };
        }));
        // Cards are due a fortnight after the last one, and when the last one was is not saved. Start the
        // clock from today rather than from never: otherwise every division would put a card on the instant a
        // career was opened, and saving and reloading would be a way of buying extra fights.
        foreach (var wc in AllDivisions) _lastCard[wc] = Date;
        foreach (var r in s.Lineage)
            _titles.LoadLineage(new BeltReign
            {
                Division = Enum.TryParse<WeightClass>(r.Div, out var lw) ? lw : WeightClass.Heavyweight,
                Belt = r.Belt, HolderId = r.HolderId, Holder = r.Holder, Country = r.Country,
                Won = ParseDate(r.Won, Date),
                Lost = string.IsNullOrEmpty(r.Lost) ? null : ParseDate(r.Lost, Date),
                TookFrom = r.TookFrom, LostTo = r.LostTo, Defences = r.Defences,
            });
        foreach (var r in s.Reigns) _titles.LoadReign(new TitleReign { Belt = r.Belt, Won = ParseDate(r.Won, Date), Lost = string.IsNullOrEmpty(r.Lost) ? null : ParseDate(r.Lost, Date), Defenses = r.Defenses });
        foreach (var kv in s.Regional)
        {
            var parts = kv.Key.Split('|');
            if (parts.Length == 2 && Enum.TryParse<WeightClass>(parts[0], out var wc) && byId.TryGetValue(kv.Value, out var rb))
                _titles.SetRegional(wc, parts[1], rb);
        }
        foreach (var m in s.HallOfFame)
        {
            var memberDiv = Enum.TryParse<WeightClass>(m.Division, out var md) ? md : WeightClass.Heavyweight;
            var history = m.History.Select(h => new BoutLine
            {
                Date = ParseDate(h.Date, Date), Opponent = h.Opponent, Result = h.Result.Length > 0 ? h.Result[0] : 'D',
                Method = h.Method, Round = h.Round, KdFor = h.KdFor, KdAgainst = h.KdAgainst, Note = h.Note, Cards = h.Cards,
                // Saves written before the Hall carried the weight fall back to the division he is
                // REMEMBERED at — right for the many who never moved, and the best guess for the rest.
                Division = Enum.TryParse<WeightClass>(h.Div, out var hd) ? hd : memberDiv,
                CareerEndingInjury = h.CareerEndingInjury
            }).ToList();

            // What he is REMEMBERED for is frozen at induction, so a man enshrined under the old code kept
            // whatever the once-a-year sampling had written down — at most one division, which is why the
            // Hall had no multi-weight champions in it at all. His ledger still knows: a won world-title
            // bout carries the weight it was made at. Rebuilt from that rather than asking a player twenty
            // years into a career to start again. An old save whose bouts have no weight recovers only the
            // division he is remembered at, which is what it already said — no worse, and never wrong.
            var divs = m.TitleDivisions.Select(x => Enum.TryParse<WeightClass>(x, out var d) ? (WeightClass?)d : null)
                                       .Where(x => x is not null).Select(x => x!.Value).ToHashSet();
            foreach (var h in history)
                if (h.Result == 'W' && IsWorldTitleNote(h.Note)) divs.Add(h.Division);

            _hall.Load(new HallOfFamer
            {
                Id = m.Id, Name = m.Name, Nickname = m.Nickname, Country = m.Country,
                Division = memberDiv,
                Record = m.Record, PeakOverall = m.PeakOverall, PeakClass = m.PeakClass, Defenses = m.Defenses, WasChampion = m.WasChampion,
                WeightTitles = Math.Max(m.WeightTitles, divs.Count),
                TitleDivisions = divs.OrderBy(d => (int)d).ToList(),
                Age = m.Age, Year = m.Year,
                History = history
            });
        }
        AwardWinner AwLoad(AwardWinnerSave w) => new()
        {
            Name = w.Name, Detail = w.Detail,
            Div = Enum.TryParse<WeightClass>(w.Div, out var wd) ? wd : WeightClass.Heavyweight,
            Commentary = w.Commentary,
            // Saves written before awards carried their fight simply have none; the citation still reads.
            Bout = w.BoutWinner is not null && w.BoutLoser is not null && DateOnly.TryParse(w.BoutDate, out var bd)
                   ? new BoutRef(w.BoutWinner, w.BoutLoser, bd) : null
        };
        foreach (var a in s.Awards) _awards.Load(new AwardsYear
        {
            Year = a.Year,
            FighterOfYear = a.FighterOfYear.Select(AwLoad).ToList(),
            UpsetOfYear = a.UpsetOfYear.Select(AwLoad).ToList(),
            KnockoutOfYear = a.KnockoutOfYear.Select(AwLoad).ToList(),
            FightOfYear = a.FightOfYear.Select(AwLoad).ToList(),
        });
        foreach (var id in s.EverChampion) _hall.MarkChampion(id);
        foreach (var kv in s.PeakOverall) if (int.TryParse(kv.Key, out var id)) _hall.LoadPeakOverall(id, kv.Value);
        foreach (var kv in s.PeakClass) if (int.TryParse(kv.Key, out var id)) _hall.LoadPeakClass(id, kv.Value);
        foreach (var kv in s.TitleDivisions)
            if (int.TryParse(kv.Key, out var id))
                _hall.LoadTitleDivisions(id, kv.Value.Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => Enum.TryParse<WeightClass>(x, out var w) ? (WeightClass?)w : null)
                    .Where(w => w is not null).Select(w => w!.Value).ToHashSet());

        // And the same repair for the living. Careers carried across the fix have belts recorded only where
        // the old New-Year sampling happened to catch a man holding one, so a fighter who won at light-middle
        // in March and moved up is on record as a one-division champion. His ledger says otherwise. Doing it
        // on LOAD rather than on save means a career already on disk is mended by being opened.
        foreach (var b in _roster)
            foreach (var h in b.History)
                if (h.Result == 'W' && IsWorldTitleNote(h.Note))
                    _hall.MarkTitleDivision(b.Id, h.Division);
        foreach (var kv in s.BeltDefenses)
        {
            var parts = kv.Key.Split('|');
            if (parts.Length == 3 && Enum.TryParse<WeightClass>(parts[0], out var wc) && int.TryParse(parts[2], out var hid))
                _titles.LoadDefenses(wc, parts[1], hid, kv.Value);
        }

        OfferDate = ParseDate(s.OfferDate, Date.AddDays(42));
        if (s.Offer is OfferSave o && byId.TryGetValue(o.OpponentId, out var opp))
        {
            // The saved offer goes back on the table AND onto the slate. A career reopened without this had
            // a fight in front of it and no alternatives at all, because the slate lives in memory and the
            // save predates it — so the choice silently vanished across a reload.
            //
            // A save written before slates existed keeps its single fight for this cycle, which is right: it
            // is the fight that was agreed. The next cycle draws a full slate.
            var back = new FightOffer { Opponent = opp, Rounds = o.Rounds, TitleFight = o.TitleFight, Belt = o.Belt, Context = o.Context };
            _slate.Add(back);
            Offer = back;
        }
        else
            NewSlate();
    }

    public static CareerGame Load(CareerSave save, Random rng) => new(save, rng);

    /// <summary>Snapshot the whole career for serialization.</summary>
    public CareerSave ToSave()
    {
        var s = new CareerSave
        {
            Division = Division, Date = Date.ToString("yyyy-MM-dd"), OfferDate = OfferDate.ToString("yyyy-MM-dd"),
            PlayerId = Player.Id, LastTitleShot = _lastTitleShot,
            ShotBelt = _shot?.Belt, ShotChampionId = _shot?.ChampionId ?? 0,
            ShotGrantedAtFights = _shot?.GrantedAtFights ?? 0,
            WarmupUntilFights = _warmupUntil.GetValueOrDefault(Player.Id),
            VacantTitleBouts = _vacantBouts
                .Select(v => new VacantBoutSave { Div = v.Div.ToString(), Belt = v.Belt, On = v.On.ToString("yyyy-MM-dd") })
                .ToList(),
            Declined = _declined.ToList()
        };
        foreach (var kv in _titles.AllChampions) if (kv.Value is Boxer c) s.Champions[kv.Key.ToString()] = c.Id;
        foreach (var kv in _titles.AllWbc) if (kv.Value is Boxer c) s.WbcChampions[kv.Key.ToString()] = c.Id;
        foreach (var kv in _titles.AllIbf) if (kv.Value is Boxer c) s.IbfChampions[kv.Key.ToString()] = c.Id;
        foreach (var kv in _titles.AllLineal) if (kv.Value is Boxer c) s.LinealChampions[kv.Key.ToString()] = c.Id;
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
        foreach (var e in _news.All) s.Log.Add(new CareerEventSave { On = e.On.ToString("yyyy-MM-dd"), Text = e.Text, PlayerBout = e.PlayerBout, Kind = e.Kind, Div = e.Div?.ToString(), BoutWinner = e.Bout?.Winner, BoutLoser = e.Bout?.Loser });
        foreach (var r in AllReigns) s.Lineage.Add(new BeltReignSave
        {
            Div = r.Division.ToString(), Belt = r.Belt, HolderId = r.HolderId, Holder = r.Holder,
            Country = r.Country, Won = r.Won.ToString("yyyy-MM-dd"), Lost = r.Lost?.ToString("yyyy-MM-dd"),
            TookFrom = r.TookFrom, LostTo = r.LostTo, Defences = r.Defences,
        });
        foreach (var r in _titles.Reigns) s.Reigns.Add(new TitleReignSave { Belt = r.Belt, Won = r.Won.ToString("yyyy-MM-dd"), Lost = r.Lost?.ToString("yyyy-MM-dd"), Defenses = r.Defenses });
        foreach (var (div, region, holder) in _titles.AllRegional) s.Regional[$"{div}|{region}"] = holder.Id;
        foreach (var m in _hall.All) s.HallOfFame.Add(new HallOfFamerSave
        {
            Id = m.Id, Name = m.Name, Nickname = m.Nickname, Country = m.Country, Division = m.Division.ToString(),
            Record = m.Record, PeakOverall = m.PeakOverall, PeakClass = m.PeakClass, Defenses = m.Defenses,
            WasChampion = m.WasChampion, WeightTitles = m.WeightTitles, TitleDivisions = m.TitleDivisions.Select(d => d.ToString()).ToList(), Age = m.Age, Year = m.Year,
            // The per-round grid and commentary are dropped deliberately — they are the heavy part and the
            // Hall only needs the ledger. The DIVISION is not heavy and was dropped by accident: written
            // without it, every stored bout came back as the enum's default on load, so the fix that made
            // division travel with a bout was undone by the save round-trip. It is also the only record of
            // WHERE a man won his belts once he is gone, which is what a multi-weight champion is made of.
            History = m.History.Select(h => new BoutLineSave
            {
                Date = h.Date.ToString("yyyy-MM-dd"), Opponent = h.Opponent, Result = h.Result.ToString(),
                Method = h.Method, Round = h.Round, KdFor = h.KdFor, KdAgainst = h.KdAgainst, Note = h.Note,
                CareerEndingInjury = h.CareerEndingInjury, Div = h.Division.ToString(), Cards = h.Cards
            }).ToList()
        });
        AwardWinnerSave AwSave(AwardWinner w) => new()
        {
            Name = w.Name, Detail = w.Detail, Div = w.Div.ToString(), Commentary = w.Commentary,
            BoutWinner = w.Bout?.Winner, BoutLoser = w.Bout?.Loser,
            BoutDate = w.Bout?.Date.ToString("yyyy-MM-dd")
        };
        foreach (var a in _awards.All) s.Awards.Add(new AwardsYearSave
        {
            Year = a.Year,
            FighterOfYear = a.FighterOfYear.Select(AwSave).ToList(),
            UpsetOfYear = a.UpsetOfYear.Select(AwSave).ToList(),
            KnockoutOfYear = a.KnockoutOfYear.Select(AwSave).ToList(),
            FightOfYear = a.FightOfYear.Select(AwSave).ToList(),
        });
        s.EverChampion.AddRange(_hall.EverChampions);
        foreach (var kv in _hall.PeakOveralls) s.PeakOverall[kv.Key.ToString()] = kv.Value;
        foreach (var kv in _hall.PeakClasses) s.PeakClass[kv.Key.ToString()] = kv.Value;
        foreach (var kv in _hall.TitleDivisions) s.TitleDivisions[kv.Key.ToString()] = string.Join("|", kv.Value);
        foreach (var kv in _titles.AllDefenses) s.BeltDefenses[$"{kv.Key.Div}|{kv.Key.Belt}|{kv.Key.Holder}"] = kv.Value;
        if (Offer is not null) s.Offer = new OfferSave { OpponentId = Offer.Opponent.Id, Rounds = Offer.Rounds, TitleFight = Offer.TitleFight, Belt = Offer.Belt, Context = Offer.Context };
        return s;
    }

}
