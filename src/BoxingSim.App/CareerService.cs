using System.Text.Json;
using System.Text.Json.Serialization;
using BoxingSim.Core.Career;
using BoxingSim.Core.Engine;
using BoxingSim.Core.Model;
using Microsoft.JSInterop;

namespace BoxingSim.App;

/// <summary>Holds the running career and persists it to browser local storage (auto-saved each turn).</summary>
public sealed class CareerService
{
    private const string Key = "boxingsim.career.v1";
    private static readonly JsonSerializerOptions Opts = new()
    {
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly FightersService _fighters;
    private readonly IJSRuntime _js;

    public CareerService(FightersService fighters, IJSRuntime js)
    {
        _fighters = fighters;
        _js = js;
    }

    public CareerGame? Game { get; private set; }
    public FightResult? LastResult { get; private set; }
    public bool HasCareer => Game is not null;

    public async Task StartAsync(string name, string country, int startYear, int potential, WeightClass division, bool fullHistory = false)
    {
        var roster = await _fighters.GetAsync();
        var rng = new Random();
        var player = CareerGame.CreatePlayer(rng, name, country, division, potential);
        Game = new CareerGame(startYear, player, roster, rng, division, seedHistory: fullHistory);
        LastResult = null;
        await SaveAsync();
    }

    /// <summary>Divisions that have a real historical roster (≥12 fighters) — the ones offered in career setup.</summary>
    public async Task<IReadOnlyList<WeightClass>> AvailableDivisionsAsync()
    {
        var roster = await _fighters.GetAsync();
        return roster.GroupBy(b => b.WeightClass)
                     .Where(g => g.Count() >= 12)
                     .Select(g => g.Key)
                     .OrderByDescending(wc => (int)wc)
                     .ToList();
    }

    public async Task TakeAsync()
    {
        LastResult = Game?.TakeOffer();
        await SaveAsync();
    }

    public async Task DeclineAsync()
    {
        Game?.DeclineOffer();
        LastResult = null;
        await SaveAsync();
    }

    public async Task RelinquishWbcAsync()
    {
        Game?.RelinquishWbc();
        LastResult = null;
        await SaveAsync();
    }

    public async Task MoveUpAsync()
    {
        Game?.MoveUp();
        LastResult = null;
        await SaveAsync();
    }

    public async Task AbandonAsync()
    {
        Game = null;
        LastResult = null;
        await _js.InvokeVoidAsync("localStorage.removeItem", Key);
    }

    public async Task SaveAsync()
    {
        if (Game is null) return;
        // A save must never break the turn (e.g. a localStorage quota error) — the fight has already happened.
        try
        {
            var json = JsonSerializer.Serialize(Game.ToSave(), Opts);
            await _js.InvokeVoidAsync("localStorage.setItem", Key, json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Career auto-save failed: {ex.Message}");
        }
    }

    public async Task<bool> HasSaveAsync()
    {
        var json = await _js.InvokeAsync<string?>("localStorage.getItem", Key);
        return !string.IsNullOrEmpty(json);
    }

    public async Task<bool> LoadAsync()
    {
        var json = await _js.InvokeAsync<string?>("localStorage.getItem", Key);
        if (string.IsNullOrEmpty(json)) return false;
        var save = JsonSerializer.Deserialize<CareerSave>(json, Opts);
        if (save is null) return false;
        Game = CareerGame.Load(save, new Random());
        LastResult = null;
        return true;
    }
}
