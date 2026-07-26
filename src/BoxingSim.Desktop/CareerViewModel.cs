using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using BoxingSim.Core.Career;
using BoxingSim.Core.Engine;
using BoxingSim.Core.Generation;
using BoxingSim.Core.Model;

namespace BoxingSim.Desktop;

public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A command backed by a plain delegate — enough for this app, no framework needed.</summary>
public sealed class Cmd : ICommand
{
    private readonly Action<object?> _run;
    private readonly Func<bool>? _can;
    public Cmd(Action run, Func<bool>? can = null) { _run = _ => run(); _can = can; }
    public Cmd(Action<object?> run, Func<bool>? can = null) { _run = run; _can = can; }
    public bool CanExecute(object? p) => _can?.Invoke() ?? true;
    public void Execute(object? p) => _run(p);
    public event EventHandler? CanExecuteChanged;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public enum Page { Career, Rankings, P4P, Champions, Hall, Awards, News, Stats }

public sealed record NavItem(Page Page, string Label);

// ---- row shapes the views bind to ----

public sealed record RankRow(string Rank, int Class, string Name, string Detail, string Record,
                             bool IsPlayer, bool IsChampion, Boxer? Fighter, HallOfFamer? Legend = null);
public sealed record BeltRow(string Belt, string Holder, string Detail, bool Lineal, bool Vacant, Boxer? Fighter);
public sealed record DivisionRow(string Division, string Undisputed, IReadOnlyList<BeltRow> Belts, bool IsPlayerDivision);
public sealed record NewsRow(string Date, string Text, string Kind, bool PlayerBout);
public sealed record AwardRow(string Category, int Year, IReadOnlyList<AwardPlace> Places);
public sealed record AwardPlace(string Position, string Name, string Division, string Detail, bool Winner);
public sealed record LedgerRow(string Date, string Result, string Opponent, string Detail, bool Win, bool Loss);
public sealed record StatRow(string Label, string Value, string Note);

/// <summary>One attribute in the tale of the tape. The fractions drive the bar widths.</summary>
public sealed record TapeRow(string Attribute, int Mine, int Theirs, double MineWidth, double TheirsWidth, bool IAmBetter);

/// <summary>One round as the playback reveals it.</summary>
public sealed record PlaybackRow(int Round, string Score, string Landed, string Note, bool Knockdown, bool Stoppage);

/// <summary>The drill-down card for any fighter in any list.</summary>
public sealed record CardStat(string Name, int Value, double Width);
public sealed class FighterCard
{
    public string Name { get; init; } = "";
    public string Meta { get; init; } = "";
    public int Class { get; init; }
    public string Record { get; init; } = "";
    public string Belts { get; init; } = "";
    public IReadOnlyList<CardStat> Ratings { get; init; } = Array.Empty<CardStat>();
    public IReadOnlyList<LedgerRow> Recent { get; init; } = Array.Empty<LedgerRow>();
    public bool HasRatings => Ratings.Count > 0;
}

/// <summary>Everything the window binds to. Owns the career service and rebuilds its display collections after
/// each turn — the sim is synchronous and fast enough that there's nothing to stream.</summary>
public sealed class CareerViewModel : Observable
{
    private readonly DesktopCareerService _svc = new();
    private readonly Random _rng = new();
    private readonly DispatcherTimer _playbackTimer;
    private List<PlaybackRow> _pending = new();

    public CareerViewModel()
    {
        TakeFight = new Cmd(Take, () => Game?.Offer is not null && Game?.Player.Retired == false);
        HoldOut = new Cmd(Decline, () => Game?.Offer is not null && Game?.Player.Retired == false);
        MoveUp = new Cmd(DoMoveUp, () => Game?.CanMoveUp == true);
        StartCareer = new Cmd(Start, () => !Busy && Ready);
        ContinueCareer = new Cmd(Continue, () => Ready && DesktopCareerService.HasSave);
        AbandonCareer = new Cmd(Abandon);
        RollName = new Cmd(() => PlayerName = NameGen.Generate(Country, _rng));
        ShowFighter = new Cmd(OnShowFighter);
        CloseCard = new Cmd(() => { SelectedCard = null; });
        FinishPlayback = new Cmd(EndPlayback);

        _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        _playbackTimer.Tick += (_, _) => RevealNextRound();

        PlayerName = NameGen.Generate(Country, _rng);
    }

