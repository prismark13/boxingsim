using System.Collections.ObjectModel;
using BoxingSim.Core;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;

namespace BoxingSim.Desktop.Pages;

/// <summary>Career mode's hub: your form, where you stand in your own division, the man you are measured
/// against, and the fight on the table — each a way INTO the fuller screen behind it rather than another
/// list.
///
/// Most of what this page DRAWS is not its own. The offer, the camp state and the commands under them belong
/// to the shell, because the Career page shows the same fight and the same buttons; the view reaches them
/// rather than this object holding a second copy. What lives here is what only the dashboard shows.</summary>
public sealed class DashboardViewModel : Observable
{
    private readonly Func<CareerGame?> _game;
    private readonly Func<int> _playerRank;

    public DashboardViewModel(Func<CareerGame?> game, Func<int> playerRank)
    {
        _game = game;
        _playerRank = playerRank;
    }

    public ObservableCollection<LedgerRow> RecentForm { get; } = new();
    public ObservableCollection<RankRow> DivisionTop { get; } = new();

    /// <summary>Your last five, and the top of your own division.
    ///
    /// Kept separate from RebuildRival on purpose: a universe refresh rebuilds the rival and NOT this, and
    /// collapsing the two would quietly start doing a career's work on every week of a world with no career
    /// in it.</summary>
    public void Rebuild()
    {
        RecentForm.Clear(); DivisionTop.Clear();
        var game = _game();
        if (game is null) { RaiseAll(); return; }
        var p = game.Player;

        foreach (var h in p.History.OrderByDescending(h => h.Date).Take(5))
            RecentForm.Add(CareerViewModel.ToLedger(h, p.Name));

        int r = 1;
        foreach (var b in game.RankingBoard(p.WeightClass, 5))
        {
            bool champ = game.IsWorldChampion(b);
            var belts = game.BeltsHeld(b).Select(x => x.Belt).ToList();
            DivisionTop.Add(new RankRow(champ ? "C" : r.ToString(), b.Class, b.Name,
                                        belts.Count > 0 ? string.Join(" · ", belts) : "",
                                        b.Record.ToString(), b.Id == p.Id, champ, b));
            if (!champ) r++;
        }
        RaiseAll();
    }

    // ---- the man you are measured against ----
    public string RivalName { get; private set; } = "";
    public string RivalRecord { get; private set; } = "";
    public string RivalStanding { get; private set; } = "";
    public string RivalReason { get; private set; } = "";
    public bool HasRival => RivalName.Length > 0;
    private Boxer? _rival;
    public Boxer? RivalFighter => _rival;

    public void RebuildRival()
    {
        var game = _game();
        _rival = game?.Rival;
        RivalName = _rival?.Name ?? "";
        RivalRecord = _rival is null ? "" : $"{_rival.Record}  ·  class {_rival.Class}";
        RivalStanding = _rival is null || game is null ? "" : game.RivalStanding(_rival);
        RivalReason = _rival is null || game is null ? "" : game.RivalReason(_rival);
        RaiseAll();
    }

    /// <summary>Where he stands, on one line under his name.
    ///
    /// This was four cards across the top of the dashboard — Record, Division rank, Titles, Rating — and
    /// three of the four repeated what the sidebar already showed two inches to the left: the same record,
    /// the same belts, the same class badge. Only his rank, his defence count and the raw overall were
    /// genuinely new, so those are what this keeps, and the full record is one click away rather than
    /// spread across a row of tiles nobody needed twice.
    ///
    /// Not to be confused with the shell's PlayerStanding, which is the sidebar's one-line belt or rank
    /// caption. This is the fuller line at the head of the dashboard.</summary>
    public string StandingLine
    {
        get
        {
            if (_game() is not { } game || game.Player is not { } p) return "";
            string div = p.WeightClass.DisplayName();
            int rank = _playerRank();
            var bits = new List<string> { rank > 0 ? $"#{rank} in {div}" : $"unranked in {div}" };

            var belts = game.BeltsHeld(p).Select(x => x.Belt).ToList();
            if (belts.Count > 0)
                bits.Add(game.TitleDefenses > 0
                         ? $"{string.Join(" · ", belts)}, {game.TitleDefenses} defence{(game.TitleDefenses == 1 ? "" : "s")}"
                         : string.Join(" · ", belts));

            bits.Add($"class {p.Class} · {p.Overall} OVR");
            return string.Join("   ·   ", bits);
        }
    }
}
