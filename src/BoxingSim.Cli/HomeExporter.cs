using System.Text.Json;
using BoxingSim.Core.Analysis;
using BoxingSim.Core.Model;

namespace BoxingSim.Cli;

/// <summary>Exports a clean landing page that drills into the card viewer and the fight simulator.</summary>
public static class HomeExporter
{
    public static void Export(string path, IReadOnlyList<Boxer> deck, string title, string cardsFile, string fightFile)
    {
        int countries = deck.Where(b => !string.IsNullOrEmpty(b.Country)).Select(b => b.Country!).Distinct().Count();
        var years = deck.Select(DebutYearOf).Where(y => y.HasValue).Select(y => y!.Value).ToList();
        string era = years.Count > 0 ? $"{years.Min() / 10 * 10}s–{years.Max() / 10 * 10}s" : "";

        var top = deck.OrderByDescending(b => b.Overall).ThenByDescending(b => b.Record.Wins).Take(12)
            .Select(b => new
            {
                name = b.Name, nickname = b.Nickname, country = b.Country,
                style = StyleClassifier.Of(b).DisplayName(), record = b.Record.ToString(), ovr = b.Overall
            }).ToList();

        string html = Template
            .Replace("__TITLE__", Enc(title))
            .Replace("__CARDS__", Enc(cardsFile))
            .Replace("__FIGHT__", Enc(fightFile))
            .Replace("__TOTAL__", deck.Count.ToString())
            .Replace("__COUNTRIES__", countries.ToString())
            .Replace("__ERA__", Enc(era))
            .Replace("__TOP__", JsonSerializer.Serialize(top));
        File.WriteAllText(path, html);
    }

    private static string Enc(string s) => System.Net.WebUtility.HtmlEncode(s);

    private static int? DebutYearOf(Boxer b)
    {
        if (b.DebutYear is int d) return d;
        return FirstYear(b.DateOfBirth) is int birth ? birth + 19 : (int?)null;
    }

    private static int? FirstYear(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        for (int i = 0; i + 4 <= s.Length; i++)
            if (char.IsDigit(s[i]) && char.IsDigit(s[i + 1]) && char.IsDigit(s[i + 2]) && char.IsDigit(s[i + 3]))
                return int.Parse(s.Substring(i, 4));
        return null;
    }

