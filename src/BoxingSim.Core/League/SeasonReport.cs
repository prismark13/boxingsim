using BoxingSim.Core.Engine;
using BoxingSim.Core.Model;

namespace BoxingSim.Core.League;

public sealed class DivisionReport
{
    public required WeightClass WeightClass { get; init; }
    public Boxer? Champion { get; set; }
    public FightResult? TitleFight { get; set; }
    public bool TitleChanged { get; set; }
    public bool TitleVacantFight { get; set; }
    public List<Boxer> TopContenders { get; set; } = new();
}

public sealed class SeasonReport
{
    public int Year { get; init; }
    public int TotalFights { get; set; }
    public int Knockouts { get; set; }
    public int Retirements { get; set; }
    public int Debuts { get; set; }
    public List<DivisionReport> Divisions { get; } = new();
    public List<string> NewChampions { get; } = new();

    public Boxer? BiggestUpsetWinner { get; set; }
    public Boxer? BiggestUpsetLoser { get; set; }
    public double BiggestUpsetGap { get; set; }
}