    /// <summary>Parse the 2200-fighter roster OFF the UI thread. Doing it in the constructor held the window back
    /// for seconds before anything appeared on screen, which on a cold start looked like a hang.</summary>
    public async Task WarmupAsync()
    {
        var divisions = await Task.Run(() => _svc.AvailableDivisions());
        foreach (var d in divisions) Divisions.Add(d);
        SetupDivision = Divisions.FirstOrDefault(WeightClass.Middleweight);
        Ready = true;
    }

    private bool _ready;
    public bool Ready
    {
        get => _ready;
        private set { _ready = value; Raise(); Raise(nameof(Loading)); RefreshCommands(); }
    }
    public bool Loading => !_ready;

    public CareerGame? Game => _svc.Game;
    public bool InCareer => _svc.HasCareer;

    // ---- navigation ----
    public IReadOnlyList<NavItem> Nav { get; } = new[]
    {
        new NavItem(Page.Career, "Fight night"),
        new NavItem(Page.Rankings, "Rankings"),
        new NavItem(Page.P4P, "Pound-for-pound"),
        new NavItem(Page.Champions, "Champions"),
        new NavItem(Page.Hall, "Hall of Fame"),
        new NavItem(Page.Awards, "Awards"),
        new NavItem(Page.News, "News"),
        new NavItem(Page.Stats, "Career"),
    };

    private NavItem? _selectedNav;
    public NavItem? SelectedNav
    {
        get => _selectedNav ??= Nav[0];
        set { _selectedNav = value; Raise(); Raise(nameof(CurrentPage)); }
    }
    public Page CurrentPage => SelectedNav?.Page ?? Page.Career;

    // ---- setup screen ----
    public ObservableCollection<WeightClass> Divisions { get; } = new();
    public IReadOnlyList<string> Countries { get; } = new[]
    {
        "USA", "England", "Mexico", "Germany", "Russia", "Ukraine", "Canada",
        "Italy", "Argentina", "Cuba", "Nigeria", "Poland"
    };
    public IReadOnlyList<int> Years { get; } = Enumerable.Range(1945, 71).ToList();
    public IReadOnlyList<string> Talents { get; } = new[] { "elite", "contender", "journeyman", "club", "random915" };

    private string _playerName = "";
    public string PlayerName { get => _playerName; set { _playerName = value; Raise(); } }

    private string _country = "USA";
    public string Country { get => _country; set { _country = value; Raise(); } }

    private int _setupYear = 1965;
    public int SetupYear { get => _setupYear; set { _setupYear = value; Raise(); } }

    private WeightClass _setupDivision;
    public WeightClass SetupDivision { get => _setupDivision; set { _setupDivision = value; Raise(); } }

    private string _talent = "contender";
    public string Talent { get => _talent; set { _talent = value; Raise(); } }

    private bool _fullHistory;
    public bool FullHistory { get => _fullHistory; set { _fullHistory = value; Raise(); } }

    private bool _busy;
    public bool Busy { get => _busy; set { _busy = value; Raise(); RefreshCommands(); } }

    // ---- commands ----
    public Cmd TakeFight { get; }
    public Cmd HoldOut { get; }
    public Cmd MoveUp { get; }
    public Cmd StartCareer { get; }
    public Cmd ContinueCareer { get; }
    public Cmd AbandonCareer { get; }
    public Cmd RollName { get; }
    public Cmd ShowFighter { get; }
    public Cmd CloseCard { get; }
    public Cmd FinishPlayback { get; }

