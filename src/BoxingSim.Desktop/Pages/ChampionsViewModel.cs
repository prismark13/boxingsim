using System.Collections.ObjectModel;
using BoxingSim.Core;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;

namespace BoxingSim.Desktop.Pages;

/// <summary>Every belt in every division.
///
/// A vacant belt is a row like any other, with a null holder — the board has to show that nobody holds it,
/// which is a fact about the division, not an absence of one.</summary>
public sealed class ChampionsViewModel : Observable
{
    private readonly Func<CareerGame?> _game;

    public ChampionsViewModel(Func<CareerGame?> game) => _game = game;

    public ObservableCollection<DivisionRow> Champions { get; } = new();

    public void Rebuild()
    {
        Champions.Clear();
        var game = _game();
        if (game is not null)
        {
            foreach (var d in game.ChampionsBoard())
            {
                var belts = new List<BeltRow>();
                void Add(string belt, Boxer? holder, int def, bool lineal) =>
                    belts.Add(holder is null
                        ? new BeltRow(belt, "vacant", "", lineal, true, null)
                        : new BeltRow(belt, holder.Name, holder.Record + (def > 0 ? $" · {def} def" : ""), lineal, false, holder));

                Add(game.LinealBelt, d.Lineal, d.LinealDefenses, true);
                Add(game.PrimaryBelt, d.Wba, d.WbaDefenses, false);
                if (game.WbcActive) Add("WBC", d.Wbc, d.WbcDefenses, false);
                if (game.IbfActive) Add("IBF", d.Ibf, d.IbfDefenses, false);

                Champions.Add(new DivisionRow(d.Division.DisplayName(),
                                              d.Undisputed is Boxer u ? $"undisputed · {u.Name}" : "",
                                              belts, d.Division == game.Player.WeightClass));
            }
        }
        RaiseAll();
    }
}
