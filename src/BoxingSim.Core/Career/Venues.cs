namespace BoxingSim.Core.Career;

/// <summary>What kind of night it is.
///
/// Every fight in the sim happened in the same nowhere, on a card of no particular size, and a man's
/// twelfth six-rounder was staged exactly like a world title fight. Boxing does not work that way: there is
/// a ladder of shows, and where you are on it says as much about you as your record does.</summary>
public enum CardTier
{
    /// <summary>A small hall on a wet Tuesday. Four- and six-rounders, a couple of hundred people, no
    /// television. This is where almost every career starts and where most of them stay.</summary>
    ClubShow,
    /// <summary>A proper local promotion — a regional title on top, a hall that sells out, the local paper
    /// there. A good night for a fighter who is going somewhere.</summary>
    Regional,
    /// <summary>A national card: a ranked contender headlining or an eliminator on top, and the country
    /// watching.</summary>
    National,
    /// <summary>A world title on the night. The building is full and everyone on the card knows it.</summary>
    Championship,
}

/// <summary>Where a man is on the card.
///
/// This is the part that makes a prospect feel like a prospect: you do not headline the Garden at 6-0, you
/// open it. Being third from the top on a championship bill is a bigger night than headlining a club show,
/// and both should read differently.</summary>
public enum Billing
{
    Opener,
    SwingBout,
    ChiefSupport,
    MainEvent,
}

public static class Bills
{
    public static string Describe(this CardTier t) => t switch
    {
        CardTier.ClubShow => "club show",
        CardTier.Regional => "regional card",
        CardTier.National => "national card",
        _ => "championship bill",
    };

    public static string Describe(this Billing b) => b switch
    {
        Billing.Opener => "opening bout",
        Billing.SwingBout => "swing bout",
        Billing.ChiefSupport => "chief support",
        _ => "main event",
    };
}

/// <summary>A real hall, and when it was one.
///
/// <paramref name="Top"/> is the biggest kind of night the place holds: York Hall has never staged a world
/// title and Madison Square Garden has never needed to stage a four-rounder in front of two hundred people.
/// <paramref name="From"/> and <paramref name="To"/> bound the years it was actually in use for boxing, so a
/// 1955 card cannot be at Caesars Palace and a 1990 one cannot be at the old Madison Square Garden.</summary>
public sealed record Venue(string Name, string City, string Country, CardTier Top, int From = 0, int To = 9999)
{
    public string Full => $"{Name}, {City}";
}

