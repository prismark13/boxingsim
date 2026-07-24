namespace BoxingSim.App;

/// <summary>Generates a plausible fighter name for a given country.</summary>
public static class NameGen
{
    public static string Generate(string? country, Random rng)
    {
        var (first, last) = Pool(country);
        return $"{first[rng.Next(first.Length)]} {last[rng.Next(last.Length)]}";
    }

    private static (string[] First, string[] Last) Pool(string? c) => c switch
    {
        "Mexico" or "Argentina" or "Cuba" => (Hispanic, HispanicLast),
        "Germany" => (German, GermanLast),
        "Russia" => (Russian, RussianLast),
        "Ukraine" => (Ukrainian, UkrainianLast),
        "Poland" => (Polish, PolishLast),
        "Italy" => (Italian, ItalianLast),
        "Nigeria" => (Nigerian, NigerianLast),
        _ => (Anglo, AngloLast)   // USA / England / Canada / default
    };

    private static readonly string[] Anglo = { "Tommy", "Jack", "Mike", "Frank", "Danny", "Joe", "Billy", "Marcus", "Tyrell", "Andre", "Charlie", "Dexter", "Lewis", "Ronnie", "Sonny", "Curtis" };
    private static readonly string[] AngloLast = { "Malone", "Doyle", "Carter", "Sullivan", "Mercer", "Brooks", "Hutchins", "Daniels", "Wallace", "Bennett", "Hayes", "Quinn", "Boyd", "Sutton", "Marsh", "Fox" };

    private static readonly string[] Hispanic = { "Carlos", "Juan", "Miguel", "Diego", "Hector", "Luis", "Ramon", "Alejandro", "Eduardo", "Rafael", "Javier", "Marco" };
    private static readonly string[] HispanicLast = { "Garcia", "Morales", "Ramirez", "Vargas", "Castillo", "Herrera", "Reyes", "Mendoza", "Salazar", "Ortega", "Delgado", "Cruz" };

    private static readonly string[] German = { "Hans", "Karl", "Max", "Stefan", "Andreas", "Jurgen", "Klaus", "Dieter", "Lukas", "Florian", "Marcel" };
    private static readonly string[] GermanLast = { "Schmidt", "Wagner", "Brandt", "Hoffmann", "Bauer", "Richter", "Vogel", "Keller", "Krause", "Lang", "Sommer" };

    private static readonly string[] Russian = { "Sergei", "Vladimir", "Dmitri", "Andrei", "Pavel", "Oleg", "Viktor", "Nikolai", "Roman", "Igor", "Maxim" };
    private static readonly string[] RussianLast = { "Volkov", "Kuznetsov", "Popov", "Sokolov", "Morozov", "Lebedev", "Orlov", "Smirnov", "Belov", "Fedorov" };

    private static readonly string[] Ukrainian = { "Oleksandr", "Vitali", "Taras", "Yuriy", "Andriy", "Bohdan", "Serhiy", "Denys", "Ruslan", "Pavlo" };
    private static readonly string[] UkrainianLast = { "Kovalenko", "Bondarenko", "Tkachenko", "Shevchenko", "Boyko", "Kravchuk", "Lysenko", "Marchenko", "Savchuk" };

    private static readonly string[] Polish = { "Tomasz", "Krzysztof", "Marek", "Andrzej", "Pawel", "Jacek", "Mariusz", "Wojciech", "Dariusz", "Grzegorz" };
    private static readonly string[] PolishLast = { "Kowalski", "Nowak", "Wojcik", "Kaminski", "Lewandowski", "Zielinski", "Szymanski", "Wozniak", "Dabrowski" };

    private static readonly string[] Italian = { "Marco", "Luca", "Giovanni", "Antonio", "Salvatore", "Vito", "Paolo", "Roberto", "Franco", "Bruno" };
    private static readonly string[] ItalianLast = { "Rossi", "Ferrari", "Romano", "Greco", "Marino", "Conti", "Esposito", "Bruno", "Gallo", "Costa" };

    private static readonly string[] Nigerian = { "Samuel", "Emeka", "Chidi", "Tunde", "Bola", "Ikenna", "Sunday", "Obi", "Femi", "Kelechi" };
    private static readonly string[] NigerianLast = { "Okafor", "Adeyemi", "Eze", "Balogun", "Okonkwo", "Nwosu", "Adebayo", "Okoro", "Olawale", "Uche" };
}
