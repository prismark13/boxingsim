namespace BoxingSim.Core.Generation;

/// <summary>Generates plausible, varied boxer names from blended name pools.</summary>
public sealed class NameGenerator
{
    private readonly Random _rng;
    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] First =
    {
        // Anglo / American
        "Tommy", "Jack", "Mickey", "Frankie", "Danny", "Joey", "Billy", "Sonny", "Bobby", "Charlie",
        "Ronnie", "Curtis", "Dexter", "Lewis", "Marcus", "Andre", "Devon", "Darnell", "Terrence", "Jermaine",
        "Floyd", "Deontay", "Errol", "Caleb", "Shawn", "Virgil", "Buster", "Rocky", "Vinny", "Gene",
        "Wesley", "Roy", "Cornelius", "Emanuel", "Tyrell", "Rashad", "Malik", "Isiah", "Demetrius", "Keith",
        "Lamont", "Terrell", "Micky", "Sean", "Declan", "Liam", "Cormac", "Marvin", "Reggie", "Cody",
        // Hispanic
        "Carlos", "Juan", "Miguel", "Diego", "Hector", "Luis", "Ramon", "Alejandro", "Eduardo", "Rafael",
        "Javier", "Julio", "Cesar", "Erik", "Saul", "Oscar", "Pedro", "Jorge", "Manny", "Gabriel",
        "Roberto", "Salvador", "Antonio", "Fernando", "Ricardo", "Joaquin", "Rey", "Israel", "Leo", "Guillermo",
        "Orlando", "Mateo", "Marco",
        // Italian
        "Luca", "Giovanni", "Vito", "Paolo", "Bruno", "Franco", "Rocco", "Salvatore", "Enzo", "Dario", "Nino",
        // Slavic / Eastern European
        "Aleksei", "Dmitri", "Ivan", "Sergei", "Nikolai", "Pavel", "Roman", "Vitali", "Oleksandr", "Kostya",
        "Gennady", "Ruslan", "Artur", "Tomasz", "Krzysztof", "Marek", "Andrei", "Igor", "Viktor", "Maxim",
        "Denys", "Taras",
        // African
        "Kwame", "Emeka", "Chidi", "Tunde", "Ikenna", "Obi", "Femi", "Kelechi", "Samuel", "Azumah", "Ike",
        // East Asian
        "Kenji", "Takeshi", "Hiroto", "Ryo", "Naoya", "Shinsuke", "Daigo", "Jin", "Sung",
        // Arab / Middle Eastern / Turkish
        "Omar", "Rashid", "Tariq", "Hassan", "Yusuf", "Khalil", "Amir", "Karim", "Bilal", "Nabil",
        "Cyrus", "Felix"
    };

    private static readonly string[] Last =
    {
        // Anglo
        "Cole", "Brennan", "Donovan", "Hayes", "Walsh", "Sullivan", "Quinn", "McGrath", "Fitzgerald", "Whitfield",
        "Banks", "Pryce", "Doyle", "Carter", "Mercer", "Brooks", "Hutchins", "Daniels", "Wallace", "Bennett",
        "Boyd", "Sutton", "Marsh", "Fox", "Malone", "Dalton", "Hendrix", "Cross", "Gaines", "Rhodes",
        "Steele", "Vaughn", "Archer", "Ford", "Barnes", "Hollis", "Nash", "Reed", "Tate", "Sharpe",
        // Hispanic
        "Vasquez", "Mendez", "Castillo", "Reyes", "Guerrero", "Delgado", "Salazar", "Calderon", "Acosta", "Lozano",
        "Hidalgo", "Santos", "Garcia", "Morales", "Ramirez", "Vargas", "Herrera", "Mendoza", "Ortega", "Cruz",
        "Rivera", "Torres", "Marquez", "Barrera", "Chavez", "Nunez", "Fuentes", "Aguilar", "Rojas", "Cordova",
        // Italian
        "Romano", "Marchetti", "Ferrari", "Rossi", "Greco", "Marino", "Conti", "Esposito", "Gallo", "Costa",
        "Serra", "Moretti", "Bianchi",
        // Slavic
        "Petrov", "Volkov", "Ivankov", "Novak", "Kowalski", "Stojanovic", "Kuznetsov", "Popov", "Sokolov", "Morozov",
        "Lebedev", "Orlov", "Smirnov", "Fedorov", "Kovalenko", "Bondarenko", "Shevchenko", "Boyko", "Wojcik", "Kaminski",
        // African
        "Okafor", "Adeyemi", "Boateng", "Diallo", "Osei", "Abara", "Eze", "Balogun", "Okonkwo", "Nwosu",
        "Adebayo", "Okoro", "Mensah", "Owusu", "Dlamini", "Ndlovu",
        // East Asian
        "Nakamura", "Tanaka", "Sato", "Suzuki", "Yamamoto", "Ito", "Watanabe", "Kim", "Park", "Choi",
        // Arab / Turkish / French / Portuguese
        "Aziz", "Rashidov", "Haddad", "Nasser", "Demir", "Yilmaz", "Kaya", "Mansour",
        "Moreau", "Dubois", "Laurent", "Girard", "Lemaire",
        "Ferreira", "Silva", "Almeida", "Cardoso"
    };

    public NameGenerator(Random rng) => _rng = rng;

    /// <summary>Reserve names so they are never generated — e.g. the real historical roster, so a
    /// filler fighter can't be born as a second "Carlos Ortiz".</summary>
    public void Reserve(IEnumerable<string> names)
    {
        foreach (var n in names)
            if (!string.IsNullOrWhiteSpace(n)) _used.Add(n);
    }

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
