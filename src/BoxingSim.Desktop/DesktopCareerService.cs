using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoxingSim.Core.Career;
using BoxingSim.Core.Engine;
using BoxingSim.Core.Model;
using BoxingSim.Core.Roster;

namespace BoxingSim.Desktop;

/// <summary>Holds the running career and persists it to a file under %AppData%. The web build keeps its save in
/// browser local storage; on the desktop it's a real file the player can back up, copy between machines, or keep
/// several of. The save format is identical, so a career moves between the two builds unchanged.</summary>
public sealed class DesktopCareerService
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false
    };

    public static string SaveDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BoxingSim");

    public static string SavePath { get; } = Path.Combine(SaveDirectory, "career.json");

    private IReadOnlyList<Boxer>? _roster;

    public CareerGame? Game { get; private set; }
    public FightResult? LastResult { get; private set; }
    public bool HasCareer => Game is not null;

    /// <summary>The universe, when one is running. A universe and a career are alternatives - one has a fighter
    /// to follow, the other has a whole sport and nobody in it - so only ever one is live.</summary>
    public Universe? Universe { get; private set; }
    public bool InUniverse => Universe is not null;

    /// <summary>Open a universe. A fresh copy of the roster each time, because the world ages what it is
    /// handed, and the career (if any) is put down first.</summary>
    public void StartUniverse(UniverseSettings settings)
    {
        Game = null;
        LastResult = null;
        Universe = new Universe(settings, Roster.ToList());
    }

    /// <summary>Close it, and put the process-wide mileage dials back so a career started afterwards behaves
    /// like a career again.</summary>
    public void EndUniverse()
    {
        Universe = null;
        BoxingSim.Core.Career.Universe.Release();
    }
    public static bool HasSave => File.Exists(SavePath);

    /// <summary>The real roster, read from the file shipped beside the executable.</summary>
    public IReadOnlyList<Boxer> Roster => _roster ??= LoadRoster();

    /// <summary>A loose data\fighters.json beside the executable wins, so the roster can be edited without a
    /// rebuild. Falling back to the copy embedded in the assembly is what lets a single-file publish be one
    /// genuinely standalone .exe.</summary>
    private static IReadOnlyList<Boxer> LoadRoster()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "fighters.json");
        string json;
        if (File.Exists(path))
        {
            json = File.ReadAllText(path);
        }
        else
        {
            using var s = typeof(DesktopCareerService).Assembly.GetManifestResourceStream("fighters.json")
                ?? throw new FileNotFoundException($"The fighter roster is missing — no file at {path} and none embedded.");
            using var r = new StreamReader(s);
            json = r.ReadToEnd();
        }
        var defs = JsonSerializer.Deserialize<List<FighterDefinition>>(json, Opts)
                   ?? throw new InvalidDataException("The fighter roster could not be read.");
        return RosterIo.DedupePeople(RosterIo.ToBoxers(defs));
    }

    /// <summary>Divisions with a real historical roster (12+ fighters) — the ones offered at career setup.</summary>
    public IReadOnlyList<WeightClass> AvailableDivisions() =>
        Roster.GroupBy(b => b.WeightClass)
              .Where(g => g.Count() >= 12)
              .Select(g => g.Key)
              .OrderByDescending(wc => (int)wc)
              .ToList();

    public void Start(string name, string country, int startYear, int potential, WeightClass division, bool fullHistory)
    {
        // A career never inherits a universe's dials.
        EndUniverse();
        var rng = new Random();
        var player = CareerGame.CreatePlayer(rng, name, country, division, potential);
        // A fresh copy each time: CareerGame ages and mutates the roster it is handed.
        Game = new CareerGame(startYear, player, Roster.ToList(), rng, division, seedHistory: fullHistory);
        LastResult = null;
        Save();
    }

    public void Take() { LastResult = Game?.TakeOffer(); Save(); }
    public void Decline() { Game?.DeclineOffer(); LastResult = null; Save(); }
    public void MoveUp() { Game?.MoveUp(); LastResult = null; Save(); }
    public void RelinquishWbc() { Game?.RelinquishWbc(); LastResult = null; Save(); }

    public void Abandon()
    {
        Game = null;
        LastResult = null;
        if (File.Exists(SavePath)) File.Delete(SavePath);
    }

    /// <summary>Auto-save after every turn. A failure here must never lose the turn — the fight has happened
    /// either way — so it is reported through <see cref="LastSaveError"/> rather than thrown.</summary>
    public string? LastSaveError { get; private set; }

    public void Save()
    {
        if (Game is null) return;
        try
        {
            Directory.CreateDirectory(SaveDirectory);
            // Write to a temporary file and swap it in, so a crash mid-write can't leave a half-written save
            // where a whole career used to be.
            var tmp = SavePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Game.ToSave(), Opts));
            File.Move(tmp, SavePath, overwrite: true);
            LastSaveError = null;
        }
        catch (Exception ex)
        {
            LastSaveError = ex.Message;
        }
    }

    public bool Load()
    {
        if (!File.Exists(SavePath)) return false;
        try
        {
            var save = JsonSerializer.Deserialize<CareerSave>(File.ReadAllText(SavePath), Opts);
            if (save is null) return false;
            Game = CareerGame.Load(save, new Random());
            LastResult = null;
            return true;
        }
        catch
        {
            // An incompatible or corrupt save shouldn't jam the app on "Continue". Keep the file — renamed, so
            // it isn't silently destroyed — and fall back to the create screen.
            try { File.Move(SavePath, SavePath + ".broken", overwrite: true); } catch { /* nothing more to do */ }
            Game = null;
            return false;
        }
    }
}
