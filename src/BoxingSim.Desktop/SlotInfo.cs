namespace BoxingSim.Desktop;

/// <summary>What is in a save slot, as the title screen needs to show it.
///
/// A career is fifteen in-game years of somebody's time, and three numbered buttons give a player no way to
/// tell which one is his. So a slot says who is in it, what his record is, and how far he had got — enough to
/// recognise a career you put down a month ago without loading it to find out.</summary>
/// <param name="Slot">1-based, matching the file on disk: slot 1 is career.json.</param>
/// <param name="Occupied">Whether there is a save here at all.</param>
/// <param name="Fighter">His name, "Empty", or "Damaged save".</param>
/// <param name="Record">"12-1-0", or empty for a slot with nobody in it.</param>
/// <param name="Where">Division and the month he had reached.</param>
/// <param name="Damaged">A save that exists but could not be read. Offered for deletion, never overwritten
/// silently — a file that fails to parse today may be recoverable, and a career is not worth guessing about.</param>
public sealed record SlotInfo(int Slot, bool Occupied, string Fighter, string Record, string Where, bool Damaged)
{
    /// <summary>Whether the title screen is pointed at this slot. Set by the shell when the choice moves —
    /// the service that reads the disk has no business knowing which one is highlighted.</summary>
    public bool IsSelected { get; set; }

    public string Title => $"SLOT {Slot}";
    public bool IsEmpty => !Occupied;

    /// <summary>What the button under the slot says. "Continue" and "New career" are different promises and
    /// must never be the same word on a slot that already holds somebody.</summary>
    public string ActionLabel => Damaged ? "Delete" : Occupied ? "Continue" : "New career";

    /// <summary>The line under the name. An empty slot says what it is for rather than nothing at all.</summary>
    public string Detail => Damaged ? "This save could not be read"
                          : Occupied ? $"{Record}  ·  {Where}"
                          : "No career here yet";
}
