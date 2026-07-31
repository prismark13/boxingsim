using BoxingSim.Core.Model;

namespace BoxingSim.Core.Career;

/// <summary>Who is on the shelf, and until when.
///
/// A knockout means a medical suspension, and so can a bad cut or a hand — a suspended man must not be
/// matched, so every matchmaker in the sim asks here before it considers anybody. The layoff dates are held
/// nowhere else: the rest of the world can book a suspension and ask whether a man is fit, and cannot reach
/// the dates to read or edit them directly.
///
/// It is handed the clock rather than a date, because "fit again" is a question about TODAY and the world
/// moves the calendar underneath it.</summary>
internal sealed class MedicalRoom
{
    private readonly Func<DateOnly> _today;
    private readonly Dictionary<int, DateOnly> _outUntil = new();   // fighter id → date he's fit again after an injury (KO layoff)

    public MedicalRoom(Func<DateOnly> today) => _today = today;

    /// <summary>True if a fighter is fit to be matched — not currently on the shelf recovering from an injury.</summary>
    public bool Available(Boxer b) => !_outUntil.TryGetValue(b.Id, out var d) || _today() >= d;

    /// <summary>Rule a fighter out until a given date. A fresh injury replaces whatever he was already carrying.</summary>
    public void Suspend(int fighterId, DateOnly until) => _outUntil[fighterId] = until;

    /// <summary>How well a fighter weathers punishment — the injury model's stand-in for a durable frame.</summary>
    public static int Durability(Ratings r) => (int)Math.Round(r.Chin * 0.5 + r.Heart * 0.3 + r.Conditioning * 0.2);
}