    // ---- collections ----
    public ObservableCollection<RankRow> Rankings { get; } = new();
    public ObservableCollection<RankRow> PoundForPound { get; } = new();
    public ObservableCollection<DivisionRow> Champions { get; } = new();
    public ObservableCollection<RankRow> HallOfFame { get; } = new();
    public ObservableCollection<AwardRow> Awards { get; } = new();
    public ObservableCollection<NewsRow> News { get; } = new();
    public ObservableCollection<LedgerRow> Ledger { get; } = new();
    public ObservableCollection<TapeRow> Tape { get; } = new();
    public ObservableCollection<PlaybackRow> Playback { get; } = new();
    public ObservableCollection<StatRow> Stats { get; } = new();

    public bool HasLedger => Ledger.Count > 0;

    private static (int Lo, int Hi) TalentRange(string t) => t switch
    {
        "elite" => (85, 100),
        "journeyman" => (50, 69),
        "club" => (38, 49),
        "random915" => (80, 100),
        _ => (70, 84)
    };

    private void Start()
    {
        Busy = true;
        try
        {
            var (lo, hi) = TalentRange(Talent);
            int potential = _rng.Next(lo, hi + 1);
            var name = string.IsNullOrWhiteSpace(PlayerName) ? NameGen.Generate(Country, _rng) : PlayerName.Trim();
            // Never start in a division that didn't exist yet.
            var div = SetupDivision.FoundedYear() <= SetupYear
                ? SetupDivision
                : Divisions.Where(d => d.FoundedYear() <= SetupYear)
                           .OrderByDescending(d => (int)d)
                           .DefaultIfEmpty(WeightClass.Heavyweight).First();
            _svc.Start(name, Country, SetupYear, potential, div, FullHistory);
        }
        finally { Busy = false; }
        SelectedNav = Nav[0];
        RefreshAll();
    }

    private void Continue()
    {
        if (_svc.Load()) { SelectedNav = Nav[0]; RefreshAll(); }
        else { ContinueCareer.Refresh(); Raise(nameof(HasSave)); }
    }

    private void Take()
    {
        _svc.Take();
        RefreshAll();
        StartPlayback();
    }

    private void Decline() { _svc.Decline(); RefreshAll(); }
    private void DoMoveUp() { _svc.MoveUp(); RefreshAll(); }
    private void Abandon() { EndPlayback(); SelectedCard = null; _svc.Abandon(); RefreshAll(); }

    public static bool HasSave => DesktopCareerService.HasSave;
    public string? SaveError => _svc.LastSaveError;
    public string SaveLocation => DesktopCareerService.SavePath;

    // ---- round-by-round playback ----

    private bool _isPlayingBack;
    public bool IsPlayingBack
    {
        get => _isPlayingBack;
        private set { _isPlayingBack = value; Raise(); Raise(nameof(ShowResultBanner)); }
    }

    /// <summary>The verdict banner stays hidden behind the playback overlay — showing it there would give the
    /// result away before a single round had been watched.</summary>
    public bool ShowResultBanner => HasResult && !IsPlayingBack;

    public string PlaybackTitle { get; private set; } = "";
    public string PlaybackVerdict { get; private set; } = "";

    private bool _playbackFinished;
    public bool PlaybackFinished
    {
        get => _playbackFinished;
        private set { _playbackFinished = value; Raise(); Raise(nameof(PlaybackButtonLabel)); }
    }

    public string PlaybackButtonLabel => PlaybackFinished ? "Continue" : "Skip to the verdict";

