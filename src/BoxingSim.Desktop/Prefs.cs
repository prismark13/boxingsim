using System.IO;
using System.Text.Json;

namespace BoxingSim.Desktop;

/// <summary>How the player wants the game to present itself, kept beside the save rather than inside it.
///
/// A preference belongs to the person, not the fighter. Putting these in the career file would mean
/// abandoning a career and starting another turned the sound back on and reinstated a build-up the player
/// had already decided they did not want; it would also leave universe mode with no settings at all, since
/// a universe has no career file.
///
/// It also gives the sound toggle and the playback speed somewhere to live. Both were held in memory only
/// and reset to their defaults on every launch, so turning the sound off was a decision you had to make
/// again every single session.</summary>
public sealed class Prefs
{
    /// <summary>Run the weeks between taking a fight and the opening bell, with the sport happening as they
    /// pass. Off means a fight starts the moment it is accepted, which is how it worked before.</summary>
    public bool FightWeek { get; set; } = true;

    /// <summary>Fight the undercard in front of the player on the night, one bout at a time, before his own
    /// walk-out. Off means the card is a list he reads and the main event starts immediately.</summary>
    public bool LiveUndercard { get; set; } = true;

    /// <summary>Wait for a press between each week of the build-up and each bout on the undercard, instead of
    /// running on a timer. On by default: a build-up that advances itself is something you watch, and the point
    /// of putting the weeks on screen was to let them be read.</summary>
    public bool StepByStep { get; set; } = true;

    /// <summary>How much of the sport the build-up feed reports: Titles, Normal or Detailed. Remembered,
    /// because it is a standing preference about how you like to follow boxing and not a per-fight choice.</summary>
    public string CampDetail { get; set; } = "Normal";

    /// <summary>Narrow the feed to the player's own weight, on top of whichever level is chosen. Independent
    /// of the level: a man can want titles only, and only in his own division.</summary>
    public bool CampMineOnly { get; set; }

    public bool SoundOn { get; set; } = true;

    /// <summary>Playback speed multiplier. Only the three the UI offers are meaningful, but it is stored as
    /// the number so a hand-edited file can ask for anything.</summary>
    public double Speed { get; set; } = 1.0;

    private static string FilePath =>
        Path.Combine(DesktopCareerService.SaveDirectory, "settings.json");

    /// <summary>Read them back, falling back to the defaults on anything at all going wrong. A settings file
    /// that cannot be parsed must not stop the game opening — the worst case is one session of defaults.</summary>
    public static Prefs Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Prefs>(File.ReadAllText(FilePath)) ?? new Prefs();
        }
        catch (IOException) { }
        catch (JsonException) { }
        catch (UnauthorizedAccessException) { }
        return new Prefs();
    }

    /// <summary>Written on every change, because there is no "apply" button and a preference the player set
    /// and then lost to a crash is worse than one that costs a few bytes of disk each time it is toggled.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DesktopCareerService.SaveDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
}
