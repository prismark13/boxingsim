namespace BoxingSim.Core.Generation;

/// <summary>Generates plausible, varied boxer names from blended name pools.</summary>
public sealed class NameGenerator
{
    private readonly Random _rng;
    private readonly HashSet<string> _used = new();

    private static readonly string[] First =
    {
        "Marcus", "Tyrell", "Diego", "Aleksei", "Rashid", "Jorge", "Kwame", "Sonny",
        "Vince", "Hector", "Dmitri", "Liam", "Carlos", "Andre", "Bruno", "Kenji",
        "Omar", "Felix", "Roman", "Caleb", "Ivan", "Mateo", "Terrence", "Khalil",
        "Joaquin", "Bobby", "Emeka", "Nikolai", "Sergei", "Rafael", "Devon", "Hassan",
        "Luca", "Mickey", "Tariq", "Pavel", "Manny", "Cyrus", "Darnell", "Gennady",
        "Oscar", "Floyd", "Jermaine", "Pedro", "Kostya", "Yusuf", "Tomas", "Raheem"
    };

    private static readonly string[] Last =
    {
        "Cole", "Vasquez", "Okafor", "Petrov", "Nakamura", "Mendez", "Brennan", "Silva",
        "Adeyemi", "Kowalski", "Romano", "Boateng", "Castillo", "Donovan", "Reyes", "Kim",
        "Ortiz", "Hayes", "Volkov", "Santos", "Walsh", "Diallo", "Marchetti", "Park",
        "Guerrero", "Sullivan", "Abara", "Ivankov", "Delgado", "Quinn", "Osei", "Tanaka",
        "Ferreira", "McGrath", "Salazar", "Novak", "Rashidov", "Pryce", "Calderon", "Banks",
        "Moreau", "Hidalgo", "Stojanovic", "Acosta", "Whitfield", "Lozano", "Fitzgerald", "Aziz"
    };

    public NameGenerator(Random rng) => _rng = rng;

    public string Next()
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            var name = $"{First[_rng.Next(First.Length)]} {Last[_rng.Next(Last.Length)]}";
            if (_used.Add(name))
                return name;
        }
        // Pool exhausted for a unique full name — disambiguate with a "Jr."/suffix.
        var baseName = $"{First[_rng.Next(First.Length)]} {Last[_rng.Next(Last.Length)]}";
        var suffixed = $"{baseName} Jr.";
        int n = 2;
        while (!_used.Add(suffixed))
            suffixed = $"{baseName} {numeral(n++)}";
        return suffixed;
    }

    private static string numeral(int n) => n switch
    {
        2 => "II", 3 => "III", 4 => "IV", 5 => "V", _ => n.ToString()
    };
}