    /// <summary>Reveal the bout a round at a time rather than jumping to the verdict. Only the player's own
    /// fights carry round-by-round detail — the NPC resolver doesn't produce it — so a bout without rounds
    /// simply shows its result.</summary>
    private void StartPlayback()
    {
        var res = _svc.LastResult;
        if (res is null || Game is null) return;
        var me = Game.Player;
        bool iAmA = res.A.Id == me.Id;
        var them = iAmA ? res.B : res.A;

        Playback.Clear();
        _pending = res.Rounds.Select(r =>
        {
            int myScore = iAmA ? r.ScoreA : r.ScoreB;
            int theirScore = iAmA ? r.ScoreB : r.ScoreA;
            int myLanded = iAmA ? r.LandedA : r.LandedB;
            int theirLanded = iAmA ? r.LandedB : r.LandedA;
            int myKd = iAmA ? r.KnockdownsB : r.KnockdownsA;      // knockdowns I scored
            int theirKd = iAmA ? r.KnockdownsA : r.KnockdownsB;   // knockdowns against me
            var note = myKd > 0 ? $"{me.Name} drops him"
                     : theirKd > 0 ? $"{them.Name} puts him down"
                     : "";
            return new PlaybackRow(r.Round, $"{myScore}–{theirScore}", $"{myLanded} / {theirLanded}",
                                   note, myKd + theirKd > 0,
                                   r.Round == res.EndRound && res.Outcome is FightOutcome.Knockout or FightOutcome.TechnicalKnockout);
        }).ToList();

        PlaybackTitle = $"{me.Name}  vs  {them.Name}";
        PlaybackVerdict = ResultHeadline;
        Raise(nameof(PlaybackTitle));

        if (_pending.Count == 0) { PlaybackFinished = true; Raise(nameof(PlaybackVerdict)); IsPlayingBack = true; return; }

        PlaybackFinished = false;
        Raise(nameof(PlaybackVerdict));
        IsPlayingBack = true;
        _playbackTimer.Start();
    }

    private void RevealNextRound()
    {
        if (Playback.Count >= _pending.Count) { _playbackTimer.Stop(); PlaybackFinished = true; return; }
        Playback.Add(_pending[Playback.Count]);
    }

    /// <summary>Skip to the verdict, or dismiss it once shown.</summary>
    private void EndPlayback()
    {
        _playbackTimer.Stop();
        if (!PlaybackFinished && Playback.Count < _pending.Count)
        {
            for (int i = Playback.Count; i < _pending.Count; i++) Playback.Add(_pending[i]);
            PlaybackFinished = true;
            return;
        }
        IsPlayingBack = false;
    }

    // ---- fighter drill-down ----

    private FighterCard? _selectedCard;
    public FighterCard? SelectedCard { get => _selectedCard; private set { _selectedCard = value; Raise(); Raise(nameof(HasCard)); } }
    public bool HasCard => _selectedCard is not null;

    private void OnShowFighter(object? param)
    {
        if (Game is null) return;
        if (param is RankRow row)
        {
            if (row.Fighter is Boxer b) { SelectedCard = BuildCard(b); return; }
            if (row.Legend is HallOfFamer m) { SelectedCard = BuildCard(m); return; }
        }
        if (param is BeltRow belt && belt.Fighter is Boxer bf) SelectedCard = BuildCard(bf);
        if (param is Boxer only) SelectedCard = BuildCard(only);
    }

    private FighterCard BuildCard(Boxer b)
    {
        var g = Game!;
        var belts = g.BeltsHeld(b).Select(x => x.Defenses > 0 ? $"{x.Belt} ({x.Defenses} def)" : x.Belt).ToList();
        return new FighterCard
        {
            Name = b.Name,
            Class = b.Class,
            Record = b.Record.ToString(),
            Meta = string.Join("  ·  ", new[]
            {
                Ui.Code(b.Country), $"age {b.Age}", b.WeightClass.DisplayName(),
                CareerStages.Label(CareerStages.Of(b)), $"{b.Overall} OVR"
            }),
            Belts = string.Join("  ·  ", belts),
            Ratings = AttributeBars(b.Ratings),
            Recent = b.History.OrderByDescending(h => h.Date).Take(12).Select(ToLedger).ToList()
        };
    }

