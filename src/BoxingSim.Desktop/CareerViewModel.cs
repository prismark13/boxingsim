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

public enum Page { Dashboard, Career, Rankings, P4P, Champions, Hall, Awards, News, Stats }

/// <summary>A sidebar entry. Group headers are not selectable — they only label the section beneath.</summary>
public sealed record NavItem(Page Page, string Label, bool IsHeader = false, string Shortcut = "")
{
    public bool IsPage => !IsHeader;
}

// ---- row shapes the views bind to ----

public sealed record RankRow(string Rank, int Class, string Name, string Detail, string Record,
                             bool IsPlayer, bool IsChampion, Boxer? Fighter, HallOfFamer? Legend = null);
public sealed record BeltRow(string Belt, string Holder, string Detail, bool Lineal, bool Vacant, Boxer? Fighter);
public sealed record DivisionRow(string Division, string Undisputed, IReadOnlyList<BeltRow> Belts, bool IsPlayerDivision);
public sealed record NewsRow(string Date, string Text, string Kind, bool PlayerBout);
public sealed record AwardRow(string Category, int Year, IReadOnlyList<AwardPlace> Places);
public sealed record AwardPlace(string Position, string Name, string Division, string Detail, bool Winner,
                                string Category = "", int Year = 0, string Commentary = "");

/// <summary>An award opened out — the citation in full, and a way through to the man who won it when the
/// name resolves to a single fighter (a "Fight of the Year" names two, so it doesn't).</summary>
public sealed class AwardDetail
{
    public string Heading { get; init; } = "";
    public string Position { get; init; } = "";
    public string Name { get; init; } = "";
    public string Division { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Commentary { get; init; } = "";
    public bool Winner { get; init; }
    public Boxer? Fighter { get; init; }
    public bool CanOpenFighter => Fighter is not null;
    public string OpenFighterLabel => Fighter is not null ? $"See {Fighter.Name}'s card" : "";
    public bool HasCommentary => !string.IsNullOrWhiteSpace(Commentary);
}
public sealed record LedgerRow(string Date, string Result, string Opponent, string Detail, bool Win, bool Loss,
                               BoutLine? Bout = null);

/// <summary>One round of a stored bout, from the owning fighter's point of view, with what was said about it.</summary>
public sealed record FightRoundRow(string Round, string Score, string Landed, string Knockdowns,
                                   bool WonRound, bool Knockdown, double ShareFor,
                                   IReadOnlyList<string> Commentary)
{
    public bool HasCommentary => Commentary.Count > 0;
}

/// <summary>A single fight opened out: the round-by-round card, the totals, the judges and the highlights.
/// Everything here was already being stored on the bout — it simply had nowhere to be seen.</summary>
public sealed class FightDetail
{
    public string Opponent { get; init; } = "";
    public string Date { get; init; } = "";
    public string Verdict { get; init; } = "";
    public string Note { get; init; } = "";
    public string Cards { get; init; } = "";
    public bool Win { get; init; }
    public bool Loss { get; init; }
    public IReadOnlyList<FightRoundRow> Rounds { get; init; } = Array.Empty<FightRoundRow>();
    public IReadOnlyList<string> Commentary { get; init; } = Array.Empty<string>();
    public IReadOnlyList<StatRow> Totals { get; init; } = Array.Empty<StatRow>();
    public bool HasRounds => Rounds.Count > 0;
    public bool HasCommentary => Commentary.Count > 0;
    public bool HasCards => !string.IsNullOrWhiteSpace(Cards);
    /// <summary>Only the full engine records rounds; the fast NPC resolver doesn't, so old or minor bouts
    /// legitimately have none.</summary>
    public string NoRoundsNote => "No round-by-round detail was recorded for this bout.";
}
public sealed record StatRow(string Label, string Value, string Note);

/// <summary>One attribute in the tale of the tape. The fractions drive the bar widths.</summary>
public sealed record TapeRow(string Attribute, int Mine, int Theirs, double MineWidth, double TheirsWidth, bool IAmBetter);

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
    /// <summary>The division to jump to from the card, so you can follow a fighter to his rankings.</summary>
    public WeightClass? Division { get; init; }
    public string DivisionLink => Division is WeightClass w ? $"See the {w.DisplayName()} rankings" : "";
    public bool HasDivisionLink => Division is not null;
}

