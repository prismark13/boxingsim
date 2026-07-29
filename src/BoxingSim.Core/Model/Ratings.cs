namespace BoxingSim.Core.Model;

/// <summary>
/// A pragmatic, Title-Bout-inspired rating set. Every attribute is on a 1..100 scale
/// where higher is always better for the fighter who owns it.
/// </summary>
public sealed class Ratings
{
    /// <summary>How much damage punches do; drives knockdowns and KOs.</summary>
    public int Power { get; set; }

    /// <summary>Resistance to being hurt and knocked down. The classic "chin".</summary>
    public int Chin { get; set; }

    /// <summary>Hand and foot speed; helps land first and avoid being hit.</summary>
    public int Speed { get; set; }

    /// <summary>Slipping, blocking and ring craft; reduces opponent connect rate.</summary>
    public int Defense { get; set; }

    /// <summary>Endurance. High stamina fighters keep output up in the late rounds.</summary>
    public int Stamina { get; set; }

    /// <summary>Connect rate and clean punching.</summary>
    public int Accuracy { get; set; }

    /// <summary>Recovery between rounds and getting up from knockdowns.</summary>
    public int Conditioning { get; set; }

    /// <summary>Resistance to cuts and swelling (higher = cuts less easily).</summary>
    public int CutResistance { get; set; }

    /// <summary>Punch volume / willingness to engage.</summary>
    public int Aggression { get; set; }

    /// <summary>Performance when hurt or behind. Champions dig deep.</summary>
    public int Heart { get; set; }

    /// <summary>A single headline number, weighted toward what wins fights.</summary>
    public int Overall
    {
        get
        {
            // Weighted toward what defines heavyweight greatness: dominance (power) and boxing
            // skill (accuracy, defense, speed). Durability (chin/stamina/conditioning) and heart
            // matter but are deliberately light so a tough gatekeeper's iron chin can't carry him
            // into the all-time elite.
            double raw =
                Power * 0.21 +
                Accuracy * 0.15 +
                Defense * 0.14 +
                Speed * 0.12 +
                Chin * 0.09 +
                Stamina * 0.07 +
                Aggression * 0.06 +
                Heart * 0.06 +
                Conditioning * 0.06 +
                CutResistance * 0.04;

            // Map the raw score onto career-achievement tiers via piecewise-linear anchors:
            //   90-99 all-time great · 80-89 world champion · 70-79 contender · 60-69 national · 50-59 journeyman.
            return Math.Clamp((int)Math.Round(TierCurve(raw)), 1, 99);
        }
    }

    /// <summary>
    /// (raw, OVR) anchor points defining the rating tiers. OVR interpolates linearly between
    /// anchors, so each raw band lands in the intended career tier. Tuned to the heavyweight roster.
    /// </summary>
    private static readonly (double Raw, double Ovr)[] TierAnchors =
    {
        (60, 50),   // journeyman floor
        (66, 60),   // national/regional belt class
        (72, 70),   // world-title contender
        (78, 80),   // world champion level
        (83, 90),   // all-time great
        (87.4, 99), // greatest ever
    };

    private static double TierCurve(double raw)
    {
        var a = TierAnchors;
        if (raw <= a[0].Raw) return a[0].Ovr + (raw - a[0].Raw) * 1.667; // below the journeyman floor
        for (int i = 1; i < a.Length; i++)
            if (raw <= a[i].Raw)
            {
                var (r0, o0) = a[i - 1];
                var (r1, o1) = a[i];
                return o0 + (raw - r0) * (o1 - o0) / (r1 - r0);
            }
        var top = a[^1];
        return top.Ovr + (raw - top.Raw) * 2.0; // above the top anchor (clamps at 99)
    }

    /// <summary>The weighted raw score (uncompressed) that <see cref="Overall"/> is derived from.
    /// Unlike Overall it does not saturate, so it still separates the all-time greats from each other.</summary>
    public double RawScore =>
        Power * 0.21 + Accuracy * 0.15 + Defense * 0.14 + Speed * 0.12 + Chin * 0.09 +
        Stamina * 0.07 + Aggression * 0.06 + Heart * 0.06 + Conditioning * 0.06 + CutResistance * 0.04;

    /// <summary>The headline "class" on a 1–15 scale, and what each band is meant to MEAN:
    ///
    ///   13–15   all-time greats
    ///   10–12   multiple world champions
    ///    7–9    champion calibre
    ///    4–6    contenders and gatekeepers
    ///    1–3    journeymen and opponents
    ///
    /// The old floors did not say that. They put 43% of the roster in the champion-calibre band, made Nino
    /// Benvenuti and Bob Foster all-time greats, and had Willie Pep — who lost eleven fights in 241 — down
    /// among the multiple-champions at 11 while George Chuvalo, who never won a title, sat at 9.
    ///
    /// Recalibrated by reading the bands off men whose standing is not in dispute. The roster now comes out
    /// 2.0% all-time great, 9.1% multiple champion, 13.6% champion calibre, 54.3% contender or gatekeeper,
    /// 20.9% journeyman — bearing in mind it is a curated set of notable fighters, so it skews high; a
    /// generated division sits far lower.</summary>
    public int Class => ClassFromRaw(RawScore);

