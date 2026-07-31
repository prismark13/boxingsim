namespace BoxingSim.Core.Model;

/// <summary>How a set of judges' cards is written into a fight ledger, and read back out of it.
///
/// The format was written in one place and parsed in two, and one of the parsers disagreed with the writer:
/// the ledger split on the separator actually used, while the playback parser split on a comma, which nothing
/// has ever written. So every stored decision read back as no cards at all — watch an old fight again and it
/// reached the verdict with the scoring simply missing, on exactly the fights where the cards are the point.
///
/// One format needs one owner. Writing and reading live together here so neither can drift from the other.</summary>
public static class ScoreCards
{
    private const string Separator = " · ";   // " · "

    public static string Write(IEnumerable<(int A, int B)> cards) =>
        string.Join(Separator, cards.Select(c => $"{c.A}-{c.B}"));

    /// <summary>Read the cards back, from the owning fighter's point of view. Tolerant of the comma an older
    /// save or a hand-written roster may carry, and of either dash — a reader should not be the thing that
    /// loses a fight's scoring.</summary>
    public static IReadOnlyList<(int A, int B)> Read(string? cards)
    {
        if (string.IsNullOrWhiteSpace(cards)) return Array.Empty<(int, int)>();
        var outp = new List<(int, int)>();
        foreach (var part in cards.Split(new[] { '·', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var bits = part.Trim().Split('-', '–');
            if (bits.Length == 2 && int.TryParse(bits[0], out int a) && int.TryParse(bits[1], out int b))
                outp.Add((a, b));
        }
        return outp;
    }
}