    private static FighterCard BuildCard(HallOfFamer m) => new()
    {
        Name = m.Name,
        Class = m.PeakClass,
        Record = m.Record,
        Meta = string.Join("  ·  ", new[]
        {
            Ui.Code(m.Country), m.Division.DisplayName(), $"retired {m.Year} aged {m.Age}",
            $"peak {m.PeakOverall} OVR"
        }),
        Belts = (m.WeightTitles >= 2 ? $"{m.WeightTitles}-weight champion" : m.WasChampion ? "World champion" : "")
                + (m.Defenses > 0 ? $"  ·  {m.Defenses} defences" : ""),
        Recent = m.History.OrderByDescending(h => h.Date).Take(12).Select(ToLedger).ToList()
    };

    private static IReadOnlyList<CardStat> AttributeBars(Ratings r) => new[]
    {
        new CardStat("Power", r.Power, r.Power / 100.0),
        new CardStat("Chin", r.Chin, r.Chin / 100.0),
        new CardStat("Speed", r.Speed, r.Speed / 100.0),
        new CardStat("Defence", r.Defense, r.Defense / 100.0),
        new CardStat("Stamina", r.Stamina, r.Stamina / 100.0),
        new CardStat("Accuracy", r.Accuracy, r.Accuracy / 100.0),
        new CardStat("Conditioning", r.Conditioning, r.Conditioning / 100.0),
        new CardStat("Cut resistance", r.CutResistance, r.CutResistance / 100.0),
        new CardStat("Aggression", r.Aggression, r.Aggression / 100.0),
        new CardStat("Heart", r.Heart, r.Heart / 100.0),
    };

    private static LedgerRow ToLedger(BoutLine h)
    {
        string detail = h.Method + (h.Round > 0 && h.Method is "KO" or "TKO" ? $" rd{h.Round}" : "");
        if (h.Note is not null) detail = $"{h.Note} · {detail}";
        return new LedgerRow(h.Date.ToString("d MMM yyyy"), h.Result.ToString(), h.Opponent, detail,
                             h.Result == 'W', h.Result == 'L');
    }

    // ---- the last bout ----
    public bool HasResult => _svc.LastResult is not null;
    public string ResultHeadline
    {
        get
        {
            var r = _svc.LastResult;
            if (r is null || Game is null) return "";
            if (r.IsDraw) return $"DRAW — {Game.Player.Name} drew with {LastOpponent}";
            bool won = r.Winner!.Id == Game.Player.Id;
            return (won ? "WIN — " : "LOSS — ")
                 + (won ? $"{Game.Player.Name} beat {LastOpponent}" : $"{Game.Player.Name} lost to {LastOpponent}")
                 + $" by {r.Method}" + (r.Method is "KO" or "TKO" ? $", round {r.EndRound}" : "");
        }
    }
    public bool LastBoutWon => _svc.LastResult is { IsDraw: false } r && Game is not null && r.Winner!.Id == Game.Player.Id;
    public bool LastBoutLost => _svc.LastResult is { IsDraw: false } r && Game is not null && r.Winner!.Id != Game.Player.Id;
    private string LastOpponent => Game?.Player.History.LastOrDefault()?.Opponent ?? "his opponent";

    // ---- header / identity ----
    public string PlayerHeadline => Game?.Player.Name ?? "";
    public int PlayerClass => Game?.Player.Class ?? 0;
    public string PlayerRecord => Game?.Player.Record.ToString() ?? "";
    public string PlayerIdentity
    {
        get
        {
            if (Game is null) return "";
            var p = Game.Player;
            return $"{Ui.Code(p.Country)}  ·  age {p.Age}  ·  {p.WeightClass.DisplayName()}";
        }
    }
    public string PlayerStanding
    {
        get
        {
            if (Game is null) return "";
            int rank = PlayerRank;
            var belts = Game.BeltsHeld(Game.Player).Select(b => b.Belt).ToList();
            if (belts.Count > 0) return string.Join(" · ", belts);
            return rank > 0 ? $"#{rank} {Game.Player.WeightClass.DisplayName()}" : CareerStages.Label(CareerStages.Of(Game.Player));
        }
    }
    public bool PlayerIsChampion => Game is not null && Game.BeltsHeld(Game.Player).Any();