/// <summary>Everything the window binds to. Owns the career service and rebuilds its display collections after
/// each turn — the sim is synchronous and fast enough that there's nothing to stream.</summary>
public sealed class CareerViewModel : Observable
{
    private readonly DesktopCareerService _svc = new();
    private readonly Random _rng = new();
    private readonly DispatcherTimer _playbackTimer;

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
        ShowFight = new Cmd(OnShowFight);
        ShowAward = new Cmd(OnShowAward);
        CloseAward = new Cmd(() => { SelectedAward = null; });
        OpenAwardFighter = new Cmd(() =>
        {
            var f = SelectedAward?.Fighter;
            SelectedAward = null;
            if (f is not null) SelectedCard = BuildCard(f);
        });
        CloseFight = new Cmd(() => { SelectedFight = null; });
        CloseCard = new Cmd(() => { SelectedCard = null; });
        FinishPlayback = new Cmd(EndPlayback);
        TogglePause = new Cmd(DoTogglePause);
        SetSpeed = new Cmd(DoSetSpeed);
        ToggleRound = new Cmd(p => { if (p is RoundBlock b) b.IsExpanded = !b.IsExpanded; });
        ToggleSound = new Cmd(() => SoundOn = !SoundOn);
        GoBack = new Cmd(DoGoBack, () => CanGoBack);
        Navigate = new Cmd(DoNavigate);
        ViewDivisionCmd = new Cmd(DoViewDivision);
        GoHomeDivision = new Cmd(() => { if (Game is not null) ViewDivision = Game.Player.WeightClass; });
        // Escape backs out of whatever is on top: the fighter card, the playback, then the page you came from.
        Dismiss = new Cmd(() =>
        {
            if (SelectedAward is not null) SelectedAward = null;
            else if (SelectedFight is not null) SelectedFight = null;
            else if (SelectedCard is not null) SelectedCard = null;
            else if (IsPlayingBack) EndPlayback();
            else if (CanGoBack) DoGoBack();
        });

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
    // Career mode is the app; the boards are reference you dip into. The sidebar says so — your own two screens
    // first, then the sport, then the record books — rather than eight peers where "Fight night" and "Career"
    // sounded like the same thing.
    public IReadOnlyList<NavItem> Nav { get; } = new[]
    {
        new NavItem(Page.Dashboard, "Dashboard", Shortcut: "Ctrl+1"),
        new NavItem(Page.Career, "Next fight", Shortcut: "Ctrl+2"),
        new NavItem(Page.Stats, "My record", Shortcut: "Ctrl+3"),
        new NavItem(Page.Dashboard, "THE SPORT", IsHeader: true),
        new NavItem(Page.Rankings, "Rankings", Shortcut: "Ctrl+4"),
        new NavItem(Page.P4P, "Pound-for-pound", Shortcut: "Ctrl+5"),
        new NavItem(Page.Champions, "Champions", Shortcut: "Ctrl+6"),
        new NavItem(Page.News, "News", Shortcut: "Ctrl+7"),
        new NavItem(Page.Dashboard, "THE RECORD BOOKS", IsHeader: true),
        new NavItem(Page.Hall, "Hall of Fame", Shortcut: "Ctrl+8"),
        new NavItem(Page.Awards, "Awards", Shortcut: "Ctrl+9"),
    };

    private NavItem? _selectedNav;
    public NavItem? SelectedNav
    {
        get => _selectedNav ??= Nav[0];
        set
        {
            if (_selectedNav is NavItem prev && value is not null && prev.Page != value.Page)
                PushHistory(prev.Page);
            _selectedNav = value;
            Raise();
            Raise(nameof(CurrentPage));
        }
    }
    public Page CurrentPage => SelectedNav?.Page ?? Page.Career;

    // ---- going back ----
    // A shell you can only move through by aiming at the sidebar is tiring. Every jump is remembered, so Back
    // (Alt+Left, or the mouse's back button) returns you where you were — including the division you were
    // looking at, which is the thing you lose most often when following a fighter across the boards.
    private readonly Stack<(Page Page, WeightClass Division)> _back = new();
    private bool _restoring;

    private void PushHistory(Page from)
    {
        if (_restoring) return;
        _back.Push((from, ViewDivision));
        if (_back.Count > 40) { var keep = _back.Take(40).Reverse().ToList(); _back.Clear(); foreach (var h in keep) _back.Push(h); }
        Raise(nameof(CanGoBack));
        GoBack.Refresh();
    }

    public bool CanGoBack => _back.Count > 0;

    private void DoGoBack()
    {
        if (_back.Count == 0) return;
        var (page, division) = _back.Pop();
        _restoring = true;
        try
        {
            _viewDivision = division;
            Raise(nameof(ViewDivision));
            SelectedNav = Nav.First(n => n.IsPage && n.Page == page);
            BuildRankings();
            Raise(nameof(RankingsSubtitle));
            Raise(nameof(IsAwayDivision));
        }
        finally { _restoring = false; }
        Raise(nameof(CanGoBack));
        GoBack.Refresh();
    }

    /// <summary>Jump straight to a page, remembering where we came from.</summary>
    private void DoNavigate(object? param)
    {
        var page = param switch
        {
            Page p => p,
            string s when Enum.TryParse<Page>(s, out var p) => p,
            _ => Page.Career
        };
        SelectedNav = Nav.First(n => n.IsPage && n.Page == page);
    }

    /// <summary>Show a given division's rankings — the cross-link from the champions board and a fighter's card,
    /// so following someone to his division doesn't mean hunting through the sidebar.</summary>
    private void DoViewDivision(object? param)
    {
        var wc = param switch
        {
            WeightClass w => w,
            DivisionRow d when Game is not null =>
                Game.LiveDivisions.FirstOrDefault(x => x.DisplayName() == d.Division, Game.Player.WeightClass),
            _ => (WeightClass?)null
        } ?? ViewDivision;
        ViewDivision = wc;
        SelectedCard = null;
        SelectedNav = Nav.First(n => n.IsPage && n.Page == Page.Rankings);
    }

    private WeightClass _viewDivision;
    /// <summary>Which division the rankings page is showing. Defaults to the player's, but any can be inspected.</summary>
    public WeightClass ViewDivision
    {
        get => _viewDivision;
        set
        {
            if (_viewDivision == value) return;
            _viewDivision = value;
            Raise();
            Raise(nameof(RankingsSubtitle));
            Raise(nameof(IsAwayDivision));
            Raise(nameof(AwayDivisionNote));
            BuildRankings();
        }
    }

    public IReadOnlyList<WeightClass> RankingDivisions => Game?.LiveDivisions ?? Array.Empty<WeightClass>();

