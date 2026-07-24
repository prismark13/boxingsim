using BoxingSim.Core.Model;

namespace BoxingSim.Core.League;

/// <summary>A weight class: its fighters, its champion and its live rankings.</summary>
public sealed class Division
{
    public WeightClass WeightClass { get; }
    public List<Boxer> Boxers { get; } = new();
    public Boxer? Champion { get; set; }

    public Division(WeightClass wc) => WeightClass = wc;

    public IEnumerable<Boxer> Active => Boxers.Where(b => !b.Retired);

    /// <summary>All active fighters ordered by ranking points, best first.</summary>
    public List<Boxer> Ranked() =>
        Active.OrderByDescending(b => b.RankPoints).ThenByDescending(b => b.Overall).ToList();

    /// <summary>The ranked contenders excluding the reigning champion.</summary>
    public List<Boxer> Contenders()
    {
        var ranked = Ranked();
        if (Champion is not null) ranked.RemoveAll(b => b.Id == Champion.Id);
        return ranked;
    }

    public void Add(Boxer b) => Boxers.Add(b);
}
