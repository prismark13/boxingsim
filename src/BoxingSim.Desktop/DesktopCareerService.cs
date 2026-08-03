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

    public static string SaveDirectory { get; } = ResolveSaveDirectory();

    /// <summary>Where the save lives, moving it out of the old BoxingSim folder if it is still there.
    ///
    /// The app was called BoxingSim until the Store submission; anyone who played it before that has a career
    /// under the old name, and a rename that simply pointed somewhere new would leave it stranded — the game
    /// would open on an empty title screen and the career would look lost. So the first run under the new name
    /// carries the old folder across.
    ///
    /// Move rather than copy, so there is exactly one save and no chance of editing the wrong one. If the move
    /// fails for any reason — the folder open in Explorer, the file locked, permissions — keep using the OLD
    /// directory instead of starting empty beside a career we could not reach. A player would far rather have
    /// their fighter under an out-of-date folder name than not at all.</summary>
    private static string ResolveSaveDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string home = Path.Combine(appData, "The Final Bell");
        string old = Path.Combine(appData, "BoxingSim");

        if (Directory.Exists(home) || !Directory.Exists(old)) return home;

        try { Directory.Move(old, home); }
        catch (IOException) { return old; }
        catch (UnauthorizedAccessException) { return old; }
        return home;
    }

    /// <summary>How many careers can be kept at once.</summary>
    public const int Slots = 3;

    /// <summary>Where a slot lives. Slot 1 is career.json — the name it has always had — so an existing
    /// career becomes slot 1 on first run rather than disappearing behind a numbering scheme invented after
    /// it was saved. Someone with a career in progress should not be able to tell this feature arrived.</summary>
    public static string PathOf(int slot) =>
        Path.Combine(SaveDirectory, slot <= 1 ? "career.json" : $"career-{slot}.json");

    /// <summary>The slot being played, and therefore the one every auto-save after a turn writes to.</summary>
    public int ActiveSlot { get; private set; } = 1;

    public static string SavePath => PathOf(1);

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
    public static bool HasSave => Enumerable.Range(1, Slots).Any(HasSaveIn);
    public static bool HasSaveIn(int slot) => File.Exists(PathOf(slot));

    /// <summary>What is in a slot, without loading it into play: the fighter, his record, and where he had got
    /// to. Three files of two megabytes each are read to build the menu, which is slower than a flag would be
    /// and worth it — a row of numbered slots tells you nothing, and picking the wrong one costs a career.
    ///
    /// Everything is read defensively. A slot that cannot be understood is reported as occupied-but-unreadable
    /// rather than thrown away or shown as empty: an empty slot invites starting a new career on top of a save
    /// that might only need a fix.</summary>
    public static SlotInfo Peek(int slot)
    {
        var path = PathOf(slot);
        if (!File.Exists(path)) return new SlotInfo(slot, false, "Empty", "", "", false);
        try
        {
            var save = JsonSerializer.Deserialize<CareerSave>(File.ReadAllText(path), Opts);
            var me = save?.Roster.FirstOrDefault(b => b.Id == save.PlayerId);
            if (save is null || me is null) return new SlotInfo(slot, true, "Damaged save", "", "", true);
            string when = DateOnly.TryParse(save.Date, out var d) ? d.ToString("MMMM yyyy") : "";
            string record = $"{me.Wins}-{me.Losses}-{me.Draws}";
            return new SlotInfo(slot, true, me.Name, record, $"{save.Division}  ·  {when}", false);
        }
        catch
        {
            return new SlotInfo(slot, true, "Damaged save", "", "", true);
        }
    }

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

    /// <param name="startAge">The age he turns pro at, 18 to 21. It is not cosmetic: every career stage is
    /// measured in bouts, but the years still count against the age his prime ends by — so a man who debuts
    /// at twenty-one reaches that limit three years sooner in fights he has not had. Late starters trade
    /// runway for nothing, which is the trade a real late starter makes.</param>
    public void Start(string name, string country, int startYear, int potential, WeightClass division, bool fullHistory, int slot = 1, int startAge = 18)
    {
        ActiveSlot = Math.Clamp(slot, 1, Slots);
        // A career never inherits a universe's dials.
        EndUniverse();
        var rng = new Random();
        var player = CareerGame.CreatePlayer(rng, name, country, division, potential, Math.Clamp(startAge, 18, 21));
        // A fresh copy each time: CareerGame ages and mutates the roster it is handed.
        Game = new CareerGame(startYear, player, Roster.ToList(), rng, division, seedHistory: fullHistory);
        LastResult = null;
        Save();
    }

    public void Take() { LastResult = Game?.TakeOffer(); Save(); }
    public void Decline() { Game?.DeclineOffer(); LastResult = null; Save(); }
    public void MoveUp() { Game?.MoveUp(); LastResult = null; Save(); }
    public void RelinquishWbc() { Game?.RelinquishWbc(); LastResult = null; Save(); }

    /// <summary>Throw away the career in a slot. Deliberately takes the slot rather than assuming the active
    /// one: the setup screen deletes a save it is not playing.</summary>
    public void AbandonSlot(int slot)
    {
        if (slot == ActiveSlot) { Game = null; LastResult = null; }
        var p = PathOf(slot);
        if (File.Exists(p)) File.Delete(p);
    }

    public void Abandon()
    {
        Game = null;
        LastResult = null;
        if (File.Exists(PathOf(ActiveSlot))) File.Delete(PathOf(ActiveSlot));
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
            var path = PathOf(ActiveSlot);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Game.ToSave(), Opts));
            File.Move(tmp, path, overwrite: true);
            LastSaveError = null;
        }
        catch (Exception ex)
        {
            LastSaveError = ex.Message;
        }
    }

    public bool Load(int slot = 1)
    {
        ActiveSlot = Math.Clamp(slot, 1, Slots);
        var loadPath = PathOf(ActiveSlot);
        if (!File.Exists(loadPath)) return false;
        try
        {
            var save = JsonSerializer.Deserialize<CareerSave>(File.ReadAllText(loadPath), Opts);
            if (save is null) return false;
            Game = CareerGame.Load(save, new Random());
            LastResult = null;
            return true;
        }
        catch
        {
            // An incompatible or corrupt save shouldn't jam the app on "Continue". Keep the file — renamed, so
            // it isn't silently destroyed — and fall back to the create screen.
            try { File.Move(loadPath, loadPath + ".broken", overwrite: true); } catch { /* nothing more to do */ }
            Game = null;
            return false;
        }
    }
}
