using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using BoxingSim.Core;
using BoxingSim.Core.Analysis;
using System.Windows.Threading;
using BoxingSim.Core.Career;
using BoxingSim.Core.Engine;
using BoxingSim.Core.Generation;
using BoxingSim.Core.Model;
using BoxingSim.Desktop.Pages;

namespace BoxingSim.Desktop;


/// <summary>A command backed by a plain delegate — enough for this app, no framework needed.</summary>
public sealed class Cmd : ICommand
{
    private readonly Action<object?> _run;
    private readonly Func<bool>? _can;
    public Cmd(Action run, Func<bool>? can = null) { _run = _ => run(); _can = can; }
    public Cmd(Action<object?> run, Func<bool>? can = null) { _run = run; _can = can; }
    public bool CanExecute(object? p) => _can?.Invoke() ?? true;

    /// <summary>Every click puts the pointer into a wait cursor for as long as the work takes.
    ///
    /// The app only did this for the handful of jobs routed through BusyAsync - starting a world, advancing to
    /// fight night. Everything else runs synchronously on the UI thread, so opening a card, switching division
    /// or rebuilding a ranking froze the window for a moment with no sign that anything was happening, which
    /// reads as a click that did not register. Doing it here covers every command at once rather than needing
    /// each one to remember. A fast command clears it within a frame and nothing is seen; a slow one shows it.
    ///
    /// Work that continues after the command returns (anything awaiting) keeps its own Busy flag, which the
    /// window style already watches.</summary>
    public void Execute(object? p)
    {
        var previous = System.Windows.Input.Mouse.OverrideCursor;
        System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        try { _run(p); }
        finally { System.Windows.Input.Mouse.OverrideCursor = previous; }
    }
    public event EventHandler? CanExecuteChanged;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public enum SetupMode { Career, Universe }

public enum Page { Dashboard, Career, Rankings, P4P, Champions, Hall, Awards, News, Stats, About, Universe, Settings }

/// <summary>One division the universe may or may not run. It reports back when toggled so the summary line
/// above the list stays honest.</summary>
public sealed class DivisionChoice : Observable
{
    private readonly Action _changed;
    public DivisionChoice(WeightClass division, string name, Action changed)
        { Division = division; Name = name; _changed = changed; }
    public WeightClass Division { get; }
    public string Name { get; }
    private bool _on;
    public bool On { get => _on; set { _on = value; Raise(); _changed(); } }
}

/// <summary>A sidebar entry. Group headers are not selectable — they only label the section beneath.</summary>
public sealed record NavItem(Page Page, string Label, bool IsHeader = false, string Shortcut = "")
{
    public bool IsPage => !IsHeader;
}

// ---- row shapes the views bind to ----

public sealed record RankRow(string Rank, int Class, string Name, string Detail, string Record,
                             bool IsPlayer, bool IsChampion, Boxer? Fighter, HallOfFamer? Legend = null);
public sealed record BeltRow(string Belt, string Holder, string Detail, bool Lineal, bool Vacant, Boxer? Fighter,
                             WeightClass Division = default)
{
    /// <summary>The key the line of succession is filed under. The primary belt is called "World" before 1962
    /// and the lineal one "Lineal" before 1922, but both are recorded under one name for their whole history —
    /// a belt does not become a different belt because the era renamed it.</summary>
    public string LineageKey => Belt is "Ring" or "Lineal" ? "Ring" : Belt is "WBC" or "IBF" ? Belt : "WBA";
}

/// <summary>One man's reign, as the succession panel reads it.</summary>
public sealed record ReignRow(string Holder, string Where, string Span, string How, bool IsCurrent, bool IsPlayer);
public sealed record DivisionRow(string Division, string Undisputed, IReadOnlyList<BeltRow> Belts, bool IsPlayerDivision);
/// <summary>One option in the news feed's division filter. Null means every division; its string form is the
/// label, because a ComboBox's closed state falls back to ToString() and would otherwise show the type name.</summary>
/// <summary>One bout on tonight's card, as the night runs through it. Mutable, because a row goes from
/// pending to current to fought while you are looking at it.</summary>
public sealed class CardBout : Observable
{
    public required string Fight { get; init; }
    public required string Distance { get; init; }
    public required string What { get; init; }

    /// <summary>Each corner separately, carried over from the BillLine this was made from — the night you sit
    /// through should read like the poster you read beforehand, not like a queue of unfamiliar pairs.
    ///
    /// Defaulted to empty and paired with HasCorners because a bout without them must fall back to the flat
    /// "A vs B" line rather than draw two blank columns. That is not hypothetical: binding the card straight
    /// to these before they existed rendered every fight on the bill as " · vs · ", and WPF said nothing.</summary>
    public string AName { get; init; } = "";
    public string ACountry { get; init; } = "";
    public string ARecord { get; init; } = "";
    public string BName { get; init; } = "";
    public string BCountry { get; init; } = "";
    public string BRecord { get; init; } = "";
    public bool HasCorners => AName.Length > 0 && BName.Length > 0;
    public string AUnder => Join(ACountry, ARecord);
    public string BUnder => Join(BCountry, BRecord);
    private static string Join(string a, string b) =>
        a.Length > 0 && b.Length > 0 ? $"{a}  ·  {b}" : a.Length > 0 ? a : b;
    public string Verdict { get; init; } = "";
    public string Note { get; init; } = "";
    public bool IsPlayer { get; init; }

    /// <summary>The line under the names: division, what is at stake, and where each man stands. A card of
    /// unfamiliar names tells you nothing about which of them matters without it.</summary>
    public string Billing { get; init; } = "";
    public bool HasBilling => Billing.Length > 0;

    /// <summary>MAIN EVENT / CHIEF SUPPORT / OPENER, as the poster prints it — so the list reads as a bill
    /// rather than as a queue.</summary>
    public string Slot { get; init; } = "";
    public bool HasSlot => Slot.Length > 0;

    private string _state = "pending";           // pending | current | done
    public string State
    {
        get => _state;
        set
        {
            _state = value;
            foreach (var n in new[] { nameof(State), nameof(IsPending), nameof(IsCurrent), nameof(IsDone) })
                Raise(n);
        }
    }
    public bool IsPending => _state == "pending";
    public bool IsCurrent => _state == "current";
    public bool IsDone    => _state == "done";
}

/// <summary>One of the three levels the build-up feed can be read at. Its string form IS its label, because a
/// ComboBox falls back to ToString() when it is closed and would otherwise show the type name.</summary>
public sealed record DetailChoice(CareerViewModel.CampDetail Level, string Label, string Explains)
{
    public override string ToString() => Label;
}

/// <summary>One line of the build-up feed, with how much it matters already worked out. The view should not be
/// comparing weight classes to decide how brightly to draw a row.</summary>
/// <summary>One line of the build-up feed.
///
/// A class rather than a record because one line in the feed can CHANGE: the bout the player is being offered
/// to watch has its result withheld, and has to be able to give it up once he has watched it or waved it away.
/// As an immutable record the withheld version was baked in at creation, so a fight he chose not to watch kept
/// its secret for the rest of the career.</summary>
public sealed class CampRow : Observable
{
    private readonly string _full;
    private readonly string _withheld;
    private bool _revealed;

    public CampRow(string date, string text, bool mine, bool isTitle, bool isUpset,
                   bool playerBout, BoutRef? bout, string detail, string? withheld = null)
    {
        Date = date; _full = text; _withheld = withheld ?? text; _revealed = withheld is null;
        Mine = mine; IsTitle = isTitle; IsUpset = isUpset; PlayerBout = playerBout; Bout = bout; Detail = detail;
    }

    public string Date { get; }
    public bool Mine { get; }
    public bool IsTitle { get; }
    public bool IsUpset { get; }
    public bool PlayerBout { get; }
    public BoutRef? Bout { get; }
    public string Detail { get; }

    /// <summary>What the line says — the result, or the matchup with the result held back.</summary>
    public string Text => _revealed ? _full : _withheld;

    /// <summary>True while this line is deliberately keeping its result back.</summary>
    public bool Withholding => !_revealed;

    /// <summary>Give up the result: he has watched it, or said he would rather not.</summary>
    public void Reveal()
    {
        if (_revealed) return;
        _revealed = true;
        Raise(nameof(Text));
        Raise(nameof(Withholding));
    }

    /// <summary>A belt changing hands, a boilover, or anything in his own division. Everything else is weather.</summary>
    public bool Major => IsTitle || IsUpset || Mine || PlayerBout;
    public string Tag => IsTitle ? "TITLE" : IsUpset ? "UPSET" : Mine ? "YOUR DIVISION" : "";
    public bool HasTag => Tag.Length > 0;
}

public sealed record NewsDivChoice(string Label, WeightClass? Div, bool IsMine = false)
{
    public override string ToString() => Label;
}

public sealed record NewsRow(string Date, string Text, string Kind, bool PlayerBout, BoutRef? Bout = null,
                             string Detail = "")
{
    /// <summary>A headline reporting a fight can be opened and watched; the rest are just news.</summary>
    public bool CanWatch => Bout is not null;
}
public sealed record AwardRow(string Category, int Year, IReadOnlyList<AwardPlace> Places);
public sealed record AwardPlace(string Position, string Name, string Division, string Detail, bool Winner,
                                string Category = "", int Year = 0, string Commentary = "",
                                BoutRef? Bout = null);

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

    /// <summary>The fight the honour was given for. An award is a claim about a night, and the night should be
    /// watchable rather than only described.</summary>
    public BoutRef? Bout { get; init; }
    public bool CanWatch => Bout is not null;
    public bool HasCommentary => !string.IsNullOrWhiteSpace(Commentary);
}
public sealed record LedgerRow(string Date, string Result, string Opponent, string Detail, bool Win, bool Loss,
                               BoutLine? Bout = null, string? OwnerName = null, bool Notable = false,
                               string Title = "")
{
    /// <summary>A title fight is the thing you scan a record FOR, so it gets its own mark rather than being
    /// buried in the middle of a detail line.</summary>
    public bool IsTitleFight => !string.IsNullOrEmpty(Title);
}

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
    /// <summary>The ledger row this panel was opened from — carries what is needed to rebuild and watch it.</summary>
    public LedgerRow? Source { get; init; }
    public string Opponent { get; init; } = "";
    public string Date { get; init; } = "";
    public string Verdict { get; init; } = "";
    public string Note { get; init; } = "";
    public string Cards { get; init; } = "";

    /// <summary>The three judges, broken out. A decision used to be a bare string - "116-112 · 115-113 ·
    /// 114-114" - which is the raw data rather than the story of the fight. Split up, you can see at a glance
    /// that two had it close and one had it wide, or that a man was a point from a draw.</summary>
    public IReadOnlyList<JudgeCard> Judges { get; init; } = Array.Empty<JudgeCard>();
    public bool HasJudges => Judges.Count > 0;

    /// <summary>The belt that was on the line, if any.</summary>
    public string Title { get; init; } = "";
    public bool IsTitleFight => !string.IsNullOrEmpty(Title);
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

/// <summary>One judge's card, read from the owning fighter's side.</summary>
public sealed record JudgeCard(string Judge, string Score, string Verdict, bool ForMe, bool Level);

/// <summary>One punch's share of a fighter's arsenal, with the colour it is drawn in.</summary>
public sealed record ArsenalSlice(string Name, int Percent, double Width, string Colour);

/// <summary>One point on a fighter's career arc: the stage, the mileage, and the man he was at it. The card's
/// stage picker binds to these, and selecting one re-reads every rating on the card at that point.</summary>
public sealed record ArcRow(string Stage, int Fights, Ratings Ratings, bool IsNow)
{
    public string Label => IsNow ? $"Now  ·  {Fights} fights" : $"{Stage}  ·  {Fights} fights";
    public override string ToString() => Label;   // the closed ComboBox falls back to this
}
public sealed class FighterCard : Observable
{
    public string Name { get; init; } = "";
    public string Meta { get; init; } = "";
    public int Class { get; init; }
    public string Record { get; init; } = "";
    public string Belts { get; init; } = "";

    /// <summary>What kind of fighter he is, and what that means. The sim has classified every man since it
    /// was written - style drives the engine's exchanges, how dirty he is, how dangerous he is countering,
    /// and which punches he throws - but it was never once said out loud on screen.</summary>
    public string Style { get; init; } = "";
    public string StyleNote { get; init; } = "";

    /// <summary>The fighter himself, so his stats can be recomputed at any point on his arc.</summary>
    public Boxer? Fighter { get; init; }

    public IReadOnlyList<LedgerRow> Recent { get; init; } = Array.Empty<LedgerRow>();

    /// <summary>How many of his bouts this world actually saw, out of how many he has had. Fighters on the
    /// roster start with a record already built and no ledger behind it, so a 29-1-0 veteran can show two
    /// fights — which reads as a bug unless the card says otherwise.</summary>
    public string RecordNote { get; init; } = "";

    /// <summary>Every point on his career the card can be read at, most recent last.</summary>
    public IReadOnlyList<ArcRow> Arc { get; init; } = Array.Empty<ArcRow>();
    public bool HasArc => Arc.Count > 1;