/// <summary>The halls the sport is actually staged in, chosen for the country, the year and the size of the
/// night. Not a complete list of the world's boxing venues — a list of the ones a card of each size would
/// plausibly be held in, which is what the sim needs.</summary>
public static class VenueBook
{
    private static readonly Venue[] All =
    {
        // ---- United States ----
        new("Madison Square Garden", "New York", "USA", CardTier.Championship),
        new("Yankee Stadium", "New York", "USA", CardTier.Championship, 1923, 1976),
        new("The Polo Grounds", "New York", "USA", CardTier.Championship, 1920, 1963),
        new("St. Nicholas Arena", "New York", "USA", CardTier.ClubShow, 1906, 1962),
        new("Sunnyside Garden", "New York", "USA", CardTier.ClubShow, 1945, 1977),
        new("Felt Forum", "New York", "USA", CardTier.Regional, 1968),
        new("Caesars Palace", "Las Vegas", "USA", CardTier.Championship, 1970),
        new("The Sands", "Las Vegas", "USA", CardTier.National, 1952, 1996),
        new("The Forum", "Inglewood", "USA", CardTier.Championship, 1967),
        new("Olympic Auditorium", "Los Angeles", "USA", CardTier.Regional, 1925, 2005),
        new("Hollywood Legion Stadium", "Los Angeles", "USA", CardTier.ClubShow, 1920, 1966),
        new("The Blue Horizon", "Philadelphia", "USA", CardTier.ClubShow, 1961),
        new("The Arena", "Philadelphia", "USA", CardTier.Regional, 1920, 1977),
        new("Boardwalk Hall", "Atlantic City", "USA", CardTier.Championship, 1980),
        new("Chicago Stadium", "Chicago", "USA", CardTier.National, 1929, 1994),
        new("Marigold Gardens", "Chicago", "USA", CardTier.ClubShow, 1920, 1963),
        new("Cow Palace", "San Francisco", "USA", CardTier.National, 1941),
        new("The Astrodome", "Houston", "USA", CardTier.Championship, 1965),
        new("Miami Beach Auditorium", "Miami", "USA", CardTier.National, 1950, 1990),
        new("Boston Garden", "Boston", "USA", CardTier.National, 1928, 1995),
        new("Kezar Pavilion", "San Francisco", "USA", CardTier.ClubShow),
        new("Civic Auditorium", "Baltimore", "USA", CardTier.ClubShow),

        // ---- Britain, Ireland and the Commonwealth ----
        new("York Hall", "Bethnal Green", "England", CardTier.Regional, 1929),
        new("Royal Albert Hall", "London", "England", CardTier.National),
        new("Wembley Arena", "London", "England", CardTier.Championship, 1934),
        new("Wembley Stadium", "London", "England", CardTier.Championship),
        new("Earls Court", "London", "England", CardTier.National, 1937, 2014),
        new("Harringay Arena", "London", "England", CardTier.National, 1936, 1958),
        new("The National Sporting Club", "London", "England", CardTier.ClubShow),
        new("Belle Vue", "Manchester", "England", CardTier.Regional, 1920, 1981),
        new("Free Trade Hall", "Manchester", "England", CardTier.Regional, 1920, 1996),
        new("Anfield", "Liverpool", "England", CardTier.National),
        new("St Andrew's", "Birmingham", "England", CardTier.National),
        new("The Ulster Hall", "Belfast", "Ireland", CardTier.Regional),
        new("The National Stadium", "Dublin", "Ireland", CardTier.National, 1939),
        new("Kelvin Hall", "Glasgow", "England", CardTier.National, 1927, 1985),
        new("Festival Hall", "Melbourne", "Australia", CardTier.National, 1955),
        new("Sydney Stadium", "Sydney", "Australia", CardTier.National, 1908, 1970),
        new("Rand Stadium", "Johannesburg", "South Africa", CardTier.National),
        new("The Wembley Arena", "Johannesburg", "South Africa", CardTier.Regional, 1940),
        new("National Stadium", "Kingston", "Jamaica", CardTier.National),
        new("Accra Sports Stadium", "Accra", "Ghana", CardTier.National, 1952),
        new("The Trade Fair Centre", "Accra", "Ghana", CardTier.Regional, 1967),
        new("National Stadium", "Lagos", "Nigeria", CardTier.National, 1930),

        // ---- Latin America ----
        new("Arena Mexico", "Mexico City", "Mexico", CardTier.Championship, 1956),
        new("Arena Coliseo", "Mexico City", "Mexico", CardTier.Regional, 1943),
        new("Plaza de Toros", "Tijuana", "Mexico", CardTier.National),
        new("Auditorio Municipal", "Tijuana", "Mexico", CardTier.ClubShow),
        new("Estadio Azteca", "Mexico City", "Mexico", CardTier.Championship, 1966),
        new("Luna Park", "Buenos Aires", "Argentina", CardTier.Championship, 1932),
        new("Estadio Luis Ramallo", "Buenos Aires", "Argentina", CardTier.Regional),
        new("Roberto Clemente Coliseum", "San Juan", "Puerto Rico", CardTier.Championship, 1973),
        new("Hiram Bithorn Stadium", "San Juan", "Puerto Rico", CardTier.National, 1962),
        new("Sixto Escobar Stadium", "San Juan", "Puerto Rico", CardTier.National, 1935),
        new("Gimnasio Nuevo Panama", "Panama City", "Panama", CardTier.National),
        new("Gimnasio Kid Pambele", "Cartagena", "Colombia", CardTier.Regional),
        new("Poliedro de Caracas", "Caracas", "Venezuela", CardTier.National, 1974),
        new("El Nuevo Circo", "Caracas", "Venezuela", CardTier.Regional),
        new("Gimnasio Kid Chocolate", "Havana", "Cuba", CardTier.National),
        new("Ginasio do Ibirapuera", "Sao Paulo", "Brazil", CardTier.National, 1957),
        new("Maracanazinho", "Rio de Janeiro", "Brazil", CardTier.National, 1954),
        new("Maple Leaf Gardens", "Toronto", "Canada", CardTier.National, 1931, 1999),
        new("The Forum", "Montreal", "Canada", CardTier.Championship, 1924, 1996),

        // ---- Continental Europe ----
        new("Palazzo dello Sport", "Rome", "Italy", CardTier.Championship, 1960),
        new("San Siro", "Milan", "Italy", CardTier.Championship),
        new("Palalido", "Milan", "Italy", CardTier.Regional, 1961),
        new("Westfalenhalle", "Dortmund", "Germany", CardTier.Championship, 1952),
        new("Olympiahalle", "Munich", "Germany", CardTier.Championship, 1972),
        new("Ernst-Merck-Halle", "Hamburg", "Germany", CardTier.National, 1950, 2004),
        new("Palais des Sports", "Paris", "France", CardTier.Championship),
        new("Cirque d'Hiver", "Paris", "France", CardTier.ClubShow),
        new("Palacio de Deportes", "Madrid", "Spain", CardTier.National, 1960),
        new("Plaza de Toros de Las Ventas", "Madrid", "Spain", CardTier.National),
        new("Luzhniki", "Moscow", "Russia", CardTier.Championship, 1956),
        new("Palace of Sports", "Kyiv", "Ukraine", CardTier.National, 1960),
        new("Torwar Hall", "Warsaw", "Poland", CardTier.National, 1953),
        new("Johanneshovs Isstadion", "Stockholm", "Sweden", CardTier.National, 1955),
        new("Rasunda Stadium", "Stockholm", "Sweden", CardTier.Championship, 1937, 2012),
        new("Almaty Arena", "Almaty", "Kazakhstan", CardTier.National, 1990),
    };

