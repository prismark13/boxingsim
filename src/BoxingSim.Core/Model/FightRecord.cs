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

    /// <summary>One figure, the way a record is actually written: "48-0-0 (32 KO)".
    ///
    /// The two columns were shown apart for a version — "(30 KO / 14 TKO)" — and it reads as clutter on a
    /// list of forty fighters. Every record in the sport is published this way, KOs and stoppages in one
    /// number, and a reader takes "KO" to mean "did not hear the final bell".
    ///
    /// The SPLIT IS KEPT, because it is the honest data and something real depends on it: a cut stoppage is
    /// a technical knockout and belongs nowhere near a measure of punching power. Anything asking what a man
    /// can do to people should read KnockoutWins; this is the headline, and the headline is the total.</summary>
    public override string ToString() =>
        StoppageWins > 0 ? $"{Wins}-{Losses}-{Draws} ({StoppageWins} KO)"
                         : $"{Wins}-{Losses}-{Draws}";
}
