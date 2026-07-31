using System.Collections.ObjectModel;
using System.Windows;
using BoxingSim.Core;
using BoxingSim.Core.Career;

namespace BoxingSim.Desktop.Pages;

/// <summary>A week of boxing as it is actually followed: not one long list of results, but what happened in
/// Britain and what happened in Mexico. Region first, because a Tuesday in Bethnal Green and a Tuesday in
/// Tijuana are not the same sport.
///
/// The cards are PUSHED in rather than pulled: they are what PlayWeek returned, not something the world can
/// be asked for afterwards, so only the caller that ran the week has them.</summary>
public sealed class UniverseViewModel : Observable
{
    private readonly Func<Universe?> _universe;

    public UniverseViewModel(Func<Universe?> universe) => _universe = universe;

    public ObservableCollection<RegionCard> UniverseWeek { get; } = new();

    /// <summary>Show a week's cards. Ends in RaiseAll: every label on this page is derived from either the
    /// world's clock or the contents of this collection.</summary>
    public void Show(IReadOnlyList<RegionCard> cards)
    {
        UniverseWeek.Clear();
        foreach (var c in cards) UniverseWeek.Add(c);
        RaiseAll();
    }

    public void Clear()
    {
        UniverseWeek.Clear();
        RaiseAll();
    }

    /// <summary>Re-read the labels without touching the cards — for when the world moved but this page was
    /// not the thing that moved it.</summary>
    public void Refresh() => RaiseAll();

    public string UniverseDate => _universe() is { } u ? u.Date.ToString("d MMMM yyyy") : "";
    public string UniverseWeekLabel => _universe() is { } u ? $"WEEK {u.Week}" : "";

    public bool UniverseManyDivisions => _universe() is not { } u || u.Settings.Divisions.Count != 1;

    /// <summary>A one-division world has nothing to say in a division column, so it collapses to nothing
    /// rather than printing the same word down the page.</summary>
    public GridLength DivisionColumn =>
        new(UniverseManyDivisions ? 126 : 0, GridUnitType.Pixel);

    public bool UniverseQuiet => _universe() is not null && UniverseWeek.Count == 0;

    public string UniverseSummary => _universe() is null ? ""
        : $"{UniverseWeek.Sum(r => r.Bouts)} bouts · {UniverseWeek.Sum(r => r.TitleBouts)} for a title";
}