    private int PlayerRank
    {
        get
        {
            if (Game is null) return 0;
            var ranked = Game.RankingOf(Game.Player.WeightClass, 100).ToList();
            return ranked.FindIndex(b => b.Id == Game.Player.Id) + 1;
        }
    }

    public string DateLabel => Game?.DateLabel ?? "";
    public string OfferDateLabel => Game?.OfferDateLabel ?? "";
    public bool PlayerRetired => Game?.Player.Retired == true;

    // ---- the offer ----
    public bool HasOffer => Game?.Offer is not null;
    public string OfferOpponent => Game?.Offer?.Opponent.Name ?? "";
    public int OfferOpponentClass => Game?.Offer?.Opponent.Class ?? 0;
    public string OfferOpponentRecord => Game?.Offer?.Opponent.Record.ToString() ?? "";
    public string OfferOpponentMeta
    {
        get
        {
            var b = Game?.Offer?.Opponent;
            return b is null ? "" : $"{Ui.Code(b.Country)}  ·  age {b.Age}";
        }
    }
    public string OfferRounds => Game?.Offer is { } o ? $"{o.Rounds} rounds" : "";
    public string OfferContext
    {
        get
        {
            var o = Game?.Offer;
            if (o is null) return "";
            return o.TitleFight ? $"{o.Belt} TITLE" : (o.Context ?? "").ToUpperInvariant();
        }
    }
    public bool OfferIsTitle => Game?.Offer?.TitleFight == true;
    public string MoveUpLabel => Game?.NextDivision is WeightClass w ? $"Move up to {w.DisplayName()}" : "";

    // ---- refresh ----

    public void RefreshAll()
    {
        BuildRankings();
        BuildP4P();
        BuildChampions();
        BuildHof();
        BuildAwards();
        BuildNews();
        BuildLedger();
        BuildTape();
        BuildStats();

        foreach (var n in new[]
        {
            nameof(InCareer), nameof(Game), nameof(PlayerHeadline), nameof(PlayerClass), nameof(PlayerRecord),
            nameof(PlayerIdentity), nameof(PlayerStanding), nameof(PlayerIsChampion),
            nameof(DateLabel), nameof(OfferDateLabel), nameof(HasOffer), nameof(OfferOpponent),
            nameof(OfferOpponentClass), nameof(OfferOpponentRecord), nameof(OfferOpponentMeta),
            nameof(OfferRounds), nameof(OfferContext), nameof(OfferIsTitle),
            nameof(MoveUpLabel), nameof(HasResult), nameof(ResultHeadline), nameof(LastBoutWon),
            nameof(LastBoutLost), nameof(PlayerRetired), nameof(HasSave), nameof(SaveError),
            nameof(ShowResultBanner), nameof(HasLedger)
        }) Raise(n);
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        TakeFight.Refresh(); HoldOut.Refresh(); MoveUp.Refresh();
        StartCareer.Refresh(); ContinueCareer.Refresh();
    }

    private void BuildRankings()
    {
        Rankings.Clear();
        if (Game is null) return;
        int r = 1;
        foreach (var b in Game.RankingOf(Game.Player.WeightClass, 15))
        {
            var belts = Game.BeltsHeld(b).Select(x => x.Belt).ToList();
            Rankings.Add(new RankRow(Game.IsWorldChampion(b) ? "C" : r.ToString(), b.Class, b.Name,
                                     belts.Count > 0 ? string.Join(" · ", belts) : "",
                                     b.Record.ToString(), b.Id == Game.Player.Id, Game.IsWorldChampion(b), b));
            r++;
        }
    }

