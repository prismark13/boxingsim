using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
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
    private readonly Action _run;
    private readonly Func<bool>? _can;
    public Cmd(Action run, Func<bool>? can = null) { _run = run; _can = can; }
    public bool CanExecute(object? p) => _can?.Invoke() ?? true;
    public void Execute(object? p) => _run();
    public event EventHandler? CanExecuteChanged;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

// ---- row shapes the views bind to ----

public sealed record RankRow(string Rank, int Class, string Name, string Detail, string Record, bool IsPlayer, bool IsChampion);
public sealed record BeltRow(string Belt, string Holder, string Detail, bool Lineal, bool Vacant, int Class);
public sealed record DivisionRow(string Division, string Undisputed, IReadOnlyList<BeltRow> Belts, bool IsPlayerDivision);
public sealed record NewsRow(string Date, string Text, string Kind, bool PlayerBout);
public sealed record HofRow(int Rank, int Class, string Name, string Detail, string Record);
public sealed record AwardRow(string Category, int Year, IReadOnlyList<AwardPlace> Places);
public sealed record AwardPlace(string Position, string Name, string Division, string Detail, bool Winner);
public sealed record LedgerRow(string Date, string Result, string Opponent, string Detail, bool Win, bool Loss);

/// <summary>Everything the window binds to. It owns the career service and rebuilds its display collections after
/// each turn — the sim is synchronous and fast enough that there's nothing to stream.</summary>
public sealed class CareerViewModel : Observable
{
    private readonly DesktopCareerService _svc = new();
    private readonly Random _rng = new();

    public CareerViewModel()
    {
        TakeFight = new Cmd(Take, () => Game?.Offer is not null && Game?.Player.Retired == false);
        HoldOut = new Cmd(Decline, () => Game?.Offer is not null && Game?.Player.Retired == false);
        MoveUp = new Cmd(DoMoveUp, () => Game?.CanMoveUp == true);
        StartCareer = new Cmd(Start, () => !Busy);
        ContinueCareer = new Cmd(Continue, () => DesktopCareerService.HasSave);
        AbandonCareer = new Cmd(Abandon);
        RollName = new Cmd(() => PlayerName = NameGen.Generate(Country, _rng));

        Divisions = _svc.AvailableDivisions();
        SetupDivision = Divisions.FirstOrDefault(WeightClass.Middleweight);
        PlayerName = NameGen.Generate(Country, _rng);
    }

    public CareerGame? Game => _svc.Game;
    public bool InCareer => _svc.HasCareer;

    // ---- setup screen ----
    public IReadOnlyList<WeightClass> Divisions { get; }
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
    public bool Busy { get => _busy; set { _busy = value; Raise(); Refresh(); } }

    // ---- commands ----
    public Cmd TakeFight { get; }
    public Cmd HoldOut { get; }
    public Cmd MoveUp { get; }
    public Cmd StartCareer { get; }
    public Cmd ContinueCareer { get; }
    public Cmd AbandonCareer { get; }
    public Cmd RollName { get; }

