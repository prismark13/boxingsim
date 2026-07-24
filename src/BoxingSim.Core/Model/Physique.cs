namespace BoxingSim.Core.Model;

/// <summary>Physical measurements that aren't skills. Reach is a frame trait, not a rating — it never
/// feeds Overall/Class, it only creates a matchup effect in the ring (the longer man controls distance).
/// The historical roster has no measured reach, so we synthesise a believable one deterministically from
/// the fighter's frame (his top campaigning weight) plus a stable per-name spread — so it never changes
/// between sessions or saves, and future rosters can still override it explicitly.</summary>
public static class Physique
{
    /// <summary>Average pro reach in inches by division — lighter men are shorter-armed, heavies rangy.</summary>
    private static double BaseReach(WeightClass wc) => wc switch
    {
        WeightClass.Flyweight => 63.0,
        WeightClass.Bantamweight => 64.5,
        WeightClass.Featherweight => 66.0,
        WeightClass.Lightweight => 68.0,
        WeightClass.LightWelterweight => 69.5,
        WeightClass.Welterweight => 71.5,
        WeightClass.LightMiddleweight => 72.5,
        WeightClass.Middleweight => 73.0,
        WeightClass.LightHeavyweight => 75.5,
        WeightClass.Cruiserweight => 77.5,
        WeightClass.Heavyweight => 79.0,
        _ => 70.0
    };

    /// <summary>A stable, run-independent hash of a string (String.GetHashCode is randomised per process).</summary>
    private static int StableHash(string s)
    {
        unchecked
        {
            int h = 17;
            foreach (char c in s) h = h * 31 + c;
            return h & 0x7fffffff;
        }
    }

    /// <summary>A plausible reach in inches for a fighter of the given frame (use his top campaigning weight),
    /// spread ~[-4.5, +5.5]" around the divisional average and stable for a given name.</summary>
    public static int ReachInchesFor(WeightClass frame, string name)
    {
        double offset = StableHash(name) % 1001 / 1000.0 * 10.0 - 4.5;   // -4.5 .. +5.5
        return (int)Math.Round(BaseReach(frame) + offset);
    }
}
