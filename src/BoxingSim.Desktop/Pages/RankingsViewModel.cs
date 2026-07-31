using System.Collections.ObjectModel;
using BoxingSim.Core;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;

namespace BoxingSim.Desktop.Pages;

/// <summary>The rankings board.
///
/// It can show ANY division, not just the player's — otherwise following a fighter to his own division was
/// impossible without abandoning the page. That is why the division being viewed lives here rather than on
/// the world: it is a property of what you are LOOKING at, not of the career.
///
/// The shell still reads and writes ViewDivision, because going back is defined as returning to the page AND
/// the division you were on — the thing you lose most often when following a fighter across the boards. It
/// does that through Restore, which deliberately does not announce a change, so replaying history cannot
/// push more history.</summary>
public sealed class RankingsViewModel : Observable
{
    private readonly Func<CareerGame?> _game;

    public RankingsViewModel(Func<CareerGame?> game)
    {
        _game = game;
        GoHomeDivision = new Cmd(() =>
        {
            var g = _game();
            if (g is not null) ViewDivision = g.Player.WeightClass;
        });
    }

    public ObservableCollection<RankRow> Rankings { get; } = new();

    private WeightClass _viewDivision;

    /// <summary>Which division the page is showing. Defaults to the player's, but any can be inspected.</summary>
    public WeightClass ViewDivision
    {
        get => _viewDivision;
        set
        {
            if (_viewDivision == value) return;
            _viewDivision = value;
            Rebuild();
        }
    }

    /// <summary>Put the page back on a division without treating it as a move — for replaying nav history, and
    /// for the two places a career begins. Setting ViewDivision would be equivalent today, but only by
    /// accident: the moment going back is asked to do anything more than assign, a setter that also records
    /// the move would record the replay as a fresh one and back would stop reaching the start.</summary>
    public void Restore(WeightClass division)
    {
        _viewDivision = division;
        Rebuild();
    }

    public IReadOnlyList<WeightClass> RankingDivisions => _game()?.LiveDivisions ?? Array.Empty<WeightClass>();

    public string RankingsSubtitle =>
        _game() is { } g && ViewDivision == g.Player.WeightClass
            ? $"{ViewDivision.DisplayName()} · your division"
            : ViewDivision.DisplayName();

    // Say plainly when you're reading somebody else's division, with the way home.
    public bool IsAwayDivision => _game() is { } g && ViewDivision != g.Player.WeightClass;
    public string AwayDivisionNote => $"Viewing {ViewDivision.DisplayName()} — not your division";
    public string HomeDivisionLabel => _game() is { } g ? $"Back to {g.Player.WeightClass.DisplayName()}" : "";
    public Cmd GoHomeDivision { get; }

    /// <summary>Ends in RaiseAll rather than a list of names — the shell's own RaiseAll cannot reach this
    /// object's bindings now that it is a separate one.</summary>
    public void Rebuild()
    {
        Rankings.Clear();
        var game = _game();
        if (game is not null)
        {
            int r = 1;
            foreach (var b in game.RankingBoard(ViewDivision, 15))
            {
                bool champ = game.IsWorldChampion(b);
                var belts = game.BeltsHeld(b).Select(x => x.Belt).ToList();
                Rankings.Add(new RankRow(champ ? "C" : r.ToString(), b.Class, b.Name,
                                         belts.Count > 0 ? string.Join(" · ", belts) : "",
                                         b.Record.ToString(), b.Id == game.Player.Id, champ, b));
                if (!champ) r++;   // contenders are #1 down; the champions sit above the numbering
            }
        }
        RaiseAll();
    }
}