    private ArcRow? _stage;
    /// <summary>Which point of his career the card is showing. Changing it re-reads the attributes, the
    /// arsenal and the derived ratings as they were then — a faded champion's card can be wound back to the
    /// fighter who won the title.</summary>
    public ArcRow? SelectedStage
    {
        get => _stage ??= Arc.LastOrDefault();
        set
        {
            if (value is null || ReferenceEquals(value, _stage)) return;
            _stage = value;
            foreach (var n in new[] { nameof(SelectedStage), nameof(Ratings), nameof(Arsenal),
                                      nameof(Secondary), nameof(HasRatings), nameof(IsHistoric), nameof(StageNote) })
                Raise(n);
        }
    }

    /// <summary>True when the card is wound back, so the screen can say so rather than quietly showing
    /// numbers that are not the man's current ones.</summary>
    public bool IsHistoric => SelectedStage is { IsNow: false };
    public string StageNote => IsHistoric
        ? $"as he was at {SelectedStage!.Fights} fights — not his ratings today" : "";

    private Ratings? Now => SelectedStage?.Ratings ?? Fighter?.Ratings;

    public IReadOnlyList<CardStat> Ratings => Now is { } r ? CareerViewModel.AttributeBars(r) : Array.Empty<CardStat>();
    public bool HasRatings => Now is not null;

    /// <summary>What the ledger says about him rather than what his ratings do — how he finishes, how he is
    /// finished, and the volume he works at. A 1-15 bar cannot say "he has stopped two thirds of them".</summary>
    public IReadOnlyList<StatRow> Form { get; init; } = Array.Empty<StatRow>();
    public bool HasForm => Form.Count > 0;

    /// <summary>Which punches he actually throws, as a share of everything he lets go. Two men on identical
    /// ratings fight nothing alike if one lives behind a jab and the other digs to the body all night.</summary>
    public IReadOnlyList<ArsenalSlice> Arsenal =>
        Fighter is { } f && Now is { } r ? CareerViewModel.ArsenalOf(f.WithRatings(r)) : Array.Empty<ArsenalSlice>();
    public bool HasArsenal => Arsenal.Count > 0;

    /// <summary>The derived qualities: what his raw attributes ADD UP TO. Killer instinct, durability,
    /// recovery, pressure and countering are each a blend of several ratings, and they are the things people
    /// actually describe a fighter with.</summary>
    public IReadOnlyList<CardStat> Secondary =>
        Fighter is { } f && Now is { } r ? CareerViewModel.SecondaryOf(f.WithRatings(r)) : Array.Empty<CardStat>();
    public bool HasSecondary => Secondary.Count > 0;
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
    private readonly Prefs _prefs = Prefs.Load();

