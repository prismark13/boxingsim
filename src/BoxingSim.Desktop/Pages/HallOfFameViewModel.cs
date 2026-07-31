using System.Collections.ObjectModel;
using BoxingSim.Core;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;

namespace BoxingSim.Desktop.Pages;

/// <summary>The retired greats of this world.
///
/// Its rows carry a HallOfFamer in the Legend slot rather than a Boxer: these men are gone, and what is left
/// of them is the ledger entry, not a fighter the world can still be asked about. The shell's card opener
/// checks Legend first for that reason.</summary>
public sealed class HallOfFameViewModel : Observable
{
    private readonly Func<CareerGame?> _game;

    public HallOfFameViewModel(Func<CareerGame?> game) => _game = game;

    public ObservableCollection<RankRow> HallOfFame { get; } = new();

    public void Rebuild()
    {
        HallOfFame.Clear();
        var game = _game();
        if (game is not null)
        {
            int r = 1;
            foreach (var m in game.HallOfFame.Take(50))
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
        RaiseAll();
    }
}
