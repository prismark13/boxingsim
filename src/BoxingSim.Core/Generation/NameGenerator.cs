namespace BoxingSim.Core.Generation;

/// <summary>Names for invented fighters, drawn to match where a man is from and when he was born.
///
/// This used to hold one flat list of first names and one flat list of surnames and pick from each at
/// random, with the fighter's country chosen separately and independently. The results were men called
/// Tomasz Ramirez fighting out of Argentina and Chidi Reed out of Poland - every name a coin-flip across
/// nine cultures with no relationship to the flag beside it. There was no notion of era at all, so a man
/// turning professional in 1930 drew from the same list as one turning professional in 2010.
///
/// Names now come from the country. Each culture has its own surnames and its own first names split across
/// three eras, because what a boxer is called moves with the decades: the 1930s are full of Alberts and
/// Cecils, the 1970s of Waynes and Darrens, and neither sounds like the other. The pools are also far
/// larger - about 25 times the distinct combinations - so a universe running a century stops recycling the
/// same two dozen surnames.</summary>
public sealed class NameGenerator
{
    private readonly Random _rng;
    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);

    public NameGenerator(Random rng) => _rng = rng;

    /// <summary>Reserve names so they are never generated - e.g. the real historical roster, so a
    /// filler fighter can't be born as a second "Carlos Ortiz".</summary>
    public void Reserve(IEnumerable<string> names)
    {
        foreach (var n in names)
            if (!string.IsNullOrWhiteSpace(n)) _used.Add(n);
    }

    /// <summary>The cultures the pools are organised by. Every country the sim uses maps to one.</summary>
    private enum Culture { American, British, Irish, Hispanic, Brazilian, Italian, German, French, Slavic, WestAfrican, Nordic, Caribbean }

    private static Culture Of(string? country) => country switch
    {
        "England" or "Australia" or "South Africa" => Culture.British,
        "Ireland" => Culture.Irish,
        "Mexico" or "Cuba" or "Argentina" or "Puerto Rico" or "Panama" or "Venezuela" or "Colombia" or "Spain" => Culture.Hispanic,
        "Brazil" => Culture.Brazilian,
        "Italy" => Culture.Italian,
        "Germany" => Culture.German,
        "France" => Culture.French,
        "Russia" or "Ukraine" or "Poland" or "Kazakhstan" => Culture.Slavic,
        "Nigeria" or "Ghana" => Culture.WestAfrican,
        "Sweden" => Culture.Nordic,
        "Jamaica" => Culture.Caribbean,
        _ => Culture.American,
    };

    /// <summary>Which of the three name eras a birth year falls in. Banded by BIRTH, not by when he fights:
    /// a man boxing in 1975 was named in the 1940s.</summary>
    private static int Era(int birthYear) => birthYear <= 0 ? 1 : birthYear < 1935 ? 0 : birthYear < 1970 ? 1 : 2;

    /// <summary>A name for a fighter from <paramref name="country"/>, born about <paramref name="birthYear"/>.
    /// Zero for the year draws from the middle era.</summary>
    public string Next(string? country = null, int birthYear = 0)
    {
        var c = Of(country);
        var first = First[c][Era(birthYear)];
        var last = Last[c];

        for (int attempt = 0; attempt < 60; attempt++)
        {
            var name = $"{first[_rng.Next(first.Length)]} {last[_rng.Next(last.Length)]}";
            if (_used.Add(name)) return name;
        }
        // This culture and era are exhausted for a unique full name. Disambiguate rather than reach into
        // another country's pool, which is the thing this class exists to stop.
        var baseName = $"{first[_rng.Next(first.Length)]} {last[_rng.Next(last.Length)]}";
        var suffixed = $"{baseName} Jr.";
        int n = 2;
        while (!_used.Add(suffixed))
            suffixed = $"{baseName} {Numeral(n++)}";
        return suffixed;
    }

    private static string Numeral(int n) => n switch
    {
        2 => "II", 3 => "III", 4 => "IV", 5 => "V", _ => n.ToString()
    };

    // ---- first names: [culture][era], era 0 = born before 1935, 1 = 1935-69, 2 = 1970 on ----

    private static readonly Dictionary<Culture, string[][]> First = new()
    {
        [Culture.American] = new[]
        {
            new[]
            {
                "Albert", "Cecil", "Clarence", "Curtis", "Earl", "Elmer", "Ernie", "Floyd", "Harold", "Herman",
                "Homer", "Horace", "Howard", "Jesse", "Joe", "Leroy", "Lester", "Lloyd", "Marvin", "Melvin",
                "Mickey", "Milton", "Orville", "Otis", "Ralph", "Ray", "Roscoe", "Rudy", "Sonny", "Sylvester",
                "Vernon", "Walter", "Wesley", "Willie", "Bud", "Chester", "Dewey", "Emmett", "Grover", "Roland"
            },
            new[]
            {
                "Andre", "Anthony", "Barry", "Bernard", "Bobby", "Brian", "Bruce", "Calvin", "Carl", "Charlie",
                "Chuck", "Danny", "Darnell", "Dennis", "Duane", "Eddie", "Frankie", "Gary", "Gene", "Gregory",
                "Jerry", "Jimmy", "Johnny", "Keith", "Kenny", "Larry", "Lamont", "Leon", "Marcus", "Michael", "Nate",
                "Randy", "Reggie", "Ricky", "Ronnie", "Terry", "Tommy", "Tyrone", "Vinny", "Warren", "Wayne",
                "Dwight", "Rodney", "Stanley", "Aaron", "Alonzo", "Clifford", "Maurice", "Isiah", "Buster"
            },
            new[]
            {
                "Antonio", "Austin", "Brandon", "Bryant", "Cameron", "Chris", "Cody", "Cole", "Corey", "Darius",
                "Demetrius", "Derek", "Deontay", "Devon", "Dominic", "Dustin", "Elijah", "Eric", "Ethan", "Gabriel",
                "Isaiah", "Jamal", "Jared", "Jason", "Javon", "Jermaine", "Justin", "Kevin", "Kyle", "Lamar",
                "Malik", "Marquis", "Nathan", "Quincy", "Rashad", "Shawn", "Terrell", "Travis", "Trevor", "Tyler",
                "Tyrell", "Xavier", "Zachary", "Damon", "Julian", "Keon", "Rico", "Shane", "Trey", "Caleb"
            },
        },
        [Culture.British] = new[]
        {
            new[]
            {
                "Albert", "Alf", "Arthur", "Bert", "Cecil", "Charlie", "Cyril", "Eric", "Ernest", "Frank", "Fred",
                "George", "Gordon", "Harold", "Harry", "Horace", "Jack", "Leonard", "Norman", "Percy", "Reg",
                "Ronald", "Sid", "Stanley", "Sydney", "Ted", "Tom", "Walter", "Wilfred", "Bill"
            },
            new[]
            {
                "Alan", "Barry", "Billy", "Brian", "Colin", "Dave", "Dennis", "Derek", "Eddie", "Gary", "Graham",
                "Ian", "Jeff", "Jim", "John", "Keith", "Ken", "Kevin", "Malcolm", "Martin", "Micky", "Neil", "Nigel",
                "Paul", "Pete", "Phil", "Ray", "Roy", "Steve", "Terry", "Tony", "Trevor", "Wally", "Clive", "Des",
                "Errol", "Herol", "Lloyd", "Maurice", "Vic"
            },
            new[]
            {
                "Adam", "Anthony", "Ben", "Callum", "Carl", "Chris", "Craig", "Curtis", "Daniel", "Danny", "Darren",
                "Dean", "Frankie", "Gavin", "Jamie", "Joe", "Jordan", "Josh", "Kell", "Lee", "Liam", "Luke",
                "Marcus", "Mark", "Matthew", "Nathan", "Ricky", "Ryan", "Scott", "Sean", "Simon", "Stuart", "Tommy",
                "Wayne", "Ashley", "Conor", "Dillian", "Jake", "Leon", "Tyson"
            },
        },
        [Culture.Irish] = new[]
        {
            new[]
            {
                "Bernard", "Cormac", "Declan", "Eamon", "Fergus", "Gerard", "Hugh", "Kevin", "Liam", "Michael",
                "Niall", "Padraig", "Patrick", "Seamus", "Sean", "Thomas", "Brendan", "Colm", "Donal", "Malachy"
            },
            new[]
            {
                "Barry", "Brendan", "Charlie", "Ciaran", "Dermot", "Eamonn", "Fintan", "Gerry", "Jimmy", "Joe",
                "Kieran", "Mick", "Noel", "Paddy", "Ruairi", "Shay", "Steve", "Tommy", "Wayne", "Christy"
            },
            new[]
            {
                "Aidan", "Andy", "Carl", "Cathal", "Conor", "Darren", "Eoin", "Gary", "Jamie", "Luke", "Michael",
                "Oisin", "Paddy", "Ronan", "Ryan", "Sean", "Stephen", "Tyrone", "Jason", "Dean"
            },
        },
        [Culture.Hispanic] = new[]
        {
            new[]
            {
                "Alfonso", "Alfredo", "Antonio", "Arturo", "Benito", "Carlos", "Cesar", "Domingo", "Eduardo",
                "Emilio", "Enrique", "Ernesto", "Federico", "Felipe", "Fernando", "Francisco", "Gregorio",
                "Guillermo", "Hector", "Ignacio", "Jesus", "Joaquin", "Jorge", "Jose", "Juan", "Julio", "Lorenzo",
                "Luis", "Manuel", "Mariano", "Miguel", "Pablo", "Pedro", "Rafael", "Ramon", "Raul", "Ricardo",
                "Roberto", "Simon", "Tomas"
            },
            new[]
            {
                "Adolfo", "Alberto", "Alejandro", "Andres", "Angel", "Armando", "Bernardo", "Daniel", "Eusebio",
                "Fidel", "Gabriel", "Gerardo", "Gilberto", "Gustavo", "Hugo", "Humberto", "Israel", "Javier", "Lupe",
                "Marco", "Mario", "Martin", "Nicolino", "Orlando", "Oscar", "Pipino", "Ramiro", "Reynaldo",
                "Rodolfo", "Ruben", "Salvador", "Sergio", "Vicente", "Wilfredo", "Baltazar", "Efren", "Jaime",
                "Mando", "Rigoberto", "Ubaldo"
            },
            new[]
            {
                "Abner", "Adrian", "Alexis", "Brandon", "Bryan", "Christian", "Diego", "Edgar", "Emanuel", "Erik",
                "Ivan", "Jhonny", "Leo", "Marcos", "Mauricio", "Moises", "Nonito", "Omar", "Rey", "Roman", "Saul",
                "Sebastian", "Ulises", "Victor", "Yuriorkis", "Cristian", "Elias", "Isaac", "Josue", "Kevin",
                "Luis Alberto", "Juan Manuel", "Julio Cesar", "Miguel Angel", "Jose Luis", "Angel", "Fernando",
                "Gilberto", "Rafael", "Eduardo"
            },
        },
        [Culture.Brazilian] = new[]
        {
            new[]
            {
                "Adalberto", "Alcides", "Amilcar", "Anibal", "Benedito", "Custodio", "Djalma", "Edgar", "Eleuterio",
                "Gentil", "Heitor", "Jayme", "Joao", "Jose", "Manoel", "Nelson", "Osvaldo", "Raimundo", "Sebastiao",
                "Waldemar"
            },
            new[]
            {
                "Adilson", "Antonio", "Carlos", "Cassio", "Edinho", "Eder", "Everaldo", "Francisco", "Gilberto",
                "Jofre", "Juarez", "Luiz", "Marcelo", "Mauricio", "Milton", "Paulo", "Ricardo", "Roberto",
                "Servilio", "Wilson"
            },
            new[]
            {
                "Alan", "Anderson", "Bruno", "Cleiton", "Diego", "Douglas", "Emerson", "Esquiva", "Fabio", "Felipe",
                "Gabriel", "Hebert", "Igor", "Jonas", "Leandro", "Lucas", "Marcos", "Michel", "Patrick", "Rafael",
                "Robson", "Rodrigo", "Thiago", "Vinicius", "Yuri", "Adriano", "Caio", "Everton", "Juliano",
                "Wanderley"
            },
        },
        [Culture.Italian] = new[]
        {
            new[]
            {
                "Achille", "Alfredo", "Aldo", "Amleto", "Angelo", "Antonio", "Bruno", "Carlo", "Cesare", "Domenico",
                "Enrico", "Ettore", "Ferdinando", "Giovanni", "Giuseppe", "Guido", "Italo", "Leone", "Luigi",
                "Mario", "Michele", "Nello", "Orlando", "Primo", "Renato", "Roberto", "Salvatore", "Silvio",
                "Umberto", "Vittorio"
            },
            new[]
            {
                "Alessandro", "Andrea", "Bruno", "Carmelo", "Duilio", "Emiliano", "Fabio", "Franco", "Gianfranco",
                "Giulio", "Loris", "Luciano", "Marcello", "Massimo", "Mauro", "Nino", "Patrizio", "Piero", "Rocco",
                "Sandro", "Sergio", "Silvano", "Tiberio", "Ugo", "Vito", "Dario", "Elio", "Fulvio", "Giacomo",
                "Natale"
            },
            new[]
            {
                "Alessio", "Christian", "Cristian", "Daniele", "Davide", "Emanuele", "Fabrizio", "Federico",
                "Francesco", "Gabriele", "Giovanni", "Ivan", "Leonardo", "Lorenzo", "Luca", "Marco", "Matteo",
                "Michele", "Nicola", "Paolo", "Riccardo", "Roberto", "Simone", "Stefano", "Tommaso", "Valerio",
                "Vincenzo", "Alberto", "Andrea", "Diego"
            },
        },
        [Culture.German] = new[]
        {
            new[]
            {
                "Albert", "Anton", "Bruno", "Ernst", "Franz", "Friedrich", "Fritz", "Georg", "Gustav", "Hans",
                "Heinrich", "Helmut", "Hermann", "Johann", "Josef", "Karl", "Kurt", "Ludwig", "Max", "Otto", "Paul",
                "Richard", "Rudolf", "Walter", "Werner", "Wilhelm", "Willi", "Erich", "Gerhard", "Ewald"
            },
            new[]
            {
                "Andreas", "Bernd", "Dieter", "Detlef", "Frank", "Gunter", "Hartmut", "Horst", "Jurgen", "Klaus",
                "Lothar", "Manfred", "Norbert", "Peter", "Rainer", "Rolf", "Siegfried", "Uwe", "Volker", "Wolfgang",
                "Axel", "Bernhard", "Dirk", "Gerd", "Hubert", "Joachim", "Ulrich", "Karl-Heinz", "Reiner", "Eckhard"
            },
            new[]
            {
                "Alexander", "Christian", "Daniel", "Dennis", "Dominic", "Felix", "Florian", "Jan", "Jens", "Julian",
                "Kevin", "Lars", "Leon", "Luca", "Marco", "Markus", "Martin", "Matthias", "Michael", "Nico",
                "Patrick", "Philipp", "Robert", "Sebastian", "Stefan", "Sven", "Thomas", "Tim", "Tobias", "Vincent"
            },
        },
        [Culture.French] = new[]
        {
            new[]
            {
                "Andre", "Auguste", "Charles", "Emile", "Eugene", "Fernand", "Gaston", "Georges", "Henri", "Jules",
                "Leon", "Louis", "Lucien", "Marcel", "Maurice", "Paul", "Pierre", "Raymond", "Rene", "Robert",
                "Roger", "Albert", "Camille", "Edouard", "Gustave", "Jean", "Justin", "Marius", "Victor", "Yves"
            },
            new[]
            {
                "Alain", "Bernard", "Christian", "Claude", "Daniel", "Denis", "Didier", "Dominique", "Eric",
                "Francis", "Gilbert", "Gilles", "Guy", "Herve", "Jacky", "Michel", "Patrick", "Philippe", "Serge",
                "Thierry", "Yannick", "Bruno", "Fabrice", "Gerard", "Laurent", "Olivier", "Pascal", "Jean-Claude",
                "Jean-Pierre", "Rene"
            },
            new[]
            {
                "Alexandre", "Anthony", "Baptiste", "Cedric", "Cyril", "David", "Fabien", "Florian", "Gaetan",
                "Hugo", "Jeremy", "Julien", "Kevin", "Lucas", "Ludovic", "Mathieu", "Maxime", "Nicolas", "Nordine",
                "Romain", "Sebastien", "Souleymane", "Stephane", "Sylvain", "Thomas", "Tony", "Vincent", "Yoan",
                "Karim", "Mehdi"
            },
        },
        [Culture.Slavic] = new[]
        {
            new[]
            {
                "Anatoly", "Boleslaw", "Boris", "Bronislaw", "Czeslaw", "Feliks", "Grigori", "Henryk", "Ignacy",
                "Jozef", "Kazimierz", "Konstantin", "Leonid", "Mieczyslaw", "Nikolai", "Pavel", "Piotr", "Stanislaw",
                "Stefan", "Tadeusz", "Vasily", "Vladimir", "Wladyslaw", "Zbigniew", "Zygmunt", "Aleksei", "Fyodor",
                "Ivan", "Sergei", "Yuri"
            },
            new[]
            {
                "Andrzej", "Bogdan", "Dariusz", "Dmitri", "Eduard", "Gennady", "Grzegorz", "Igor", "Janusz", "Jerzy",
                "Krzysztof", "Leszek", "Marek", "Mikhail", "Miroslav", "Oleg", "Roman", "Ryszard", "Stanislav",
                "Valery", "Viktor", "Vitali", "Vladislav", "Waldemar", "Wieslaw", "Yevgeny", "Zenon", "Aleksandr",
                "Bohdan", "Rostislav"
            },
            new[]
            {
                "Artur", "Denys", "Dmytro", "Kostya", "Maksim", "Mateusz", "Michal", "Oleksandr", "Pawel", "Rafal",
                "Ruslan", "Sergiy", "Taras", "Tomasz", "Vasyl", "Vladyslav", "Wojciech", "Yaroslav", "Andriy",
                "Bartosz", "Damian", "Jakub", "Kamil", "Lukasz", "Marcin", "Piotr", "Przemyslaw", "Radoslaw",
                "Szymon", "Ihor"
            },
        },
        [Culture.WestAfrican] = new[]
        {
            new[]
            {
                "Abdul", "Ade", "Akin", "Amadu", "Bala", "Chike", "Emeka", "Ezekiel", "Godwin", "Ibrahim", "Ige",
                "Isaac", "Joshua", "Kofi", "Kwabena", "Kwame", "Kwasi", "Musa", "Nnamdi", "Obi", "Okon", "Olu",
                "Samuel", "Sunday", "Tunde", "Yaw", "Yakubu", "Bassey", "Dele", "Adebayo"
            },
            new[]
            {
                "Azumah", "Bukom", "Chidi", "David", "Dick", "Eddie", "Emmanuel", "Femi", "Friday", "Gabriel", "Ike",
                "Ikenna", "Jonah", "Joseph", "Kelechi", "Kojo", "Michael", "Moses", "Nana", "Nelson", "Obisia",
                "Peter", "Power", "Raymond", "Roy", "Samson", "Solomon", "Thomas", "Uche", "Young"
            },
            new[]
            {
                "Anthony", "Bright", "Chinedu", "Daniel", "Ebenezer", "Efe", "Emeka", "Fatai", "Isaac", "Joshua",
                "Kelvin", "Kwesi", "Larry", "Lateef", "Ola", "Olanrewaju", "Patrick", "Rasheed", "Richard", "Segun",
                "Sherif", "Sulley", "Tayo", "Uchenna", "Wale", "Yusuf", "Chidubem", "Ifeanyi", "Obinna", "Nurudeen"
            },
        },
        [Culture.Nordic] = new[]
        {
            new[]
            {
                "Anders", "Arne", "Axel", "Bertil", "Birger", "Erik", "Folke", "Gosta", "Gunnar", "Gustav", "Harald",
                "Helge", "Hjalmar", "Ingemar", "Ivar", "Karl", "Knut", "Lars", "Nils", "Olof", "Ragnar", "Rune",
                "Sigurd", "Sven", "Tage", "Torsten", "Ture", "Verner", "Yngve", "Ake"
            },
            new[]
            {
                "Bengt", "Bo", "Christer", "Dan", "Goran", "Hakan", "Jan", "Kent", "Kjell", "Lennart", "Leif",
                "Mats", "Ove", "Per", "Roland", "Rolf", "Stefan", "Sten", "Tommy", "Ulf", "Bjorn", "Hans", "Jorgen",
                "Krister", "Magnus", "Peter", "Ronny", "Thomas", "Torbjorn", "Anders"
            },
            new[]
            {
                "Adam", "Alexander", "Anton", "Daniel", "David", "Elias", "Emil", "Erik", "Filip", "Fredrik",
                "Gustav", "Hampus", "Isak", "Jesper", "Johan", "Jonas", "Kristoffer", "Linus", "Ludvig", "Marcus",
                "Mattias", "Niklas", "Oscar", "Patrik", "Robin", "Sebastian", "Simon", "Tobias", "Viktor", "William"
            },
        },
        [Culture.Caribbean] = new[]
        {
            new[]
            {
                "Alphonso", "Berkley", "Clifton", "Delroy", "Egbert", "Errol", "Everton", "Ferdinand", "Gladstone",
                "Hopeton", "Hubert", "Lascelles", "Lloyd", "Neville", "Osmond", "Percival", "Rupert", "Stanford",
                "Uriah", "Winston"
            },
            new[]
            {
                "Bunny", "Clive", "Colin", "Courtney", "Dennis", "Desmond", "Devon", "Donovan", "Errol", "Garfield",
                "Glen", "Horace", "Leroy", "Lester", "Michael", "Milton", "Newton", "Patrick", "Trevor", "Vernon"
            },
            new[]
            {
                "Andre", "Ashley", "Damion", "Deon", "Dwight", "Jermaine", "Kemar", "Kevaughn", "Leon", "Marlon",
                "Nicholas", "Odane", "Omar", "Ricardo", "Rohan", "Sanjay", "Shane", "Tafari", "Tevin", "Wayne"
            },
        },
    };

    // ---- surnames, by culture ----

    private static readonly Dictionary<Culture, string[]> Last = new()
    {
        [Culture.American] = new[]
        {
            "Adams", "Archer", "Bailey", "Banks", "Barnes", "Bennett", "Boyd", "Brooks", "Bryant", "Burke", "Carter",
            "Chandler", "Coleman", "Collins", "Cross", "Dalton", "Daniels", "Dawson", "Dixon", "Doyle", "Ellis",
            "Fields", "Fisher", "Ford", "Foster", "Fox", "Franklin", "Gaines", "Gibson", "Grant", "Graves", "Greene",
            "Griffin", "Hale", "Hall", "Hammond", "Harper", "Hayes", "Hendrix", "Hollis", "Hopkins", "Hudson",
            "Hunter", "Jackson", "Jennings", "Kane", "Keller", "Lane", "Lawson", "Lewis", "Malone", "Marsh", "Mason",
            "Mercer", "Monroe", "Moore", "Morgan", "Nash", "Norton", "Owens", "Parker", "Payne", "Pierce", "Porter",
            "Powell", "Preston", "Price", "Quinn", "Reed", "Reeves", "Rhodes", "Riley", "Rollins", "Ross", "Sharpe",
            "Shaw", "Simmons", "Sloan", "Steele", "Stokes", "Sutton", "Tate", "Thornton", "Turner", "Vaughn", "Wade",
            "Walker", "Wallace", "Ward", "Warren", "Watkins", "Webb", "Wells", "Whitfield", "Wilder", "Winters",
            "Wright", "Yates", "Young", "Brennan"
        },
        [Culture.British] = new[]
        {
            "Ashton", "Bailey", "Barker", "Baxter", "Bell", "Bennett", "Booth", "Bradley", "Brooks", "Buchanan",
            "Burton", "Chandler", "Clarke", "Collins", "Cooper", "Cox", "Curtis", "Davies", "Dawson", "Dixon",
            "Downes", "Eastwood", "Edwards", "Ellis", "Evans", "Finnegan", "Fletcher", "Gardner", "Gibbs", "Graham",
            "Green", "Hardy", "Harrison", "Hayward", "Hobson", "Holmes", "Hughes", "Hunter", "Jones", "Kelly",
            "Lewis", "Lloyd", "Marsh", "Mason", "Matthews", "Mitchell", "Moore", "Morrison", "Murray", "Nelson",
            "Norton", "Oakley", "Palmer", "Parkes", "Pearce", "Phillips", "Pike", "Powell", "Preston", "Rees",
            "Richards", "Roberts", "Robinson", "Rowe", "Sanders", "Sheridan", "Shipley", "Simmons", "Smith",
            "Spencer", "Stacey", "Stevens", "Stone", "Sykes", "Taylor", "Thomas", "Thompson", "Turner", "Wade",
            "Walker", "Ward", "Watson", "Webb", "Wells", "West", "Whitaker", "Williams", "Wilson", "Wood", "Wright",
            "Yates", "Burns", "Fairweather", "Hastings", "Kingsley", "Marlow", "Redfern", "Thurlow", "Vickers"
        },
        [Culture.Irish] = new[]
        {
            "Brady", "Brennan", "Byrne", "Cahill", "Callaghan", "Carey", "Carroll", "Casey", "Clancy", "Coleman",
            "Collins", "Conlan", "Connolly", "Costello", "Cullen", "Curran", "Daly", "Delaney", "Dempsey", "Devlin",
            "Doherty", "Donnelly", "Donovan", "Doyle", "Duffy", "Dunne", "Fallon", "Farrell", "Finnegan",
            "Fitzgerald", "Flanagan", "Fleming", "Flynn", "Gallagher", "Gorman", "Griffin", "Hayes", "Healy",
            "Hogan", "Hughes", "Kavanagh", "Keane", "Kearney", "Kelly", "Kennedy", "Kenny", "Lynch", "Magee",
            "Maguire", "Mallon", "McBride", "McCarthy", "McCullough", "McGrath", "McKenna", "McLaughlin", "Molloy",
            "Monaghan", "Mooney", "Moran", "Morrissey", "Mullan", "Murphy", "Nolan", "O'Brien", "O'Connor",
            "O'Donnell", "O'Neill", "O'Rourke", "O'Sullivan", "Phelan", "Quinn", "Quigley", "Reilly", "Ryan",
            "Shanahan", "Sheehan", "Sullivan", "Traynor", "Walsh", "Ward", "Whelan", "Barrett", "Boyle", "Cassidy",
            "Cronin", "Fahy", "Keegan", "Lyons"
        },
        [Culture.Hispanic] = new[]
        {
            "Acosta", "Aguilar", "Alvarado", "Alvarez", "Arce", "Arias", "Avila", "Ayala", "Barrera", "Bautista",
            "Benitez", "Bravo", "Caballero", "Calderon", "Camacho", "Campos", "Cardenas", "Carrasco", "Castillo",
            "Castro", "Cervantes", "Chavez", "Cordova", "Cortez", "Cruz", "Delgado", "Diaz", "Dominguez", "Duran",
            "Escobar", "Espinoza", "Estrada", "Fierro", "Flores", "Fuentes", "Galindo", "Gamboa", "Garcia", "Gomez",
            "Gonzalez", "Guerrero", "Gutierrez", "Guzman", "Herrera", "Hidalgo", "Ibarra", "Jimenez", "Juarez",
            "Lara", "Leyva", "Lopez", "Lozano", "Luna", "Maldonado", "Marquez", "Martinez", "Medina", "Mendez",
            "Mendoza", "Meza", "Molina", "Montoya", "Morales", "Moreno", "Munoz", "Navarro", "Nunez", "Ochoa",
            "Ojeda", "Olivares", "Ortega", "Ortiz", "Padilla", "Palacios", "Paredes", "Pena", "Perez", "Quintana",
            "Ramirez", "Ramos", "Reyes", "Rios", "Rivera", "Robles", "Rodriguez", "Rojas", "Romero", "Rosario",
            "Salazar", "Salinas", "Sanchez", "Sandoval", "Santana", "Santiago", "Serrano", "Solis", "Soto", "Suarez",
            "Tapia", "Torres", "Trejo", "Valdez", "Vargas", "Vasquez", "Vega", "Velazquez", "Vera", "Villa",
            "Zamora", "Zarate"
        },
        [Culture.Brazilian] = new[]
        {
            "Almeida", "Alves", "Andrade", "Araujo", "Azevedo", "Barbosa", "Barros", "Batista", "Bezerra", "Braga",
            "Cardoso", "Carvalho", "Castro", "Cavalcanti", "Correia", "Costa", "Cunha", "Dias", "Duarte", "Farias",
            "Fernandes", "Ferreira", "Fonseca", "Freitas", "Gomes", "Goncalves", "Guimaraes", "Lima", "Lopes",
            "Macedo", "Machado", "Maciel", "Marques", "Martins", "Melo", "Mendes", "Miranda", "Monteiro", "Moraes",
            "Moreira", "Nascimento", "Neves", "Nogueira", "Nunes", "Oliveira", "Pacheco", "Paiva", "Pereira",
            "Pinheiro", "Pinto", "Queiroz", "Ramos", "Rezende", "Ribeiro", "Rocha", "Rodrigues", "Sales", "Sampaio",
            "Santana", "Santos", "Silva", "Soares", "Sousa", "Tavares", "Teixeira", "Vasconcelos", "Vieira",
            "Xavier", "Bastos", "Camargo"
        },
        [Culture.Italian] = new[]
        {
            "Amato", "Barbieri", "Basile", "Bellini", "Benedetti", "Bernardi", "Bianchi", "Bruno", "Caruso",
            "Castellano", "Cattaneo", "Colombo", "Conti", "Coppola", "Costa", "D'Amato", "De Luca", "De Santis",
            "Donati", "Esposito", "Fabbri", "Farina", "Ferrari", "Ferraro", "Ferretti", "Fiore", "Fontana", "Franco",
            "Galli", "Gallo", "Gatti", "Gentile", "Giordano", "Grasso", "Greco", "Guerra", "Leone", "Lombardi",
            "Longo", "Mancini", "Marchetti", "Marini", "Marino", "Martinelli", "Mazza", "Messina", "Milani", "Monti",
            "Morelli", "Moretti", "Neri", "Orlando", "Pagano", "Palumbo", "Parisi", "Pellegrini", "Piras", "Ricci",
            "Riva", "Rizzo", "Romano", "Rossetti", "Rossi", "Russo", "Sala", "Sanna", "Santoro", "Sartori", "Serra",
            "Silvestri", "Sorrentino", "Testa", "Valentini", "Villa", "Vitale", "Rinaldi", "Caputo", "Barone",
            "Fabrizi"
        },
        [Culture.German] = new[]
        {
            "Bauer", "Baumann", "Becker", "Berger", "Bergmann", "Beyer", "Bohm", "Brandt", "Braun", "Busch",
            "Dietrich", "Engel", "Ernst", "Fischer", "Frank", "Franke", "Friedrich", "Fuchs", "Graf", "Gross",
            "Gruber", "Haas", "Hahn", "Hartmann", "Heinrich", "Hermann", "Herzog", "Hoffmann", "Hofmann", "Huber",
            "Jager", "Kaiser", "Kaufmann", "Keller", "Kern", "Klein", "Koch", "Kohler", "Konig", "Krause", "Kraus",
            "Kruger", "Kuhn", "Lang", "Lehmann", "Lorenz", "Ludwig", "Maier", "Martin", "Mayer", "Meier", "Meyer",
            "Muller", "Neumann", "Nowak", "Otto", "Peters", "Pfeiffer", "Richter", "Riedel", "Roth", "Sauer",
            "Schafer", "Scherer", "Schmid", "Schmidt", "Schneider", "Scholz", "Schreiber", "Schroder", "Schulz",
            "Schulze", "Schumacher", "Schuster", "Schwarz", "Seidel", "Simon", "Sommer", "Stein", "Stern", "Thiel",
            "Vogel", "Vogt", "Wagner", "Walter", "Weber", "Wegner", "Weiss", "Werner", "Winkler", "Winter", "Wolf",
            "Zimmermann", "Ziegler", "Hein", "Bock", "Ackermann", "Reinhardt", "Bruhn"
        },
        [Culture.French] = new[]
        {
            "Andre", "Aubert", "Barbier", "Baron", "Benoit", "Bernard", "Bertrand", "Blanc", "Blanchard", "Bonnet",
            "Boucher", "Bourgeois", "Boyer", "Brun", "Caron", "Chevalier", "Clement", "Colin", "Cordier", "Dubois",
            "Duchesne", "Dufour", "Dumas", "Dupont", "Durand", "Duval", "Fabre", "Faure", "Fontaine", "Fournier",
            "Gaillard", "Garnier", "Gauthier", "Gerard", "Girard", "Giraud", "Guerin", "Henry", "Huet", "Jacquet",
            "Julien", "Lacroix", "Lambert", "Laurent", "Lecomte", "Lefebvre", "Legrand", "Lemaire", "Leroy", "Lucas",
            "Marchand", "Martin", "Masson", "Mathieu", "Menard", "Mercier", "Meunier", "Michel", "Moreau", "Morel",
            "Nicolas", "Noel", "Olivier", "Perrin", "Petit", "Picard", "Poirier", "Renard", "Renaud", "Rey",
            "Richard", "Robin", "Roche", "Rolland", "Rousseau", "Roussel", "Roux", "Schmitt", "Simon", "Thomas",
            "Vidal", "Vincent", "Charnay", "Bouttier", "Delorme", "Fournel"
        },
        [Culture.Slavic] = new[]
        {
            "Adamek", "Andreev", "Antonov", "Baranov", "Belov", "Bondarenko", "Borisov", "Boyko", "Chernov",
            "Danilov", "Dmitriev", "Fedorov", "Filatov", "Frolov", "Gavrilov", "Gorbachev", "Grigoriev", "Ivanov",
            "Ivankov", "Jaworski", "Kaminski", "Karpov", "Klimenko", "Kolesnik", "Kowalczyk", "Kowalski", "Kozlov",
            "Kravchenko", "Krylov", "Kuznetsov", "Lebedev", "Lewandowski", "Loginov", "Makarov", "Malinowski",
            "Markov", "Mazur", "Medvedev", "Melnyk", "Mikhailov", "Morozov", "Nowak", "Orlov", "Ostrowski", "Pavlov",
            "Petrov", "Piotrowski", "Popov", "Rybak", "Savchenko", "Semenov", "Shevchenko", "Sidorov", "Smirnov",
            "Sobolev", "Sokolov", "Stepanov", "Stojanovic", "Szymanski", "Tarasov", "Titov", "Tkachenko", "Volkov",
            "Voronin", "Wisniewski", "Wojcik", "Wojciechowski", "Zaitsev", "Zielinski", "Zubkov", "Nowicki",
            "Pawlak", "Sikora", "Baranowski", "Dabrowski", "Gajewski", "Krawczyk", "Michalski"
        },
        [Culture.WestAfrican] = new[]
        {
            "Abara", "Abiola", "Achebe", "Adebayo", "Adeyemi", "Adjei", "Afolabi", "Agyeman", "Amankwah", "Amoah",
            "Anane", "Annan", "Ansah", "Asante", "Ayew", "Badu", "Balogun", "Bediako", "Boateng", "Chukwu",
            "Danquah", "Darko", "Duodu", "Eze", "Ezeani", "Frimpong", "Gyan", "Ibeh", "Ikpeba", "Kalu", "Kotey",
            "Kwakye", "Lawal", "Mensah", "Mumuni", "Nwachukwu", "Nwankwo", "Nwosu", "Obeng", "Obi", "Odartey",
            "Odoi", "Ofori", "Ogunlesi", "Okafor", "Okeke", "Okine", "Okonkwo", "Okoro", "Olaniyan", "Olawale",
            "Oluwole", "Omotoso", "Onyekwere", "Opoku", "Osei", "Owusu", "Quaye", "Quartey", "Sackey", "Sarpong",
            "Sowah", "Tetteh", "Tackie", "Udo", "Uzoma"
        },
        [Culture.Nordic] = new[]
        {
            "Ahlberg", "Ahlgren", "Andersson", "Berg", "Berglund", "Bergstrom", "Bjork", "Blomqvist", "Carlsson",
            "Dahl", "Danielsson", "Eklund", "Eriksson", "Falk", "Forsberg", "Fransson", "Gustafsson", "Hagg",
            "Hallberg", "Hansson", "Hedlund", "Hellstrom", "Holm", "Holmberg", "Isaksson", "Jakobsson", "Jansson",
            "Johansson", "Jonsson", "Karlsson", "Larsson", "Lind", "Lindberg", "Lindgren", "Lindqvist", "Lundberg",
            "Lundgren", "Lundqvist", "Magnusson", "Martensson", "Nilsson", "Norberg", "Nordin", "Nystrom", "Olsson",
            "Palm", "Persson", "Petersson", "Pettersson", "Sandberg", "Sjoberg", "Sjogren", "Stromberg", "Sundberg",
            "Svensson", "Wallin", "Werner", "Wikstrom", "Ostlund", "Ekstrom"
        },
        [Culture.Caribbean] = new[]
        {
            "Anderson", "Bailey", "Barrett", "Beckford", "Bennett", "Blake", "Brown", "Bryan", "Campbell", "Clarke",
            "Cole", "Daley", "Dixon", "Douglas", "Edwards", "Ellis", "Ferguson", "Forbes", "Francis", "Gordon",
            "Grant", "Gray", "Green", "Hall", "Harris", "Henry", "Hibbert", "Hines", "Jackson", "James", "Johnson",
            "Lawrence", "Lewis", "Malcolm", "McKenzie", "Miller", "Mitchell", "Morgan", "Morris", "Palmer", "Powell",
            "Reid", "Richards", "Roberts", "Robinson", "Rose", "Samuels", "Scott", "Simpson", "Smith", "Spence",
            "Stewart", "Taylor", "Thomas", "Thompson", "Walters", "Watson", "White", "Williams", "Wright"
        },
    };
}
