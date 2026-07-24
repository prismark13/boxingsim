namespace BoxingSim.Core.Model;

/// <summary>
/// Maps a fighter's nationality to the regional/continental titles they are eligible to contest.
/// World belts (WBC/WBA/IBF/WBO) are open to everyone, so "World" is always included.
/// </summary>
public static class Belts
{
    public static IReadOnlyList<string> Eligibility(string? country)
    {
        string c = (country ?? "").Trim().ToLowerInvariant();
        var b = new List<string>();
        if (c.Length == 0) return new[] { "World" };

        bool uk = In(c, "england", "scotland", "wales", "britain", "british", "united kingdom", "uk", "northern ireland");
        bool ireland = c.Contains("ireland") && !c.Contains("northern");
        bool canada = c.Contains("canada");
        bool usa = In(c, "usa", "united states", "u.s.", "america") && !c.Contains("south");
        bool mexico = c.Contains("mexico");
        bool ausnz = In(c, "australia", "new zealand");
        bool pacific = In(c, "tonga", "samoa", "fiji", "papua");
        bool africaCommon = In(c, "nigeria", "ghana", "south africa", "zimbabwe", "kenya", "uganda", "jamaica", "guyana", "trinidad");
        bool europe = uk || ireland || In(c, "italy", "france", "germany", "spain", "sweden", "norway", "denmark",
            "netherlands", "belgium", "finland", "austria", "switzerland", "poland", "russia", "yugoslavia",
            "greece", "portugal", "hungary", "croatia", "serbia", "romania");
        bool latin = mexico || In(c, "argentina", "puerto rico", "cuba", "venezuela", "colombia", "panama",
            "dominican", "brazil", "chile", "uruguay", "ecuador");
        bool asia = In(c, "japan", "korea", "philippines", "thailand", "indonesia", "china");

        if (uk) b.Add("British");
        if (uk || canada || ausnz || africaCommon) b.Add("Commonwealth");
        if (europe) b.Add("European");
        if (usa) b.Add("USBA");
        if (usa || canada || mexico) b.Add("NABF");
        if (latin) b.Add("Latino");
        if (africaCommon && !uk) b.Add("African");
        if (ausnz || pacific) b.Add("Australasian");
        if (asia) b.Add("Oriental");

        b.Add("World");
        return b.Distinct().ToList();
    }

    private static bool In(string haystack, params string[] needles)
    {
        foreach (var n in needles)
            if (haystack.Contains(n)) return true;
        return false;
    }
}
