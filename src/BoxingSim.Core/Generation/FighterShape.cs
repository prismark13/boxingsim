using BoxingSim.Core.Model;

namespace BoxingSim.Core.Generation;

/// <summary>How a fighter's attributes are laid out, in one place.
///
/// This existed twice. BoxerFactory built the sport's fighters and CareerGame.CreatePlayer built the
/// player, and the two were eleven near-identical lines in different files — the same attributes, in the
/// same order, with the same reasoning about which of them arrive with a man and which he has to build.
/// Only the numbers differed.
///
/// Twice now a change has been made to one and silently missed the other. Reach was given a fallback in
/// the model and the player kept getting zero. Stamina was given a young-age floor in the factory, the fix
/// was measured across 2,473 generated prospects, published, and every career still opened with STAMINA 1
/// — because the player is not built by the factory. The second one was caught by a screenshot, after
/// release.
///
/// So the shape lives here and the numbers are passed in. The callers keep the values they had, exactly:
/// this changed no fighter anywhere, which the golden master confirms. What it changes is that the next
/// person to alter how a young fighter is built cannot alter him for half the sport.</summary>
public static class FighterShape
{
    /// <summary>How far each attribute may scatter from the headline potential, so fighters come out as
    /// bangers, boxers, iron chins and glass jaws rather than ten copies of the same number.</summary>
    public readonly record struct Spreads(
        int Power, int Speed, int Defense, int Accuracy, int Stamina,
        int Conditioning, int Chin, int CutResistance, int Aggression, int Heart);

    /// <summary>What fraction of his ceiling a man has in each attribute at the very start of a career.
    ///
    /// This is the part that carries the meaning. A young fighter HAS his power, his legs, his engine and
    /// his chin — those are physical and they arrive with him. What he lacks is placement and ring craft,
    /// which is why accuracy sits lowest and defence not much above it. Anything absent from this list is
    /// innate and does not develop at all: cut resistance, aggression and heart are simply what he is.</summary>
    public readonly record struct Floors(
        double Power, double Speed, double Defense, double Accuracy,
        double Stamina, double Conditioning, double Chin,
        double DefenseWhenFading);

    /// <summary>Compose a set of ratings. The draw order matters and is fixed: any reordering changes every
    /// fighter the sim has ever generated from a given seed.</summary>
    public static Ratings Compose(Random rng, int potential, double dev, bool young, Spreads s, Floors f)
    {
        int Ceiling(int spread) => Ratings.Clamp(potential + rng.Next(-spread, spread + 1));
        static int Scale(int ceiling, double d) => Ratings.Clamp((int)Math.Round(ceiling * d));
        static double Lerp(double dev, double floor) => floor + (1.0 - floor) * dev;

        return new Ratings
        {
            Power = Scale(Ceiling(s.Power), young ? Lerp(dev, f.Power) : dev),
            Speed = Scale(Ceiling(s.Speed), young ? Lerp(dev, f.Speed) : dev),
            Defense = Scale(Ceiling(s.Defense), Lerp(dev, young ? f.Defense : f.DefenseWhenFading)),
            Accuracy = Scale(Ceiling(s.Accuracy), Lerp(dev, f.Accuracy)),
            Stamina = Scale(Ceiling(s.Stamina), young ? Lerp(dev, f.Stamina) : dev),
            Conditioning = Scale(Ceiling(s.Conditioning), young ? Lerp(dev, f.Conditioning) : dev),
            Chin = Scale(Ceiling(s.Chin), Lerp(dev, f.Chin)),
            CutResistance = Ceiling(s.CutResistance),
            Aggression = Ceiling(s.Aggression),
            Heart = Ceiling(s.Heart),
        };
    }

    /// <summary>An invented fighter: a wide scatter, because the sport needs bangers and stylists in it.</summary>
    public static readonly Spreads GeneratedSpreads = new(18, 14, 15, 14, 14, 14, 16, 20, 22, 18);
    public static readonly Floors GeneratedFloors = new(0.85, 0.82, 0.72, 0.50, 0.84, 0.75, 0.60, 0.40);

    /// <summary>The player: a tighter scatter, so the talent he was promised is the talent he gets.</summary>
    public static readonly Spreads PlayerSpreads = new(12, 12, 13, 12, 12, 12, 14, 18, 20, 16);
    public static readonly Floors PlayerFloors = new(0.85, 0.82, 0.72, 0.58, 0.84, 0.75, 0.70, 0.40);
}