    // Absolute raw-score thresholds (min raw for each class). Fixed, not percentile, so "15" means the same
    // thing — an all-time-great ceiling — no matter how many divisions or fighters are added later.
    private static readonly double[] ClassFloors =
    {   // index 0 => class 1 ... index 14 => class 15
        0.0, 58.0, 64.0, 68.0, 72.0, 76.0, 78.5, 80.2, 81.5, 82.8, 84.0, 85.2, 87.0, 89.0, 90.5
    };

    public static int ClassFromRaw(double raw)
    {
        for (int c = ClassFloors.Length - 1; c >= 0; c--)
            if (raw >= ClassFloors[c]) return c + 1;
        return 1;
    }

    // Base KO rate from the winner's power alone (vs a nominal ~75 chin, even fight), calibrated to the roster:
    // median power (~75) stops ~40%, the p95 punchers (~88) ~65%, and the all-time bangers (Foreman/Wilder/Tyson,
    // rated 96–99) clear 90%.
    private static readonly (int P, double Ko)[] KoAnchors =
    {
        (50, 0.06), (60, 0.11), (70, 0.18), (78, 0.28), (85, 0.39), (90, 0.50), (94, 0.64), (96, 0.75), (99, 0.88)
    };

    /// <summary>The chance a win comes by knockout: driven mainly by the winner's power, sharpened by the gap to
    /// the loser's chin, and — crucially — by how badly he outclasses the loser, since a mismatch ends in a
    /// stoppage far more often than an even fight. <paramref name="skillGap"/> is winner Overall − loser Overall
    /// (only a positive edge helps; an upset winner gets no bonus). An all-time banger clears 90%, a big puncher
    /// stops well over half, a light-hitting boxer sits around 15–20% — and any of them stops a lesser man more.</summary>
    public static double KnockoutChance(int power, int chin, double skillGap = 0)
    {
        var a = KoAnchors;
        double baseKo = a[^1].Ko;
        if (power <= a[0].P) baseKo = a[0].Ko;
        else for (int i = 1; i < a.Length; i++)
            if (power <= a[i].P) { var (p0, k0) = a[i - 1]; baseKo = k0 + (power - p0) * (a[i].Ko - k0) / (a[i].P - p0); break; }

        // A weaker chin and a mismatch both mean more stoppages, but the mismatch term used to be unbounded:
        // it simply divided the rating gap by 130 and added it. That was survivable while every generated
        // fighter sat within twenty points of every other, and stopped being survivable the moment the
        // generated population was allowed to span the whole ladder — gaps of forty and fifty appeared, the
        // term added a third of a probability on its own, and the sport went from 44.8% of bouts ending
        // inside the distance to 58.6%. Real boxing runs 35-45%.
        //
        // So the mismatch still matters and still has the larger say, but it saturates: past about twenty
        // points of difference a man is already being outclassed, and being outclassed by forty does not
        // double it. The chin term is unchanged.
        double mismatch = 0.18 * (1 - Math.Exp(-Math.Max(0, skillGap) / 16.0));
        double adj = (75 - chin) / 280.0 + mismatch;
        return Math.Clamp(baseKo + adj, 0.05, 0.97);
    }

    // Attribute display floors (min 1–99 value for each 1–15 level). Real fighters' attributes cluster in
    // the 55–90 band, so a naive linear 1–99→1–15 map wastes the bottom third and bunches everyone at 11–12.
    // These floors are calibrated to the actual roster: a typical pro attribute (~72) shows ~7–8, weak
    // attributes drop to 2–4, and only the all-time extremes (Foreman/Hearns power, etc.) reach 14–15.
    private static readonly int[] AttrFloors =
    {   // index 0 => 1 ... index 14 => 15
        0, 48, 54, 59, 63, 67, 70, 73, 76, 79, 82, 85, 88, 92, 96
    };

    /// <summary>Map a single 1–99 attribute onto the 1–15 display scale, calibrated to the real spread
    /// so most fighters sit mid-scale and the top end stays rare.</summary>
    public static int Scale15(int attr)
    {
        for (int c = AttrFloors.Length - 1; c >= 0; c--)
            if (attr >= AttrFloors[c]) return c + 1;
        return 1;
    }

    public Ratings Clone() => (Ratings)MemberwiseClone();

    public static int Clamp(int v) => Math.Clamp(v, 1, 100);
}
