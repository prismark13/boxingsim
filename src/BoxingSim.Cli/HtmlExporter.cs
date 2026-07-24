using System.Text.Json;
using BoxingSim.Core.Analysis;
using BoxingSim.Core.Model;

namespace BoxingSim.Cli;

/// <summary>Exports a deck of fighters to a single self-contained HTML viewer.</summary>
public static class HtmlExporter
{
    // Stat order shared by every card. Abbreviation is shown on the tile.
    private static readonly (string Label, string Abbr, Func<Ratings, int> Get)[] Stats =
    {
        ("Power", "PWR", r => r.Power),
        ("Chin", "CHN", r => r.Chin),
        ("Speed", "SPD", r => r.Speed),
        ("Defense", "DEF", r => r.Defense),
        ("Stamina", "STA", r => r.Stamina),
        ("Accuracy", "ACC", r => r.Accuracy),
        ("Conditioning", "CON", r => r.Conditioning),
        ("Cut Resist", "CUT", r => r.CutResistance),
        ("Aggression", "AGG", r => r.Aggression),
        ("Heart", "HRT", r => r.Heart),
    };

    public static void Export(string path, IReadOnlyList<Boxer> deck, string title)
    {
        var data = deck.Select(b =>
        {
            var style = StyleClassifier.Of(b);
            var (activeFrom, activeTo) = ActiveWindow(b);
            return new
            {
                name = b.Name,
                nickname = b.Nickname,
                activeFrom,
                activeTo,
                division = b.WeightClass.DisplayName(),
                age = b.Age,
                dob = b.DateOfBirth,
                active = ActiveFrom(b),
                debutDecade = DebutDecade(b),
                prime = b.PrimeYears,
                decade = PrimeDecade(b),
                country = b.Country,
                titles = b.Titles ?? new List<string>(),
                eligible = Belts.Eligibility(b.Country),
                record = b.Record.ToString(),
                overall = b.Overall,
                style = style.DisplayName(),
                styleDesc = style.Describe(),
                stats = Stats.Select(s => new { a = s.Abbr, t = s.Label, v = s.Get(b.Ratings) }).ToArray()
            };
        }).ToList();

        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });

        string html = Template
            .Replace("__TITLE__", System.Net.WebUtility.HtmlEncode(title))
            .Replace("__DATA__", json);

        File.WriteAllText(path, html);
    }

    /// <summary>Year the fighter turned pro: exact debut year if known, else estimated from birth year.</summary>
    private static string? ActiveFrom(Boxer b)
    {
        if (b.DebutYear is int d) return d.ToString();
        if (FirstYear(b.DateOfBirth) is int birth) return "~" + (birth + 19);
        return null;
    }

    /// <summary>Approximate years a fighter was active: debut (or estimate) to a few years past prime.</summary>
    private static (int? From, int? To) ActiveWindow(Boxer b)
    {
        int? from = b.DebutYear ?? (FirstYear(b.DateOfBirth) is int birth ? birth + 19 : (int?)null);
        if (from is null) return (null, null);
        int primeEnd = LastYear(b.PrimeYears) ?? (from.Value + 8);
        int to = Math.Max(primeEnd, from.Value) + 6;
        return (from, to);
    }

    /// <summary>Last 4-digit run in a string (e.g. "1973-1976" -> 1976), or null.</summary>
    private static int? LastYear(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        int? last = null;
        for (int i = 0; i + 4 <= s.Length; i++)
            if (char.IsDigit(s[i]) && char.IsDigit(s[i + 1]) && char.IsDigit(s[i + 2]) && char.IsDigit(s[i + 3]))
                last = int.Parse(s.Substring(i, 4));
        return last;
    }

    /// <summary>The decade a fighter turned professional, for the debut filter.</summary>
    private static string? DebutDecade(Boxer b)
    {
        int? y = b.DebutYear ?? (FirstYear(b.DateOfBirth) is int birth ? birth + 19 : (int?)null);
        if (y is null) return null;
        return (y.Value / 10 * 10) + "s";
    }

    /// <summary>The decade a fighter peaked in: from prime years if known, else birth year + 27.</summary>
    private static string? PrimeDecade(Boxer b)
    {
        int? year = FirstYear(b.PrimeYears);
        if (year is null && FirstYear(b.DateOfBirth) is int birth) year = birth + 27;
        if (year is null) return null;
        return (year.Value / 10 * 10) + "s";
    }

    /// <summary>First 4-digit run in a string (e.g. "1973-1976" -> 1973), or null.</summary>
    private static int? FirstYear(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        for (int i = 0; i + 4 <= s.Length; i++)
            if (char.IsDigit(s[i]) && char.IsDigit(s[i + 1]) && char.IsDigit(s[i + 2]) && char.IsDigit(s[i + 3]))
                return int.Parse(s.Substring(i, 4));
        return null;
    }

    // Token-replacement template (verbatim) so CSS/JS braces don't fight C# interpolation.
    private const string Template = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>__TITLE__</title>