    public string RankingsSubtitle =>
        Game is not null && ViewDivision == Game.Player.WeightClass
            ? $"{ViewDivision.DisplayName()} · your division"
            : ViewDivision.DisplayName();

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
    public Cmd ShowFight { get; }
    public Cmd ShowAward { get; }
    public Cmd CloseAward { get; }
    public Cmd OpenAwardFighter { get; }
    public Cmd CloseFight { get; }
    public Cmd FinishPlayback { get; }
    public Cmd TogglePause { get; }
    public Cmd SetSpeed { get; }
    public Cmd ToggleRound { get; }
    public Cmd ToggleSound { get; }
    public Cmd GoBack { get; }
    public Cmd Navigate { get; }
    public Cmd ViewDivisionCmd { get; }
    public Cmd Dismiss { get; }

    // ---- collections ----
    public ObservableCollection<RankRow> Rankings { get; } = new();
    public ObservableCollection<RankRow> PoundForPound { get; } = new();
    public ObservableCollection<DivisionRow> Champions { get; } = new();
    public ObservableCollection<RankRow> HallOfFame { get; } = new();
    public ObservableCollection<AwardRow> Awards { get; } = new();
    public ObservableCollection<NewsRow> News { get; } = new();
    public ObservableCollection<LedgerRow> Ledger { get; } = new();
    public ObservableCollection<TapeRow> Tape { get; } = new();
    public ObservableCollection<StatRow> Stats { get; } = new();

    // ---- the dashboard: career mode's hub ----
    public ObservableCollection<LedgerRow> RecentForm { get; } = new();
    public ObservableCollection<RankRow> DivisionTop { get; } = new();
    public ObservableCollection<NewsRow> HeadlineNews { get; } = new();
    public ObservableCollection<StatRow> Headlines { get; } = new();

    public bool HasLedger => Ledger.Count > 0;

    /// <summary>True when the rankings page is showing somebody else's division, so the shell can say so
    /// instead of leaving you wondering whose list you're reading.</summary>
    public bool IsAwayDivision => Game is not null && ViewDivision != Game.Player.WeightClass;
    public string AwayDivisionNote => $"Viewing {ViewDivision.DisplayName()} — not your division";
    public string HomeDivisionLabel => Game is not null ? $"Back to {Game.Player.WeightClass.DisplayName()}" : "";
    public Cmd GoHomeDivision { get; private set; } = null!;

    private static (int Lo, int Hi) TalentRange(string t) => t switch
    {
        "elite" => (85, 100),
        "journeyman" => (50, 69),
        "club" => (38, 49),
        "random915" => (80, 100),
        _ => (70, 84)
    };

    /// <summary>Run slow work off the UI thread with the app visibly busy. Simulating a world, or advancing it
    /// to fight night, can take seconds — done inline it froze the window with no cursor change and no sign of
    /// life, which reads as a hang.</summary>
    private async Task BusyAsync(string what, Action work)
    {
        BusyMessage = what;
        Busy = true;
        try { await Task.Run(work); }
        finally { Busy = false; }
    }

    private string _busyMessage = "";
    public string BusyMessage { get => _busyMessage; private set { _busyMessage = value; Raise(); } }