    // ---- collections the career screen binds to ----
    public ObservableCollection<RankRow> Rankings { get; } = new();
    public ObservableCollection<RankRow> PoundForPound { get; } = new();
    public ObservableCollection<DivisionRow> Champions { get; } = new();
    public ObservableCollection<HofRow> HallOfFame { get; } = new();
    public ObservableCollection<AwardRow> Awards { get; } = new();
    public ObservableCollection<NewsRow> News { get; } = new();
    public ObservableCollection<LedgerRow> Ledger { get; } = new();

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
        RefreshAll();
    }

    private void Continue() { if (_svc.Load()) RefreshAll(); else { ContinueCareer.Refresh(); Raise(nameof(HasSave)); } }
    private void Take() { _svc.Take(); RefreshAll(); }
    private void Decline() { _svc.Decline(); RefreshAll(); }
    private void DoMoveUp() { _svc.MoveUp(); RefreshAll(); }
    private void Abandon() { _svc.Abandon(); RefreshAll(); }

    public static bool HasSave => DesktopCareerService.HasSave;
    public string? SaveError => _svc.LastSaveError;
    public string SaveLocation => DesktopCareerService.SavePath;

    // ---- the last bout, for the result banner ----
    public bool HasResult => _svc.LastResult is not null;
    public string ResultHeadline
    {
        get
        {
            var r = _svc.LastResult;
            if (r is null || Game is null) return "";
            if (r.IsDraw) return $"DRAW — {Game.Player.Name} drew with {LastOpponent}";
            bool won = r.Winner!.Id == Game.Player.Id;
            return won
                ? $"WIN — {Game.Player.Name} beat {LastOpponent} by {r.Method}" + RoundSuffix(r)
                : $"LOSS — {Game.Player.Name} lost to {LastOpponent} by {r.Method}" + RoundSuffix(r);
        }
    }
    public bool LastBoutWon => _svc.LastResult is { IsDraw: false } r && Game is not null && r.Winner!.Id == Game.Player.Id;
    public bool LastBoutLost => _svc.LastResult is { IsDraw: false } r && Game is not null && r.Winner!.Id != Game.Player.Id;

    private static string RoundSuffix(FightResult r) =>
        r.Method is "KO" or "TKO" ? $", round {r.EndRound}" : "";

    private string LastOpponent =>
        Game?.Player.History.LastOrDefault()?.Opponent ?? "his opponent";

    // ---- header ----
    public string PlayerHeadline => Game is null ? "" : Game.Player.Name;
    public string PlayerMeta
    {
        get
        {
            if (Game is null) return "";
            var p = Game.Player;
            int rank = PlayerRank;
            var belts = Game.BeltsHeld(p).Select(b => b.Belt).ToList();
            var bits = new List<string>
            {
                Ui.Code(p.Country), $"age {p.Age}", CareerStages.Label(CareerStages.Of(p)),
                p.Record.ToString(), rank > 0 ? $"#{rank}" : "unranked",
                p.WeightClass.DisplayName()
            };
            if (belts.Count > 0) bits.Add(string.Join(" · ", belts));
            return string.Join("  ·  ", bits);
        }
    }
    public int PlayerClass => Game?.Player.Class ?? 0;

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
    public string OfferDetail
    {
        get
        {
            var o = Game?.Offer;
            if (o is null) return "";
            var b = o.Opponent;
            return $"{Ui.Code(b.Country)} · {b.Record} · {o.Rounds} rounds";
        }
    }
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

    public void RefreshAll()
    {
        BuildRankings();
        BuildP4P();
        BuildChampions();
        BuildHof();
        BuildAwards();
        BuildNews();
        BuildLedger();

        foreach (var n in new[]
        {
            nameof(InCareer), nameof(Game), nameof(PlayerHeadline), nameof(PlayerMeta), nameof(PlayerClass),
            nameof(DateLabel), nameof(OfferDateLabel), nameof(HasOffer), nameof(OfferOpponent),
            nameof(OfferOpponentClass), nameof(OfferDetail), nameof(OfferContext), nameof(OfferIsTitle),
            nameof(MoveUpLabel), nameof(HasResult), nameof(ResultHeadline), nameof(LastBoutWon),
            nameof(LastBoutLost), nameof(PlayerRetired), nameof(HasSave), nameof(SaveError)
        }) Raise(n);
        Refresh();
    }

    private void Refresh()
    {
        TakeFight.Refresh(); HoldOut.Refresh(); MoveUp.Refresh();
        StartCareer.Refresh(); ContinueCareer.Refresh();
    }

    private void BuildRankings()
    {
        Rankings.Clear();
        if (Game is null) return;
        var div = Game.Player.WeightClass;
        int r = 1;
        foreach (var b in Game.RankingOf(div, 15))
        {
            var belts = Game.BeltsHeld(b).Select(x => x.Belt).ToList();
            Rankings.Add(new RankRow(
                Game.IsWorldChampion(b) ? "C" : r.ToString(),
                b.Class, b.Name,
                belts.Count > 0 ? string.Join(" · ", belts) : "",
                b.Record.ToString(),
                b.Id == Game.Player.Id,
                Game.IsWorldChampion(b)));
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
            if (a.Undisputed) bits.Add("UNDISPUTED");
            else bits.AddRange(a.Belts);
            if (a.Lineal) bits.Add(Game.LinealBelt);
            if (a.Defences > 0) bits.Add($"{a.Defences} defence{(a.Defences == 1 ? "" : "s")}");
            if (a.WeightTitles >= 2) bits.Add($"{a.WeightTitles}-weight champ");
            if (a.Belts.Count == 0 && !a.Lineal && a.TitleWins > 0) bits.Add($"ex-champ · {a.TitleWins} title wins");
            PoundForPound.Add(new RankRow(r.ToString(), b.Class, b.Name, string.Join(" · ", bits),
                                          b.Record.ToString(), b.Id == Game.Player.Id, Game.IsWorldChampion(b)));
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
            void Add(string belt, Boxer? holder, int def, bool lineal)
            {
                belts.Add(holder is null
                    ? new BeltRow(belt, "vacant", "", lineal, true, 0)
                    : new BeltRow(belt, holder.Name,
                                  holder.Record + (def > 0 ? $" · {def} def" : ""), lineal, false, holder.Class));
            }
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
        foreach (var m in Game.HallOfFame.Take(40))
        {
            var bits = new List<string> { m.Division.DisplayName() };
            if (m.WeightTitles >= 2) bits.Add($"{m.WeightTitles}-weight champ");
            else if (m.WasChampion) bits.Add("world champ");
            if (m.Defenses > 0) bits.Add($"{m.Defenses} def");
            bits.Add($"class {m.PeakClass}");
            HallOfFame.Add(new HofRow(r, m.PeakClass, m.Name, string.Join(" · ", bits), m.Record));
            r++;
        }
    }

    private void BuildAwards()
    {
        Awards.Clear();
        if (Game is null) return;
        foreach (var yr in Game.Awards.Take(6))
        {
            void Add(string cat, IReadOnlyList<AwardWinner> list)
            {
                if (list.Count == 0) return;
                var places = list.Select((w, i) => new AwardPlace(
                    i == 0 ? "1st" : i == 1 ? "2nd" : "3rd",
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
        var div = Game.Player.WeightClass;
        var rows = Game.Log
            .Select((e, i) => (e, i))
            .Where(x => x.e.Div == div || x.e.PlayerBout || x.e.Kind is "award" or "hof")
            .OrderByDescending(x => x.e.On).ThenByDescending(x => x.i)
            .Take(60);
        foreach (var (e, _) in rows)
            News.Add(new NewsRow(e.DateLabel, e.Text, e.Kind ?? "", e.PlayerBout));
    }

    private void BuildLedger()
    {
        Ledger.Clear();
        if (Game is null) return;
        foreach (var h in Game.Player.History.OrderByDescending(h => h.Date).Take(60))
        {
            string detail = h.Method + (h.Round > 0 && h.Method is "KO" or "TKO" ? $" rd{h.Round}" : "");
            if (h.Note is not null) detail = $"{h.Note} · {detail}";
            Ledger.Add(new LedgerRow(h.Date.ToString("d MMM yyyy"),
                                     h.Result.ToString(), h.Opponent, detail,
                                     h.Result == 'W', h.Result == 'L'));
        }
    }
}