<style>
  :root {
    --bg0:#0a0e1a; --bg1:#121a2e; --card:rgba(255,255,255,.045); --line:rgba(255,255,255,.09);
    --text:#e8ecf5; --muted:#8b96b0; --accent:#ff5a3c;
  }
  * { box-sizing:border-box; }
  body {
    margin:0; color:var(--text); font:14px/1.45 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;
    background:radial-gradient(1100px 700px at 75% -10%, #1c2747 0%, transparent 60%), linear-gradient(180deg,var(--bg0),var(--bg1));
    min-height:100vh;
  }
  header {
    position:sticky; top:0; z-index:10; backdrop-filter:blur(14px);
    background:rgba(10,14,26,.74); border-bottom:1px solid var(--line); padding:14px 22px;
  }
  .title { display:flex; align-items:baseline; gap:10px; flex-wrap:wrap; }
  .title .back { align-self:center; color:var(--muted); text-decoration:none; font-size:13px; border:1px solid var(--line); border-radius:8px; padding:5px 11px; }
  .title .back:hover { color:var(--text); border-color:var(--accent); }
  .title h1 { margin:0; font-size:19px; letter-spacing:.3px; }
  .title .count { color:var(--muted); font-size:12.5px; }
  .title .glove { color:var(--accent); }
  .controls { display:flex; gap:8px; flex-wrap:wrap; align-items:center; margin-top:11px; }
  input,select {
    background:rgba(255,255,255,.05); color:var(--text); border:1px solid var(--line);
    border-radius:9px; padding:7px 11px; font-size:13px; outline:none;
  }
  input { min-width:190px; }
  input:focus,select:focus { border-color:var(--accent); }
  .chips { display:flex; gap:5px; flex-wrap:wrap; }
  .chip {
    cursor:pointer; padding:5px 10px; border-radius:999px; border:1px solid var(--line);
    background:transparent; color:var(--muted); font-size:12px; transition:.15s;
  }
  .chip:hover { color:var(--text); }
  .chip.active { background:var(--accent); border-color:var(--accent); color:#fff; }
  main { padding:18px 22px; max-width:1500px; margin:0 auto; }
  .grid { display:grid; gap:12px; grid-template-columns:repeat(auto-fill,minmax(248px,1fr)); }

  .card {
    background:var(--card); border:1px solid var(--line); border-radius:12px; padding:12px 13px;
    transition:transform .15s, border-color .15s;
  }
  .card:hover { transform:translateY(-2px); border-color:rgba(255,90,60,.4); }
  .head { display:flex; gap:11px; align-items:center; }
  .ovr {
    flex:0 0 46px; width:46px; height:46px; border-radius:10px; display:grid; place-items:center;
    font-weight:750; font-size:21px; color:#0a0e1a; line-height:1;
  }
  .ident { min-width:0; flex:1; }
  .ident .name { font-weight:650; font-size:15px; line-height:1.15; }
  .ident .nick { color:var(--accent); font-size:12px; font-style:italic; line-height:1.2; }
  .ident .sub { color:var(--muted); font-size:11.5px; margin-top:2px; }
  .style { display:inline-block; padding:2px 8px; border-radius:999px;
    font-size:10.5px; font-weight:600; letter-spacing:.2px; cursor:help; }
  .chiprow { display:flex; flex-wrap:wrap; gap:4px; align-items:center; margin-top:5px; }
  .belt { display:inline-block; padding:2px 7px; border-radius:5px; font-size:10px; font-weight:700;
    background:linear-gradient(180deg,#e9c75a,#b8902f); color:#2a1d00; letter-spacing:.3px; }
  .elig { margin-top:4px; font-size:10px; color:var(--muted); }

  .tiles { display:grid; grid-template-columns:repeat(5,1fr); gap:5px; margin-top:11px; }
  .tile {
    border:1px solid var(--line); border-radius:7px; padding:5px 0 4px; text-align:center;
    background:var(--tc, rgba(255,255,255,.03));
  }
  .tile .k { display:block; font-size:9px; color:var(--muted); letter-spacing:.4px; }
  .tile .v { display:block; font-size:15px; font-weight:700; font-variant-numeric:tabular-nums; line-height:1.1; }

  .empty { color:var(--muted); text-align:center; padding:50px; }
  footer { color:var(--muted); font-size:11.5px; text-align:center; padding:26px; }
</style>
</head>
<body>
<header>
  <div class="title">
    <a class="back" href="index.html">&larr; Home</a>
    <h1><span class="glove">&#129354;</span> __TITLE__</h1>
    <span class="count" id="count"></span>
  </div>
  <div class="controls">
    <input id="search" type="search" placeholder="Search fighters...">
    <select id="sort">
      <option value="overall">Sort: Overall</option>
      <option value="name">Sort: Name</option>
      <option value="age">Sort: Age</option>
      <option value="power">Sort: Power</option>
      <option value="speed">Sort: Speed</option>
    </select>
    <select id="country"><option value="">All countries</option></select>
    <select id="debut"><option value="">Any debut</option></select>
    <input id="year" type="number" placeholder="Active in year" style="min-width:0;width:128px">
    <div class="chips" id="chips"></div>
  </div>
  <div class="controls"><div class="chips" id="styleChips"></div></div>
  <div class="controls"><div class="chips" id="decadeChips"></div></div>
</header>
<main><div class="grid" id="grid"></div></main>
<footer>Ratings are subjective estimates generated by Boxing Simulator &mdash; not from any published game.</footer>

<script>
const FIGHTERS = __DATA__;
const grid = document.getElementById('grid');
const search = document.getElementById('search');
const sortSel = document.getElementById('sort');
const chipBox = document.getElementById('chips');
const styleChipBox = document.getElementById('styleChips');
const decadeChipBox = document.getElementById('decadeChips');
const countEl = document.getElementById('count');

let activeDivision = 'All';
let activeStyle = 'All';
let activeDecade = 'All';
let activeCountry = '';
let activeDebut = '';
let activeYear = '';
const divisions = ['All', ...[...new Set(FIGHTERS.map(f => f.division))]];
const styles = ['All', ...[...new Set(FIGHTERS.map(f => f.style))]];
const decades = ['All', ...[...new Set(FIGHTERS.map(f => f.decade).filter(Boolean))].sort()];

const countrySel = document.getElementById('country');
[...new Set(FIGHTERS.map(f => f.country).filter(Boolean))].sort().forEach(c => {
  const o = document.createElement('option'); o.value = c; o.textContent = c; countrySel.appendChild(o);
});
countrySel.onchange = () => { activeCountry = countrySel.value; render(); };

const debutSel = document.getElementById('debut');
[...new Set(FIGHTERS.map(f => f.debutDecade).filter(Boolean))].sort().forEach(d => {
  const o = document.createElement('option'); o.value = d; o.textContent = 'Debut ' + d; debutSel.appendChild(o);
});
debutSel.onchange = () => { activeDebut = debutSel.value; render(); };

const yearInput = document.getElementById('year');
yearInput.oninput = () => { activeYear = yearInput.value.trim(); render(); };

function makeChips(box, items, onPick, label){
  items.forEach((d, i) => {
    const c = document.createElement('button');
    c.className = 'chip' + (i === 0 ? ' active' : '');
    c.textContent = d === 'All' ? label : d;
    c.onclick = () => { onPick(d); [...box.children].forEach(x => x.classList.remove('active')); c.classList.add('active'); render(); };
    box.appendChild(c);
  });
}
makeChips(chipBox, divisions, d => activeDivision = d, 'All divisions');
makeChips(styleChipBox, styles, s => activeStyle = s, 'All styles');
makeChips(decadeChipBox, decades, d => activeDecade = d, 'All eras');

const STYLE_HUE = { 'Out-Boxer':205, 'Boxer-Puncher':265, 'Slugger':10, 'Swarmer':35, 'Counter-Puncher':150 };
function styleCss(name){
  const h = STYLE_HUE[name] ?? 220;
  return `background:hsl(${h} 70% 50% / .16);border:1px solid hsl(${h} 70% 55% / .5);color:hsl(${h} 80% 80%)`;
}
function color(v){ return `hsl(${Math.round(v*1.25)} 70% 52%)`; }      // 0 red -> 100 green
function tileBg(v){ return `hsl(${Math.round(v*1.25)} 60% 45% / .14)`; }

const FLAGS = { 'USA':'🇺🇸','Canada':'🇨🇦','Mexico':'🇲🇽',
  'Italy':'🇮🇹','France':'🇫🇷','Germany':'🇩🇪','West Germany':'🇩🇪',
  'Spain':'🇪🇸','Belgium':'🇧🇪','Sweden':'🇸🇪','Norway':'🇳🇴',
  'Denmark':'🇩🇰','Netherlands':'🇳🇱','Ireland':'🇮🇪',
  'Australia':'🇦🇺','New Zealand':'🇳🇿','South Africa':'🇿🇦',
  'Zimbabwe':'🇿🇼','Nigeria':'🇳🇬','Tonga':'🇹🇴',
  'Argentina':'🇦🇷','Cuba':'🇨🇺','Puerto Rico':'🇵🇷',
  'England':'🏴','Scotland':'🏴','Wales':'🏴','Northern Ireland':'🇬🇧','Britain':'🇬🇧' };
function flag(c){ return c && FLAGS[c] ? FLAGS[c] + ' ' : ''; }

function cardHtml(f){
  const tiles = f.stats.map(s => `
    <div class="tile" style="--tc:${tileBg(s.v)}" title="${s.t}">
      <span class="k">${s.a}</span>
      <span class="v" style="color:${color(s.v)}">${s.v}</span>
    </div>`).join('');
  const nick = f.nickname ? `<div class="nick">&ldquo;${f.nickname}&rdquo;</div>` : '';
  const born = f.dob ? `b. ${f.dob}` : `Age ${f.age}`;
  const loc = f.country ? `${flag(f.country)}${f.country} &middot; ` : '';
  const prime = f.prime ? `<div class="sub">Prime ${f.prime} &middot; ${f.record}</div>` : `<div class="sub">${f.record}</div>`;
  const elig = (f.eligible && f.eligible.length) ? `<div class="elig">Eligible: ${f.eligible.join(' · ')}</div>` : '';
  return `<div class="card">
    <div class="head">
      <div class="ovr" style="background:${color(f.overall)}">${f.overall}</div>
      <div class="ident">
        <div class="name">${f.name}</div>${nick}
        <div class="sub">${loc}${born}${f.active ? ' &middot; debut ' + f.active : ''}</div>
        ${prime}
        <div class="chiprow"><span class="style" style="${styleCss(f.style)}" title="${f.styleDesc}">${f.style}</span></div>
        ${elig}
      </div>
    </div>
    <div class="tiles">${tiles}</div>
  </div>`;
}

function render(){
  const q = search.value.trim().toLowerCase();
  const key = sortSel.value;
  let list = FIGHTERS.filter(f =>
    (activeDivision === 'All' || f.division === activeDivision) &&
    (activeStyle === 'All' || f.style === activeStyle) &&
    (activeDecade === 'All' || f.decade === activeDecade) &&
    (activeCountry === '' || f.country === activeCountry) &&
    (activeDebut === '' || f.debutDecade === activeDebut) &&
    (activeYear === '' || (f.activeFrom && f.activeTo && +activeYear >= f.activeFrom && +activeYear <= f.activeTo)) &&
    (q === '' || f.name.toLowerCase().includes(q) || (f.nickname||'').toLowerCase().includes(q) || f.style.toLowerCase().includes(q)));

  const stat = s => (f => (f.stats.find(x => x.t.toLowerCase() === s)||{}).v || 0);
  list.sort((a,b) => {
    if (key === 'name') return a.name.localeCompare(b.name);
    if (key === 'age') return a.age - b.age;
    if (key === 'power') return stat('power')(b) - stat('power')(a);
    if (key === 'speed') return stat('speed')(b) - stat('speed')(a);
    return b.overall - a.overall;
  });

  countEl.textContent = `${list.length} fighter${list.length===1?'':'s'}`;
  grid.innerHTML = list.length ? list.map(cardHtml).join('') : '<div class="empty">No fighters match.</div>';
}

search.oninput = render;
sortSel.onchange = render;
const _q = new URLSearchParams(location.search).get('q');
if (_q) search.value = _q;
render();
</script>
</body>
</html>
""";
}
