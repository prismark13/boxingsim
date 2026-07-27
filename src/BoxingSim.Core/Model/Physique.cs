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

    /// <summary>Average pro height in inches by division, tracking reach closely — a rangy man is usually a tall
    /// one. Like reach this is synthesised from the frame plus a stable per-name spread, so it never moves
    /// between sessions and needs nothing stored.</summary>
    private static double BaseHeight(WeightClass wc) => BaseReach(wc) - 1.5;

    public static int HeightInchesFor(WeightClass wc, string name)
    {
        // Reuse the same stable spread as reach but on its own axis, so a long-armed man isn't automatically
        // the tallest — the two correlate without being locked together.
        int h = StableHash(name + "|h");
        double spread = ((h & 0xFF) / 255.0 - 0.5) * 5.0;   // about +/- 2.5 inches
        return (int)Math.Round(BaseHeight(wc) + spread);
    }

    /// <summary>Whether a fighter comes out sharp or takes his time: -1 slow starter, 0 even, +1 fast starter.
    /// A real trait — a slow starter genuinely gives early rounds away and comes on late, which is why the
    /// engine reads it per round rather than as a flat rating.</summary>
    public static int StartSpeedFor(string name)
    {
        int h = StableHash(name + "|start") & 0xFFFF;
        int bucket = h % 100;
        return bucket < 25 ? 1 : bucket < 50 ? -1 : 0;   // a quarter each way, half of them even
    }

    public static string StartSpeedLabel(int startSpeed) =>
        startSpeed > 0 ? "fast starter" : startSpeed < 0 ? "slow starter" : "even pace";
}
