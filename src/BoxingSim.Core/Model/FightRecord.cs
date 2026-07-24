namespace BoxingSim.Core.Model;

/// <summary>A fighter's career ledger.</summary>
public sealed class FightRecord
{
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public int KnockoutWins { get; set; }
    public int KnockoutLosses { get; set; }

    public int Fights => Wins + Losses + Draws;

    public override string ToString() =>
        KnockoutWins > 0 ? $"{Wins}-{Losses}-{Draws} ({KnockoutWins} KO)"
                         : $"{Wins}-{Losses}-{Draws}";
}
