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
    private Boxer? ChampOf(WeightClass wc) => _champions.GetValueOrDefault(wc);
    private Boxer? WbcOf(WeightClass wc) => _wbc.GetValueOrDefault(wc);
    private Boxer? IbfOf(WeightClass wc) => _ibf.GetValueOrDefault(wc);
    public string PrimaryBelt => Year < 1962 ? "World" : "WBA";
    public bool WbcActive => Year >= 1963;
    public bool IbfActive => Year >= 1983;   // the IBF is the third sanctioning body; no WBO or minor belts
    /// <summary>A division only exists from its founding year (the junior/intermediate classes came later).</summary>
    private bool DivisionActive(WeightClass wc) => Year >= wc.FoundedYear();
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
    private static readonly WeightClass[] AllDivisions = WeightClasses.All;

    // ---- read-only views of any division, for the UI's cross-division picture ----
    public IReadOnlyList<WeightClass> Divisions => AllDivisions;
    /// <summary>Divisions that exist in the current year (founded and populated), heaviest first — for the UI.</summary>
    public IReadOnlyList<WeightClass> LiveDivisions => AllDivisions.Where(wc => DivisionActive(wc) && ActiveCountOf(wc) > 0).OrderByDescending(wc => (int)wc).ToList();
    public Boxer? WorldChampionOf(WeightClass wc) => ChampOf(wc);
    public Boxer? WbcChampionOf(WeightClass wc) => WbcOf(wc);
    public Boxer? IbfChampionOf(WeightClass wc) => IbfOf(wc);

    private static string BeltSlot(string belt) => belt == "WBC" ? "WBC" : belt == "IBF" ? "IBF" : "WBA";
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
        foreach (var kv in _regional.Where(kv => kv.Key.Div == wc && kv.Value.Id == b.Id))
            yield return (kv.Key.Region, 0);
    }
    public int ActiveCountOf(WeightClass wc) => _roster.Count(b => !b.Retired && b.WeightClass == wc);
    /// <summary>The top world-ranked fighters in a division (for a rankings view).</summary>
    public IReadOnlyList<Boxer> RankingOf(WeightClass wc, int take = 15) =>
        ActiveIn(wc).Where(WorldRanked).OrderByDescending(RankScore).Take(take).ToList();

    /// <summary>True if the fighter currently holds any world belt (WBA/WBC/IBF) in his division.</summary>
    public bool IsWorldChampion(Boxer b) =>
        ChampOf(b.WeightClass)?.Id == b.Id || WbcOf(b.WeightClass)?.Id == b.Id || IbfOf(b.WeightClass)?.Id == b.Id;

    /// <summary>Pound-for-pound: the best fighters across every division, ranked by ability tempered by record.
    /// Reigning world champions are strongly favoured, so the list reads like a real P4P board (champions on top).</summary>
    public IReadOnlyList<Boxer> PoundForPound(int take = 15) =>
        _roster.Where(b => !b.Retired && (WorldRanked(b) || IsWorldChampion(b)))
               .OrderByDescending(P4PScore)
               .Take(take).ToList();

    /// <summary>P4P standing: ability is the base, tempered by how much he's actually winning — losses bite and
    /// holding a world belt lifts him hard, so a filler with a losing record can't crowd out a reigning champion.</summary>
    private double P4PScore(Boxer b)
    {
        int fights = b.Record.Wins + b.Record.Losses + b.Record.Draws;
        double winRate = fights > 0 ? (b.Record.Wins + 0.5 * b.Record.Draws) / fights : 0;
        double score = b.Overall * (0.6 + 0.5 * winRate) - b.Record.Losses * 1.5;
        if (IsWorldChampion(b)) score += 14;   // a reigning world champion belongs near the top of the board
        return score;
    }

    /// <summary>The brightest prospects in a division — promising young fighters not yet world-ranked.</summary>
    public IReadOnlyList<Boxer> ProspectsOf(WeightClass wc, int take = 12) =>
        ActiveIn(wc).Where(b => IsProspect(b) && ProFights(b) >= 3)
                    .OrderByDescending(b => b.Potential).ThenByDescending(b => b.Overall)
                    .Take(take).ToList();
    public IReadOnlyList<CareerEvent> RecentLog(int n) => _log.Skip(Math.Max(0, _log.Count - n)).ToList();

    public CareerGame(int startYear, Boxer player, IEnumerable<Boxer> historicalProtos, Random rng,
                      WeightClass division = WeightClass.Heavyweight, int warmupYears = 10)
    {
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
        int seedYear = startYear - warmupYears;
        Date = new DateOnly(seedYear, 1, 1);

        // Seed each division that exists yet with a base of journeymen, alongside the real fighters.
        foreach (var wc in AllDivisions)
            if (DivisionActive(wc))
                for (int i = 0; i < 24; i++) AddActive(_factory.CreateExisting(wc, GeneratedCap));

        foreach (var proto in protos)
        {
            if (proto.DebutYear is not int debutYear) continue;
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
        _hof.Clear();
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
        _lastTitleShot = s.LastTitleShot;
        foreach (var h in s.Historical) _historical[h.Id] = (h.Prime.ToRatings(), h.Peak);
        foreach (var f in s.Future) _future.Add((f.DebutYear, f.Proto.ToBoxer(), f.DebutAge, f.Peak));
        foreach (var e in s.Log) _log.Add(new CareerEvent { On = ParseDate(e.On, Date), Text = e.Text, PlayerBout = e.PlayerBout, Kind = e.Kind, Div = Enum.TryParse<WeightClass>(e.Div, out var ed) ? ed : null });
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
                Record = m.Record, PeakOverall = m.PeakOverall, PeakClass = m.PeakClass, Defenses = m.Defenses, WasChampion = m.WasChampion, WeightTitles = m.WeightTitles, Age = m.Age, Year = m.Year,
                History = m.History.Select(h => new BoutLine
                {
                    Date = ParseDate(h.Date, Date), Opponent = h.Opponent, Result = h.Result.Length > 0 ? h.Result[0] : 'D',
                    Method = h.Method, Round = h.Round, KdFor = h.KdFor, KdAgainst = h.KdAgainst, Note = h.Note, Cards = h.Cards
                }).ToList()
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
        foreach (var f in _future) s.Future.Add(new FutureSave { DebutYear = f.DebutYear, DebutAge = f.DebutAge, Peak = f.Peak, Proto = BoxerSave.From(f.Proto) });
        foreach (var e in _log) s.Log.Add(new CareerEventSave { On = e.On.ToString("yyyy-MM-dd"), Text = e.Text, PlayerBout = e.PlayerBout, Kind = e.Kind, Div = e.Div?.ToString() });
        foreach (var r in _reigns) s.Reigns.Add(new TitleReignSave { Belt = r.Belt, Won = r.Won.ToString("yyyy-MM-dd"), Lost = r.Lost?.ToString("yyyy-MM-dd"), Defenses = r.Defenses });
        foreach (var kv in _regional) s.Regional[$"{kv.Key.Div}|{kv.Key.Region}"] = kv.Value.Id;
        foreach (var m in _hof) s.HallOfFame.Add(new HallOfFamerSave
        {
            Id = m.Id, Name = m.Name, Nickname = m.Nickname, Country = m.Country, Division = m.Division.ToString(),
            Record = m.Record, PeakOverall = m.PeakOverall, PeakClass = m.PeakClass, Defenses = m.Defenses,
            WasChampion = m.WasChampion, WeightTitles = m.WeightTitles, Age = m.Age, Year = m.Year,
            History = m.History.Select(h => new BoutLineSave
            {
                Date = h.Date.ToString("yyyy-MM-dd"), Opponent = h.Opponent, Result = h.Result.ToString(),
                Method = h.Method, Round = h.Round, KdFor = h.KdFor, KdAgainst = h.KdAgainst, Note = h.Note, Cards = h.Cards
            }).ToList()
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
        string? note = belt is not null ? $"{belt} title" : (Offer.Context is "eliminator" ? "eliminator" : null);
        var res = _engine.Simulate(Player, opp, Offer.Rounds);
        ApplyOutcome(res, Player, opp, note);

        string verb = res.IsDraw ? "drew with" : (res.Winner!.Id == Player.Id ? "beat" : "lost to");
        string how = res.IsDraw ? Offer.Rounds + "-round draw" : res.Method;
        LogEvent($"{Player.Name} {verb} {opp.Name} ({how}){(belt is not null ? $" — {belt} TITLE" : "")}", playerBout: true);

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
    public void DeclineOffer()
    {
        if (Player.Retired) return;
        AdvanceTo(Date.AddDays(21 + _rng.Next(21)));
        Offer = Player.Retired ? null : BuildOffer();
    }

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
            if (yearTurned) { InjectDebuts(); AgeRetireCrown(); }
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
            int debuts = 14 + _rng.Next(10);
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

            // Track Hall-of-Fame credentials: best rating ever reached, and whether he ever held a world belt.
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
                if (winner is not null)
                {
                    _champions[wc] = winner; winner.IsChampion = true; _everChampion.Add(winner.Id);
                    LogEvent($"{winner.Name} wins the vacant {PrimaryBelt} title.", winner.Id == Player.Id, kind: "title", div: wc);
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
        Date = SpreadDate(Date.Year);
        var res = FastBout(field[0], field[1], 12);
        ApplyOutcome(res, field[0], field[1], $"{belt} title");
        var winner = res.IsDraw ? field[0] : res.Winner!;   // a draw leaves the belt with the higher-ranked man
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
        int defenses = _beltDefenses.Where(kv => kv.Key.Holder == b.Id).Sum(kv => kv.Value);
        int weightTitles = _titleDivisions.TryGetValue(b.Id, out var tds) ? tds.Count : (wasChamp ? 1 : 0);
        // A real champion with a genuine reign (3+ defences) or a multi-weight champion — but only a true top-tier
        // fighter (peakClass floor keeps journeyman champions of a thin division out) — or an outright elite talent.
        bool worthy = (((wasChamp && defenses >= 3) || weightTitles >= 2) && peakClass >= 8) || peak >= 88;
        if (!worthy) return false;

        _hof.Add(new HallOfFamer
        {
            Id = b.Id, Name = b.Name, Nickname = b.Nickname, Country = b.Country, Division = b.WeightClass,
            Record = b.Record.ToString(), PeakOverall = peak, PeakClass = peakClass, Defenses = defenses, WasChampion = wasChamp,
            WeightTitles = weightTitles, Age = b.Age, Year = Date.Year,
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

    /// <summary>Log a title event, tagged with the division being simulated so the news feed can filter it.</summary>
    private void LogTitle(string text) => LogEvent(text, kind: "title", div: _cursor);

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
        var vacated = BeltsHeld(b).Select(x => x.Belt).ToList();
        if (b.IsChampion) b.IsChampion = false;
        if (ChampOf(from)?.Id == b.Id) _champions[from] = null;
        if (WbcOf(from)?.Id == b.Id) _wbc[from] = null;
        if (IbfOf(from)?.Id == b.Id) _ibf[from] = null;
        foreach (var region in RegionalBelts) if (_regional.GetValueOrDefault((from, region))?.Id == b.Id) _regional.Remove((from, region));
        if (vacated.Count > 0 && (b.Id == Player.Id || from == Division))
            LogEvent($"{b.Name} relinquishes the {string.Join(", ", vacated)} title{(vacated.Count > 1 ? "s" : "")} to move up to {to.DisplayName()}.", b.Id == Player.Id, kind: "title", div: from);
        b.WeightClass = to;
        RebalanceRatings(b.Ratings);
        b.Potential = b.Overall;
        if (_historical.TryGetValue(b.Id, out var h)) { var prime = h.Prime.Clone(); RebalanceRatings(prime); _historical[b.Id] = (prime, h.Peak); }
        if (warmup) { _warmupUntil[b.Id] = ProFights(b) + 1 + _rng.Next(4); StepUpsPerformed++; }
    }

    /// <summary>Count of organic career move-ups performed (excludes new-division seeding). Diagnostics/tests.</summary>
    public int StepUpsPerformed { get; private set; }

    /// <summary>The escalating champion step-up hazard, exposed for verification/tuning.</summary>
    public static double DefenceStepUpHazardAt(int defenceNumber) => DefenceStepUpHazard(defenceNumber);

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
        if (!_historical.ContainsKey(b.Id)) return true;
        return b.TopWeight is WeightClass top && (int)to <= (int)top;
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
    }

    private void TryQueueStepUp(Boxer? b, double p)
    {
        if (b is null || b.Id == Player.Id || b.Retired || p <= 0) return;
        if (_stepUpQueued.Contains(b.Id)) return;
        if (NextActiveUp(b.WeightClass) is not WeightClass to || !StepUpAllowed(b, to)) return;
        if (_rng.NextDouble() < p) _stepUpQueued.Add(b.Id);
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
        if (CursorUnified)
        {
            var c = Champ!;
            if (c.Id != Player.Id && DaysSinceLastBout(c) >= 70 && _rng.NextDouble() < 0.12)   // ~2–3 defences a year, min ~10 weeks apart
            {
                if (_rng.NextDouble() < 0.10) RelinquishBelt(c);   // ~1 in 10: ducks a mandatory and gives up a belt
                else UnifiedDefence(c);
            }
        }
        else
        {
            if (Champ is not null && Champ.Id != Player.Id && DaysSinceLastBout(Champ) >= 70 && _rng.NextDouble() < 0.12)   // ~2–3 defences a year, min ~10 weeks apart
            {
                var ch = PickChallenger(Champ, Wbc);
                if (ch is not null)
                {
                    var res = FastBout(Champ, ch, 12);
                    ApplyOutcome(res, Champ, ch, $"{PrimaryBelt} title");
                    if (!res.IsDraw && res.Winner!.Id == ch.Id) { LogTitle($"{ch.Name} DETHRONES {Champ.Name} for the {PrimaryBelt} title!"); CrownChampion(ch); }
                    else { Defended(_cursor, "WBA", Champ.Id); LogTitle($"{Champ.Name} retains the {PrimaryBelt} title against {ch.Name}."); ConsiderTitleStepUp(Champ); }
                }
            }
            if (Wbc is not null && Wbc.Id != Player.Id && DaysSinceLastBout(Wbc) >= 70 && _rng.NextDouble() < 0.12)   // ~2–3 defences a year, min ~10 weeks apart
            {
                var ch = PickChallenger(Wbc, Champ);
                if (ch is not null)
                {
                    var res = FastBout(Wbc, ch, 12);
                    ApplyOutcome(res, Wbc, ch, "WBC title");
                    if (!res.IsDraw && res.Winner!.Id == ch.Id) { LogTitle($"{ch.Name} TAKES the WBC title from {Wbc.Name}!"); CrownWbc(ch); }
                    else { Defended(_cursor, "WBC", Wbc.Id); LogTitle($"{Wbc.Name} retains the WBC title against {ch.Name}."); ConsiderTitleStepUp(Wbc); }
                }
            }
        }

        // IBF title defence — the third belt, contested independently from 1983.
        if (IbfActive && Ibf is not null && Ibf.Id != Player.Id && DaysSinceLastBout(Ibf) >= 70 && _rng.NextDouble() < 0.12)
        {
            var ch = PickChallenger(Ibf, null);
            if (ch is not null)
            {
                var res = FastBout(Ibf, ch, 12);
                ApplyOutcome(res, Ibf, ch, "IBF title");
                if (!res.IsDraw && res.Winner!.Id == ch.Id) { LogTitle($"{ch.Name} TAKES the IBF title from {Ibf.Name}!"); CrownIbf(ch); }
                else { Defended(_cursor, "IBF", Ibf.Id); LogTitle($"{Ibf.Name} retains the IBF title against {ch.Name}."); ConsiderTitleStepUp(Ibf); }
            }
        }

        // Unification (rare): the two world champions finally meet, and the winner takes both belts.
        if (Champ is not null && Wbc is not null && Champ.Id != Wbc.Id
            && Champ.Id != Player.Id && Wbc.Id != Player.Id && _rng.NextDouble() < 0.015)
            Unify();

        // Regional title defences — a regional champ risks his belt against a fellow regional contender.
        foreach (var region in RegionalBelts)
        {
            if (!_regional.TryGetValue((_cursor, region), out var rc) || rc.Id == Player.Id || rc.Retired) continue;
            if (_rng.NextDouble() >= 0.05) continue;
            var chall = pool.Where(b => RegionOf(b) == region && b.Id != rc.Id && WorldRanked(b))
                            .OrderByDescending(RankScore).Skip(_rng.Next(4)).FirstOrDefault()
                        ?? pool.FirstOrDefault(b => RegionOf(b) == region && b.Id != rc.Id);
            if (chall is null) continue;
            var rres = FastBout(rc, chall, 12);
            ApplyOutcome(rres, rc, chall, $"{region} title");
            if (!rres.IsDraw && rres.Winner!.Id == chall.Id) { _regional[(_cursor, region)] = chall; LogTitle($"{chall.Name} wins the {region} title from {rc.Name}."); }
        }

        int bouts = 2 + _rng.Next(3);
        var used = new HashSet<int>();
        var top20 = Top20Ids(_cursor);
        for (int b = 0; b < bouts; b++)
        {
            int i = _rng.Next(pool.Count);
            if (used.Contains(i)) continue;
            int span = _rng.NextDouble() < 0.35 ? 40 + _rng.Next(70) : 12;   // ~35% a wide-gap tune-up
            int j = -1;
            for (int k = i + 1; k < Math.Min(pool.Count, i + span); k++)
                if (!used.Contains(k) && !BadMatch(pool[i], pool[k], top20) && (j < 0 || _rng.NextDouble() < 0.5)) j = k;
            if (j < 0) for (int k = i + 1; k < pool.Count; k++) if (!used.Contains(k) && !BadMatch(pool[i], pool[k], top20)) { j = k; break; }
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

        // Unification: about once a year the two world champions can meet — the winner takes both belts.
        if (Champ is not null && Wbc is not null && Champ.Id != Wbc.Id
            && Champ.Id != Player.Id && Wbc.Id != Player.Id && _rng.NextDouble() < 0.30)
        { Date = SpreadDate(yr); Unify(); }

        // Two undercards. Matchmaking is by ability with the better man favoured: each fighter generally
        // meets someone a notch below him (a showcase). Champions sit these out — they only defend.
        var top20 = Top20Ids(_cursor);
        for (int pass = 0; pass < 2; pass++)
        {
            var pool = ActiveHere.Where(b => b.Id != Player.Id && b.Id != Champ?.Id && b.Id != Wbc?.Id && !AtYearCap(b) && Available(b))
                             .OrderByDescending(b => b.Overall).ToList();
            int n = pool.Count;
            var used = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (used[i]) continue;
                // usually a competitive fight (a notch below); ~30% a wide-gap tune-up vs a journeyman.
                int hi = _rng.NextDouble() < 0.30 ? Math.Min(n - 1, i + 30 + _rng.Next(80))
                                                  : Math.Min(n - 1, i + 4 + _rng.Next(8));
                int j = -1;
                for (int k = i + 1; k <= hi; k++) if (!used[k] && !BadMatch(pool[i], pool[k], top20) && (j < 0 || _rng.NextDouble() < 0.5)) j = k;
                if (j < 0) for (int k = i + 1; k < n; k++) if (!used[k] && !BadMatch(pool[i], pool[k], top20)) { j = k; break; }
                if (j < 0) break;
                used[i] = used[j] = true;
                int rounds = i < 6 ? 10 : 8;
                Date = SpreadDate(yr, pass, 2);
                ApplyOutcome(FastBout(pool[i], pool[j], rounds), pool[i], pool[j]);
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
            LogTitle($"{w.Name} UNIFIES the {PrimaryBelt} and WBC titles!");
            CrownChampion(w); CrownWbc(w);
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
            LogTitle($"{ch.Name} DETHRONES {champ.Name} to take the unified {PrimaryBelt} and WBC titles!");
            CrownChampion(ch); CrownWbc(ch);
        }
        else { Defended(champ.WeightClass, "WBA", champ.Id); Defended(champ.WeightClass, "WBC", champ.Id); LogTitle($"{champ.Name} retains the unified {PrimaryBelt} and WBC titles against {ch.Name}."); ConsiderTitleStepUp(champ); }
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
            Date = SpreadDate(yr, d, titleBouts);
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
            if (challenger is null) return;
            Date = SpreadDate(yr, d, titleBouts);
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

    /// <summary>Date bout <paramref name="index"/> of <paramref name="count"/> within its own slice of the year,
    /// so a fighter's bouts in a season land a couple of months apart instead of clustering days apart.</summary>
    private DateOnly SpreadDate(int yr, int index, int count)
    {
        int slice = 365 / Math.Max(1, count);
        int day = Math.Clamp(index * slice + _rng.Next(Math.Max(1, slice - 30)), 0, 364);
        return new DateOnly(yr, 1, 1).AddDays(day);
    }

    private static int ProFights(Boxer b) => b.Record.Wins + b.Record.Losses + b.Record.Draws;

    /// <summary>How many times the player has already fought a given opponent (by name).</summary>
    private int TimesFaced(string name) => Player.History.Count(h => h.Opponent == name);

    /// <summary>How many bouts a fighter has had in the current calendar year — for the 8-a-year cap.</summary>
    private int FightsThisYear(Boxer b) => b.History.Count(h => h.Date.Year == Date.Year);
    private bool AtYearCap(Boxer b) => FightsThisYear(b) >= MaxFightsPerYear;

    /// <summary>Days since a fighter's most recent bout (large if he has no ledger) — stops a champion
    /// from defending too frequently.</summary>
    private int DaysSinceLastBout(Boxer b) => b.History.Count == 0 ? 999 : Date.DayNumber - b.History[^1].Date.DayNumber;

    /// <summary>True if pairing these two would be a mismatch a prospect shouldn't be in: a raw fighter
    /// against an elite, or anyone with under 20 pro bouts sharing the ring with a top-20 man.</summary>
    private bool BadMatch(Boxer x, Boxer y, HashSet<int> top20)
    {
        // No stale rematches: don't pair two men who've met in either's last few bouts.
        if (RecentFoes(x, 4).Contains(y.Name) || RecentFoes(y, 4).Contains(x.Name)) return true;
        // Contenders build a record before facing each other: two genuine contender-calibre fighters (a high
        // ceiling — the real-roster talents) avoid each other until BOTH have earned a ranking (20 bouts). Until
        // then they campaign against journeymen and gatekeepers rather than trading losses among themselves.
        if (x.Potential >= 72 && y.Potential >= 72 && (!WorldRanked(x) || !WorldRanked(y))) return true;
        var strong = x.Overall >= y.Overall ? x : y;
        var weak = ReferenceEquals(strong, x) ? y : x;
        if (strong.Overall >= 78 && ProFights(weak) < 12) return true;
        if ((top20.Contains(x.Id) && ProFights(y) < 20) || (top20.Contains(y.Id) && ProFights(x) < 20)) return true;
        // Prospects are protected: two unbeaten-ish young hopefuls generally avoid each other, building
        // records against journeymen and gatekeepers rather than risking a blemish early.
        if (IsProspect(x) && IsProspect(y) && _rng.NextDouble() < 0.9) return true;
        return false;
    }

    /// <summary>A prospect: a young fighter still building a record, unranked, with real headroom left.</summary>
    private bool IsProspect(Boxer b) =>
        !WorldRanked(b) && ProFights(b) < 16 && b.Age <= 27 && (b.Potential - b.Overall) >= 3;

    /// <summary>How much a young fighter over-performs his current rating — most of the ceiling he's
    /// still to realise. A can't-miss prospect (huge gap to a high ceiling) already handles lesser men
    /// with ease, so a journeyman almost never upsets a future great. Zero once he's at/past his peak.</summary>
    private static double YouthEdge(Boxer b) => b.Age <= b.PeakAge ? Math.Min(16, Math.Max(0, b.Potential - b.Overall) * 0.4) : 0;

    /// <summary>Opponents a fighter has met in his last few bouts — used to avoid stale rematches.</summary>
    private static HashSet<string> RecentFoes(Boxer b, int n) =>
        b.History.Skip(Math.Max(0, b.History.Count - n)).Select(h => h.Opponent).ToHashSet();

    /// <summary>Pick a title challenger: a top-10 contender the champion hasn't just fought. The other
    /// world champion is never an option here — they only meet in a deliberate unification bout.</summary>
    private Boxer? PickChallenger(Boxer champ, Boxer? otherChamp)
    {
        var recent = RecentFoes(champ, 4);
        var here = ActiveIn(champ.WeightClass);   // challengers come from the champion's own division
        bool Ok(Boxer b) => b.Id != Player.Id && b.Id != champ.Id
                         && (otherChamp is null || b.Id != otherChamp.Id) && WorldRanked(b) && !RecentlyMovedUp(b) && Available(b);
        // Prefer a contender he hasn't just fought and hasn't already met several times.
        var ranked = here.Where(b => Ok(b) && !recent.Contains(b.Name) && champ.History.Count(h => h.Opponent == b.Name) < 3).ToList();
        if (ranked.Count == 0) ranked = here.Where(b => Ok(b) && !recent.Contains(b.Name)).ToList();
        if (ranked.Count == 0) ranked = here.Where(Ok).ToList();   // fall back if he's fought everyone lately
        if (ranked.Count == 0) return null;
        var top10 = ranked.OrderByDescending(RankScore).Take(10).ToList();
        return top10[_rng.Next(top10.Count)];
    }

    // ---- matchmaking for the player ----

    private FightOffer BuildOffer()
    {
        int gap = Math.Max(DaysForStage(CareerStages.Of(Player)), _layoffDays);   // recovery pushes the next bout out
        // A world champion fights on a title schedule — long camps, ~3 defences a year — not a
        // club-show frequency, however young he is.
        if (Player.IsChampion || WbcChampion?.Id == Player.Id || IbfChampion?.Id == Player.Id) gap = Math.Max(gap, 98 + _rng.Next(42));
        _layoffDays = 0;
        OfferDate = Date.AddDays(gap);
        // Hard cap: no more than 8 bouts in a calendar year. Once he's had his eight, the next one waits for the new year.
        if (FightsThisYear(Player) >= MaxFightsPerYear && OfferDate.Year == Date.Year)
            OfferDate = new DateOnly(Date.Year + 1, 1, 12 + _rng.Next(24));
        var ranked = Active.OrderByDescending(b => RankScore(b)).ToList();   // index 0 = strongest
        int idx = ranked.FindIndex(b => b.Id == Player.Id);
        if (ranked.Count <= 1) return new FightOffer { Opponent = _factory.CreateProspect(Player.WeightClass, GeneratedCap), Rounds = 6, Context = "stay-busy" };

        int proFights = ProFights(Player);
        // Build a career properly: an opponent's ceiling rises with experience, so a green fighter is
        // never thrown in with the elite. World class (80+) is off-limits until he has ~20 pro fights.
        int maxOvr = proFights >= 20 ? 99 : 50 + proFights * 3 / 2;

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

        // A title shot is earned by being world-ranked (top 5) with a real body of work behind you:
        // an exceptional talent breaks in after 20 fights, an ordinary fighter needs 24. (Champions are
        // 80+, so this never lets a novice near the belt inside his first 20 bouts.)
        int fightsToRank = Player.Potential >= 85 ? 20 : 24;
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
                var chall = ranked.FirstOrDefault(b => b.Id != Player.Id && RegionOf(b) == region);
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
            if (fresh.Count > 0) opp = fresh.OrderBy(b => Math.Abs(b.Overall - opp.Overall)).First();
        }

        // FINAL guard: a fighter with fewer than 20 pro bouts hasn't earned a ranked contender yet — keep him
        // away from the division's top men BY RANKING (not just by current rating: an unproven #1 is often a
        // young fighter whose rating hasn't caught up), so he can never be leapfrogged into a top contender.
        // This runs last so nothing above (the rating cap, the rematch swap) can re-introduce a ranked man.
        if (proFights < 20)
        {
            var topRanked = ActiveIn(Player.WeightClass).Where(WorldRanked)
                                .OrderByDescending(RankScore).Take(15).Select(b => b.Id).ToHashSet();
            if (topRanked.Contains(opp.Id))
            {
                var below = ranked.Where(b => b.Id != Player.Id && !topRanked.Contains(b.Id) && b.Overall <= maxOvr
                                           && !RecentFoes(Player, 3).Contains(b.Name) && TimesFaced(b.Name) < 3).ToList();
                if (below.Count > 0) opp = below.OrderBy(b => Math.Abs(b.Overall - opp.Overall)).First();
            }
        }

        int rounds = stage == CareerStage.Starter ? 6 : idx <= 5 ? 10 : 8;
        string ctx = capped ? "building a record"
                   : target < idx ? (idx <= 5 ? "eliminator" : "step-up")
                   : stage == CareerStage.Starter || stage == CareerStage.PrePrime ? "building a record"
                   : "stay-busy";
        return new FightOffer { Opponent = opp, Rounds = rounds, Context = ctx };
    }

    // ---- outcomes & ratings ----

    private void ApplyOutcome(FightResult res, Boxer a, Boxer b, string? note = null)
    {
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
        if (ko && res.Winner is not null) res.Winner.RankPoints += 6;

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

    // ---- regional belts ----

    private readonly Dictionary<(WeightClass Div, string Region), Boxer> _regional = new();   // (division, region) → belt holder
    private static readonly string[] RegionalBelts = { "NABF", "European", "Commonwealth" };

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
            if (winner is not null) { _wbc[wc] = winner; _everChampion.Add(winner.Id); LogEvent($"{winner.Name} wins the vacant WBC title.", winner.Id == Player.Id, kind: "title", div: wc); }
        }
        // The IBF is established in 1983; fill it from the leading contender who isn't already a world champ.
        if (IbfOf(wc) is Boxer iw && iw.Retired) _ibf[wc] = null;
        if (IbfActive && IbfOf(wc) is null)
        {
            var winner = ContestVacantTitle(wc, "IBF", ChampOf(wc)?.Id ?? 0, WbcOf(wc)?.Id ?? 0);
            if (winner is not null) { _ibf[wc] = winner; _everChampion.Add(winner.Id); LogEvent($"{winner.Name} wins the vacant IBF title.", winner.Id == Player.Id, kind: "title", div: wc); }
        }

        // Regional belts: each region's title goes to its best fighter in this division who isn't a world champion.
        foreach (var region in RegionalBelts)
        {
            var champ = ChampOf(wc); var wbc = WbcOf(wc); var ibf = IbfOf(wc);
            if (_regional.TryGetValue((wc, region), out var cur) && (cur.Retired || cur.WeightClass != wc || RegionOf(cur) != region)) _regional.Remove((wc, region));
            if (!_regional.ContainsKey((wc, region)))
            {
                var pick = ActiveIn(wc).Where(b => b.Id != Player.Id && RegionOf(b) == region
                                          && b.Id != champ?.Id && b.Id != wbc?.Id && b.Id != ibf?.Id && WorldRanked(b))
                                 .OrderByDescending(RankScore).FirstOrDefault();
                if (pick is not null) _regional[(wc, region)] = pick;
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

    private static void AgeHistorical(Boxer b, Ratings prime, int peak)
    {
        double dev = BoxerFactory.Development(b.Age, peak);
        bool young = b.Age <= peak;
        var r = b.Ratings;
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
        double drawP = 0.05 * (1 - Math.Abs(pa - 0.5) * 2);
        Boxer? winner = null, loser = null;
        FightOutcome outcome = FightOutcome.Draw;
        string method = "D";
        int endRound = rounds, kdA = 0, kdB = 0;
        bool draw = _rng.NextDouble() < drawP;
        bool aWins = false, ko = false;

        if (!draw)
        {
            aWins = _rng.NextDouble() < pa;
            winner = aWins ? a : b; loser = aWins ? b : a;
            double koP = Ratings.KnockoutChance(winner.Ratings.Power, loser.Ratings.Chin, winner.Overall - loser.Overall);
            ko = _rng.NextDouble() < koP;
            if (ko)
            {
                outcome = FightOutcome.Knockout; method = "KO"; endRound = 1 + _rng.Next(rounds);
                if (aWins) kdB = 1; else kdA = 1;
            }
            else outcome = FightOutcome.Decision;
        }

        // Sketch a believable card: the winner takes most rounds, punch output tracks ability, and a
        // stoppage is a 10-8 final round. Cheap enough to run for the whole division, so even quick
        // NPC undercards are inspectable round by round.
        int lastRound = ko ? endRound : rounds;
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
        if (!ko)   // went to the cards (a decision or a draw)
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

        if (WorldRanked(l) && l.Overall - w.Overall >= 8 && _rng.NextDouble() < 0.7)
        {
            LogEvent(Pick($"UPSET! {w.Name} shocks {l.Name}{(ko ? $", stopped in {res.EndRound}" : "")}.",
                          $"Boilover — {w.Name} outpoints the fancied {l.Name}.",
                          $"{l.Name} is stunned by {w.Name} in a major upset."), kind: "upset");
            return;
        }
        if (ko)
        {
            int streak = KoStreak(w);
            if (streak >= 3 && _rng.NextDouble() < 0.6)
            {
                LogEvent(Pick($"{w.Name} rolls on — {streak} straight inside the distance.",
                              $"{w.Name} keeps the KO streak going, now {streak} in a row.",
                              $"Another one bites the dust — {w.Name} up to {streak} consecutive KOs."), kind: "streak");
                return;
            }
            if (w.Overall >= 76 && WorldRanked(l) && _rng.NextDouble() < 0.4)
                LogEvent(Pick($"{w.Name} halts {l.Name} in {res.EndRound}.",
                              $"{w.Name} takes out {l.Name} inside the distance."), kind: "ko");
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

    private string Pick(params string[] opts) => opts[_rng.Next(opts.Length)];
    private void LogEvent(string text, bool playerBout = false, string? kind = null, WeightClass? div = null)
    {
        _log.Add(new CareerEvent { On = Date, Text = text, PlayerBout = playerBout, Kind = kind, Div = div ?? Division });
        if (_log.Count > 1500) _log.RemoveAt(0);   // bounded; eight divisions produce more news
    }

    private static DateOnly ParseDate(string s, DateOnly fallback) => DateOnly.TryParse(s, out var d) ? d : fallback;

    private static string LayoffText(int days) => days >= 60 ? $"out ~{Math.Max(2, days / 30)} months" : $"out ~{Math.Max(1, days / 7)} weeks";

    private int DaysForStage(CareerStage s) => s switch
    {
        CareerStage.Starter => 45 + _rng.Next(21),      // 6.5–9.5 weeks — a busy club-show schedule, up to 8 a year
        CareerStage.PrePrime => 52 + _rng.Next(28),     // 7.5–11.5 weeks — still active, stepping up (~5–7 a year)
        CareerStage.Prime => 84 + _rng.Next(56),        // 12–20 weeks — big fights, long camps (~3–4 a year)
        CareerStage.PostPrime => 98 + _rng.Next(46),    // 14–20 weeks (~3 a year) — still active
        CareerStage.End => 126 + _rng.Next(60),         // 18–27 weeks (~2–3 a year), winding down
        _ => 90
    };

    /// <summary>Generated filler is capped to journeyman class (OVR ~40) — the contender, champion and
    /// elite tiers belong to the real fighters and the player.</summary>
    private const int GeneratedCap = 56;

    /// <summary>A fighter is only "world-ranked" once he's built a real body of work — 20 pro bouts.</summary>
    public static bool WorldRanked(Boxer b) => b.Record.Wins + b.Record.Losses + b.Record.Draws >= 20;

    /// <summary>The current top-20 of a division (world-ranked fighters by ranking score).</summary>
    private HashSet<int> Top20Ids(WeightClass wc) => ActiveIn(wc).Where(WorldRanked).OrderByDescending(RankScore).Take(20).Select(b => b.Id).ToHashSet();

    /// <summary>Ranking score: Elo, pulled toward reality by the fighter's win/loss margin, so a
    /// padded Elo can't keep a losing record near the top of the ratings.</summary>
    public static double RankScore(Boxer b) => b.RankPoints + (b.Record.Wins - b.Record.Losses) * 6.0;

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
        return new Boxer
        {
            Id = -1, Name = name, Country = country, WeightClass = wc,
            Ratings = r, Age = startAge, PeakAge = peak, Potential = potential, RankPoints = 1000
        };
    }
}