    private void BuildP4P()
    {
        PoundForPound.Clear();
        if (Game is null) return;
        int r = 1;
        foreach (var b in Game.PoundForPound(15))
        {
            var a = Game.AchievementsOf(b);
            var bits = new List<string> { b.WeightClass.DisplayName() };
            if (a.Undisputed) bits.Add("UNDISPUTED"); else bits.AddRange(a.Belts);
            if (a.Lineal) bits.Add(Game.LinealBelt);
            if (a.Defences > 0) bits.Add($"{a.Defences} defence{(a.Defences == 1 ? "" : "s")}");
            if (a.WeightTitles >= 2) bits.Add($"{a.WeightTitles}-weight champ");
            if (a.Belts.Count == 0 && !a.Lineal && a.TitleWins > 0) bits.Add($"ex-champ · {a.TitleWins} title wins");
            PoundForPound.Add(new RankRow(r.ToString(), b.Class, b.Name, string.Join(" · ", bits),
                                          b.Record.ToString(), b.Id == Game.Player.Id, Game.IsWorldChampion(b), b));
            r++;
        }
    }

    private void BuildChampions()
    {
        Champions.Clear();
        if (Game is null) return;
        foreach (var d in Game.ChampionsBoard())
        {
            var belts = new List<BeltRow>();
            void Add(string belt, Boxer? holder, int def, bool lineal) =>
                belts.Add(holder is null
                    ? new BeltRow(belt, "vacant", "", lineal, true, null)
                    : new BeltRow(belt, holder.Name, holder.Record + (def > 0 ? $" · {def} def" : ""), lineal, false, holder));

            Add(Game.LinealBelt, d.Lineal, d.LinealDefenses, true);
            Add(Game.PrimaryBelt, d.Wba, d.WbaDefenses, false);
            if (Game.WbcActive) Add("WBC", d.Wbc, d.WbcDefenses, false);
            if (Game.IbfActive) Add("IBF", d.Ibf, d.IbfDefenses, false);

            Champions.Add(new DivisionRow(d.Division.DisplayName(),
                                          d.Undisputed is Boxer u ? $"undisputed · {u.Name}" : "",
                                          belts, d.Division == Game.Player.WeightClass));
        }
    }

    private void BuildHof()
    {
        HallOfFame.Clear();
        if (Game is null) return;
        int r = 1;
        foreach (var m in Game.HallOfFame.Take(50))
        {
            var bits = new List<string> { m.Division.DisplayName() };
            if (m.WeightTitles >= 2) bits.Add($"{m.WeightTitles}-weight champ");
            else if (m.WasChampion) bits.Add("world champ");
            if (m.Defenses > 0) bits.Add($"{m.Defenses} def");
            HallOfFame.Add(new RankRow(r.ToString(), m.PeakClass, m.Name, string.Join(" · ", bits),
                                       m.Record, false, m.WasChampion, null, m));
            r++;
        }
    }

    private void BuildAwards()
    {
        Awards.Clear();
        if (Game is null) return;
        foreach (var yr in Game.Awards.Take(8))
        {
            void Add(string cat, IReadOnlyList<AwardWinner> list)
            {
                if (list.Count == 0) return;
                var places = list.Select((w, i) => new AwardPlace(i == 0 ? "1st" : i == 1 ? "2nd" : "3rd",
                                                                 w.Name, w.Div.DisplayName(), w.Detail, i == 0)).ToList();
                Awards.Add(new AwardRow(cat, yr.Year, places));
            }
            Add("Fighter of the Year", yr.FighterOfYear);
            Add("Fight of the Year", yr.FightOfYear);
            Add("Knockout of the Year", yr.KnockoutOfYear);
            Add("Upset of the Year", yr.UpsetOfYear);
        }
    }