    private const string Template = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>__TITLE__</title>
<style>
  :root{--bg0:#070b14;--bg1:#10182b;--line:rgba(255,255,255,.08);--text:#eef1f8;--muted:#8893ad;--accent:#ff5a3c;--gold:#e9c75a;}
  *{box-sizing:border-box;}
  body{margin:0;color:var(--text);font:15px/1.5 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;
    background:radial-gradient(1300px 900px at 50% -20%,#26345d 0,transparent 55%),radial-gradient(820px 600px at 86% 4%,#2c1d35 0,transparent 50%),linear-gradient(180deg,var(--bg0),var(--bg1));min-height:100vh;}
  .wrap{max-width:900px;margin:0 auto;padding:0 22px;}
  .hero{text-align:center;padding:74px 20px 26px;}
  .hero .glove{font-size:44px;line-height:1;}
  .hero h1{margin:16px 0 0;font-size:42px;font-weight:850;letter-spacing:.5px;}
  .hero .sub{color:var(--muted);font-size:15px;margin-top:13px;letter-spacing:.3px;}
  .hero .sub b{color:var(--text);}
  .tiles{display:grid;grid-template-columns:1fr 1fr;gap:16px;margin:32px 0 46px;}
  @media(max-width:640px){.tiles{grid-template-columns:1fr;}}
  .tile{display:block;text-decoration:none;color:inherit;border:1px solid var(--line);border-radius:20px;padding:30px 28px;
    background:linear-gradient(180deg,rgba(255,255,255,.05),rgba(255,255,255,.015));transition:transform .18s,border-color .18s,box-shadow .18s;}
  .tile:hover{transform:translateY(-5px);border-color:rgba(255,90,60,.5);box-shadow:0 26px 54px -30px rgba(0,0,0,.9);}
  .tile .ic{font-size:38px;}
  .tile .h{font-size:23px;font-weight:800;margin:10px 0 7px;}
  .tile .d{color:var(--muted);font-size:14px;line-height:1.55;}
  .tile .go{display:inline-block;margin-top:16px;color:var(--accent);font-weight:750;font-size:14px;}
  .p4p-head{display:flex;align-items:baseline;justify-content:space-between;gap:12px;margin:0 0 14px;flex-wrap:wrap;}
  .p4p-head h2{margin:0;font-size:14px;letter-spacing:.7px;text-transform:uppercase;color:var(--gold);}
  .p4p-head .note{color:var(--muted);font-size:12px;}
  .p4p{display:grid;gap:8px;}
  .row{display:grid;grid-template-columns:26px 44px 1fr auto;gap:14px;align-items:center;
    border-radius:14px;padding:11px 16px;background:rgba(255,255,255,.022);border:1px solid transparent;transition:background .15s,border-color .15s;}
  .row:hover{background:rgba(255,255,255,.05);border-color:var(--line);}
  .row .rk{color:var(--muted);font-weight:800;text-align:center;font-size:14px;}
  .ovr{width:42px;height:42px;border-radius:11px;display:grid;place-items:center;font-weight:850;font-size:18px;color:#0a0e1a;}
  .who a{color:var(--text);text-decoration:none;font-weight:700;font-size:16px;}
  .who a:hover{color:var(--accent);}
  .who .s{color:var(--muted);font-size:12.5px;margin-top:1px;}
  .fightlink{text-decoration:none;border:1px solid var(--line);border-radius:10px;padding:8px 13px;color:var(--muted);font-size:13px;white-space:nowrap;}
  .fightlink:hover{border-color:var(--accent);color:var(--text);}
  footer{color:var(--muted);font-size:12px;text-align:center;padding:38px 20px;}
</style>
</head>
<body>
<div class="wrap">
  <div class="hero">
    <div class="glove">&#129354;</div>
    <h1>__TITLE__</h1>
    <div class="sub"><b>__TOTAL__</b> fighters &middot; __ERA__ &middot; <b>__COUNTRIES__</b> nations</div>
  </div>
  <div class="tiles">
    <a class="tile" href="__CARDS__">
      <div class="ic">&#128199;</div><div class="h">Fighter Cards</div>
      <div class="d">Browse, search and rank every fighter. Filter by era, debut decade, country and style.</div>
      <span class="go">Open the cards &rarr;</span>
    </a>
    <a class="tile" href="__FIGHT__">
      <div class="ic">&#9876;&#65039;</div><div class="h">Fight Night</div>
      <div class="d">Pick any two and watch it live &mdash; round by round, on a 3:00 clock, with judges and commentary.</div>
      <span class="go">Step into the ring &rarr;</span>
    </a>
  </div>
  <div class="p4p-head"><h2>Pound-for-Pound</h2><span class="note">click a name for the card &middot; &#9876;&#65039; to simulate</span></div>
  <div class="p4p" id="p4p"></div>
</div>
<footer>Ratings are subjective estimates generated by Boxing Simulator. Everything runs in your browser.</footer>
<script>
const TOP=__TOP__, CARDS="__CARDS__", FIGHT="__FIGHT__";
const color=v=>`hsl(${Math.round(v*1.25)} 70% 52%)`;
const FL={'USA':'🇺🇸','England':'🏴','Canada':'🇨🇦','Ukraine':'🇺🇦','Russia':'🇷🇺','Germany':'🇩🇪','Sweden':'🇸🇪','Italy':'🇮🇹'};
const flag=c=>c&&FL[c]?FL[c]+' ':'';
document.getElementById('p4p').innerHTML=TOP.map((f,i)=>`
  <div class="row">
    <div class="rk">${i+1}</div>
    <div class="ovr" style="background:${color(f.ovr)}">${f.ovr}</div>
    <div class="who"><a href="${CARDS}?q=${encodeURIComponent(f.name)}">${f.name}</a><div class="s">${flag(f.country)}${f.style} &middot; ${f.record}</div></div>
    <a class="fightlink" href="${FIGHT}?a=${encodeURIComponent(f.name)}">&#9876;&#65039; Fight</a>
  </div>`).join('');
</script>
</body>
</html>
""";
}
