using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoxingSim.Core.Model;
using BoxingSim.Core.Roster;

namespace BoxingSim.App;

/// <summary>Loads the heavyweight roster (static JSON) and builds live <see cref="Boxer"/> objects.</summary>
public sealed class FightersService
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _http;
    private List<Boxer>? _cache;

    public FightersService(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<Boxer>> GetAsync()
    {
        if (_cache is not null) return _cache;
        var defs = await _http.GetFromJsonAsync<List<FighterDefinition>>("data/fighters.json", Opts) ?? new();
        _cache = RosterIo.ToBoxers(defs);
        _cache.Sort((a, b) => b.Overall.CompareTo(a.Overall));
        return _cache;
    }
}