    private async void Start()
    {
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
            await BusyAsync(FullHistory ? "Simulating a full history — this takes a moment…" : "Building the world…",
                            () => _svc.Start(name, Country, SetupYear, potential, div, FullHistory));
            _viewDivision = div;
        }
        catch (Exception ex) { BusyMessage = ex.Message; return; }
        SelectedNav = Nav[0];
        RefreshAll();
    }

    private async void Continue()
    {
        bool ok = false;
        await BusyAsync("Loading your career…", () => ok = _svc.Load());
        if (ok) { _viewDivision = _svc.Game!.Player.WeightClass; SelectedNav = Nav[0]; RefreshAll(); }
        else { ContinueCareer.Refresh(); Raise(nameof(HasSave)); }
    }

    private async void Take()
    {
        await BusyAsync("Fight night…", () => _svc.Take());
        RefreshAll();
        StartPlayback();
    }

    private async void Decline() { await BusyAsync("Waiting for the next offer…", () => _svc.Decline()); RefreshAll(); }
    private async void DoMoveUp() { await BusyAsync("Moving up…", () => _svc.MoveUp()); RefreshAll(); }
    private void Abandon() { EndPlayback(); SelectedCard = null; SelectedFight = null; SelectedAward = null; _svc.Abandon(); RefreshAll(); }

    public static bool HasSave => DesktopCareerService.HasSave;
    public string? SaveError => _svc.LastSaveError;
    public string SaveLocation => DesktopCareerService.SavePath;

    // ---- FIGHT NIGHT ----
    //
    // The fight is the point of the app, so it gets called rather than tabulated. FightCall reads the engine's
    // 15-second ticks — the punch thrown, the combination, the counter, the man who got hurt — and turns them
    // into commentary; this streams it out with pacing so a fight plays as an event.

    private bool _isPlayingBack;
    public bool IsPlayingBack
    {
        get => _isPlayingBack;
        private set { _isPlayingBack = value; Raise(); Raise(nameof(ShowResultBanner)); }
    }

    /// <summary>The verdict banner stays hidden behind the fight night — showing it there would give the
    /// result away before a punch had been thrown.</summary>
    public bool ShowResultBanner => HasResult && !IsPlayingBack;

    /// <summary>The call, grouped by round. A finished round folds down to its one-line verdict so the round
    /// being fought is always the thing in front of you; any of them can be opened again.</summary>
    public ObservableCollection<RoundBlock> Rounds { get; } = new();
    private RoundBlock? _live;
    private List<CallLine> _call = new();
    private int _fed;

    public string FightNightHome { get; private set; } = "";
    public string FightNightAway { get; private set; } = "";
    public string FightNightHomeRecord { get; private set; } = "";
    public string FightNightAwayRecord { get; private set; } = "";
    public string FightNightBillLine { get; private set; } = "";
    public string PlaybackVerdict { get; private set; } = "";

    private bool _playbackFinished;
    public bool PlaybackFinished
    {
        get => _playbackFinished;
        private set { _playbackFinished = value; Raise(); Raise(nameof(PlaybackButtonLabel)); }
    }

    public string PlaybackButtonLabel => PlaybackFinished ? "Continue" : "Skip to the verdict";

    // ---- the live scoreboard, moved by the call ----

    private int _liveRound, _liveMine, _liveHis, _liveMyHurt, _liveHisHurt;
    public string LiveRound => _liveRound > 0 ? $"ROUND {_liveRound}" : "";
    public string LiveClock { get; private set; } = "";
    public int LiveMine => _liveMine;
    public int LiveHis => _liveHis;
    /// <summary>Each man's share of the punches landed so far — two bars leaning against each other, so who is
    /// winning is visible at a glance and moves with every line.</summary>
    public double LiveMineShare => _liveMine + _liveHis > 0 ? (double)_liveMine / (_liveMine + _liveHis) : 0.5;
    public double LiveHisShare => 1.0 - LiveMineShare;
    public bool MyManHurt => _liveMyHurt >= 2;
    public bool HisManHurt => _liveHisHurt >= 2;

    /// <summary>Place one line of the call: a ROUND heading folds the previous round away and opens a new one,
    /// the end-of-round card becomes that round's summary, everything else is action inside it.</summary>
    private void Feed(CallLine line)
    {
        if (line.IsRound)
        {
            if (_live is not null) _live.IsExpanded = false;   // the round just fought folds away
            _live = new RoundBlock { Round = line.Round };
            Rounds.Add(_live);
        }
        else if (line.IsScore && _live is not null)
        {
            _live.Summary = line.Text;
        }
        else
        {
            _live ??= AddOpeningBlock();
            _live.Lines.Add(line);
        }
        PushState(line);
        Sound(line);
    }

    /// <summary>Sparse by design: a bell to open a round, a thud when a man goes down, the final bell on the
    /// verdict. Silent while skipping, because forty lines at once would be forty sounds at once.</summary>
    private void Sound(CallLine line)
    {
        if (!SoundOn || _skipping) return;
        // Keyed off the EVENT, never the wording. Matching on text missed every knockdown phrased "on the
        // canvas" or "on the floor" — half of them, depending on which variant the rotation reached for.
        switch (line.Event)
        {
            case CallEvent.RoundBell: Sfx.Bell(); break;
            case CallEvent.HardPunch: Sfx.Thud(); break;
            case CallEvent.Hurt: Sfx.Ooh(); break;                    // the intake of breath, not a cheer
            case CallEvent.Cut: Sfx.Ooh(); break;
            case CallEvent.Knockdown: Sfx.Thud(1.6); Sfx.Roar(); break;   // the shot AND the reaction, layered
            case CallEvent.Stoppage: Sfx.Roar(); Sfx.FinalBell(); break;
        }
    }

    private bool _skipping;

    private bool _soundOn = true;
    public bool SoundOn
    {
        get => _soundOn;
        private set
        {
            _soundOn = value;
            Sfx.Enabled = value;
            if (value && IsPlayingBack && _svc.LastResult is { } r) Sfx.StartBed(OccasionOf(r));
            Raise(); Raise(nameof(SoundLabel));
        }
    }
    public string SoundLabel => _soundOn ? "Sound on" : "Sound off";

    private RoundBlock AddOpeningBlock()
    {
        var b = new RoundBlock { Round = 0 };
        Rounds.Add(b);
        return b;
    }

    private void PushState(CallLine line)
    {
        if (line.Round > 0) _liveRound = line.Round;
        if (!string.IsNullOrEmpty(line.Clock)) LiveClock = line.Clock;
        _liveMine = line.MyLanded;
        _liveHis = line.HisLanded;
        _liveMyHurt = line.MyHurt;
        _liveHisHurt = line.HisHurt;
        foreach (var n in new[] { nameof(LiveRound), nameof(LiveClock), nameof(LiveMine), nameof(LiveHis),
                                  nameof(LiveMineShare), nameof(LiveHisShare), nameof(MyManHurt), nameof(HisManHurt) })
            Raise(n);
    }

    // ---- watching it at your own pace ----

    private double _speed = 1.0;
    /// <summary>Playback speed multiplier. Applied to every line's dwell, so the shape of the pacing — a
    /// knockdown hanging longer than a jab — survives at any speed.</summary>
    public double Speed
    {
        get => _speed;
        private set
        {
            _speed = value;
            foreach (var n in new[] { nameof(Speed), nameof(IsHalfSpeed), nameof(IsNormalSpeed), nameof(IsDoubleSpeed) })
                Raise(n);
        }
    }
    public bool IsHalfSpeed => Math.Abs(_speed - 0.5) < 0.01;
    public bool IsNormalSpeed => Math.Abs(_speed - 1.0) < 0.01;
    public bool IsDoubleSpeed => Math.Abs(_speed - 2.0) < 0.01;

    private bool _paused;
    public bool IsPaused
    {
        get => _paused;
        private set { _paused = value; Raise(); Raise(nameof(PauseLabel)); }
    }
    public string PauseLabel => _paused ? "Resume" : "Pause";

    /// <summary>Pausing stops the call where it is so the feed can be scrolled back and read.</summary>
    private void DoTogglePause()
    {
        if (PlaybackFinished) return;
        IsPaused = !IsPaused;
        if (IsPaused) _playbackTimer.Stop();
        else _playbackTimer.Start();
    }

    private void DoSetSpeed(object? param)
    {
        Speed = param switch
        {
            double d => d,
            string t when double.TryParse(t, System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
            _ => 1.0
        };
    }

    private void StartPlayback()
    {
        var res = _svc.LastResult;
        if (res is null || Game is null) return;
        var me = Game.Player;
        bool iAmA = res.A.Id == me.Id;
        var them = iAmA ? res.B : res.A;

        FightNightHome = me.Name;
        FightNightAway = them.Name;
        FightNightHomeRecord = me.Record.ToString();
        FightNightAwayRecord = them.Record.ToString();
        FightNightBillLine = Game.Offer is { } o && o.TitleFight ? $"{o.Belt} TITLE" : $"{res.ScheduledRounds} rounds";
        PlaybackVerdict = ResultHeadline;
        foreach (var n in new[] { nameof(FightNightHome), nameof(FightNightAway), nameof(FightNightHomeRecord),
                                  nameof(FightNightAwayRecord), nameof(FightNightBillLine), nameof(PlaybackVerdict) })
            Raise(n);

        Rounds.Clear();
        _live = null;
        _call = FightCall.Build(res, me).ToList();
        _fed = 0;
        _liveRound = 0; _liveMine = 0; _liveHis = 0; _liveMyHurt = 0; _liveHisHurt = 0;
        LiveClock = "";
        PushState(new CallLine("", "", CallKind.Action));

        if (_call.Count == 0) { PlaybackFinished = true; IsPlayingBack = true; return; }

        // How big the night is, so a four-rounder in a club room doesn't sound like a unification.
        PlaybackFinished = false;
        IsPaused = false;
        IsPlayingBack = true;
        if (SoundOn) Sfx.StartBed(OccasionOf(res));
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(420 / Math.Max(0.1, _speed));
        _playbackTimer.Start();
    }

    /// <summary>Read the occasion from the bout itself: a title fight fills a arena, a six-rounder against a
    /// journeyman fills a hall.</summary>
    private Occasion OccasionOf(FightResult res)
    {
        var o = Game?.Offer;
        if (o?.TitleFight == true)
            return o.Belt is "Undisputed" or "unification" ? Occasion.Unification : Occasion.Title;
        int opp = (res.A.Id == Game?.Player.Id ? res.B : res.A).Overall;
        return res.ScheduledRounds >= 10 || opp >= 74 ? Occasion.Ranked : Occasion.Club;
    }

    private void RevealNextRound()
    {
        if (_fed >= _call.Count) { _playbackTimer.Stop(); PlaybackFinished = true; return; }
        var line = _call[_fed++];
        Feed(line);
        // Let the big moments hang before the next line lands; rattle through the routine ones. The speed
        // multiplier scales the whole shape rather than flattening it.
        double dwell = line.Kind switch
        {
            CallKind.Round => 700,
            CallKind.Drama => 950,
            CallKind.Verdict => 1100,
            CallKind.Score => 800,
            CallKind.Pattern => 1000,
            CallKind.Big => 500,
            _ => 380
        };
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(40, dwell / Math.Max(0.1, _speed)));
    }

    /// <summary>Skip to the verdict, or dismiss it once shown.</summary>
    private void EndPlayback()
    {
        _playbackTimer.Stop();
        IsPaused = false;
        if (!PlaybackFinished && _fed < _call.Count)
        {
            _skipping = true;
            for (; _fed < _call.Count; _fed++) Feed(_call[_fed]);
            _skipping = false;
            PlaybackFinished = true;
            if (SoundOn) Sfx.FinalBell();   // one bell for the end, not one per skipped line
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
            Recent = b.History.OrderByDescending(h => h.Date).Take(12).Select(ToLedger).ToList(),
            Division = b.WeightClass
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
        Recent = m.History.OrderByDescending(h => h.Date).Take(12).Select(ToLedger).ToList(),
        Division = m.Division
    };

    /// <summary>Attributes on the SAME 1–15 scale as the class pills, not the engine's internal 1–100. Two
    /// scales side by side meant nothing: "Power 75" told you nothing about whether 75 was good.</summary>
    private const int TopClass = 15;
    private static int OnClassScale(int raw) => Ratings.ClassFromRaw(raw);

    private static CardStat Bar(string name, int raw)
    {
        int c = OnClassScale(raw);
        return new CardStat(name, c, c / (double)TopClass);
    }

    private static IReadOnlyList<CardStat> AttributeBars(Ratings r) => new[]
    {
        Bar("Power", r.Power), Bar("Chin", r.Chin), Bar("Speed", r.Speed), Bar("Defence", r.Defense),
        Bar("Stamina", r.Stamina), Bar("Accuracy", r.Accuracy), Bar("Conditioning", r.Conditioning),
        Bar("Cut resistance", r.CutResistance), Bar("Aggression", r.Aggression), Bar("Heart", r.Heart),
    };

    private static LedgerRow ToLedger(BoutLine h)
    {
        string detail = h.Method + (h.Round > 0 && h.Method is "KO" or "TKO" ? $" rd{h.Round}" : "");
        if (h.Note is not null) detail = $"{h.Note} · {detail}";
        if (h.Rounds is { Count: > 0 } rs)
        {
            int f = rs.Sum(r => r.LandedFor), a = rs.Sum(r => r.LandedAgainst);
            detail += $"  ·  {rs.Count} rd · {f}/{a} landed";
            int kd = rs.Sum(r => r.KdFor), kda = rs.Sum(r => r.KdAgainst);
            if (kd + kda > 0) detail += $" · KD {kd}-{kda}";
        }
        return new LedgerRow(h.Date.ToString("d MMM yyyy"), h.Result.ToString(), h.Opponent, detail,
                             h.Result == 'W', h.Result == 'L', h);
    }

    // ---- a single fight, opened out ----

    private FightDetail? _selectedFight;
    public FightDetail? SelectedFight
    {
        get => _selectedFight;
        private set { _selectedFight = value; Raise(); Raise(nameof(HasFight)); }
    }
    public bool HasFight => _selectedFight is not null;

    /// <summary>A stored bout keeps its commentary as flat lines tagged with their round ("R3 — he STOPS him!").
    /// Group them by round and drop the tag, since the card already shows which round you're looking at.</summary>
    private static Dictionary<int, IReadOnlyList<string>> CommentaryByRound(IReadOnlyList<string>? lines)
    {
        var byRound = new Dictionary<int, List<string>>();
        foreach (var line in lines ?? Array.Empty<string>())
        {
            var m = System.Text.RegularExpressions.Regex.Match(line, @"^R(\d+)\s*[^\w]\s*(.+)$");
            if (!m.Success || !int.TryParse(m.Groups[1].Value, out int rd)) continue;
            if (!byRound.TryGetValue(rd, out var list)) byRound[rd] = list = new List<string>();
            list.Add(m.Groups[2].Value.Trim());
        }
        return byRound.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value);
    }

    private void OnShowFight(object? param)
    {
        if (param is not LedgerRow row || row.Bout is not BoutLine h) return;
        var rounds = h.Rounds ?? Array.Empty<BoutRound>();
        int lf = rounds.Sum(r => r.LandedFor), la = rounds.Sum(r => r.LandedAgainst);
        int kf = rounds.Sum(r => r.KdFor), ka = rounds.Sum(r => r.KdAgainst);
        int won = rounds.Count(r => r.ScoreFor > r.ScoreAgainst);
        int lost = rounds.Count(r => r.ScoreAgainst > r.ScoreFor);

        // Commentary sits WITH its round rather than in a block underneath: on anything longer than a
        // three-rounder that block was pushed off the bottom of the card and the fight read as bare numbers.
        var byRound = CommentaryByRound(h.Commentary);
        var placed = byRound.Values.SelectMany(v => v).ToHashSet();
        var unplaced = (h.Commentary ?? Array.Empty<string>())
            .Where(l => !placed.Any(p => l.EndsWith(p, StringComparison.Ordinal)))
            .ToList();

        var totals = new List<StatRow>();
        if (rounds.Count > 0)
        {
            totals.Add(new StatRow("Rounds", rounds.Count.ToString(), $"{won} won · {lost} lost"));
            totals.Add(new StatRow("Punches landed", lf.ToString(),
                                   $"{(double)lf / rounds.Count:0.0} a rd · range {rounds.Min(r => r.LandedFor)}–{rounds.Max(r => r.LandedFor)}"));
            totals.Add(new StatRow("Punches absorbed", la.ToString(),
                                   $"{(double)la / rounds.Count:0.0} a rd · range {rounds.Min(r => r.LandedAgainst)}–{rounds.Max(r => r.LandedAgainst)}"));
            totals.Add(new StatRow("Knockdowns", $"{kf}–{ka}", kf > ka ? "scored more" : ka > kf ? "took more" : ""));
        }

        SelectedFight = new FightDetail
        {
            Opponent = h.Opponent,
            Date = h.Date.ToString("d MMMM yyyy"),
            Verdict = (h.Result == 'W' ? "Won" : h.Result == 'L' ? "Lost" : "Drew") + $" by {h.Method}"
                      + (h.Round > 0 && h.Method is "KO" or "TKO" ? $", round {h.Round}" : ""),
            Note = h.Note ?? "",
            Cards = h.Cards ?? "",
            Win = h.Result == 'W',
            Loss = h.Result == 'L',
            Commentary = unplaced,
            Totals = totals,
            Rounds = rounds.Select(r => new FightRoundRow(
                $"R{r.Round}",
                $"{r.ScoreFor}–{r.ScoreAgainst}",
                $"{r.LandedFor} / {r.LandedAgainst}",
                r.KdFor + r.KdAgainst > 0 ? $"{r.KdFor}–{r.KdAgainst}" : "",
                r.ScoreFor > r.ScoreAgainst,
                r.KdFor + r.KdAgainst > 0,
                r.LandedFor + r.LandedAgainst > 0 ? (double)r.LandedFor / (r.LandedFor + r.LandedAgainst) : 0.5,
                byRound.GetValueOrDefault(r.Round) ?? (IReadOnlyList<string>)Array.Empty<string>()))
                .ToList()
        };
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

    // The physical matchup, which the engine already fights over: the longer man controls the range, and a
    // fast starter banks early rounds a slow one has to take back.
    private static string Frame(Boxer b) => $"{b.Height / 12}′{b.Height % 12}″  ·  {b.Reach}″ reach  ·  {b.StartSpeedLabel}";
    public string PlayerFrame => Game is null ? "" : Frame(Game.Player);
    public string OfferOpponentFrame => Game?.Offer is { } o ? Frame(o.Opponent) : "";
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

    /// <summary>The dashboard is a hub, not another list: your form, where you stand in your own division, what
    /// the sport is saying, and the headline numbers — each a way INTO the fuller screen behind it.</summary>
    private void BuildDashboard()
    {
        RecentForm.Clear(); DivisionTop.Clear(); HeadlineNews.Clear(); Headlines.Clear();
        if (Game is null) return;
        var p = Game.Player;

        foreach (var h in p.History.OrderByDescending(h => h.Date).Take(5)) RecentForm.Add(ToLedger(h));

        int r = 1;
        foreach (var b in Game.RankingBoard(p.WeightClass, 5))
        {
            bool champ = Game.IsWorldChampion(b);
            var belts = Game.BeltsHeld(b).Select(x => x.Belt).ToList();
            DivisionTop.Add(new RankRow(champ ? "C" : r.ToString(), b.Class, b.Name,
                                        belts.Count > 0 ? string.Join(" · ", belts) : "",
                                        b.Record.ToString(), b.Id == p.Id, champ, b));
            if (!champ) r++;
        }

        foreach (var (e, _) in Game.Log.Select((e, i) => (e, i))
                                       .Where(x => x.e.Div == p.WeightClass || x.e.PlayerBout)
                                       .OrderByDescending(x => x.e.On).ThenByDescending(x => x.i)
                                       .Take(6))
            HeadlineNews.Add(new NewsRow(e.DateLabel, e.Text, e.Kind ?? "", e.PlayerBout));

        int rank = PlayerRank;
        var myBelts = Game.BeltsHeld(p).Select(x => x.Belt).ToList();
        Headlines.Add(new StatRow("Record", p.Record.ToString(), CareerStages.Label(CareerStages.Of(p))));
        Headlines.Add(new StatRow("Division rank", rank > 0 ? $"#{rank}" : "unranked",
                                  p.WeightClass.DisplayName()));
        Headlines.Add(new StatRow("Titles", myBelts.Count > 0 ? string.Join(" · ", myBelts) : "none",
                                  Game.TitleDefenses > 0 ? $"{Game.TitleDefenses} defences" : ""));
        // Lead with the CLASS, the same number the rating pills show everywhere else. The raw 0–100 overall was
        // the odd one out on this tile and meant nothing next to the pills.
        Headlines.Add(new StatRow("Rating", $"Class {p.Class}", $"{p.Overall} OVR · age {p.Age}"));
    }

    public void RefreshAll()
    {
        BuildDashboard();
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
            nameof(PlayerFrame), nameof(OfferOpponentFrame),
            nameof(MoveUpLabel), nameof(HasResult), nameof(ResultHeadline), nameof(LastBoutWon),
            nameof(LastBoutLost), nameof(PlayerRetired), nameof(HasSave), nameof(SaveError),
            nameof(ShowResultBanner), nameof(HasLedger), nameof(RankingDivisions),
            nameof(ViewDivision), nameof(RankingsSubtitle), nameof(CanGoBack),
            nameof(IsAwayDivision), nameof(AwayDivisionNote), nameof(HomeDivisionLabel)
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
        foreach (var b in Game.RankingBoard(ViewDivision, 15))
        {
            bool champ = Game.IsWorldChampion(b);
            var belts = Game.BeltsHeld(b).Select(x => x.Belt).ToList();
            Rankings.Add(new RankRow(champ ? "C" : r.ToString(), b.Class, b.Name,
                                     belts.Count > 0 ? string.Join(" · ", belts) : "",
                                     b.Record.ToString(), b.Id == Game.Player.Id, champ, b));
            if (!champ) r++;   // contenders are #1 down; the champions sit above the numbering
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

    // ---- award filters ----

    public IReadOnlyList<string> AwardCategories { get; } = new[]
    {
        AllAwards, "Fighter of the Year", "Fight of the Year", "Knockout of the Year", "Upset of the Year"
    };
    private const string AllAwards = "All awards";
    private const string AllYears = "All years";

    public ObservableCollection<string> AwardYears { get; } = new();

    private string _awardCategory = AllAwards;
    public string AwardCategory
    {
        get => _awardCategory;
        set { if (_awardCategory == value) return; _awardCategory = value; Raise(); BuildAwards(); }
    }

    private string _awardYear = AllYears;
    public string AwardYear
    {
        get => _awardYear;
        set { if (_awardYear == value) return; _awardYear = value; Raise(); BuildAwards(); }
    }

    public string AwardsSubtitle => Awards.Count == 0
        ? "Nothing matches this filter — awards are handed out at the end of each year."
        : $"{Awards.Count} categor{(Awards.Count == 1 ? "y" : "ies")} · {AwardCategory} · {AwardYear}";

    private void BuildAwards()
    {
        Awards.Clear();
        if (Game is null) { Raise(nameof(AwardsSubtitle)); return; }

        // Keep the year list in step with the career without losing the current pick.
        var years = new[] { AllYears }.Concat(Game.Awards.Select(a => a.Year.ToString())).ToList();
        if (!AwardYears.SequenceEqual(years))
        {
            var keep = _awardYear;
            AwardYears.Clear();
            foreach (var y in years) AwardYears.Add(y);
            if (!years.Contains(keep)) { _awardYear = AllYears; Raise(nameof(AwardYear)); }
        }

        foreach (var yr in Game.Awards)
        {
            if (_awardYear != AllYears && yr.Year.ToString() != _awardYear) continue;
            void Add(string cat, IReadOnlyList<AwardWinner> list)
            {
                if (list.Count == 0) return;
                if (_awardCategory != AllAwards && cat != _awardCategory) return;
                var places = list.Select((w, i) => new AwardPlace(
                    i == 0 ? "1st" : i == 1 ? "2nd" : "3rd",
                    w.Name, w.Div.DisplayName(), w.Detail, i == 0, cat, yr.Year, w.Commentary)).ToList();
                Awards.Add(new AwardRow(cat, yr.Year, places));
            }
            Add("Fighter of the Year", yr.FighterOfYear);
            Add("Fight of the Year", yr.FightOfYear);
            Add("Knockout of the Year", yr.KnockoutOfYear);
            Add("Upset of the Year", yr.UpsetOfYear);
        }
        Raise(nameof(AwardsSubtitle));
    }

    // ---- an award opened out ----

    private AwardDetail? _selectedAward;
    public AwardDetail? SelectedAward
    {
        get => _selectedAward;
        private set { _selectedAward = value; Raise(); Raise(nameof(HasAward)); }
    }
    public bool HasAward => _selectedAward is not null;

    private void OnShowAward(object? param)
    {
        if (param is not AwardPlace a || Game is null) return;
        SelectedAward = new AwardDetail
        {
            Heading = $"{a.Year} · {a.Category}",
            Position = a.Position,
            Name = a.Name,
            Division = a.Division,
            Detail = a.Detail,
            Commentary = a.Commentary,
            Winner = a.Winner,
            Fighter = FindFighter(a.Name)
        };
    }

    /// <summary>Resolve an award's name to a live fighter. A "Fight of the Year" names both men, so it won't
    /// match — which is why the card link only appears when it does.</summary>
    private Boxer? FindFighter(string name)
    {
        if (Game is null) return null;
        foreach (var wc in Game.LiveDivisions)
        {
            var hit = Game.RankingOf(wc, 500).FirstOrDefault(b => b.Name == name);
            if (hit is not null) return hit;
        }
        return Game.Player.Name == name ? Game.Player : null;
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
        // Both men's attributes on the 1–15 class scale, so the tape reads in the same units as the pills.
        void Row(string name, int rawA, int rawB)
        {
            int a = OnClassScale(rawA), b = OnClassScale(rawB);
            Tape.Add(new TapeRow(name, a, b, a / (double)TopClass, b / (double)TopClass, a >= b));
        }
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

        // Everything below is aggregated from the per-round cards stored on each bout — the same data the
        // fight detail view shows one fight at a time.
        var scored = p.History.Where(h => h.Rounds is { Count: > 0 }).ToList();
        if (scored.Count > 0)
        {
            var rounds = scored.SelectMany(h => h.Rounds!).ToList();
            int lf = rounds.Sum(r => r.LandedFor), la = rounds.Sum(r => r.LandedAgainst);
            int kf = rounds.Sum(r => r.KdFor), ka = rounds.Sum(r => r.KdAgainst);
            int roundsWon = rounds.Count(r => r.ScoreFor > r.ScoreAgainst);

            Stats.Add(new StatRow("Rounds boxed", rounds.Count.ToString(),
                                  $"{roundsWon} won ({100.0 * roundsWon / rounds.Count:0}%)"));
            Stats.Add(new StatRow("Punches landed", lf.ToString("N0"),
                                  $"{(double)lf / rounds.Count:0.0} a round"));
            Stats.Add(new StatRow("Punches absorbed", la.ToString("N0"),
                                  $"{(double)la / rounds.Count:0.0} a round"));
            // The spread, not just the average — a man averaging 14 who ranges 4 to 30 is a different fighter
            // from one who lands 13 or 15 every round.
            Stats.Add(new StatRow("Output range",
                                  $"{rounds.Min(r => r.LandedFor)}–{rounds.Max(r => r.LandedFor)}",
                                  "landed in a round, worst to best"));
            Stats.Add(new StatRow("Absorbed range",
                                  $"{rounds.Min(r => r.LandedAgainst)}–{rounds.Max(r => r.LandedAgainst)}",
                                  "taken in a round, best to worst"));
            Stats.Add(new StatRow("Punch differential", (lf - la >= 0 ? "+" : "") + (lf - la).ToString("N0"),
                                  lf >= la ? "outlanding them" : "being outlanded"));
            Stats.Add(new StatRow("Knockdowns", $"{kf}–{ka}",
                                  $"{kf} scored, {ka} suffered"));
        }

        int koWins = p.History.Count(h => h.Result == 'W' && h.Method is "KO" or "TKO");
        int decWins = p.Record.Wins - koWins;
        int koLosses = p.History.Count(h => h.Result == 'L' && h.Method is "KO" or "TKO");
        Stats.Add(new StatRow("Wins by stoppage", $"{koWins}", $"{decWins} on the cards"));
        Stats.Add(new StatRow("Times stopped", $"{koLosses}",
                              koLosses == 0 && p.Record.Losses > 0 ? "never stopped" : ""));
        Stats.Add(new StatRow("Peak potential", $"{p.Potential}", ""));
        if (bestWin is not null)
            Stats.Add(new StatRow("Latest title win", bestWin.Opponent, $"{bestWin.Note} · {bestWin.Date:d MMM yyyy}"));
    }
}