    /// <summary>A hall for this night: in his country if the sport has one there, the right size for the
    /// card, and open for business in the year. Falls back outward — the right country at the wrong size,
    /// then the sport's default homes — rather than ever coming back empty.</summary>
    public static Venue Pick(string? country, CardTier tier, int year, Random rng)
    {
        bool Open(Venue v) => year >= v.From && year <= v.To;

        // The right size in the right country: a club show wants a small hall, not an empty stadium.
        var exact = All.Where(v => v.Country == country && v.Top == tier && Open(v)).ToList();
        if (exact.Count > 0) return exact[rng.Next(exact.Count)];

        // A hall big enough, in his country. Better a big room half full than the wrong city.
        var bigEnough = All.Where(v => v.Country == country && v.Top >= tier && Open(v)).ToList();
        if (bigEnough.Count > 0) return bigEnough[rng.Next(bigEnough.Count)];

        // His country has nothing on that scale — he is boxing abroad, which is what actually happens to a
        // fighter from a small scene.
        var anywhere = All.Where(v => v.Top == tier && Open(v)).ToList();
        if (anywhere.Count == 0) anywhere = All.Where(v => v.Top >= tier && Open(v)).ToList();
        if (anywhere.Count == 0) anywhere = All.Where(Open).ToList();
        return anywhere.Count > 0 ? anywhere[rng.Next(anywhere.Count)] : All[0];
    }
}