    private void BuildNews()
    {
        News.Clear();
        if (Game is null) return;
        // By DATE, newest first — the world resolves a division at a time, so the order events are logged in is
        // not the order they happened in.
        foreach (var (e, _) in Game.Log.Select((e, i) => (e, i))
                                       .OrderByDescending(x => x.e.On).ThenByDescending(x => x.i)
                                       .Take(120))
            News.Add(new NewsRow(e.DateLabel, e.Text, e.Kind ?? "", e.PlayerBout));
    }

    private void BuildLedger()
    {
        Ledger.Clear();
        if (Game is null) return;
        foreach (var h in Game.Player.History.OrderByDescending(h => h.Date).Take(60))
            Ledger.Add(ToLedger(h));
    }

    /// <summary>Tale of the tape: the player's attributes against the man he's being offered, so the decision to
    /// take the fight is an informed one rather than a name and a record.</summary>
    private void BuildTape()
    {
        Tape.Clear();
        if (Game?.Offer is not { } o) return;
        var me = Game.Player.Ratings;
        var them = o.Opponent.Ratings;
        void Row(string name, int a, int b) =>
            Tape.Add(new TapeRow(name, a, b, a / 100.0, b / 100.0, a >= b));
        Row("Power", me.Power, them.Power);
        Row("Chin", me.Chin, them.Chin);
        Row("Speed", me.Speed, them.Speed);
        Row("Defence", me.Defense, them.Defense);
        Row("Stamina", me.Stamina, them.Stamina);
        Row("Accuracy", me.Accuracy, them.Accuracy);
        Row("Heart", me.Heart, them.Heart);
    }

    private void BuildStats()
    {
        Stats.Clear();
        if (Game is null) return;
        var p = Game.Player;
        int fights = p.Record.Wins + p.Record.Losses + p.Record.Draws;
        int koPct = p.Record.Wins > 0 ? (int)Math.Round(100.0 * p.Record.KnockoutWins / p.Record.Wins) : 0;

        var titleWins = p.History.Count(h => h.Result == 'W' && h.Note is not null && h.Note.EndsWith(" title"));
        var reigns = Game.Reigns.ToList();
        var divisions = p.History.Count > 0
            ? Game.Reigns.Select(r => r.Belt).Distinct().Count()
            : 0;

        // Longest win streak across the whole ledger.
        int best = 0, run = 0;
        foreach (var h in p.History.OrderBy(h => h.Date))
        {
            if (h.Result == 'W') { run++; best = Math.Max(best, run); } else run = 0;
        }

        var bestWin = p.History.Where(h => h.Result == 'W' && h.Note is not null)
                               .OrderByDescending(h => h.Date).FirstOrDefault();

        Stats.Add(new StatRow("Record", p.Record.ToString(), $"{fights} fights"));
        Stats.Add(new StatRow("Knockout wins", $"{p.Record.KnockoutWins}", $"{koPct}% of wins"));
        Stats.Add(new StatRow("Longest win streak", best.ToString(), best >= 10 ? "a real run" : ""));
        Stats.Add(new StatRow("Title bouts won", titleWins.ToString(), ""));
        Stats.Add(new StatRow("Title reigns", reigns.Count.ToString(),
                              reigns.Count > 0 ? string.Join(", ", reigns.Select(r => r.Belt).Distinct()) : ""));
        Stats.Add(new StatRow("Title defences", Game.TitleDefenses.ToString(), ""));
        Stats.Add(new StatRow("Days as champion", Game.DaysAsChampion.ToString("N0"),
                              Game.DaysAsChampion > 365 ? $"{Game.DaysAsChampion / 365} years" : ""));
        Stats.Add(new StatRow("Current rating", $"{p.Overall} OVR", $"class {p.Class}"));
        Stats.Add(new StatRow("Peak potential", $"{p.Potential}", ""));
        if (bestWin is not null)
            Stats.Add(new StatRow("Latest title win", bestWin.Opponent, $"{bestWin.Note} · {bestWin.Date:d MMM yyyy}"));
    }
}
