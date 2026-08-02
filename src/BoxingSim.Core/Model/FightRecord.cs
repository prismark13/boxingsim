namespace BoxingSim.Core.Model;

/// <summary>A fighter's career ledger.</summary>
public sealed class FightRecord
{
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }

    /// <summary>Clean knockouts — the man was counted out.</summary>
    public int KnockoutWins { get; set; }
    public int KnockoutLosses { get; set; }

    /// <summary>Stoppages that were not knockouts: the referee stepped in, or a cut ended it. A cut IS a
    /// technical knockout and belongs here — it is not a knockout, and it never was decided by punching
    /// power. Both used to be summed into the KO column, which is why fighters who could not crack an egg
    /// carried gaudy KO tallies: the cut stoppage reads the LOSER'S cut resistance and nothing else.</summary>
    public int TechnicalKnockoutWins { get; set; }
    public int TechnicalKnockoutLosses { get; set; }

    /// <summary>Every win inside the distance, however it came.</summary>
    public int StoppageWins => KnockoutWins + TechnicalKnockoutWins;

    /// <summary>Every time he failed to hear the final bell. What "he has been stopped a lot" means, and
    /// what the wear-and-tear and retirement rules ask for — being pulled out on a cut counts.</summary>
    public int StoppageLosses => KnockoutLosses + TechnicalKnockoutLosses;

    public int Fights => Wins + Losses + Draws;

    public override string ToString()
    {
        string wld = $"{Wins}-{Losses}-{Draws}";
        if (KnockoutWins == 0 && TechnicalKnockoutWins == 0) return wld;
        if (TechnicalKnockoutWins == 0) return $"{wld} ({KnockoutWins} KO)";
        if (KnockoutWins == 0) return $"{wld} ({TechnicalKnockoutWins} TKO)";
        return $"{wld} ({KnockoutWins} KO / {TechnicalKnockoutWins} TKO)";
    }
}