    public CareerViewModel()
    {
        // Read before anything binds, so the first frame already shows what the player chose last time.
        _campDetail = Enum.TryParse<CampDetail>(_prefs.CampDetail, out var cd) ? cd : CampDetail.Normal;
        _campMineOnly = _prefs.CampMineOnly;
        _soundOn = _prefs.SoundOn;
        _speed = _prefs.Speed;
        Sfx.Enabled = _soundOn;

        // The slate reaches the world through delegates rather than holding this object, so the dependency
        // runs one way. Game is fetched rather than passed because it is replaced wholesale when a career is
        // started, loaded or abandoned.
        OfferSlate = new OfferSlateViewModel(() => Game, () => NothingAgreedYet, OnPicked);
        RankingsPage = new RankingsViewModel(() => Game);
        PoundForPoundPage = new PoundForPoundViewModel(() => Game);
        HallOfFamePage = new HallOfFameViewModel(() => Game);
        ChampionsPage = new ChampionsViewModel(() => Game);
        AwardsPage = new AwardsViewModel(() => Game);
        NewsPage = new NewsViewModel(() => Game, () => InCareer);
        StatsPage = new StatsViewModel(() => Game);
        UniversePage = new UniverseViewModel(() => _svc.Universe);
        DashboardPage = new DashboardViewModel(() => Game, () => PlayerRank);

        TakeFight = new Cmd(() => Take(), () => Game?.Offer is not null && Game?.Player.Retired == false);
        HoldOut = new Cmd(Decline, () => Game?.Offer is not null && Game?.Player.Retired == false);
        MoveUp = new Cmd(DoMoveUp, () => Game?.CanMoveUp == true);
        StartCareer = new Cmd(Start, () => !Busy && Ready);
        ContinueCareer = new Cmd(Continue, () => Ready && DesktopCareerService.HasSaveIn(SetupSlot));
        PickSlot = new Cmd(p => { if (p is int n) SetupSlot = n; else if (p is string t && int.TryParse(t, out var m)) SetupSlot = m; });
        DeleteSlot = new Cmd(() => { _svc.AbandonSlot(SetupSlot); RefreshSlots(); },
                             () => DesktopCareerService.HasSaveIn(SetupSlot));
        AbandonCareer = new Cmd(Abandon);
        RollName = new Cmd(() => PlayerName = NameGen.Generate(Country, _rng));
        StartUniverse = new Cmd(DoStartUniverse);
        PlayWeek = new Cmd(DoPlayWeek);
        PlayMonth = new Cmd(() => { for (int i = 0; i < 4; i++) DoPlayWeek(); });
        PlayYear = new Cmd(DoPlayYear);
        LeaveUniverse = new Cmd(() =>
        {
            _svc.EndUniverse();
            UniversePage.Clear();
            _selectedNav = CareerNav[0];
            RefreshAll();
            Raise(nameof(SelectedNav)); Raise(nameof(CurrentPage));
        });
        CloseYearAwards = new Cmd(() =>
        {
            ShowYearAwards = false; Raise(nameof(ShowYearAwards));
            // If the honours interrupted a walk to the ring, finish the walk. The alternative is dropping the
            // player back on the poster page to work out for himself that the fight is still waiting.
            if (_resumeAfterAwards) { _resumeAfterAwards = false; Take(resuming: true); }
        });
        ToggleNews = new Cmd(() => NewsOpen = !NewsOpen);
        // Held between weeks: let the next one land. Stopped altogether: pick the walk up where it left off.
        PlayBout = new Cmd(DoPlayBout);
        ResolveCard = new Cmd(DoResolveCard, () => CanResolveCard);
        SkipBout = new Cmd(DoSkipBout);
        LeaveArena = new Cmd(DoLeaveArena);
        RunTheRest = new Cmd(() => { if (!_waiting) Take(resuming: true); });
        CloseNewsDrawer = new Cmd(() => NewsOpen = false);
        WaitForFight = new Cmd(DoWaitForFight);
        StopWaiting = new Cmd(() => { _waiting = false; Raise(nameof(IsWaiting)); Raise(nameof(ShowCamp)); RaiseCampGates(); });
        WatchTheOne = new Cmd(DoWatchTheOne);
        SkipTheOne = new Cmd(DoSkipTheOne);
        ShowFighter = new Cmd(OnShowFighter);
        // Your own card, from the sidebar. Same card every other fighter gets.
        ShowMyCard = new Cmd(() => { if (Game is not null) SelectedCard = BuildCard(Game.Player); });
        ShowFight = new Cmd(OnShowFight);
        WatchFight = new Cmd(OnWatchFight);
        ShowAward = new Cmd(OnShowAward);
        WatchAward = new Cmd(OnWatchAward);
        WatchNews = new Cmd(OnWatchNews);
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
        // Escape backs out of whatever is on top: the fighter card, the playback, then the page you came from.
        Dismiss = new Cmd(() =>
        {
            if (SelectedAward is not null) SelectedAward = null;
            else if (NewsOpen) NewsOpen = false;
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
        // Read the slots off disk once the roster is in — the title screen needs them before it can offer
        // anything, and Peek is the only thing that knows whether "Continue" means anything.
        var slots = await Task.Run(ReadSlots);
        ApplySlots(slots);
        // Open on a career if there is one, rather than on slot 1 whether it holds anybody or not.
        SetupSlot = Slots.FirstOrDefault(x => x.Occupied && !x.Damaged)?.Slot ?? 1;
        SetupDivision = Divisions.FirstOrDefault(WeightClass.Middleweight);
        UniDivisions.Clear();
        foreach (var d in Divisions)
            UniDivisions.Add(new DivisionChoice(d, d.DisplayName(), () => Raise(nameof(UniDivisionsLabel))));
        Ready = true;
    }

    private bool _ready;
    public bool Ready
    {
        get => _ready;
        private set { _ready = value; Raise(); Raise(nameof(Loading)); RefreshCommands(); }
    }
    public bool Loading => !_ready;

    /// <summary>The world every page reads. In a career that is the player's game; in a universe it is the
    /// sport itself. Everything that shows rankings, champions, news or the Hall of Fame works off this, so
    /// both modes get those pages from the same code.</summary>
    public CareerGame? Game => _svc.Game ?? _svc.Universe?.World;
    public bool InCareer => _svc.HasCareer;

    // ---- universe ----
    public bool InUniverse => _svc.InUniverse;
    /// <summary>Either mode fills the shell; only the sidebar and the first page differ.</summary>
    public bool InPlay => _svc.HasCareer || _svc.InUniverse;

    private SetupMode _setupMode = SetupMode.Career;
    /// <summary>Which of the two the setup screen is offering.</summary>
    public SetupMode SetupMode { get => _setupMode; set { _setupMode = value; Raise(); } }
    public bool SetupIsCareer { get => _setupMode == SetupMode.Career; set { if (value) SetupMode = SetupMode.Career; } }
    public bool SetupIsUniverse { get => _setupMode == SetupMode.Universe; set { if (value) SetupMode = SetupMode.Universe; } }
    /// <summary>The setup screen shows only when neither a career nor a universe is running.</summary>
    public bool AtSetup => !_svc.HasCareer && !_svc.InUniverse;

    private int _uniStartYear = 1960;
    public int UniStartYear { get => _uniStartYear; set { _uniStartYear = value; Raise(); } }
    private int _uniEntrants = 18;
    public int UniEntrants { get => _uniEntrants; set { _uniEntrants = Math.Clamp(value, 2, 60); Raise(); } }
    private double _uniCareerLength = 1.0;
    public double UniCareerLength { get => _uniCareerLength; set { _uniCareerLength = value; Raise(); Raise(nameof(UniCareerLengthLabel)); } }
    public string UniCareerLengthLabel => $"{_uniCareerLength:0.0}×  (median ≈ {(int)Math.Round(50 * _uniCareerLength)} fights)";
    private double _uniActivity = 1.0;
    public double UniActivity { get => _uniActivity; set { _uniActivity = value; Raise(); Raise(nameof(UniActivityLabel)); } }
    /// <summary>Stated as cards rather than bouts-per-man, because the dial scales the number of cards exactly
    /// and the per-fighter figure that follows from it is not linear.</summary>
    public string UniActivityLabel => _uniActivity == 1.0
        ? "1.0×  (the sim's own pace — a contender is out three or four times a year)"
        : $"{_uniActivity:0.0}×  ({(_uniActivity < 1 ? "quieter" : "busier")} — {_uniActivity:0.0}× as many cards)";
    private int _uniWarmup = 8;
    public int UniWarmup { get => _uniWarmup; set { _uniWarmup = Math.Clamp(value, 0, 30); Raise(); } }
    private bool _uniRealFighters = true;
    public bool UniRealFighters { get => _uniRealFighters; set { _uniRealFighters = value; Raise(); } }

    /// <summary>Which divisions the universe runs. All of them is the default — a sport, not a division —
    /// but a single-division world is much faster and much easier to follow.</summary>
    public ObservableCollection<DivisionChoice> UniDivisions { get; } = new();
    public string UniDivisionsLabel
    {
        get
        {
            var on = UniDivisions.Where(d => d.On).ToList();
            return on.Count == 0 || on.Count == UniDivisions.Count ? "every division"
                 : on.Count <= 3 ? string.Join(", ", on.Select(d => d.Name))
                 : $"{on.Count} divisions";
        }
    }






    // ---- navigation ----
    // Career mode is the app; the boards are reference you dip into. The sidebar says so — your own two screens
    // first, then the sport, then the record books — rather than eight peers where "Fight night" and "Career"
    // sounded like the same thing.
    public IReadOnlyList<NavItem> Nav => InUniverse ? UniverseNav : CareerNav;

    private static readonly NavItem[] CareerNav =
    {
        new NavItem(Page.Dashboard, "Dashboard", Shortcut: "Ctrl+1"),
        new NavItem(Page.Career, "Camp", Shortcut: "Ctrl+2"),
        new NavItem(Page.Stats, "My record", Shortcut: "Ctrl+3"),
        new NavItem(Page.Dashboard, "THE SPORT", IsHeader: true),
        new NavItem(Page.Rankings, "Rankings", Shortcut: "Ctrl+4"),
        new NavItem(Page.P4P, "Pound-for-pound", Shortcut: "Ctrl+5"),
        new NavItem(Page.Champions, "Champions", Shortcut: "Ctrl+6"),
        new NavItem(Page.News, "News", Shortcut: "Ctrl+7"),
        new NavItem(Page.Dashboard, "THE RECORD BOOKS", IsHeader: true),
        new NavItem(Page.Hall, "Hall of Fame", Shortcut: "Ctrl+8"),
        new NavItem(Page.Awards, "Awards", Shortcut: "Ctrl+9"),
        // Not a feature — the crowd recordings are CC-BY, and that licence obliges the app itself to carry the
        // credit, not just the repository.
        new NavItem(Page.Settings, "Settings"),
        new NavItem(Page.About, "About", Shortcut: "Ctrl+0"),
    };

    // A universe has no player, so the three screens about one are gone. What is left is the sport, which in
    // career mode is the reference section and here is the whole thing.
    private static readonly NavItem[] UniverseNav =
    {
        new NavItem(Page.Universe, "This week", Shortcut: "Ctrl+1"),
        new NavItem(Page.Dashboard, "THE SPORT", IsHeader: true),
        new NavItem(Page.Rankings, "Rankings", Shortcut: "Ctrl+2"),
        new NavItem(Page.P4P, "Pound-for-pound", Shortcut: "Ctrl+3"),
        new NavItem(Page.Champions, "Champions", Shortcut: "Ctrl+4"),
        new NavItem(Page.News, "News", Shortcut: "Ctrl+5"),
        new NavItem(Page.Dashboard, "THE RECORD BOOKS", IsHeader: true),
        new NavItem(Page.Hall, "Hall of Fame", Shortcut: "Ctrl+6"),
        new NavItem(Page.Awards, "Awards", Shortcut: "Ctrl+7"),
        new NavItem(Page.Settings, "Settings"),
        new NavItem(Page.About, "About", Shortcut: "Ctrl+0"),
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
        _back.Push((from, RankingsPage.ViewDivision));
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
            RankingsPage.Restore(division);
            SelectedNav = Nav.FirstOrDefault(n => n.IsPage && n.Page == page) ?? SelectedNav;
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
        SelectedNav = Nav.FirstOrDefault(n => n.IsPage && n.Page == page) ?? SelectedNav;
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
        } ?? RankingsPage.ViewDivision;
        RankingsPage.ViewDivision = wc;
        SelectedCard = null;
        SelectedNav = Nav.FirstOrDefault(n => n.IsPage && n.Page == Page.Rankings) ?? SelectedNav;
    }

    /// <summary>The rankings board — its own page, its own view-model. See RankingsViewModel.</summary>
    public RankingsViewModel RankingsPage { get; }

    /// <summary>The pound-for-pound list — its own page. See PoundForPoundViewModel.</summary>
    public PoundForPoundViewModel PoundForPoundPage { get; }

    /// <summary>The hall of fame — its own page. See HallOfFameViewModel.</summary>
    public HallOfFameViewModel HallOfFamePage { get; }

    /// <summary>The champions board — its own page. See ChampionsViewModel.</summary>
    public ChampionsViewModel ChampionsPage { get; }

    /// <summary>The year's honours, and the two filters they are read through. See AwardsViewModel.</summary>
    public AwardsViewModel AwardsPage { get; }

    /// <summary>The feed. Read by the News page AND the shell's drawer, which share it on purpose. See
    /// NewsViewModel.</summary>
    public NewsViewModel NewsPage { get; }

    /// <summary>What this fighter has done so far. See StatsViewModel.</summary>
    public StatsViewModel StatsPage { get; }

    /// <summary>A week of the whole sport, region by region. See UniverseViewModel.</summary>
    public UniverseViewModel UniversePage { get; }

    /// <summary>Career mode's hub. See DashboardViewModel.</summary>
    public DashboardViewModel DashboardPage { get; }

    // ---- setup screen ----
    public ObservableCollection<WeightClass> Divisions { get; } = new();
    public IReadOnlyList<string> Countries { get; } = new[]
    {
        "USA", "England", "Mexico", "Germany", "Russia", "Ukraine", "Canada",
        "Italy", "Argentina", "Cuba", "Nigeria", "Poland"
    };
    public IReadOnlyList<int> Years { get; } = Enumerable.Range(1945, 71).ToList();
    /// <summary>What you are starting with, in the app's own 1-15 units and in plain words. The combo used to
    /// show the raw keys - "random915" and "club" - which told a new player nothing at all.
    ///
    /// Elite leads, and the screen therefore opens on it: a career is a long thing to sit down to, and the
    /// fighter most people want to spend it on is the one who can win the belt. New Star sits behind it for
    /// anyone who would rather have the ceiling left to chance.</summary>
    public IReadOnlyList<TalentOption> Talents { get; } = new[]
    {
        TalentOption.Make("elite", "Elite"),
        TalentOption.Make("random915", "New Star"),
        TalentOption.Make("contender", "Contender"),
        TalentOption.Make("journeyman", "Journeyman"),
        TalentOption.Make("club", "Club fighter"),
    };

    private string _playerName = "";
    public string PlayerName { get => _playerName; set { _playerName = value; Raise(); } }

    private string _country = "USA";
    public string Country { get => _country; set { _country = value; Raise(); } }

    private int _setupYear = 1965;
    public int SetupYear { get => _setupYear; set { _setupYear = value; Raise(); } }

    private WeightClass _setupDivision;
    public WeightClass SetupDivision { get => _setupDivision; set { _setupDivision = value; Raise(); } }

    private string _talent = "elite";   // Elite leads the list, so it is what the screen opens on
    public string Talent { get => _talent; set { _talent = value; Raise(); } }

    /// <summary>The three careers that can be kept at once, and which one the title screen is pointed at.
    ///
    /// Rebuilt rather than cached, because the disk is the truth: a career abandoned, started or saved changes
    /// what the slots say, and a stale row would offer to continue a save that is no longer there.</summary>
    public ObservableCollection<SlotInfo> Slots { get; } = new();

    private int _setupSlot = 1;
    public int SetupSlot
    {
        get => _setupSlot;
        set { _setupSlot = Math.Clamp(value, 1, DesktopCareerService.Slots); Raise(); MarkSelected(); }
    }

    public SlotInfo SelectedSlot => Slots.FirstOrDefault(s => s.Slot == _setupSlot)
                                    ?? new SlotInfo(_setupSlot, false, "Empty", "", "", false);

    /// <summary>Move the highlight without touching the disk. Reading three saves to answer "which one is
    /// ticked" would put a two-megabyte parse behind every click on the title screen.</summary>
    private void MarkSelected()
    {
        foreach (var s in Slots) s.IsSelected = s.Slot == _setupSlot;
        foreach (var n in new[] { nameof(Slots), nameof(SelectedSlot) }) Raise(n);
        RefreshCommands();
    }

    public Cmd PickSlot { get; private set; } = null!;

    /// <summary>Read the three saves off disk. Safe on any thread — it touches no bound collection, which is
    /// the whole reason it is separate from applying them.</summary>
    private static List<SlotInfo> ReadSlots() =>
        Enumerable.Range(1, DesktopCareerService.Slots).Select(DesktopCareerService.Peek).ToList();

    /// <summary>Put what was read into the bound collection. MUST be on the dispatcher thread: WPF's
    /// CollectionView refuses changes to its source from anywhere else, and it does not refuse quietly — it
    /// throws, and the first version of this did exactly that during warm-up because the disk read and the
    /// collection update were the same method behind one Task.Run.</summary>
    private void ApplySlots(List<SlotInfo> read)
    {
        Slots.Clear();
        foreach (var s in read) Slots.Add(s);
        foreach (var s in Slots) s.IsSelected = s.Slot == _setupSlot;
        foreach (var n in new[] { nameof(Slots), nameof(SelectedSlot), nameof(SetupSlot), nameof(HasSave) }) Raise(n);
        RefreshCommands();
    }

    /// <summary>Re-read the slots and show them. Dispatcher thread only — see ApplySlots.</summary>
    public void RefreshSlots() => ApplySlots(ReadSlots());

    /// <summary>Throw away the career in the slot the title screen is pointed at. Guarded in the view by a
    /// confirmation, because there is no undo and no second copy.</summary>
    public Cmd DeleteSlot { get; private set; } = null!;

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
    /// <summary>Open a universe with the chosen settings.</summary>
    public Cmd StartUniverse { get; }
    /// <summary>Run the sport on: a week, a month, a year.</summary>
    public Cmd PlayWeek { get; }
    public Cmd PlayMonth { get; }
    public Cmd PlayYear { get; }
    public Cmd LeaveUniverse { get; }

    /// <summary>Let the weeks run to fight night, one at a time, with the sport happening in front of you.</summary>
    public Cmd WaitForFight { get; }
    public Cmd StopWaiting { get; }
    /// <summary>Watch the fight that came up while you were waiting.</summary>
    public Cmd WatchTheOne { get; }

    /// <summary>Turn the offer down. The result is revealed rather than lost.</summary>
    public Cmd SkipTheOne { get; }

    public Cmd ShowFighter { get; }

    /// <summary>Open your own fighter card - attributes, form and full record.</summary>
    public Cmd ShowMyCard { get; }
    public Cmd CloseCard { get; }
    public Cmd ShowFight { get; }

    /// <summary>Replay a fight from the record books, blow by blow.</summary>
    public Cmd WatchFight { get; }
    public Cmd ShowAward { get; }

    /// <summary>Play the fight an honour was given for.</summary>
    public Cmd WatchAward { get; }

    /// <summary>Play a fight straight from a news headline.</summary>
    public Cmd WatchNews { get; }
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
    public ObservableCollection<LedgerRow> Ledger { get; } = new();
    public ObservableCollection<TapeRow> Tape { get; } = new();

    // ---- the dashboard: career mode's hub ----

    public bool HasLedger => Ledger.Count > 0;

    /// <summary>One pick on the setup screen: the key the sim uses, and what a person reads.</summary>
    public sealed record TalentOption(string Key, string Label)
    {
        public static TalentOption Make(string key, string name)
        {
            var (lo, hi) = TalentRange(key);
            // Shown on the 1-15 class scale, because that is the scale every rating in the app uses — but
            // CONVERTED, not handed over raw. These bounds are potential, which caps a man's Overall, and
            // ClassFromRaw wants a raw weighted score: two different rulers. Passed straight through, "elite"
            // advertised class 11-15 and produced fighters who peaked at 8, and the fighters were the honest
            // half of that.
            return new TalentOption(key, $"{name}  ({Ratings.ClassFromOverall(lo)}–{Ratings.ClassFromOverall(hi)})");
        }

        /// <summary>A record's generated ToString prints its whole shape - "TalentOption { Key = random915,
        /// Label = ... }" - and the ComboBox's closed state fell back to it, so the setup screen was showing
        /// the type name to the player. The string form of this IS its label.</summary>
        public override string ToString() => Label;
    }

    private static (int Lo, int Hi) TalentRange(string t) => t switch
    {
        "elite" => (90, 100),   // class 10-13 — a genuine prospect, not merely a good one
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
                            () => _svc.Start(name, Country, SetupYear, potential, div, FullHistory, SetupSlot));
            RankingsPage.Restore(div);
        }
        catch (Exception ex) { BusyMessage = ex.Message; return; }
        SetCommitted(false);
        SelectedNav = Nav[0];
        RefreshAll();
    }

    private async void Continue()
    {
        bool ok = false;
        await BusyAsync("Loading your career…", () => ok = _svc.Load(SetupSlot));
        if (ok) { SetCommitted(false); _pickedOffer = _svc.Game?.Offer; RankingsPage.Restore(_svc.Game!.Player.WeightClass); SelectedNav = Nav[0]; RefreshAll(); }
        else { ContinueCareer.Refresh(); Raise(nameof(HasSave)); }
    }

    /// <summary>Set from the moment a fight is taken until its playback is over.
    ///
    /// The year's honours are announced when the calendar turns, and taking a fight carries the calendar
    /// months forward — so a fight that crossed New Year opened the awards panel on top of fight night,
    /// over the round-by-round call, with the verdict already showing behind it. RefreshAll guarded on
    /// IsPlayingBack, but Take refreshes BEFORE playback starts, so the guard read false at exactly the
    /// moment it mattered. The awards can wait; the fight cannot be interrupted.</summary>
    private bool _awardsWait;

    /// <summary>Set when the year's honours arrived on the very night he was walking out, so closing them
    /// carries on to the fight rather than abandoning him on the poster page.</summary>
    private bool _resumeAfterAwards;

    /// <summary>Take the fight — which is a night, not a button.
    ///
    /// Taking a fight used to jump from the dashboard to the opening bell in one frame, silently running
    /// however many months of the sport lay in between. Now the weeks run in front of you on the poster page,
    /// stopping if something in your own division is worth watching; then the undercard is fought; then you
    /// walk out. Both stages are preferences, and with both off this is exactly the old single frame.</summary>
    private async void Take(bool resuming = false)
    {
        if (Game is null || Game.Player.Retired || Game.Offer is null) return;

        if (_prefs.FightWeek && Game.DaysToFight > 0)
        {
            DoNavigate(Page.Career);          // the poster and the weeks live there
            SetCommitted(true);
            await RunToFightNight(resuming);

            // He stopped for something, or the year turned and wants acknowledging. The fight keeps, and
            // "On to fight night" brings him back here with the weeks so far still on the page.
            //
            // ShowYearAwards has to be part of this. Without it, a year that turned on the LAST week of the
            // wait opened the honours panel and then fell straight through into the fight: the panel sits at
            // ZIndex 12 and fight night at 10, so the bout ran on behind it. The earlier fix only stopped the
            // honours being RAISED during a fight — it never considered them already being on screen when one
            // started. Instead of making the player work out what to press afterwards, dismissing the panel
            // walks him out: see CloseYearAwards.
            // AND STOP THERE. The run-up used to fall straight through into the opening bell when it reached
            // fight night, so the weeks and the fight were one press — and now that the weeks run in a single
            // pass rather than one at a time, that press took you from agreeing to a fight to standing in the
            // ring with no chance to read the build-up or look at the card you are on. The walk-out is its own
            // decision: the page shows "Fight now" and waits to be told.
            //
            // Coming back through here with the date already reached skips this block entirely, so the second
            // press goes to the bout.
            _resumeAfterAwards = ShowYearAwards && Game?.DaysToFight == 0;
            return;
        }

        _awardsWait = true;
        // Where tonight is, captured before the bout replaces the offer with the next one.
        _nightTitle = Game.BillHeader;
        _nightNote = Game.CardNote;
        await BusyAsync("Fight night…", () => _svc.Take());
        // The bout is in the books and a fresh offer has been drawn behind it: out of camp, and the weeks just
        // walked belong to a fight that has already happened.
        SetCommitted(false);
        Camp.Clear(); _campAll.Clear(); Raise(nameof(CampCountLabel)); Raise(nameof(ShowCamp));
        RefreshAll();

        if (_prefs.LiveUndercard)
        {
            BuildCardNight();
            IsCardRunning = CardNight.Count > 0;
        }

        if (!IsCardRunning)
        {
            StartPlayback();
            // Nothing to play back — a declined or vanished offer. Without this the flag would stay raised and
            // the year's honours would never be announced again for the rest of the career.
            if (!IsPlayingBack) { _awardsWait = false; CheckForAwards(); }
        }
    }

    // ---- the night, run through bout by bout ----
    //
    // A card used to be a list you read before the night and a list you read after it, with your own bout the
    // only thing that actually happened. Now the whole bill runs in order — the openers first, yours in its
    // proper place on the bill, whatever is above you afterwards — and you stay in the arena until the last
    // man has been out.

    public ObservableCollection<CardBout> CardNight { get; } = new();

    private bool _cardRunning;
    public bool IsCardRunning { get => _cardRunning; private set { _cardRunning = value; Raise(); Raise(nameof(CardOnScreen)); } }

    /// <summary>The card is up, but stands aside while the player's own bout is being called.</summary>
    public bool CardOnScreen => _cardRunning && !IsPlayingBack;

    public CardBout? Current => CardNight.FirstOrDefault(b => b.IsCurrent);
    public bool CardFinished => _cardRunning && CardNight.Count > 0 && CardNight.All(b => b.IsDone);
    private string _nightTitle = "", _nightNote = "";
    public string CardTitle => _nightTitle;
    public string CardTonightNote => _nightNote;

    /// <summary>"Play" on somebody else's bout; "walk out" on your own.</summary>
    public string PlayLabel => Current?.IsPlayer == true ? "Walk out" : "Play";

    public Cmd PlayBout { get; private set; } = null!;

    /// <summary>Settle the rest of the undercard in one press, stopping at the player's own fight.</summary>
    public Cmd ResolveCard { get; private set; } = null!;
    public Cmd SkipBout { get; private set; } = null!;
    public Cmd LeaveArena { get; private set; } = null!;

    private void RaiseCard()
    {
        foreach (var n in new[] { nameof(Current), nameof(CardFinished), nameof(PlayLabel), nameof(CardOnScreen),
                                  nameof(CardTitle), nameof(CardTonightNote) })
            Raise(n);
        Raise(nameof(CanResolveCard));
        ResolveCard?.Refresh();
    }

    /// <summary>Build the night as a POSTER, and box it from the foot upward.
    ///
    /// These are two different orders and the card used to show only one of them. It was built reversed —
    /// running order — so the screen listed the opener at the top and the main event last, underneath it.
    /// That is not what a card looks like anywhere: the biggest fight is at the TOP of the bill and goes on
    /// LAST. Reading down the list you should see the main event first and the four-rounder at the bottom,
    /// and the fight being boxed should climb from the bottom of that list toward the top.
    ///
    /// So the list is the bill as printed, and the running order is expressed by which line is current —
    /// starting at the last and walking up. Bouts the sim did not stage (a man hurt, a man retired) are left
    /// off rather than shown as fights that never happened.</summary>
    private void BuildCardNight()
    {
        CardNight.Clear();
        foreach (var l in Game?.LastNightsCard ?? Array.Empty<BillLine>())
        {
            if (!l.Fought && !l.IsPlayer) continue;
            CardNight.Add(new CardBout
            {
                Fight = l.Fight, Distance = l.Distance, What = l.What,
                Verdict = l.Verdict, Note = l.Note, IsPlayer = l.IsPlayer,
                Billing = BillingLine(l), Slot = l.Slot,
                AName = l.AName, ACountry = l.ACountry, ARecord = l.ARecord,
                BName = l.BName, BCountry = l.BCountry, BRecord = l.BRecord,
            });
        }
        if (CardNight.Count > 0) CardNight[^1].State = "current";   // the opener boxes first
        RaiseCard();
    }

    /// <summary>"Middleweight · WBA title · champion vs #4" — as much of that as is true.</summary>
    private static string BillingLine(BillLine l)
    {
        var bits = new List<string> { l.Div.DisplayName() };
        if (l.What.Length > 0) bits.Add(l.What);
        // Say WHOSE the standing is when only one man has one. A bare "#5" on a line that sits under the
        // left-hand name reads as the left-hand man's — so a #5 opponent made the player look ranked, on his
        // own bill, in the one place the two men are being compared. With both ranked the positional form is
        // unambiguous, because it lines up with the two corners above it.
        static string Who(string full)
        {
            int sp = full.LastIndexOf(' ');
            return sp > 0 ? full[(sp + 1)..] : full;
        }
        string ranks = (l.ARank, l.BRank) switch
        {
            ("", "") => "",
            (var a, "") => $"{Who(l.AName)} {a}",
            ("", var b) => $"{Who(l.BName)} {b}",
            var (a, b) => $"{a} vs {b}",
        };
        if (ranks.Length > 0) bits.Add(ranks);
        return string.Join("  ·  ", bits);
    }

    /// <summary>Up the bill, not down it. The list is the poster, so the next bout on is the LAST one still
    /// waiting — the night climbs toward the main event.</summary>
    private void AdvanceCard()
    {
        var cur = Current;
        if (cur is not null) cur.State = "done";
        var next = CardNight.LastOrDefault(b => b.IsPending);
        if (next is not null) next.State = "current";
        RaiseCard();
    }

    private async void DoPlayBout()
    {
        if (Current is not { } b) return;
        if (b.IsPlayer) { StartPlayback(); return; }   // AdvanceCard happens when the call ends
        await Task.Delay((int)(700 / Math.Max(0.25, Speed)));
        AdvanceCard();
    }

    /// <summary>Settle every supporting bout still to come, and stop at the player's own.
    ///
    /// A club show is four or five fights and pressing through each of them one at a time is not a decision,
    /// it is a queue. His OWN fight is never auto-resolved: the card runs on until it reaches him and then
    /// hands back, because that is the one night he came for.</summary>
    private void DoResolveCard()
    {
        // Guarded rather than looped on Current alone: if anything ever failed to advance, an unguarded
        // while would spin the UI thread for ever with no way out.
        for (int guard = 0; guard < 64; guard++)
        {
            if (Current is not { } b || b.IsPlayer) break;
            AdvanceCard();
        }
        RaiseCard();
    }

    /// <summary>True while there is somebody else's fight still to settle — the only time the button means
    /// anything. On the player's own bout it goes, because "resolve the rest" must never resolve his.</summary>
    public bool CanResolveCard => IsCardRunning && Current is { IsPlayer: false }
                                  && CardNight.Count(b => b.IsPending && !b.IsPlayer) > 0;

    private void DoSkipBout()
    {
        if (Current is not { } b) return;
        if (b.IsPlayer)
        {
            // Straight to the verdict, without the round-by-round.
            StartPlayback();
            EndPlayback();   // reveals the whole call at once, exactly as the skip button in the ring does
            return;
        }
        AdvanceCard();
    }

    private void DoLeaveArena()
    {
        IsCardRunning = false;
        CardNight.Clear();
        RaiseCard();
        _awardsWait = false;
        CheckForAwards();   // the night may have carried the calendar into a new year
        DoNavigate(Page.Dashboard);   // out of the arena and back to the career, not to an empty camp
    }

    // ---- going at your own pace ----
    //
    // The build-up ran itself: a week every 550ms, a bout every 1250ms, and you sat and watched it go past.
    // Putting the weeks on screen was meant to let them be READ. So the default now is that it waits for you.


    /// <summary>Whether there is any wait left to carry on with — held between weeks OR stopped for something.
    ///
    /// Continue used to appear only while the run was actively holding, so stopping for a fight in your own
    /// division made it vanish and left "On to fight night" as the only way onward: a different control, in a
    /// different place, labelled as though it skipped to the end when it actually resumed week by week. One
    /// control, one place, whatever the reason for the pause.</summary>
    // There was a CanCarryOn here — "there is a run-up to carry on with" — defined as `=> InCamp` and nothing
    // else, and the two Continue buttons bound to it. It is the reason the camp action row kept coming up
    // empty, on and off, for weeks.
    //
    // A binding listens for a property NAME. SetCommitted raised InCamp and not CanCarryOn, so the row's own
    // visibility (which keys off ShowCampActions, and was raised) turned on while the buttons inside it never
    // heard that their gate had changed and stayed collapsed — a row that laid out, was measured, and drew
    // nothing. With no content it has no height, so most of the time it looked like the row was missing
    // rather than empty. That is why the magenta band "proved" the row existed and got us no further.
    //
    // Intermittent because Take() calls SetCommitted(true) and then RunToFightNight, which raises the full
    // list — but RunToFightNight returns immediately if a run is already going, and on that path nothing ever
    // raised it. Same click, same screen, different answer depending on state you cannot see.
    //
    // One condition should have one name. The buttons bind to InCamp directly now, and there is no second
    // name left to forget. See RaiseCampGates for the other half of the fix.

    /// <summary>The date has come. Deliberately NOT dependent on being in camp: InCamp requires days still to
    /// run, so at zero it goes false and takes Continue with it — and the camp page had nothing left, because
    /// accepting and refusing moved to Home when the pages were split. Counting down to a fight and then being
    /// shown no way to have it is the second time that has happened, from a different direction.</summary>
    public bool ReadyToFight =>
        _committed && Game?.Offer is not null && Game?.Player.Retired == false && Game?.DaysToFight == 0;

    /// <summary>Whether this page has anything to offer at all — and it must always have something, because it
    /// shows a countdown either way.
    ///
    /// The page could sit there reading "4 weeks to fight night" with no button on it, because accepting a
    /// fight had moved to Home when the pages were split and this page only ever offered the things that come
    /// AFTER accepting. A screen that counts down to a fight has to let you have the fight.</summary>
    public bool ShowCampActions => InCamp || ReadyToFight || FightIsStillAnOffer;

    /// <summary>Every gate the camp action row keys off, raised together.
    ///
    /// These were five hand-written lists of property names at five call sites, and they did not agree — one
    /// was missing a name, which is the whole story of the disappearing buttons. A row whose visibility and
    /// whose contents are decided by different properties has to raise ALL of them or it renders a state that
    /// never existed: visible and empty, or hidden with a live fight behind it.
    ///
    /// So there is one list. Callers that need more (the countdown, the waiting flag) raise those on top; what
    /// they cannot do any more is raise some of these and not the others.</summary>
    private void RaiseCampGates()
    {
        foreach (var n in new[] { nameof(InCamp), nameof(ReadyToFight), nameof(FightIsStillAnOffer),
                                  nameof(NothingAgreedYet), nameof(HasChosenFight), nameof(ShowCampActions),
                                  nameof(ShowResultBanner), nameof(CanWait) }) Raise(n);
        // The slate is its own object, so this one's Raise cannot reach it — and whether it shows hangs off
        // the same flags.
        OfferSlate.RaiseVisible();
    }

    /// <summary>Whether the fight has been taken and the run-up is under way.
    ///
    /// The page went on offering "go to fight night" and "turn it down" all the way through the build-up, as
    /// though the fight were still a decision — while the countdown to it ran above. It is not a decision any
    /// more: you took it. In camp, the page is the camp, and the poster below it is what you are heading
    /// towards rather than something to accept.
    ///
    /// Deliberately not saved. Reopening a career mid-camp puts the choice back, which is the kinder reading of
    /// a session you walked away from.</summary>
    private bool _committed;

    // THREE STATES, and every one of them derived from the same two facts: have you taken the fight, and has
    // the date come. They are mutually exclusive and they cover everything, which is the point — four dead
    // ends have shipped from rules that each read ONE fact and were therefore right about one state and wrong
    // about a neighbouring one.
    //
    //                         taken?   date come?
    //   FightIsStillAnOffer     no         -          accept it, or turn it down
    //   InCamp                  yes        no         continue, or run the weeks out
    //   ReadyToFight            yes        yes        walk out

    public bool InCamp => _committed && Game?.DaysToFight > 0 && Game?.Player.Retired == false;

    /// <summary>Not taken yet. Deliberately keyed on having COMMITTED rather than on being in camp: in camp
    /// also requires days still to run, so on the night itself it goes false and the fight you had already
    /// accepted was offered back to you with a "turn it down" beside it.</summary>
    public bool FightIsStillAnOffer =>
        Game?.Offer is not null && Game?.Player.Retired == false && !_committed;

    /// <summary>Whether he has picked a fight off this slate yet.
    ///
    /// The world has always put slate[0] on the table so that the sim has something to work with — TakeOffer,
    /// WaitAWeek, the card and the countdown all rest on an offer existing, and eighty tests say so. But a
    /// fight the MATCHMAKER picked is not a fight the PLAYER agreed to, and the dashboard was announcing
    /// "your next fight" against a man he had never chosen, with the alternatives on another page.
    ///
    /// So the third state lives here, where it belongs: the world keeps a working offer, and the screen does
    /// not call it his until he says so.</summary>
    /// The offer he picked, held BY IDENTITY rather than as a bool. Every new slate builds new FightOffer
    /// objects, so the moment the matchmaker draws again this stops matching and the choice is open once more —
    /// without every path that redraws a slate having to remember to clear a flag. Forgetting one of those is
    /// how the screen would come to show a fight nobody chose again.
    private FightOffer? _pickedOffer;
    private bool _picked => _pickedOffer is not null && ReferenceEquals(_pickedOffer, Game?.Offer);

    /// <summary>Picking a fight IS agreeing to it.
    ///
    /// Choosing one off the slate used to only SELECT it, and the fight then had to be accepted a second time
    /// on a different panel. Two presses for one decision, and the second one asked a question the first had
    /// already answered — the slate exists so that choosing between nights is the decision, and there is
    /// nothing left to confirm once it is made. Turning one down is still on the table until it is picked.</summary>
    private void OnPicked()
    {
        _pickedOffer = Game?.Offer;
        RefreshAll();
        Take();
    }

    /// <summary>There are fights on the table and he has picked none of them.</summary>
    public bool NothingAgreedYet =>
        Game is not null && !_picked && !Game.Player.Retired && Game.Slate.Count > 0;

    /// <summary>He has a fight, because he chose it. What the dashboard's "your next fight" card waits for.</summary>
    public bool HasChosenFight => Game?.Offer is not null && _picked && Game?.Player.Retired == false;

    private void SetCommitted(bool v)
    {
        if (_committed == v) return;
        _committed = v;
        RaiseCampGates();
    }

    public Cmd RunTheRest { get; }

    // There was a Gate here, and a "Wait for me between weeks" setting behind it, holding the run after every
    // week of the build-up. The run-up happens in one pass now (see RunToFightNight), so there is nothing left
    // to hold — and a setting whose checkbox does nothing is worse than no setting.

    // ---- the two build-up settings, bound straight to the checkboxes that set them ----

    /// <summary>Whether taking a fight runs the weeks first. Written through to disk on every change: there is
    /// no apply button, and a preference that does not survive the session is not a preference.</summary>
    public bool FightWeek
    {
        get => _prefs.FightWeek;
        set
        {
            if (_prefs.FightWeek == value) return;
            _prefs.FightWeek = value; _prefs.Save(); Raise();
            Raise(nameof(TakeFightLabel)); Raise(nameof(TakeFightHint));
        }
    }

    /// <summary>What the button actually does, said on the button.
    ///
    /// It read "Take the fight" whatever it was about to do, and once taking a fight started four months of
    /// build-up instead of a bout that was simply untrue — you pressed a button promising a fight and got a
    /// calendar. Three different things can happen depending on the setting and how far off the date is, so
    /// the label says which.</summary>
    public string TakeFightLabel =>
        Game?.Offer is null ? "Take the fight"
        : Game.DaysToFight == 0 ? "Fight now"
        : "Accept fight";

    public string TakeFightHint =>
        Game?.Offer is null ? ""
        : Game.DaysToFight == 0 ? "The date has come — first bell"
        : FightWeek ? $"Agree to it, and run the {Game.DaysToFight / 7} weeks between now and the first bell"
        : "Agree to it, and start the fight straight away";

    /// <summary>The fights on the table — its own page, its own view-model. See OfferSlateViewModel.</summary>
    public OfferSlateViewModel OfferSlate { get; }

    public bool LiveUndercard
    {
        get => _prefs.LiveUndercard;
        set { if (_prefs.LiveUndercard == value) return; _prefs.LiveUndercard = value; _prefs.Save(); Raise(); }
    }

    /// <summary>The sound toggle, as a checkbox rather than the button on the playback bar, so all three
    /// presentation choices sit in one place.</summary>
    public bool SoundEnabled
    {
        get => SoundOn;
        set { SoundOn = value; Raise(); }
    }

    // ---- the wait ----
    //
    // Career mode used to jump from "take the fight" straight to the opening bell, running three months of
    // the sport silently on the way. The results were all there afterwards, in a list. Nothing ever happened
    // while you waited, because you never waited.

    /// <summary>The weeks between now and fight night, as they land.</summary>
    public ObservableCollection<CampRow> Camp { get; } = new();

    /// <summary>Every week's news, kept whole so the filter can be turned on and off without losing what has
    /// already gone past. Camp is what the page shows; this is what it is drawn from.</summary>
    private readonly List<CampRow> _campAll = new();

    /// <summary>How much of the sport to report while he waits.
    ///
    /// Twelve divisions over four months is a wall of text in which the one fight that concerns him reads
    /// exactly like the other fifty — but "only my division" was too blunt the other way, hiding the belts
    /// moving elsewhere that decide who he ends up fighting.</summary>
    public enum CampDetail { Titles, Normal, Detailed }

    private CampDetail _campDetail;
    public CampDetail Detail
    {
        get => _campDetail;
        set
        {
            if (_campDetail == value) return;
            _campDetail = value;
            _prefs.CampDetail = value.ToString(); _prefs.Save();
            foreach (var n in new[] { nameof(Detail), nameof(SelectedDetail), nameof(CampCountLabel) }) Raise(n);
            RedrawCamp();
        }
    }

    private bool _campMineOnly;
    /// <summary>His own weight only, on top of the level. Two separate questions — how much of the sport, and
    /// whose part of it — so two separate controls rather than one that tries to be both.</summary>
    public bool CampMineOnly
    {
        get => _campMineOnly;
        set
        {
            if (_campMineOnly == value) return;
            _campMineOnly = value;
            _prefs.CampMineOnly = value; _prefs.Save();
            Raise(); Raise(nameof(CampCountLabel));
            RedrawCamp();
        }
    }

    /// <summary>The three levels as a list, so the panel spends one control on them rather than three.</summary>
    public IReadOnlyList<DetailChoice> DetailChoices { get; } = new[]
    {
        new DetailChoice(CampDetail.Titles,   "Titles only",  "Belts changing hands, and nothing else"),
        new DetailChoice(CampDetail.Normal,   "Normal",       "Belts, upsets anywhere, and your own division"),
        new DetailChoice(CampDetail.Detailed, "Detailed",     "The whole sport, including the prospects coming through"),
    };

    public DetailChoice SelectedDetail
    {
        get => DetailChoices.First(c => c.Level == _campDetail);
        set { if (value is not null) Detail = value.Level; Raise(); }
    }

    /// <summary>Titles: belts only. Normal: adds his own division and any upset. Detailed: the lot, which is
    /// where the prospects coming through and the movement below the champion live.</summary>
    private bool Shows(CampRow r)
    {
        if (_campMineOnly && !r.Mine && !r.PlayerBout) return false;
        return _campDetail switch
        {
            CampDetail.Titles => r.IsTitle,
            CampDetail.Normal => r.IsTitle || r.IsUpset || r.Mine || r.PlayerBout,
            _ => true,
        };
    }

    public string CampCountLabel =>
        _campAll.Count == 0 ? ""
        : Camp.Count == _campAll.Count ? $"{_campAll.Count} in the sport"
        : $"{Camp.Count} of {_campAll.Count}";

    private void RedrawCamp()
    {
        Camp.Clear();
        foreach (var r in _campAll.Where(Shows)) Camp.Add(r);
        Raise(nameof(CampCountLabel));
    }

    /// <summary>Turn a logged event into a line of the build-up feed, working out how much it matters here
    /// rather than in the view. A title changing hands, an upset, and anything in the player's own division
    /// are the three things worth raising your eyes for.</summary>
    /// <summary>A feed line. The one bout the player is being INVITED to watch has its result withheld —
    /// offering "watch it" above a line that already says who won makes the invitation pointless, and it is
    /// the only line on the page whose outcome he has asked not to be told yet.</summary>
    private CampRow ToCamp(CareerEvent e) => new(
        e.On.ToString("d MMM yyyy"),
        e.Text,
        mine: Game is not null && e.Div == Game.Player.WeightClass,
        isTitle: e.Kind == "title",
        isUpset: e.Kind == "upset" || e.Text.StartsWith("UPSET", StringComparison.Ordinal)
                 || e.Text.Contains("major upset", StringComparison.OrdinalIgnoreCase),
        playerBout: e.PlayerBout,
        bout: e.Bout,
        detail: e.Detail ?? "",
        withheld: Spoils(e) ? $"{Unordered(e.Bout!)} — in your division tonight." : null);

    private bool _waiting;
    public bool IsWaiting => _waiting;

    /// <summary>Whether to show the camp panel at all.
    ///
    /// It used to appear only once a week had produced an event, which meant a quiet first week left the
    /// player holding a Continue button with no countdown, no date and nothing on screen to say what they
    /// were continuing. The panel is up for the whole wait now, empty or not.</summary>
    public bool ShowCamp => _waiting || Camp.Count > 0;
    /// <summary>There is only something to wait for while fight night is still ahead. Once the calendar has
    /// reached it the offer to wait has to go, or it sits there inviting you to wait for today.</summary>
    public bool CanWait => !_waiting && Game?.DaysToFight > 0;
    public string CampCountdown => Game?.DaysToFight is int d && d > 0
        ? d >= 14 ? $"{d / 7} weeks to fight night" : d == 1 ? "Tomorrow" : $"{d} days to fight night"
        : "Fight night";
    public string CampDate => Game?.Date.ToString("d MMMM yyyy") ?? "";

    /// <summary>Whether this event would give away the fight currently on offer to watch.</summary>
    private bool Spoils(CareerEvent e) =>
        _theOne is BoutRef one && e.Bout is BoutRef b
        && b.Winner == one.Winner && b.Loser == one.Loser && b.Date == one.Date;

    /// <summary>Both men, in an order that does not itself say who won. A BoutRef is (Winner, Loser), so
    /// printing it as it stands hands over the result in the act of hiding it.</summary>
    private static string Unordered(BoutRef b) =>
        string.CompareOrdinal(b.Winner, b.Loser) <= 0 ? $"{b.Winner} vs {b.Loser}" : $"{b.Loser} vs {b.Winner}";

    private BoutRef? _theOne;
    /// <summary>A fight in his own division that came up while he waited, and is worth stopping for.</summary>
    public string TheOne { get; private set; } = "";
    public bool HasTheOne => _theOne is not null;

    private async void DoWaitForFight() => await RunToFightNight();

    /// <summary>Run the weeks toward fight night, in front of the player.
    ///
    /// Awaitable, because taking a fight now runs this first and has to know when it has finished — and
    /// whether it finished because the calendar arrived or because the player stopped to watch something.
    /// The caller checks DaysToFight to tell the difference.</summary>
    /// <param name="resuming">Carrying on after stopping for a fight worth watching. The weeks already run
    /// stay on the page: clearing them would throw away the build-up so far every time the sport did
    /// something interesting, which over four months is most times.</param>
    private async Task RunToFightNight(bool resuming = false)
    {
        if (Game is null || _waiting) return;
        _waiting = true;
        if (!resuming) { Camp.Clear(); _campAll.Clear(); }
        SettleTheOne();
        foreach (var n in new[] { nameof(IsWaiting), nameof(ShowCamp), nameof(HasTheOne), nameof(TheOne) }) Raise(n);
        RaiseCampGates();

        // THE WHOLE RUN-UP, IN ONE PASS.
        //
        // This used to be a week at a time on the UI thread: a background call, a busy banner, a dispatcher
        // round trip and a 550ms pause, times however many weeks were left — so agreeing to a fight four
        // months out meant a dozen visible stalls and the results of the sport arriving one line at a time.
        // On top of that the run stopped to ask whether you wanted to watch anything interesting in your own
        // division, and stopped again for the turn of the year. What should have been "get me to fight night"
        // was half a minute of pressing Continue.
        //
        // The sport is simulated once, in the background, and everything it did arrives together. It is the
        // same weeks and the same results; the only thing that has gone is the waiting.
        var all = new List<CareerEvent>();
        BusyMessage = "Running the weeks to fight night…";
        Busy = true;
        System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        try
        {
            await Task.Run(() =>
            {
                IReadOnlyList<CareerEvent>? week;
                while ((week = Game?.WaitAWeek()) is not null) all.AddRange(week);
            });
        }
        finally
        {
            System.Windows.Input.Mouse.OverrideCursor = null;
            Busy = false;
        }

        // Newest first, the way the feed reads.
        foreach (var e in all)
        {
            var row = ToCamp(e);
            _campAll.Insert(0, row);
            if (Shows(row)) Camp.Insert(0, row);
        }
        // Nothing is being held back to be watched later, so nothing is hidden.
        SettleTheOne();
        Raise(nameof(CampCountLabel));

        _waiting = false;
        foreach (var n in new[] { nameof(IsWaiting), nameof(ShowCamp), nameof(CampCountdown), nameof(CampDate) }) Raise(n);
        RaiseCampGates();
        RefreshAll();
        CheckForAwards();
    }

    /// <summary>Let the line that was holding its result back say what happened. Called however the offer is
    /// settled — watched or waved away — because a result withheld for a fight nobody is going to watch is
    /// just a hole in the feed.</summary>
    private void SettleTheOne()
    {
        _theOne = null; TheOne = "";
        foreach (var r in _campAll) r.Reveal();
        Raise(nameof(HasTheOne)); Raise(nameof(TheOne));
    }

    /// <summary>No thanks. The result goes into the feed and the build-up carries on.</summary>
    private void DoSkipTheOne() => SettleTheOne();

    private void DoWatchTheOne()
    {
        if (_theOne is not BoutRef b) return;
        SettleTheOne();
        if (Game?.FindBout(b) is not (Boxer owner, Boxer foe, BoutLine line))
        {
            WatchUnavailable = "That fight is no longer on the record.";
            return;
        }
        _ = WatchAsync(owner, foe, line, notable: line.Note is string n && n.Contains("title", StringComparison.OrdinalIgnoreCase));
    }

    // ---- the year's honours ----
    //
    // These were computed the moment the calendar turned and filed on a page the player had to go and look
    // at. A year of the sport ending is an occasion, and the sim knew who had won what and said nothing.

    /// <summary>The year just gone, when it has honours the player has not seen.</summary>
    public ObservableCollection<AwardPlace> YearAwards { get; } = new();
    public string YearAwardsTitle { get; private set; } = "";
    public bool ShowYearAwards { get; private set; }

    public Cmd CloseYearAwards { get; }

    // ---- the news drawer ----
    //
    // The sport's news was a panel at the foot of the dashboard, below the fold on a 1366-wide screen, and
    // reachable nowhere else without leaving the page you were on. It is a drawer now: it slides in over
    // whatever you are looking at, from any page, and carries the full feed rather than six headlines.

    public Cmd ToggleNews { get; }
    public Cmd CloseNewsDrawer { get; }

    private bool _newsOpen;
    public bool NewsOpen
    {
        get => _newsOpen;
        private set { _newsOpen = value; Raise(); }
    }

    /// <summary>Pull the year's honours out of the world, if it has raised any. Called after anything that
    /// can move the calendar.</summary>
    private void CheckForAwards()
    {
        if (_awardsWait || IsPlayingBack) return;   // never over the top of a fight
        if (Game?.UnseenAwards is not AwardsYear a) return;
        YearAwards.Clear();
        void Add(string honour, IReadOnlyList<AwardWinner> winners)
        {
            // The winner only. A year-end announcement names who won, not who came third.
            foreach (var w in winners.Take(1))
                YearAwards.Add(new AwardPlace(honour, w.Name, w.Div.DisplayName(), w.Detail, true,
                                              honour, a.Year, w.Commentary, w.Bout));
        }
        Add("Fighter of the Year", a.FighterOfYear);
        Add("Fight of the Year", a.FightOfYear);
        Add("Knockout of the Year", a.KnockoutOfYear);
        Add("Upset of the Year", a.UpsetOfYear);

        if (YearAwards.Count == 0) { Game.AwardsSeen(); return; }
        YearAwardsTitle = $"THE {a.Year} AWARDS";
        ShowYearAwards = true;
        Game.AwardsSeen();
        foreach (var n in new[] { nameof(YearAwardsTitle), nameof(ShowYearAwards) }) Raise(n);
    }

    // ---- the card ----
    //
    // A bill is something you read before the night, not a list you are handed afterwards. The same
    // collection serves both: announced beforehand with the matchups, and carrying the results once they
    // have been fought.
    public ObservableCollection<BillLine> Bill { get; } = new();
    public bool HasBill => Bill.Count > 0;
    public string BillHeader => Game?.BillHeader ?? "";
    public string CardNote => Game?.CardNote ?? "";

    public ObservableCollection<UndercardBout> Undercard { get; } = new();
    public bool HasUndercard => Undercard.Count > 0;

    private void BuildUndercard()
    {
        Undercard.Clear();
        foreach (var u in Game?.Undercard ?? Array.Empty<UndercardBout>()) Undercard.Add(u);
        Bill.Clear();
        foreach (var l in Game?.Bill ?? Array.Empty<BillLine>()) Bill.Add(l);
        foreach (var n in new[] { nameof(HasUndercard), nameof(HasBill), nameof(BillHeader), nameof(CardNote) })
            Raise(n);
    }

    // Both of these end whatever camp was under way: the fight it was for is no longer the fight.
    private async void Decline() { SetCommitted(false); await BusyAsync("Waiting for the next offer…", () => _svc.Decline()); RefreshAll(); }
    private async void DoMoveUp() { SetCommitted(false); await BusyAsync("Moving up…", () => _svc.MoveUp()); RefreshAll(); }
    private void Abandon() { EndPlayback(); SetCommitted(false); SelectedCard = null; SelectedFight = null; SelectedAward = null; _svc.Abandon(); RefreshAll(); }

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
        private set { _isPlayingBack = value; Raise(); Raise(nameof(ShowResultBanner)); Raise(nameof(CardOnScreen)); }
    }

    /// <summary>The verdict banner stays hidden behind the fight night — showing it there would give the
    /// result away before a punch had been thrown.</summary>
    /// <summary>The verdict of the fight just had — until the next one is accepted.
    ///
    /// It used to sit there for as long as the result did, so a man six weeks into camp for October read a
    /// green "WIN — beat Duane Reeves by TKO" about a fight in July, above a poster for a different opponent,
    /// with the same result already listed in his record underneath. It is the last thing that happened only
    /// until something else is happening.</summary>
    public bool ShowResultBanner => HasResult && !IsPlayingBack && !IsCardRunning && !_committed;

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

    // What each man has left in the tank, draining as the fight goes on. This is the engine's own fatigue -
    // the thing that actually fades a fighter - not a decorative second number.
    private double _liveMyGas = 1, _liveHisGas = 1;
    public double LiveMyGas => _liveMyGas;
    public double LiveHisGas => _liveHisGas;
    public bool MyManGassed => _liveMyGas <= 0.45;
    public bool HisManGassed => _liveHisGas <= 0.45;
    public bool MyManHurt => _liveMyHurt >= 2;
    public bool HisManHurt => _liveHisHurt >= 2;

    /// <summary>Place one line of the call: a ROUND heading folds the previous round away and opens a new one,
    /// the end-of-round card becomes that round's summary, everything else is action inside it.</summary>
    private void Feed(CallLine line)
    {
        if (line.IsRound)
        {
            // Rounds used to fold away as the next one began, so only the round being fought was ever on
            // screen and everything said before it collapsed to a one-line summary. The call itself is not
            // sparse — it averages four or more lines a round, and there is a test that says so — it was the
            // display throwing them away. A fight reads as a log now: it grows, and it scrolls.
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

    /// <summary>The three cards, held back until the fight is over and then put up together.
    ///
    /// The call reads them out one at a time, which is the drama; this is the other half of it — the cards
    /// side by side afterwards, so you can see that two judges had it close and one had it wide, or that a
    /// man was a single point from a draw. A line of commentary scrolls past and is gone; a card is a thing
    /// you look at.</summary>
    public ObservableCollection<JudgeCard> PlaybackJudges { get; } = new();

    /// <summary>Only a fight that went the distance has cards to show.</summary>
    public bool HasPlaybackCards => PlaybackJudges.Count > 0;

    private void BuildPlaybackCards(FightResult res, Boxer me, Boxer them)
    {
        PlaybackJudges.Clear();
        if (res.Scorecards.Count > 0)
        {
            bool iAmA = res.A.Id == me.Id;
            string mine = Surname(me.Name) ?? "him", theirs = Surname(them.Name) ?? "the other man";
            for (int i = 0; i < res.Scorecards.Count; i++)
            {
                var (a, b) = res.Scorecards[i];
                int my = iAmA ? a : b, his = iAmA ? b : a;
                PlaybackJudges.Add(new JudgeCard($"Judge {i + 1}", $"{my}–{his}",
                                                 my == his ? "level" : my > his ? $"for {mine}" : $"for {theirs}",
                                                 my > his, my == his));
            }
        }
        Raise(nameof(HasPlaybackCards));
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
            _prefs.SoundOn = value; _prefs.Save();
            if (value && IsPlayingBack && _svc.LastResult is { } r) Sfx.StartBed(OccasionOf(r));
            else if (!value) Sfx.StopBed();   // "Sound off" has to actually silence the room
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
        _liveMyGas = line.MyGas;
        _liveHisGas = line.HisGas;
        foreach (var n in new[] { nameof(LiveRound), nameof(LiveClock), nameof(LiveMine), nameof(LiveHis),
                                  nameof(LiveMineShare), nameof(LiveHisShare), nameof(MyManHurt), nameof(HisManHurt),
                                  nameof(LiveMyGas), nameof(LiveHisGas), nameof(MyManGassed), nameof(HisManGassed) })
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
            _prefs.Speed = value; _prefs.Save();
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
        Play(res, Game.Player,
             Game.Offer is { } o && o.TitleFight ? $"{o.Belt} TITLE" : $"{res.ScheduledRounds} rounds",
             ResultHeadline);
    }

    /// <summary>Play any fight, from any man's corner. The player's own bout is just the case where the point
    /// of view is him and the result came straight from the engine; a fight out of the record books arrives
    /// here having been rebuilt, and from this point on nothing downstream can tell the difference.</summary>
    /// <summary>A fighter's record as it stood BEFORE a given date, rebuilt from his ledger. The live Record
    /// object has already been updated by the time anything is watched - the result is applied, then the fight
    /// is played back - so showing it billed a man at 7-0 while you watched the fight that made him 7-0. It is
    /// the bout's own date that is excluded, not "today", so a replay from fifteen years ago is billed with the
    /// record he actually carried into that ring.</summary>
    private static string RecordAsOf(Boxer b, DateOnly bout)
    {
        // Counted BACKWARDS from where he stands now, not forwards from an empty slate. A roster fighter is
        // seeded with the record he already had when the career began and has no ledger entries behind it, so
        // adding up his history from zero billed a 21-16 journeyman at 0-0-0. Only the bouts from this night
        // onward are taken off.
        var r = b.Record;
        int w = r.Wins, l = r.Losses, d = r.Draws, ko = r.KnockoutWins, tko = r.TechnicalKnockoutWins;
        foreach (var h in b.History)
        {
            if (h.Date < bout) continue;
            // The two columns come off separately, and a cut is a technical knockout — the same split the
            // record itself keeps, or winding a man back would move his stoppages into the wrong column.
            if (h.Result == 'W')
            {
                w--;
                if (h.Method == "KO") ko--;
                else if (h.Method is "TKO" or "cut") tko--;
            }
            else if (h.Result == 'L') l--;
            else d--;
        }
        w = Math.Max(0, w); l = Math.Max(0, l); d = Math.Max(0, d);
        ko = Math.Max(0, ko); tko = Math.Max(0, tko);
        string wld = $"{w}-{l}-{d}";
        if (ko == 0 && tko == 0) return wld;
        if (tko == 0) return $"{wld} ({ko} KO)";
        if (ko == 0) return $"{wld} ({tko} TKO)";
        return $"{wld} ({ko} KO / {tko} TKO)";
    }

    private void Play(FightResult res, Boxer pov, string billLine, string verdict, DateOnly? bout = null)
    {
        var me = pov;
        bool iAmA = res.A.Id == me.Id;
        var them = iAmA ? res.B : res.A;

        FightNightHome = me.Name;
        FightNightAway = them.Name;
        // Billed as they came in, not as they left.
        var on = bout ?? (me.History.Count > 0 ? me.History[^1].Date : DateOnly.MaxValue);
        FightNightHomeRecord = RecordAsOf(me, on);
        FightNightAwayRecord = RecordAsOf(them, on);
        FightNightBillLine = billLine;
        PlaybackVerdict = verdict;
        BuildPlaybackCards(res, me, them);
        foreach (var n in new[] { nameof(FightNightHome), nameof(FightNightAway), nameof(FightNightHomeRecord),
                                  nameof(FightNightAwayRecord), nameof(FightNightBillLine), nameof(PlaybackVerdict) })
            Raise(n);

        Rounds.Clear();
        _live = null;
        _call = FightCall.Build(res, me).ToList();
        _fed = 0;
        _liveRound = 0; _liveMine = 0; _liveHis = 0; _liveMyHurt = 0; _liveHisHurt = 0;
        _liveMyGas = 1; _liveHisGas = 1;
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

    /// <summary>Watch a fight out of the record books. Bouts the world resolved statistically have a result and
    /// a card but no punches, so there is nothing to call — the fight is rebuilt by running the real engine
    /// until it produces a night ending the way the record says it ended, then played like any other.
    ///
    /// A fight that won an award is rebuilt asking for the best of several matching nights rather than the
    /// first, so opening Fight of the Year gives you the knockdowns and the late finish it was named for.</summary>
    private void OnWatchFight(object? param)
    {
        if (Game is null) return;
        var row = param as LedgerRow ?? (param is FightDetail fd ? fd.Source : null);
        if (row?.Bout is not BoutLine line || row.OwnerName is not string ownerName) return;

        var owner = Game.FindByName(ownerName);
        var foe = Game.FindByName(line.Opponent);
        if (owner is null || foe is null) { WatchUnavailable = "One of these fighters has left the sport."; return; }

        _ = WatchAsync(owner, foe, line, row.Notable);
    }

    private async Task WatchAsync(Boxer owner, Boxer foe, BoutLine line, bool notable)
    {
        // The call is WRITTEN from the record rather than searched for in a simulation. The record already says
        // what happened each round - punches landed, who went down, how it was scored - so there is nothing to
        // hunt for and nothing that can fail to be found. Every fight gets a call, immediately, and it agrees
        // with the card exactly because the card is what it was generated from.
        FightResult? res = null;
        await BusyAsync("Going back over the tape…", () => res = FightScript.Compose(owner, foe, line, notable));
        if (res is null) { WatchUnavailable = "That fight is no longer on the record."; return; }
        WatchUnavailable = "";
        string verdict = res.Winner is null
            ? $"DRAW — {res.A.Name} and {res.B.Name}"
            : $"{res.Winner.Name} beat {res.Loser!.Name} by {res.Method}" +
              (res.Method is "KO" or "TKO" ? $", round {res.EndRound}" : "");
        Play(res, owner, line.Note is string n ? n.ToUpperInvariant() : $"{res.ScheduledRounds} rounds", verdict, line.Date);
    }

    private string _watchUnavailable = "";
    public string WatchUnavailable
    {
        get => _watchUnavailable;
        set { _watchUnavailable = value; Raise(); Raise(nameof(HasWatchProblem)); }
    }
    public bool HasWatchProblem => !string.IsNullOrEmpty(WatchUnavailable);

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
            CallKind.Card => 900,      // a card is read out slowly; the pause is the point
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
        if (IsCardRunning)
        {
            // Back out to the card: there may be bouts above his still to come, and the honours belong to the
            // end of the night rather than to the moment he stops fighting.
            AdvanceCard();
            Sfx.StopBed();
            return;
        }
        _awardsWait = false;
        CheckForAwards();   // the fight itself may have carried the calendar into a new year
        // The crowd goes home with you. Without this the bed played on under the rankings, the news and every
        // other screen for the rest of the session, and starting a second fight layered another one over it.
        Sfx.StopBed();
    }

    // ---- fighter drill-down ----

    private FighterCard? _selectedCard;
    public FighterCard? SelectedCard { get => _selectedCard; private set { _selectedCard = value; Raise(); Raise(nameof(HasCard)); } }
    public bool HasCard => _selectedCard is not null;

    private async void DoStartUniverse()
    {
        var settings = new UniverseSettings
        {
            StartYear = UniStartYear,
            Divisions = UniDivisions.Where(d => d.On).Select(d => d.Division).ToList(),
            EntrantsPerYear = UniEntrants,
            CareerLength = UniCareerLength,
            Activity = UniActivity,
            WarmupYears = UniWarmup,
            UseRealFighters = UniRealFighters
        };
        // Warming a world through years of history is the slow part, so it runs off the UI thread.
        await BusyAsync("Building a world…", () => _svc.StartUniverse(settings));
        UniversePage.Clear();
        _selectedNav = UniverseNav[0];
        RefreshAll();
        Raise(nameof(SelectedNav)); Raise(nameof(CurrentPage));
        DoPlayWeek();
    }

    private void DoPlayWeek()
    {
        if (_svc.Universe is not { } u) return;
        UniversePage.Show(u.PlayWeek());
        RefreshUniverse();
    }

    private async void DoPlayYear()
    {
        if (_svc.Universe is not { } u) return;
        IReadOnlyList<RegionCard> last = Array.Empty<RegionCard>();
        await BusyAsync("A year of the sport…", () =>
        {
            for (int i = 0; i < 52; i++) last = u.PlayWeek();
        });
        UniversePage.Show(last);
        RefreshUniverse();
    }

    /// <summary>The world moved, so everything that reads it has to be told — the week's cards, and the
    /// rankings, champions, news and hall of fame that a universe shares with a career.</summary>
    private void RefreshUniverse()
    {
        UniversePage.Refresh();
        foreach (var n in new[] { nameof(InUniverse), nameof(InPlay), nameof(AtSetup), nameof(Nav) })
            Raise(n);
        RankingsPage.Rebuild();
        ChampionsPage.Rebuild();
        NewsPage.Rebuild();
        HallOfFamePage.Rebuild();
        AwardsPage.Rebuild();
        DashboardPage.RebuildRival();
        BuildUndercard();
    }

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
        // The universe reports its bouts as names, not objects — a week's card is text, and the men in it are
        // looked up the same way a reader would look them up.
        if (param is string name && Game.FindByName(name) is Boxer found) SelectedCard = BuildCard(found);
    }

    /// <summary>"2 of 30 on file - his earlier bouts were before this world began." Empty when the ledger is
    /// complete, because then there is nothing to explain.</summary>
    private static string RecordNoteFor(int onFile, int total)
    {
        if (total <= 0 || onFile >= total) return "";
        return $"{onFile} of {total} on file — his earlier bouts were fought before this world began, "
             + "so they count on his record but have no round-by-round.";
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
            Style = StyleClassifier.Of(b).DisplayName(),
            StyleNote = StyleClassifier.Of(b).Describe(),
            Fighter = b,
            Arc = g.CareerArc(b).Select(p => new ArcRow(p.Stage, p.Fights, p.Ratings, p.IsNow)).ToList(),
            Form = FormOf(b),
            Recent = b.History.OrderByDescending(h => h.Date).Select(h => ToLedger(h, b.Name)).ToList(),
            RecordNote = RecordNoteFor(b.History.Count, b.Record.Wins + b.Record.Losses + b.Record.Draws),
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
            Ui.Code(m.Country), m.Division.DisplayName(),
            // "Forced out" rather than "retired" when the sport took the decision from him.
            m.RetiredThroughInjury is string ended
                ? $"forced out {m.Year} aged {m.Age} — {ended}"
                : $"retired {m.Year} aged {m.Age}",
            $"peak {m.PeakOverall} OVR"
        }),
        Belts = (m.WeightTitles >= 2 ? $"{m.WeightTitles}-weight champion" : m.WasChampion ? "World champion" : "")
                + (m.Defenses > 0 ? $"  ·  {m.Defenses} defences" : ""),
        Recent = m.History.OrderByDescending(h => h.Date).Select(h => ToLedger(h, m.Name)).ToList(),
        Division = m.Division
    };

    /// <summary>Attributes on the SAME 1–15 scale as the class pills, not the engine's internal 1–100. Two
    /// scales side by side meant nothing: "Power 75" told you nothing about whether 75 was good.</summary>
    private const int TopClass = 15;
    internal static int OnClassScale(int raw) => Ratings.ClassFromRaw(raw);

    /// <summary>The part of a fighter that a rating cannot express. His knockout ratio is a fact about what he
    /// does to people; the punches he lands and takes in a round, worst to best, is the shape of his nights.
    /// Both come from his ledger, so they are what happened rather than what he is capable of.</summary>
    private static IReadOnlyList<StatRow> FormOf(Boxer b)
    {
        var rows = new List<StatRow>();
        var r = b.Record;
        int fights = r.Wins + r.Losses + r.Draws;
        if (fights == 0) return rows;

        if (r.Wins > 0)
            rows.Add(new StatRow("KO ratio", $"{100.0 * r.StoppageWins / r.Wins:0}%",
                                 r.TechnicalKnockoutWins > 0
                                     ? $"{r.StoppageWins} of {r.Wins} wins inside the distance — {r.KnockoutWins} by knockout"
                                     : $"{r.StoppageWins} of {r.Wins} wins inside the distance"));
        rows.Add(new StatRow("Stopped", r.Losses > 0 ? $"{r.StoppageLosses} of {r.Losses}" : "never",
                             r.Losses == 0 ? "unbeaten" :
                             r.StoppageLosses == 0 ? "never stopped - he has always heard the final bell"
                             : "losses that came inside the distance"));

        // Punch ranges need per-round cards, which are kept for ranked men and title fights.
        var cards = b.History.Where(h => h.Rounds is { Count: > 0 }).SelectMany(h => h.Rounds!).ToList();
        if (cards.Count >= 6)
        {
            rows.Add(new StatRow("Output range",
                                 $"{cards.Min(x => x.LandedFor)}-{cards.Max(x => x.LandedFor)}",
                                 $"landed in a round, worst to best - {cards.Average(x => x.LandedFor):0.0} typical"));
            rows.Add(new StatRow("Absorbed range",
                                 $"{cards.Min(x => x.LandedAgainst)}-{cards.Max(x => x.LandedAgainst)}",
                                 $"taken in a round, best to worst - {cards.Average(x => x.LandedAgainst):0.0} typical"));
        }
        return rows;
    }

    /// <summary>His punch mix. The engine already computes this to decide what he throws, so what is shown
    /// here is literally what he will do in the ring rather than a separate cosmetic guess.</summary>
    internal static IReadOnlyList<ArsenalSlice> ArsenalOf(Boxer b)
    {
        var d = PunchProfile.Distribution(b);
        var parts = new[]
        {
            ("Jab", d.Jab, "#8A7F70"), ("Cross", d.Cross, "#4FA3FF"), ("Hook", d.Hook, "#FFC24D"),
            ("Uppercut", d.Uppercut, "#4FD98B"), ("Body", d.Body, "#FF7A47")
        };
        int top = Math.Max(1, parts.Max(x => x.Item2));
        return parts.Select(x => new ArsenalSlice(x.Item1, x.Item2, x.Item2 / (double)top, x.Item3)).ToList();
    }

    /// <summary>The derived qualities, on the same 1-15 scale as everything else.</summary>
    internal static IReadOnlyList<CardStat> SecondaryOf(Boxer b)
    {
        var r = b.Ratings;
        return new[]
        {
            Bar("Killer instinct", SecondaryStats.KillerInstinct(r)),
            Bar("Durability", SecondaryStats.Durability(r)),
            Bar("Recovery", SecondaryStats.Recovery(r)),
            Bar("Pressure", SecondaryStats.Pressure(r)),
            Bar("Counter", SecondaryStats.Counter(b)),
            // Worth seeing before you take a fight: a man who mauls is a different night's work.
            Bar("Dirtiness", SecondaryStats.Dirtiness(b)),
        };
    }

    internal static CardStat Bar(string name, int raw)
    {
        int c = OnClassScale(raw);
        return new CardStat(name, c, c / (double)TopClass);
    }

    internal static IReadOnlyList<CardStat> AttributeBars(Ratings r) => new[]
    {
        Bar("Power", r.Power), Bar("Chin", r.Chin), Bar("Speed", r.Speed), Bar("Defence", r.Defense),
        Bar("Stamina", r.Stamina), Bar("Accuracy", r.Accuracy), Bar("Conditioning", r.Conditioning),
        Bar("Cut resistance", r.CutResistance), Bar("Aggression", r.Aggression), Bar("Heart", r.Heart),
    };

    /// <summary>Break "116-112 · 115-113 · 114-114" into three judges, each with who he had winning. The
    /// scores are stored from the owning fighter's point of view, so the first number is always his.
    ///
    /// The card names the man it favours. It used to read "for him" and "against him", which on a screen
    /// showing two fighters is a pronoun with two possible referents and no way to tell them apart — and on
    /// a split decision, where the whole interest is which judge went which way, "for him / against him /
    /// for him" says almost nothing.</summary>
    private static IReadOnlyList<JudgeCard> JudgesFrom(string? cards, string? owner = null, string? foe = null)
    {
        var outp = new List<JudgeCard>();
        if (string.IsNullOrWhiteSpace(cards)) return outp;
        string me = Surname(owner) ?? "him", them = Surname(foe) ?? "the other man";
        string[] names = { "Judge 1", "Judge 2", "Judge 3" };
        int i = 0;
        foreach (var (mine, his) in ScoreCards.Read(cards))
        {
            outp.Add(new JudgeCard(i < names.Length ? names[i] : $"Judge {i + 1}",
                                   $"{mine}–{his}",
                                   mine == his ? "level" : mine > his ? $"for {me}" : $"for {them}",
                                   mine > his, mine == his));
            i++;
        }
        return outp;
    }

    /// <summary>The surname, which is what a scorecard is read out with.</summary>
    private static string? Surname(string? full)
    {
        if (string.IsNullOrWhiteSpace(full)) return null;
        int sp = full.LastIndexOf(' ');
        return sp > 0 ? full[(sp + 1)..] : full;
    }

    /// <summary>internal rather than private: the dashboard's own view-model builds its
    /// recent-form rows with it, and one row shape beats two that drift.</summary>
    internal static LedgerRow ToLedger(BoutLine h, string? owner = null)
    {
        string detail = h.Method + (h.Round > 0 && h.Method is "KO" or "TKO" or "cut" ? $" rd{h.Round}" : "");
        // The weight it was made at, on every line. A record that spans divisions is exactly the one you
        // cannot read without it, and there is no way to tell which records those are at a glance.
        detail = $"{h.Division.DisplayName()} · {detail}";
        if (h.Note is not null) detail = $"{TitleAt(h)} · {detail}";
        if (h.Rounds is { Count: > 0 } rs)
        {
            int f = rs.Sum(r => r.LandedFor), a = rs.Sum(r => r.LandedAgainst);
            detail += $"  ·  {rs.Count} rd · {f}/{a} landed";
            int kd = rs.Sum(r => r.KdFor), kda = rs.Sum(r => r.KdAgainst);
            if (kd + kda > 0) detail += $" · KD {kd}-{kda}";
        }
        // The last line of a career says so. A man forced out of the sport by what happened on this night had
        // it recorded as an ordinary loss, and the most important entry on his record read like any other.
        if (h.CareerEndingInjury is string ended) detail += $"  ·  forced to retire — {ended}";
        // The owner travels with the row: rebuilding the fight to watch it needs BOTH men, and the bout line
        // only names the opponent. A title fight is flagged as one worth the extra search when it is replayed,
        // so the night it comes back with is the best of several rather than the first that fits.
        bool notable = IsTitleBout(h);
        return new LedgerRow(h.Date.ToString("d MMM yyyy"), h.Result.ToString(), h.Opponent, detail,
                             h.Result == 'W', h.Result == 'L', h, owner, notable,
                             notable ? TitleAt(h) : "");
    }

    /// <summary>A belt with its weight on it. "WBA title" names half a thing — a title won at welterweight
    /// is not the one won at middleweight, and a man who held both should read as having held both.</summary>
    /// <summary>A belt with its weight on it. "WBA title" names half a thing — a title won at welterweight
    /// is not the one won at middleweight — and "unification" named none of it at all: it read as a bare
    /// word on the record with no gold on it, when it is the biggest night a division has, both belts on
    /// the line and the winner walking out as the undisputed champion.</summary>
    private static string TitleAt(BoutLine h) =>
        h.Note is not string n ? ""
        : n == "unification" ? $"Undisputed {h.Division.DisplayName()} title"
        : n.EndsWith("title", StringComparison.Ordinal)
            ? $"{n[..^"title".Length]}{h.Division.DisplayName()} title"
            : n;

    /// <summary>Whether this is a bout the record is scanned FOR. A unification is, and did not say so.</summary>
    private static bool IsTitleBout(BoutLine h) =>
        h.Note is string n && (n == "unification" || n.Contains("title", StringComparison.OrdinalIgnoreCase));

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
            Source = row,
            Opponent = h.Opponent,
            Date = h.Date.ToString("d MMMM yyyy"),
            Verdict = (h.Result == 'W' ? "Won" : h.Result == 'L' ? "Lost" : "Drew")
                      + (h.Method == "cut" ? " on a cut" : $" by {h.Method}")
                      + (h.Round > 0 && h.Method is "KO" or "TKO" or "cut" ? $", round {h.Round}" : ""),
            Note = h.Note ?? "",
            Cards = h.Cards ?? "",
            Judges = JudgesFrom(h.Cards, row.OwnerName, h.Opponent),
            Title = IsTitleBout(h) ? TitleAt(h) : "",
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

    /// <summary>The man himself, so his name can be clicked through to his card. Deciding whether to take a
    /// fight is exactly when you want to look him up, and his name was the one piece of text on the screen
    /// that did not lead anywhere.</summary>
    public Boxer? OfferOpponentFighter => Game?.Offer?.Opponent;
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

    /// <summary>What a man actually does in a round, from his own cards: the punches he lands and the punches
    /// he takes, worst to best. The tape compares what two fighters ARE - their attributes - but the decision
    /// to take a fight is also about what they DO, and a man who lands 25 a round is a different night's work
    /// from one who lands 9 whatever their ratings say. Empty for a fighter with too few kept cards to mean
    /// anything, rather than shown as a misleading range off two rounds.</summary>
    private static string Output(Boxer b)
    {
        var cards = b.History.Where(h => h.Rounds is { Count: > 0 }).SelectMany(h => h.Rounds!).ToList();
        if (cards.Count >= 4)
            return $"lands {cards.Min(x => x.LandedFor)}–{cards.Max(x => x.LandedFor)} a round  ·  " +
                   $"takes {cards.Min(x => x.LandedAgainst)}–{cards.Max(x => x.LandedAgainst)}";

        // Round cards are only kept for the player's own bouts, title fights and ranked men, so the journeyman
        // across the ring from a novice - exactly the man you are trying to size up - usually has none at all.
        // Rather than leave the space blank on the opponent's side of every early-career tape, his output is
        // ESTIMATED from what drives it in the engine: how much he throws (work-rate and stamina) and how much
        // of it lands (accuracy). Worded as an expectation, not a measurement, because that is what it is.
        var r = b.Ratings;
        double thrown = 14 + r.Aggression * 0.22 + r.Stamina * 0.10;
        double rate = Math.Clamp(0.22 + (r.Accuracy - 50) / 300.0, 0.15, 0.50);
        int lo = (int)Math.Round(thrown * 0.85 * rate), hi = (int)Math.Round(thrown * 1.15 * rate);
        return $"expect {lo}–{hi} landed a round";
    }

    public string PlayerOutput => Game is null ? "" : Output(Game.Player);
    public string OfferOpponentOutput => Game?.Offer is { } o ? Output(o.Opponent) : "";
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
        // Anything that moved the world may have crossed a new year. Checking here rather than in the two
        // or three places I first thought of means taking a fight, holding out and moving up all announce
        // the year's honours, instead of only waiting for fight night doing it.
        if (!IsPlayingBack) CheckForAwards();
        DashboardPage.Rebuild();
        RankingsPage.Rebuild();
        PoundForPoundPage.Rebuild();
        ChampionsPage.Rebuild();
        HallOfFamePage.Rebuild();
        AwardsPage.Rebuild();
        DashboardPage.RebuildRival();
        BuildUndercard();
        OfferSlate.Rebuild();
        NewsPage.Rebuild();
        BuildLedger();
        BuildTape();
        StatsPage.Rebuild();

        // Everything, rather than a list of fifty-four names kept by hand.
        //
        // A null property name is WPF's "assume all of them changed": every binding on this object
        // re-evaluates. That is the whole point — the hand-written list was a bug generator. Add a computed
        // property, forget to add its name here, and the UI silently shows a stale value with nothing to
        // catch it. It cost a shipped release: InCamp and FightIsStillAnOffer both derive from the fight
        // countdown, neither was in the list, and at zero the page still believed it was in camp and offered
        // no way to start the fight.
        //
        // The cost is re-evaluating every binding on a turn. RefreshAll already rebuilds fifteen collections
        // immediately above this, so it is lost in the noise, and correctness is worth more than the
        // microseconds either way.
        RaiseAll();
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        TakeFight.Refresh(); HoldOut.Refresh(); MoveUp.Refresh();
        StartCareer.Refresh(); ContinueCareer.Refresh(); DeleteSlot?.Refresh();
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
            Fighter = FindFighter(a.Name),
            Bout = a.Bout
        };
    }

    /// <summary>Watch the fight an award was given for. Rebuilt asking for the best of several matching nights
    /// rather than the first that fits, so a fight named Fight of the Year plays like one — this is the whole
    /// point of an award pointing at a bout instead of just describing it.</summary>
    /// <summary>Open a fight straight from the news feed. The feed is the record of what is happening in the
    /// sport, so a result in it should be a way into the fight rather than a sentence about it.</summary>
    private void OnWatchNews(object? param)
    {
        if (Game is null || param is not NewsRow row || row.Bout is not BoutRef r) return;
        if (Game.FindBout(r) is not (Boxer owner, Boxer foe, BoutLine line))
        {
            WatchUnavailable = "That fight is no longer on the record.";
            return;
        }
        _ = WatchAsync(owner, foe, line, notable: line.Note is string n && n.Contains("title", StringComparison.OrdinalIgnoreCase));
    }

    private void OnWatchAward()
    {
        if (Game is null || SelectedAward?.Bout is not BoutRef r) return;
        if (Game.FindBout(r) is not (Boxer owner, Boxer foe, BoutLine line))
        {
            WatchUnavailable = "That fight is no longer on the record.";
            return;
        }
        SelectedAward = null;
        _ = WatchAsync(owner, foe, line, notable: true);
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

    private void BuildLedger()
    {
        Ledger.Clear();
        if (Game is null) return;
        // No cap: a busy twenty-year career runs past any round number worth picking.
        foreach (var h in Game.Player.History.OrderByDescending(h => h.Date))
            Ledger.Add(ToLedger(h, Game.Player.Name));
    }

    /// <summary>Tale of the tape: the player's attributes against the man he's being offered, so the decision to
    /// take the fight is an informed one rather than a name and a record.</summary>
    // The same seven attributes, in three groups rather than one flat run of bars.
    //
    // Seven rows with the name down the middle and no headings gave the eye nowhere to rest and no reason for
    // the order: Power then Chin then Speed then Defence read as a list that had simply been typed out. Grouped,
    // each block answers one question about the man.
    public ObservableCollection<TapeRow> TapeAttack { get; } = new();
    public ObservableCollection<TapeRow> TapeDefence { get; } = new();
    public ObservableCollection<TapeRow> TapeEngine { get; } = new();

    /// <summary>The tape in a sentence, for when the section is folded away. A header that only says "tale of
    /// the tape" is worth nothing closed; this says who the numbers favour and where the other man's edge is.</summary>
    public string TapeEdge { get; private set; } = "";

    private void BuildTape()
    {
        Tape.Clear(); TapeAttack.Clear(); TapeDefence.Clear(); TapeEngine.Clear();
        TapeEdge = "";
        if (Game?.Offer is not { } o) { Raise(nameof(TapeEdge)); return; }
        var me = Game.Player.Ratings;
        var them = o.Opponent.Ratings;
        BuildStyleMatchup(Game.Player, o.Opponent);

        // Both men's attributes on the 1–15 class scale, so the tape reads in the same units as the pills.
        void Row(ObservableCollection<TapeRow> into, string name, int rawA, int rawB)
        {
            int a = OnClassScale(rawA), b = OnClassScale(rawB);
            var row = new TapeRow(name, a, b, a / (double)TopClass, b / (double)TopClass, a >= b);
            into.Add(row);
            Tape.Add(row);
        }

        // All ten, not the seven it used to show. Conditioning, cut resistance and aggression were on the
        // fighter card but absent from the one screen where you decide whether to take the fight — and a man's
        // cut resistance is exactly the sort of thing you would want to know before agreeing to twelve rounds.
        Row(TapeAttack,  "Power",          me.Power,         them.Power);
        Row(TapeAttack,  "Accuracy",       me.Accuracy,      them.Accuracy);
        Row(TapeAttack,  "Aggression",     me.Aggression,    them.Aggression);
        Row(TapeDefence, "Chin",           me.Chin,          them.Chin);
        Row(TapeDefence, "Defence",        me.Defense,       them.Defense);
        Row(TapeDefence, "Cut resistance", me.CutResistance, them.CutResistance);
        Row(TapeEngine,  "Speed",          me.Speed,         them.Speed);
        Row(TapeEngine,  "Stamina",        me.Stamina,       them.Stamina);
        Row(TapeEngine,  "Conditioning",   me.Conditioning,  them.Conditioning);
        Row(TapeEngine,  "Heart",          me.Heart,         them.Heart);

        int mine = Tape.Count(r => r.Mine > r.Theirs);
        int his = Tape.Count(r => r.Theirs > r.Mine);
        var hisBest = Tape.Where(r => r.Theirs > r.Mine)
                          .OrderByDescending(r => r.Theirs - r.Mine)
                          .Take(2).Select(r => r.Attribute.ToLowerInvariant()).ToList();

        TapeEdge = mine == 0 && his == 0 ? "Nothing between you on paper."
                 : his == 0             ? "Every number is yours."
                 : mine == 0            ? "Every number is his."
                 : $"You lead {mine} of {Tape.Count} — his edge is {string.Join(" and ", hisBest)}.";
        Raise(nameof(TapeEdge));
    }

    // ---- the styles, and what they make of each other ----
    private string _myStyle = "", _theirStyle = "", _styleRead = "";
    /// <summary>What each man is, above the tape.</summary>
    public string MyStyle => _myStyle;
    public string TheirStyle => _theirStyle;
    /// <summary>What the pairing means. Styles make fights: the swarmer hunts the out-boxer, the out-boxer
    /// picks the slugger apart, the slugger walks through the swarmer. The sim has always scored this — it is
    /// in every exchange — so the card may as well say which way it leans before the bell.</summary>
    public string StyleRead => _styleRead;

    private void BuildStyleMatchup(Boxer me, Boxer them)
    {
        var a = StyleClassifier.Of(me);
        var b = StyleClassifier.Of(them);
        _myStyle = a.DisplayName();
        _theirStyle = b.DisplayName();

        double edge = FightingStyles.Advantage(a, b);
        _styleRead = a == b
            ? $"Two {a.DisplayName().ToLowerInvariant()}s — neither man's style troubles the other, so it comes down to who is better."
            : edge >= 0.45 ? $"The matchup is yours: a {a.DisplayName().ToLowerInvariant()} is trouble for a {b.DisplayName().ToLowerInvariant()}."
            : edge >= 0.15 ? $"The styles lean your way — a {b.DisplayName().ToLowerInvariant()} would rather not be in with a {a.DisplayName().ToLowerInvariant()}."
            : edge > -0.15 ? $"{a.DisplayName()} against {b.DisplayName()} — nothing in the styles either way."
            : edge > -0.45 ? $"The styles lean his way: a {b.DisplayName().ToLowerInvariant()} knows what to do with a {a.DisplayName().ToLowerInvariant()}."
            : $"A bad style night: the {b.DisplayName().ToLowerInvariant()} is exactly the wrong man for a {a.DisplayName().ToLowerInvariant()}.";

        foreach (var n in new[] { nameof(MyStyle), nameof(TheirStyle), nameof(StyleRead) }) Raise(n);
    }
}
