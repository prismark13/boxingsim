using System.Text.Json;
using BoxingSim.Core.Model;

namespace BoxingSim.Core.Roster;

/// <summary>Loads and saves decks of fighter cards as JSON.</summary>
public static class RosterIo
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static List<FighterDefinition> Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<FighterDefinition>>(json, Options)
               ?? new List<FighterDefinition>();
    }

    public static void Save(string path, IEnumerable<FighterDefinition> fighters)
    {
        var json = JsonSerializer.Serialize(fighters.ToList(), Options);
        File.WriteAllText(path, json);
    }

    /// <summary>Build live <see cref="Boxer"/> instances from a deck of cards.</summary>
    public static List<Boxer> ToBoxers(IEnumerable<FighterDefinition> defs)
    {
        int id = 1;
        return defs.Select(d => d.ToBoxer(id++)).ToList();
    }

    /// <summary>
    /// Upgrade any deck fighter that matches a curated library entry by name: the library's
    /// hand-tuned ratings/identity replace the auto-rated ones, while the deck keeps its own
    /// record and age (so peak/as-faced records from research are preserved). This is what makes
    /// network-building generic — research auto-rates everyone, the library polishes the greats.
    /// </summary>
    public static int ApplyOverrides(List<FighterDefinition> deck, IReadOnlyList<FighterDefinition> library)
    {
        var byKey = new Dictionary<string, FighterDefinition>();
        foreach (var o in library) byKey[NameKey(o.Name)] = o; // last one wins on dup

        int upgraded = 0;
        foreach (var f in deck)
        {
            if (!byKey.TryGetValue(NameKey(f.Name), out var ov)) continue;
            upgraded++;
            f.AutoRate = false;
            f.Power = ov.Power; f.Chin = ov.Chin; f.Speed = ov.Speed; f.Defense = ov.Defense;
            f.Stamina = ov.Stamina; f.Accuracy = ov.Accuracy; f.Conditioning = ov.Conditioning;
            f.CutResistance = ov.CutResistance; f.Aggression = ov.Aggression; f.Heart = ov.Heart;
            f.Nickname ??= ov.Nickname;
            f.Style ??= ov.Style;
            f.PrimeYears ??= ov.PrimeYears;
            f.Country ??= ov.Country;
            f.Titles ??= ov.Titles;
            f.DebutYear ??= ov.DebutYear;
        }
        return upgraded;
    }

    /// <summary>
    /// Collapse a deck to one card per fighter (matched by name), keeping the strongest version —
    /// highest Overall, then a dated card, then the fuller record. Lets you merge era decks into one.
    /// </summary>
    public static List<Boxer> DedupeByName(IEnumerable<Boxer> boxers) =>
        boxers.GroupBy(b => NameKey(b.Name))
              .Select(g => g.OrderByDescending(b => b.Overall)
                            .ThenByDescending(b => b.DateOfBirth != null)
                            .ThenByDescending(b => b.Record.Wins)
                            .First())
              .ToList();

    /// <summary>Match key from a name's first + last word, ignoring quotes/punctuation/middle nicknames AND
    /// folding accents so "José Nápoles" and "Jose Napoles" key the same.</summary>
    public static string NameKey(string name)
    {
        var folded = new string(name.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray());
        var words = new string(folded.Select(c => char.IsLetterOrDigit(c) || c == ' ' ? c : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "";
        if (words.Length == 1) return words[0].ToLowerInvariant();
        return (words[0] + words[^1]).ToLowerInvariant();
    }

    /// <summary>Merge genuine duplicate entries of the SAME real fighter — same name (accents/middle names folded
    /// to a first+last key) AND the same date of birth — keeping his lower (primary) division. The DOB match is
    /// what makes it safe: it never merges two different men who merely share a first and last name (John David
    /// Jackson vs John Jackson), and entries without a DOB are left untouched.</summary>
    public static List<Boxer> DedupePeople(IEnumerable<Boxer> boxers)
    {
        var result = new List<Boxer>();
        foreach (var g in boxers.GroupBy(b => (NameKey(b.Name), b.DateOfBirth ?? "")))
        {
            if (string.IsNullOrEmpty(g.Key.Item2) || g.Count() == 1)
                result.AddRange(g);
            else
                result.Add(g.OrderBy(b => (int)b.WeightClass).ThenByDescending(b => b.Overall).First());
        }
        return result;
    }
}
