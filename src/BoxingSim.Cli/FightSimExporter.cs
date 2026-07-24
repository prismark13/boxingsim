using System.Text.Json;
using BoxingSim.Core.Analysis;
using BoxingSim.Core.Model;

namespace BoxingSim.Cli;

/// <summary>
/// Self-contained HTML "fight night" simulator. The C# fight engine is ported to JavaScript with
/// punch types (jab/hook/cross/uppercut/body) and separate head/body damage, broadcast commentary,
/// cuts that worsen and can be waved off, fouls (warnings, rare point deductions, very rare DQ),
/// three-judge scoring (UD/MD/SD and draw types), and post-fight injuries carrying a minimum layoff.
/// </summary>
public static class FightSimExporter
{
    public static void Export(string path, IReadOnlyList<Boxer> deck, string title)
    {
        var data = deck.Select(b => new
        {
            name = b.Name, nickname = b.Nickname, country = b.Country,
            style = StyleClassifier.Of(b).DisplayName(), record = b.Record.ToString(),
            ovr = b.Overall, age = b.Age,
            r = new
            {
                pow = b.Ratings.Power, chn = b.Ratings.Chin, spd = b.Ratings.Speed,
                def_ = b.Ratings.Defense, sta = b.Ratings.Stamina, acc = b.Ratings.Accuracy,
                con = b.Ratings.Conditioning, cut = b.Ratings.CutResistance,
                agg = b.Ratings.Aggression, hrt = b.Ratings.Heart
            }
        }).OrderBy(x => x.name).ToList();

        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
        string html = Template.Replace("__TITLE__", System.Net.WebUtility.HtmlEncode(title)).Replace("__DATA__", json);
        File.WriteAllText(path, html);
    }

    private const string Template = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>__TITLE__ — Fight Night</title>
<style>
  :root{--bg0:#070b14;--bg1:#10182b;--line:rgba(255,255,255,.09);--text:#eef1f8;--muted:#8893ad;--accent:#ff5a3c;--a:#39a0ff;--b:#ff5d7e;--gold:#e9c75a;}
  *{box-sizing:border-box;}
  body{margin:0;color:var(--text);font:15px/1.5 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;
    background:radial-gradient(1200px 820px at 50% -15%,#23315a 0,transparent 55%),radial-gradient(900px 600px at 92% 8%,#2a1c33 0,transparent 50%),linear-gradient(180deg,var(--bg0),var(--bg1));min-height:100vh;}
  header{padding:14px 22px;border-bottom:1px solid var(--line);display:flex;align-items:center;gap:14px;}
  h1{margin:0;font-size:19px;letter-spacing:.4px;font-weight:750;} h1 .g{color:var(--accent);}
  .back{color:var(--muted);text-decoration:none;font-size:13px;border:1px solid var(--line);border-radius:9px;padding:6px 12px;white-space:nowrap;}
  .back:hover{color:var(--text);border-color:var(--accent);}
  main{max-width:1080px;margin:0 auto;padding:18px 20px 40px;}
  .controls{display:flex;gap:9px;flex-wrap:wrap;align-items:center;justify-content:center;margin-bottom:18px;}
  select,button{background:rgba(255,255,255,.05);color:var(--text);border:1px solid var(--line);border-radius:10px;padding:9px 13px;font-size:14px;outline:none;}
  select{max-width:240px;} select:focus{border-color:var(--accent);}
  button{cursor:pointer;font-weight:700;letter-spacing:.3px;}
  .fight-btn{background:linear-gradient(180deg,#ff7a52,#ff4b2b);border-color:transparent;color:#fff;padding:9px 26px;box-shadow:0 6px 18px -6px rgba(255,75,43,.7);}
  .fight-btn:disabled{opacity:.45;cursor:default;box-shadow:none;}
  .vs{color:var(--muted);font-weight:800;font-size:13px;}
  .bug{display:grid;grid-template-columns:1fr 168px 1fr;gap:12px;align-items:stretch;margin-bottom:16px;}
  .corner{position:relative;border:1px solid var(--line);border-radius:16px;padding:14px;background:linear-gradient(180deg,rgba(255,255,255,.05),rgba(255,255,255,.02));overflow:hidden;transition:box-shadow .2s;}
  .corner::before{content:"";position:absolute;top:0;left:0;right:0;height:3px;}
  .corner.A::before{background:linear-gradient(90deg,var(--a),transparent);}
  .corner.B::before{background:linear-gradient(270deg,var(--b),transparent);}
  .chead{display:flex;gap:12px;align-items:center;}
  .corner.B .chead{flex-direction:row-reverse;text-align:right;}
  .avatar{flex:0 0 52px;width:52px;height:52px;border-radius:50%;display:grid;place-items:center;font-weight:850;font-size:20px;color:#0a0e1a;}
  .corner.A .avatar{background:radial-gradient(circle at 30% 30%,#7cc4ff,var(--a));}
  .corner.B .avatar{background:radial-gradient(circle at 30% 30%,#ff9bb1,var(--b));}
  .nm{font-weight:750;font-size:16px;line-height:1.15;} .sub{color:var(--muted);font-size:12px;}
  .ovr{font-weight:850;font-size:12px;padding:1px 8px;border-radius:6px;display:inline-block;margin-top:3px;background:rgba(255,255,255,.08);}
  .bars{margin-top:11px;}
  .hpwrap{height:9px;border-radius:6px;background:rgba(0,0,0,.35);overflow:hidden;}
  .hp{height:100%;border-radius:6px;background:linear-gradient(90deg,#2ecb6f,#9bd64a);transition:width .35s,background .35s;}
  .bodybar{height:5px;border-radius:5px;background:rgba(0,0,0,.3);overflow:hidden;margin-top:5px;}
  .bodyfill{height:100%;border-radius:5px;background:linear-gradient(90deg,#c98a3a,#e0b13a);transition:width .35s;}
  .crow{display:flex;justify-content:space-between;gap:8px;margin-top:8px;font-size:12px;color:var(--muted);}
  .corner.B .crow{flex-direction:row-reverse;} .crow b{color:var(--text);font-variant-numeric:tabular-nums;}
  .status{margin-top:5px;font-size:11.5px;min-height:15px;}
  .status .cut{color:#ff6a6a;} .status .swell{color:#e0b13a;} .status .kd{color:var(--gold);} .status .bdy{color:#e0a13a;}
  .center{display:flex;flex-direction:column;align-items:center;justify-content:center;border:1px solid var(--line);border-radius:16px;background:linear-gradient(180deg,rgba(255,255,255,.05),rgba(0,0,0,.15));}
  .center .rd{color:var(--muted);font-size:11px;letter-spacing:.6px;text-transform:uppercase;}
  .center .clock{font-size:40px;font-weight:850;font-variant-numeric:tabular-nums;letter-spacing:1px;line-height:1;margin:2px 0;}
  .center .scorebug{font-size:11.5px;color:var(--muted);text-align:center;margin-top:4px;}
  .grid{display:grid;grid-template-columns:1fr 300px;gap:14px;}
  @media(max-width:820px){ .grid{grid-template-columns:1fr;} .bug{grid-template-columns:1fr;} .center{order:-1;padding:14px;} }
  .feed{border:1px solid var(--line);border-radius:16px;background:rgba(0,0,0,.22);height:380px;overflow-y:auto;padding:12px 15px;font-size:13.5px;}
  .feed .l{padding:3px 0;border-bottom:1px solid rgba(255,255,255,.04);animation:fade .25s;}
  @keyframes fade{from{opacity:0;transform:translateY(3px);}to{opacity:1;}}
  .feed .t{color:var(--muted);font-variant-numeric:tabular-nums;margin-right:8px;}
  .feed .rdh{color:var(--accent);font-weight:750;margin:8px 0 4px;letter-spacing:.3px;}
  .feed .kd{color:var(--gold);font-weight:750;} .feed .cut{color:#ff7a7a;} .feed .foul{color:#c79bff;font-weight:650;}
  .feed .doc{color:#7ad6ff;} .feed .crowd{color:#9bd6a0;font-style:italic;} .feed .corner{color:#cdb98a;}
  .feed .res{color:#fff;font-weight:850;font-size:15px;margin-top:8px;} .feed .inj{color:#ff9a9a;}
  .card-panel{border:1px solid var(--line);border-radius:16px;background:rgba(0,0,0,.22);padding:12px 13px;height:380px;display:flex;flex-direction:column;}
  .card-panel h3{margin:0 0 8px;font-size:12px;letter-spacing:.6px;text-transform:uppercase;color:var(--gold);}
  .sc-table{flex:1;overflow-y:auto;}
  table.sc{width:100%;border-collapse:collapse;font-size:13px;}
  table.sc th{color:var(--muted);font-weight:600;font-size:11px;text-align:center;padding:4px 2px;position:sticky;top:0;background:#0c1220;}
  table.sc td{text-align:center;padding:4px 2px;font-variant-numeric:tabular-nums;border-top:1px solid rgba(255,255,255,.04);}
  table.sc td.rd2{color:var(--muted);}
  .sccell .A{color:var(--a);font-weight:700;} .sccell .B{color:var(--b);font-weight:700;} .sccell .e{color:var(--muted);}
  table.sc tfoot td{border-top:2px solid var(--line);font-weight:800;padding-top:6px;}
  .banner{margin-top:14px;padding:16px;border:1px solid var(--line);border-radius:16px;background:linear-gradient(180deg,rgba(255,90,60,.12),rgba(255,90,60,.04));text-align:center;display:none;}
  .banner.show{display:block;animation:fade .3s;} .banner .m{font-size:21px;font-weight:850;} .banner .cards{color:var(--muted);font-size:13px;margin-top:6px;} .banner .aftermath{color:#ff9a9a;font-size:12.5px;margin-top:8px;}
  footer{color:var(--muted);font-size:12px;text-align:center;padding:22px;}
</style>
</head>
<body>
<header><a class="back" href="index.html">&larr; Home</a><h1><span class="g">&#129354;</span> __TITLE__ — Fight Night</h1></header>
<main>
  <div class="controls">
    <select id="selA"></select><span class="vs">VS</span><select id="selB"></select>
    <select id="rounds"><option selected>12</option><option>10</option><option>8</option><option>6</option></select>
    <select id="speed"><option value="600">Fast</option><option value="1200" selected>Normal</option><option value="2500">Slow</option><option value="15000">Live (15s)</option><option value="0">Instant</option></select>
    <button class="fight-btn" id="go">Fight!</button>
  </div>
  <div class="bug">
    <div class="corner A" id="cA"></div>
    <div class="center"><div class="rd" id="rdlabel">Round —</div><div class="clock" id="clock">3:00</div><div class="scorebug" id="scorebug"></div></div>
    <div class="corner B" id="cB"></div>
  </div>
  <div class="grid">
    <div class="feed" id="feed"></div>
    <div class="card-panel">
      <h3>Judges' Scorecards</h3>
      <div class="sc-table">
        <table class="sc">
          <thead><tr><th>Rd</th><th>J1</th><th>J2</th><th>J3</th></tr></thead>
          <tbody id="scbody"></tbody>
          <tfoot><tr><td class="rd2">Tot</td><td id="sct0">—</td><td id="sct1">—</td><td id="sct2">—</td></tr></tfoot>
        </table>
      </div>
    </div>
  </div>
  <div class="banner" id="banner"></div>
</main>
<footer>Ratings are subjective estimates. The engine runs entirely in your browser &mdash; refresh for a new draw.</footer>

<script>
const FIGHTERS = __DATA__;
const WCKO = 1.45;
const byName = Object.fromEntries(FIGHTERS.map(f => [f.name, f]));
let SEED = 0x1a2b3c;
function mulberry32(a){return function(){a|=0;a=a+0x6D2B79F5|0;let t=Math.imul(a^a>>>15,1|a);t=t+Math.imul(t^t>>>7,61|t)^t;return((t^t>>>14)>>>0)/4294967296;};}
const ADV={'Out-Boxer|Slugger':0.6,'Out-Boxer|Swarmer':-0.6,'Out-Boxer|Counter-Puncher':0.3,'Boxer-Puncher|Slugger':0.2,'Boxer-Puncher|Swarmer':0.1,'Slugger|Swarmer':0.6,'Slugger|Counter-Puncher':-0.5,'Swarmer|Counter-Puncher':-0.5};
function advantage(a,b){ if(a===b) return 0; if((a+'|'+b) in ADV) return ADV[a+'|'+b]; if((b+'|'+a) in ADV) return -ADV[b+'|'+a]; return 0; }

// ---------- commentary (original phrasing) ----------
const pick=a=>a[Math.floor(Math.random()*a.length)];
const lastN=n=>n.split(' ').pop();
const fill=(t,a,d)=>t.replace(/\{a\}/g,a).replace(/\{d\}/g,d);
const HEADSHOTS=["left hook","overhand right","straight right","short uppercut","counter left hook","chopping right hand","right cross","jab–straight right"];
const BODYSHOTS=["hook to the body","right to the ribs","left to the liver","uppercut to the body","jab to the body"];
const CUTLOCS=[{name:"over the left eye",eye:true},{name:"over the right eye",eye:true},{name:"over the left eye",eye:true},{name:"over the right eye",eye:true},{name:"on the left eyelid",eye:true},{name:"on the right eyelid",eye:true},{name:"on the bridge of the nose",eye:false},{name:"along the cheekbone",eye:false},{name:"at the corner of the mouth",eye:false}];
const JAB=["{a} flicks out the jab, keeping {d} at the end of it.","{a} pumps a stiff jab into {d}'s face.","{a} doubles up the jab and resets.","{a} paws with the jab, measuring distance.","{a} snaps the head back with a piston jab."];
const BODY=["{a} digs a hook to the body.","{a} thuds a right to the ribs.","{a} invests downstairs.","{a} works the body, trying to slow {d} down.","{a} bangs a left to the liver."];
const COMBO=["{a} lets the hands go — a sharp three-punch combination.","{a} strings together a crisp one-two.","{a} rattles off a flurry upstairs and down.","{a} opens up with a quick combination."];
const DEFENSE=["{d} rolls under the right and fires back.","{a} loads up — {d} pulls straight back and makes him miss.","{d} picks the shots off on the gloves, calm under fire.","{a} lunges in and {d} ties him up smartly.","{d} slips inside and bangs to the body."];
const CLINCH=["they tie up on the inside and the ref breaks them.","{a} smothers the work in a clinch.","a messy spell inside, plenty of holding.","{a} leans on {d} along the ropes."];
const TACTIC=["{a} is cutting the ring off, giving {d} nowhere to go.","{d} on the back foot, circling away from the power.","{a} walking him down behind a high guard.","{a} controls the centre, dictating the pace.","{d} boxing on the retreat, trying to steal the round."];
const BUSY=["{a} takes over, rattling off combinations as {d} covers up.","{a} is the busier man, pumping the jab.","{a} pours it on, outworking {d}.","{a} controls the spell with sharp, busy hands.","{a} is letting his hands go, backing {d} up."];
const EVEN=["they trade in the centre of the ring.","an even exchange, give and take.","close quarters now, neither man backing up.","both landing, standing right in the pocket.","a real tit-for-tat spell, momentum even."];
const QUIET=["a tactical, feeling-out spell, little landing clean.","both measuring distance behind the jab.","a cagey stretch — not much doing.","they circle, hunting for an opening."];
const DRAMA=["The crowd is on its feet — this is turning into a war!","What a fight this has become — the fans are loving it!","You can feel the momentum swinging back and forth!","The atmosphere is electric as these two stand and trade!"];
const CORNER=["In the corner: \"Hands up and double that jab!\"","His corner wants the body — \"go downstairs!\"","\"You're letting him steal rounds — let your hands go!\"","\"Box him, don't brawl — stick and move.\"","\"He's tired — this is your round, take it to him!\""];
function txt(pool,atk,def){ return fill(pick(pool), lastN(atk.name), lastN(def.name)); }
function hurtTxt(atk,def,sev,punch,body){ const a=lastN(atk.name), d=lastN(def.name); punch=punch||'big shot';
  if(body) return sev>=2 ? `<span class='kd'>${a}'s body attack has ${d} folding up in agony!</span>` : `${a} rips a ${punch} — ${d} feels that one deep.`;
  return sev>=2 ? pick([`<span class='kd'>${a} lands a ${punch} and ${d} is out on his feet!</span>`,`<span class='kd'>${a} has ${d} badly hurt with that ${punch} — he's wobbling!</span>`])
                : pick([`${a} hurts ${d} with a ${punch}!`,`${a} snaps ${d}'s head back with a ${punch}.`,`${a} stuns ${d} with a crisp ${punch}.`]); }
function bigTxt(atk,def,punch,body){ const a=lastN(atk.name), d=lastN(def.name); punch=punch||'hard shot';
  if(body) return pick([`${a} digs a ${punch} downstairs.`,`${a} bangs a ${punch} into the ribs.`,`${a} invests to the body with a ${punch}.`]);
  return pick([`${a} lands a clean ${punch}.`,`${a} cracks home a ${punch}.`,`${a} catches ${d} with a ${punch}.`]); }
function commentary(tk,dLa,dLb,A,B){
  if(tk.rockB) return hurtTxt(A,B,tk.rockB,tk.btA,tk.bodyA);
  if(tk.rockA) return hurtTxt(B,A,tk.rockA,tk.btB,tk.bodyB);
  if(tk.bigA && !tk.bigB) return bigTxt(A,B,tk.btA,tk.bodyA);
  if(tk.bigB && !tk.bigA) return bigTxt(B,A,tk.btB,tk.bodyB);
  if(dLa+dLb===0) return pick(QUIET);
  const busier = dLa>=dLb*2+2?A : dLb>=dLa*2+2?B : null;
  if(busier) return txt(pick([BUSY,COMBO,TACTIC]),busier,busier===A?B:A);
  const ord = Math.random()<0.5?[A,B]:[B,A];
  return txt(pick([EVEN,JAB,BODY,CLINCH,DEFENSE,TACTIC]),ord[0],ord[1]);
}

// ---------- fouls & injuries ----------
const FOUL_TYPES=[{name:"a low blow",flagrant:true,butt:false},{name:"a clash of heads",flagrant:false,butt:true},{name:"holding and hitting",flagrant:false,butt:false},{name:"a punch on the break",flagrant:false,butt:false},{name:"a rabbit punch behind the head",flagrant:true,butt:false},{name:"a stray elbow",flagrant:false,butt:true},{name:"a shove to the canvas",flagrant:false,butt:false}];
const INJ={minor:[["bruising and swelling",21,49],["a minor cut",28,56],["a sore, swollen hand",35,70],["bruised ribs",30,60]],
  moderate:[["a cut needing stitches",70,140],["a damaged hand",90,180],["a cracked rib",84,168],["a badly swollen, closed eye",60,120]],
  major:[["a fractured eye socket",300,540],["a broken jaw",330,560],["a badly broken hand",300,520],["a detached retina",365,720]],
  career:[["a career-ending retinal detachment",99999,99999],["a severe, career-ending injury",99999,99999]]};
function injuryFor(vs, v, ctx, rng, loser){
  let base = loser?0.14:0.05;
  if(ctx.ko) base += loser?0.24:0.06; if(ctx.tko) base += loser?0.13:0.03;
  base += vs.tkd*0.05 + vs.cut*0.22 + Math.max(0,(v.age-32))*0.012;
  if(rng()>Math.min(0.85,base)) return null;
  let s=rng(), sev; if(s<0.66) sev='minor'; else if(s<0.91) sev='moderate'; else if(s<0.992) sev='major'; else sev='career';
  if(!loser && (sev==='major'||sev==='career') && rng()<0.75) sev='moderate';
  let pool=INJ[sev]; if(ctx.body && (sev==='minor'||sev==='moderate')) pool=pool.filter(o=>o[0].includes('rib'));
  const o=pick(pool.length?pool:INJ[sev]); const days = sev==='career'?99999 : Math.round(o[1]+rng()*(o[2]-o[1]));
  return {name:v.name, severity:sev, type:o[0], layoffDays:days, retires:sev==='career'};
}
function determineInjuries(res, sa, sb, rng){
  const out=[], ko=res.method==='KO', tko=res.method==='TKO', body=!!res.bodyStop;
  for(const [idx,st,f] of [[0,sa,res.A],[1,sb,res.B]]){
    const loser = res.winner!==null && res.winner!==idx;
    if(st.cut>=0.5){ const deep=st.cut>=0.75; out.push({name:f.name, type:`a cut ${st.cutLoc||'on the face'}${deep?' that needed serious stitching':' needing stitches'}`, severity:deep?'moderate':'minor', layoffDays:deep?70+Math.round(rng()*70):35+Math.round(rng()*35), retires:false}); }
    if(st.handHurt) out.push({name:f.name, type:'a damaged hand', severity:'moderate', layoffDays:90+Math.round(rng()*120), retires:false});
    const inj = injuryFor(st,f,loser?{ko,tko,body}:{ko:false,tko:false,body:false},rng,loser);
    if(inj && (inj.severity==='major'||inj.severity==='career')) out.push(inj); // serious trauma on top of cuts/hand
  }
  return out;
}
// Permanent attribute deltas a fighter carries forward — the hook for the career/universe simulation.
// Permanent attribute deltas — deliberately UNUSUAL. A fighter only ages from the ring when he
// either (a) takes a serious injury (a deep cut, a broken hand) or (b) goes through a genuine war
// or a brutal knockout. A routine win, loss or decision leaves no lasting mark.
function lastingEffects(res, sa, sb, rng){
  const out=[];
  for(const [idx,st,f] of [[0,sa,res.A],[1,sb,res.B]]){
    const loser = res.winner!==null && res.winner!==idx;
    // a deep cut that needed real stitching can occasionally leave scar tissue
    if(st.cut>=0.78 && st.cutLoc && rng()<0.22) out.push({name:f.name, note:`scar tissue ${st.cutLoc} — will cut more easily here`, attr:'CutResistance', delta:-(2+Math.round(rng()*3))});
    // a genuine hand break can cost a little power for good
    if(st.handHurt && rng()<0.28) out.push({name:f.name, note:'a hand that may never be 100% — power slightly diminished', attr:'Power', delta:-(2+Math.round(rng()*2))});
    // chin erosion — rare, only after being brutally knocked out (2+ knockdowns) or battered in a war
    const brutalKO = loser && res.method==='KO' && !res.bodyStop && st.tkd>=2;
    if((brutalKO && rng()<0.18) || (res.war && st.tkd>=1 && rng()<0.07))
      out.push({name:f.name, note: brutalKO?'a chin a fraction more fragile after that knockout':'a little more worn after that war', attr:'Chin', delta:-(1+Math.round(rng()*2))});
  }
  return out;
}
function layoffStr(d){ if(d>=99999) return "career-ending"; if(d>=365) return "~"+(d/365).toFixed(1).replace(/\.0$/,'')+" yr out"; if(d>=60) return "~"+Math.round(d/30)+" mo out"; return "~"+Math.round(d/7)+" wk out"; }

// ---------- engine ----------
function mkState(f){ return {f, r:f.r, fat:0, dmg:0, bodyDmg:0, cut:0, cutLoc:null, cutEye:false, swell:0, fouls:0, handHurt:false, handPenalty:0, rkd:0, tkd:0, rl:0, rw:0, _big:false, _rock:0, _lastBig:null, _bodyBig:false, _downBody:false, _handJustHurt:false}; }
function eff(s, rating){ const heart=(s.r.hrt-50)/250; const vis=(s.cutEye && s.cut>=0.5)?(s.cut-0.4)*0.10:0; return rating*Math.max(0.36, 1 - s.fat*0.30 - Math.max(0,s.dmg-heart)*0.35 - s.swell*0.08 - s.bodyDmg*0.10 - vis); }
function punchVolume(s, rng){ const basis=14 + s.r.agg*0.22 + eff(s,s.r.sta)*0.10; return Math.max(6, Math.round(basis*(0.85+rng()*0.30))); }
function throwPunch(att, def, edge, rng){
  const offense=eff(att,att.r.acc)*0.6+eff(att,att.r.spd)*0.4;
  const defense=eff(def,def.r.def_)*0.6+eff(def,def.r.spd)*0.4;
  let lp=0.55/(1+Math.exp(-(offense-defense)/14)); lp*=1+0.10*edge;
  if(rng()>=lp) return false;
  att.rl++;
  const powerShot=rng()<0.20+att.r.pow/500;
  if(!powerShot){ att.rw += 0.5+eff(att,att.r.acc)/220; att._lastBig=null; return true; }
  const toBody = rng()<0.27;
  const type = toBody ? pick(BODYSHOTS) : pick(HEADSHOTS);
  const powerEff=eff(att,att.r.pow)*(1-att.handPenalty), chin=eff(def,def.r.chn), clean=0.6+rng()*0.8;
  const force=(powerEff/Math.max(20,chin))*WCKO*clean;
  att._big=true; att._lastBig=type; att.rw += 1.6*(0.5+powerEff/200);
  if(!att.handHurt && !toBody && rng() < (att.r.pow/100)*0.0012*Math.max(0,force-1)){ att.handHurt=true; att.handPenalty=0.18+rng()*0.16; att._handJustHurt=true; }
  if(toBody){
    att._bodyBig=true;
    def.bodyDmg=Math.min(1.3, def.bodyDmg+force*0.028);
    def.fat=Math.min(1, def.fat+0.01+force*0.004);
    if(force>2.0 || def.bodyDmg>=0.55) def._rock=Math.max(def._rock, def.bodyDmg>=0.9?2:1);
    if(rng() < force*0.013*(0.4+def.bodyDmg)) return knockdown(def,rng,true) ? 'KObody' : true;
  } else {
    def.dmg=Math.min(1.3, def.dmg+force*0.030); def.swell=Math.min(1, def.swell+0.02+force*0.005);
    if(force>2.2 || def.dmg>=0.6) def._rock=Math.max(def._rock, def.dmg>=0.9?2:1);
    if(rng() < force*0.020*(0.5+def.dmg)) return knockdown(def,rng,false) ? 'KO' : true;
  }
  return true;
}
function knockdown(def, rng, body){
  def.rkd++; def.tkd++;
  if(body){ def.bodyDmg=Math.min(1.3,def.bodyDmg+0.15); def._downBody=true; } else def.dmg=Math.min(1.3,def.dmg+0.18);
  const base = body ? 0.85-def.bodyDmg*0.5 : 0.95-def.dmg*0.55;
  const rise=Math.max(0.12,Math.min(0.98, base+(def.r.con+def.r.hrt-100)/300));
  if(rng()>rise) return true;
  if(def.rkd>=3){ if(body)def.bodyDmg=1.0; else def.dmg=1.0; return false; }
  if(body) def.bodyDmg=Math.max(def.bodyDmg-0.10,0.40); else def.dmg=Math.max(def.dmg-0.10,0.45);
  return false;
}
function applyCuts(v, atk, rng){ const cc=(atk.rw/40)*(1-v.r.cut/130); if(rng()<cc){ const fresh=v.cut<=0.01; v.cut=Math.min(1, v.cut+0.12+rng()*0.22); if(fresh){ const L=pick(CUTLOCS); v.cutLoc=L.name; v.cutEye=L.eye; } } }
function cutWorsen(v, atk, rng){ if(v.cut<=0) return; const w=(atk.rw/60)*(1-v.r.cut/150); if(rng()<0.4+w) v.cut=Math.min(1, v.cut+0.06+rng()*0.12); }
function cutStop(s, round, rng){ if(s.cut<0.62) return false; return rng() < (s.cut-0.55)*0.55*Math.min(1,round/6); }
function scoreRound(sa, sb){ const m=sa.rw-sb.rw; let a=10,b=10; if(m>0)b=9; else if(m<0)a=9; a=Math.max(6,a-sa.rkd); b=Math.max(6,b-sb.rkd); const total=Math.abs(sa.rw)+Math.abs(sb.rw)+1; return {a,b,closeness:1-Math.min(1,Math.abs(m)/total)}; }
function recover(s){ s.fat=Math.min(1, s.fat+0.06+(100-s.r.sta)/900); s.dmg=Math.max(0, s.dmg-(0.07+s.r.con/500)); s.bodyDmg=Math.max(0, s.bodyDmg-(0.05+s.r.con/700)); }
function clk(sec){ sec=Math.max(0,Math.round(sec)); return Math.floor(sec/60)+':'+String(sec%60).padStart(2,'0'); }
function finObj(rStop, rWin, foul){
  if(rStop==='DQ') return {method:'DQ',winner:rWin,foul};
  if(rStop==='cut') return {method:'cut',winner:rWin};
  return {method: rStop.startsWith('KO')?'KO':'TKO', winner:rWin, body:rStop.includes('body')};
}

function simulate(A, B, scheduled, rng){
  const sa=mkState(A), sb=mkState(B);
  const edgeA=advantage(A.style,B.style), edgeB=-edgeA;
  const rounds=[]; const jt=[[0,0],[0,0],[0,0]];
  let stop=null, winner=null, endRound=scheduled, bodyStop=false;
  for(let round=1; round<=scheduled; round++){
    sa.rkd=sb.rkd=0; sa.rl=sb.rl=0; sa.rw=sb.rw=0;
    const cutA0=sa.cut, cutB0=sb.cut, swA0=sa.swell, swB0=sb.swell;
    const attA=punchVolume(sa,rng), attB=punchVolume(sb,rng), ex=Math.max(attA,attB);
    const bnd=[]; for(let s=1;s<=12;s++) bnd.push(Math.max(s, Math.round(s*ex/12)));
    const ticks=[]; let seg=0; let rStop=null, rWin=null;
    const snap=(sec,fin)=>{ const t={clock:clk(sec),la:sa.rl,lb:sb.rl,ka:sa.rkd,kb:sb.rkd,dA:sa.dmg,dB:sb.dmg,bdA:sa.bodyDmg,bdB:sb.bodyDmg,cA:sa.cut,cB:sb.cut,swA:sa.swell,swB:sb.swell,
      bigA:sa._big,bigB:sb._big,rockA:sa._rock,rockB:sb._rock,btA:sa._lastBig,btB:sb._lastBig,bodyA:sa._bodyBig,bodyB:sb._bodyBig,downBodyA:sa._downBody,downBodyB:sb._downBody,handA:sa._handJustHurt,handB:sb._handJustHurt,fin};
      sa._big=sb._big=false; sa._rock=sb._rock=0; sa._lastBig=sb._lastBig=null; sa._bodyBig=sb._bodyBig=false; sa._downBody=sb._downBody=false; sa._handJustHurt=sb._handJustHurt=false; return t; };
    for(let e=0;e<ex;e++){
      if(e<attA){const t=throwPunch(sa,sb,edgeA,rng); if(t==='KO'){rStop='KO';rWin=0;} else if(t==='KObody'){rStop='KObody';rWin=0;}}
      if(!rStop && sb.dmg>=1.0){rStop='TKO';rWin=0;}
      if(!rStop && sb.bodyDmg>=1.0){rStop='TKObody';rWin=0;}
      if(!rStop && e<attB){const t=throwPunch(sb,sa,edgeB,rng); if(t==='KO'){rStop='KO';rWin=1;} else if(t==='KObody'){rStop='KObody';rWin=1;}}
      if(!rStop && sa.dmg>=1.0){rStop='TKO';rWin=1;}
      if(!rStop && sa.bodyDmg>=1.0){rStop='TKObody';rWin=1;}
      while(!rStop && seg<12 && (e+1)>=bnd[seg]){ ticks.push(snap(180-15*(seg+1),null)); seg++; }
      if(rStop){ ticks.push(snap(180*(1-(e+1)/ex), finObj(rStop,rWin))); break; }
    }
    while(!rStop && seg<12){ ticks.push(snap(180-15*(seg+1),null)); seg++; }
    if(!rStop){ applyCuts(sa,sb,rng); applyCuts(sb,sa,rng); cutWorsen(sa,sb,rng); cutWorsen(sb,sa,rng); }
    let foul=null;
    if(!rStop && rng()<0.03){
      const who=rng()<0.5?0:1, f=who===0?sa:sb, opp=who===0?sb:sa, ty=pick(FOUL_TYPES);
      f.fouls++; let deduct=false, dq=false;
      if(ty.butt){ const fresh=opp.cut<=0.01; opp.cut=Math.min(1, opp.cut+0.18+rng()*0.20); if(fresh){ const L=pick(CUTLOCS); opp.cutLoc=L.name; opp.cutEye=L.eye; } }
      if(ty.flagrant && rng()<0.05) dq=true;
      else if((ty.flagrant && rng()<0.18) || f.fouls>=3 || rng()<0.08) deduct=true;
      foul={who, type:ty.name, butt:ty.butt, deduct, dq};
      if(dq){ rStop='DQ'; rWin=1-who; }
    }
    if(!rStop){ if(cutStop(sa,round,rng)){rStop='cut'; rWin=1;} else if(cutStop(sb,round,rng)){rStop='cut'; rWin=0;} }
    if(foul){ const idx=Math.min(ticks.length-1, Math.floor(rng()*ticks.length)); ticks[idx].foul=foul; }
    if(rStop && !ticks.some(t=>t.fin)) ticks[ticks.length-1].fin = rStop==='DQ' ? {method:'DQ',winner:rWin,foul} : rStop==='cut' ? {method:'cut',winner:rWin} : finObj(rStop,rWin);
    const sc=scoreRound(sa,sb); let ra=sc.a, rb=sc.b;
    if(foul&&foul.deduct){ if(foul.who===0) ra=Math.max(5,ra-1); else rb=Math.max(5,rb-1); }
    let jr=null;
    if(!rStop){ jr=[]; for(let j=0;j<3;j++){ let a=ra,b=rb; if(a!==b && rng()<sc.closeness*0.35){const t=a;a=b;b=t;} jr.push([a,b]); jt[j][0]+=a; jt[j][1]+=b; } }
    rounds.push({round, ticks, sa:ra, sb:rb, ka:sa.rkd, kb:sb.rkd, jr,
      cutNewA:sa.cut>cutA0+0.01, cutNewB:sb.cut>cutB0+0.01, cutA:sa.cut, cutB:sb.cut, cutLocA:sa.cutLoc, cutLocB:sb.cutLoc,
      swNewA:sa.swell>0.4&&swA0<=0.4, swNewB:sb.swell>0.4&&swB0<=0.4, hurtEndA:sa.dmg>=0.8, hurtEndB:sb.dmg>=0.8});
    if(rStop){ stop=rStop; winner=rWin; endRound=round; bodyStop=rStop.includes('body'); break; }
    recover(sa); recover(sb);
  }
  let outcome, method; const cards=jt;
  if(stop){ outcome=stop; method = stop==='cut'?'cut' : stop.startsWith('KO')?'KO' : stop.startsWith('TKO')?'TKO' : stop; }
  else {
    let jA=0,jB=0,jE=0;
    for(let j=0;j<3;j++){ const a=jt[j][0], b=jt[j][1]; if(a>b)jA++; else if(b>a)jB++; else jE++; }
    if(jA>=2){ winner=0; outcome='Decision'; method=jA===3?'UD':jB===1?'SD':'MD'; }
    else if(jB>=2){ winner=1; outcome='Decision'; method=jB===3?'UD':jA===1?'SD':'MD'; }
    else { winner=null; outcome='Draw'; method = jE===3?'unanimous draw':(jA===1&&jB===1)?'split draw':'majority draw'; }
  }
  const res={A,B,scheduled,rounds,outcome,method,endRound,winner,cards,kdA:sa.tkd,kdB:sb.tkd,bodyStop};
  const hurtRounds=rounds.filter(r=>r.hurtEndA||r.hurtEndB).length;
  res.war = (sa.tkd+sb.tkd>=3) || (sa.tkd>=1 && sb.tkd>=1) || hurtRounds>=4; // a genuine war
  res.injuries = determineInjuries(res, sa, sb, rng);
  res.lasting = lastingEffects(res, sa, sb, rng);
  return res;
}

// ---------- UI ----------
const $=id=>document.getElementById(id);
const selA=$('selA'), selB=$('selB'), feed=$('feed'), banner=$('banner');
FIGHTERS.forEach(f=>{ selA.add(new Option(f.name,f.name)); selB.add(new Option(f.name,f.name)); });
selA.value = byName['Muhammad Ali'] ? 'Muhammad Ali' : FIGHTERS[0].name;
selB.value = byName['Mike Tyson'] ? 'Mike Tyson' : FIGHTERS[1].name;
const _pp=new URLSearchParams(location.search);
if(_pp.get('a')&&byName[_pp.get('a')]) selA.value=_pp.get('a');
if(_pp.get('b')&&byName[_pp.get('b')]) selB.value=_pp.get('b');
function flagEmoji(c){ const F={'USA':'🇺🇸','England':'🏴','Canada':'🇨🇦','Ukraine':'🇺🇦','Russia':'🇷🇺','Germany':'🇩🇪','Italy':'🇮🇹','Sweden':'🇸🇪','Argentina':'🇦🇷','Cuba':'🇨🇺','South Africa':'🇿🇦','Nigeria':'🇳🇬','New Zealand':'🇳🇿','Wales':'🏴','Poland':'🇵🇱'}; return c&&F[c]?F[c]+' ':''; }
function initials(n){ const p=n.replace(/"/g,'').split(' ').filter(Boolean); return ((p[0]?p[0][0]:'')+(p.length>1?p[p.length-1][0]:'')).toUpperCase(); }
function corner(f, side){ const el=$(side==='A'?'cA':'cB'); el.className='corner '+side; el.style.boxShadow='';
  el.innerHTML=`<div class="chead">
      <div class="avatar">${initials(f.name)}</div>
      <div><div class="nm">${f.name}</div><div class="sub">${flagEmoji(f.country)}${f.style}</div><span class="ovr">${f.ovr} OVR</span></div>
    </div>
    <div class="bars">
      <div class="hpwrap"><div class="hp" id="hp${side}" style="width:100%"></div></div>
      <div class="bodybar"><div class="bodyfill" id="bd${side}" style="width:0%"></div></div>
    </div>
    <div class="crow"><span>${f.record}</span><span>Landed <b id="land${side}">0</b></span></div>
    <div class="status" id="st${side}"></div>`; }
function refreshTape(){ corner(byName[selA.value],'A'); corner(byName[selB.value],'B'); }
selA.onchange=refreshTape; selB.onchange=refreshTape; refreshTape();
function setHp(side, dmg){ const hp=$('hp'+side); const v=Math.max(0,Math.min(1,1-dmg)); hp.style.width=(v*100)+'%';
  hp.style.background = v>0.6?'linear-gradient(90deg,#2ecb6f,#9bd64a)': v>0.3?'linear-gradient(90deg,#e0b13a,#e6d24a)':'linear-gradient(90deg,#d8453a,#e0703a)'; }
function setBody(side, bd){ $('bd'+side).style.width=Math.max(0,Math.min(1,bd))*100+'%'; }
function setStatus(side, cut, swell, kd, body){ const bits=[]; if(kd) bits.push(`<span class="kd">⬇ ${kd} down</span>`);
  if(cut>=0.3) bits.push(`<span class="cut">🩸 ${cut>=0.75?'bad cut':'cut'}</span>`); if(swell>=0.4) bits.push(`<span class="swell">eye swelling</span>`); if(body>=0.45) bits.push(`<span class="bdy">hurt to body</span>`);
  $('st'+side).innerHTML=bits.join(' · '); }
function line(html){ const d=document.createElement('div'); d.className='l'; d.innerHTML=html; feed.appendChild(d); feed.scrollTop=feed.scrollHeight; }
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
function flashHurt(side,bad){ const el=$('c'+side); el.style.boxShadow='inset 0 0 0 2px '+(bad?'#ff4030':'#ffae3a'); setTimeout(()=>{el.style.boxShadow='';},700); }

let scTot=[[0,0],[0,0],[0,0]];
function cellHtml(a,b){ const cls=a>b?'A':b>a?'B':'e'; return `<span class="sccell"><span class="${cls}">${a}-${b}</span></span>`; }
function resetScorecard(){ scTot=[[0,0],[0,0],[0,0]]; $('scbody').innerHTML=''; for(let j=0;j<3;j++) $('sct'+j).textContent='—'; }
function addScoreRow(round, jr){
  for(let j=0;j<3;j++){ scTot[j][0]+=jr[j][0]; scTot[j][1]+=jr[j][1]; }
  const tr=document.createElement('tr');
  tr.innerHTML=`<td class="rd2">${round}</td>`+jr.map(c=>`<td>${cellHtml(c[0],c[1])}</td>`).join('');
  $('scbody').appendChild(tr);
  for(let j=0;j<3;j++) $('sct'+j).innerHTML=cellHtml(scTot[j][0],scTot[j][1]);
  const box=$('scbody').closest('.sc-table'); if(box) box.scrollTop=box.scrollHeight;
}

let running=false;
async function runFight(){
  if(running) return; running=true; $('go').disabled=true; banner.classList.remove('show'); feed.innerHTML=''; resetScorecard();
  const A=byName[selA.value], B=byName[selB.value], rounds=+$('rounds').value;
  refreshTape();
  SEED=(SEED*1664525+1013904223)>>>0;
  const res=simulate(A,B,rounds,mulberry32(SEED));
  const speed=+$('speed').value; let wonA=0, wonB=0, totA=0, totB=0;
  line(`<div class="rdh">🔔 ${A.name} vs ${B.name} — ${rounds} rounds. Seconds out!</div>`);
  for(const rd of res.rounds){
    $('rdlabel').textContent='Round '+rd.round+' of '+res.scheduled;
    line(`<div class="rdh">— Round ${rd.round} —</div>`);
    let pLa=0,pLb=0,pKa=0,pKb=0, stoppedHere=false;
    for(const tk of rd.ticks){
      $('clock').textContent=tk.clock;
      const dLa=tk.la-pLa, dLb=tk.lb-pLb; totA+=dLa; totB+=dLb;
      $('landA').textContent=totA; $('landB').textContent=totB;
      setHp('A',tk.dA); setHp('B',tk.dB); setBody('A',tk.bdA); setBody('B',tk.bdB);
      setStatus('A',tk.cA,tk.swA,tk.ka,tk.bdA); setStatus('B',tk.cB,tk.swB,tk.kb,tk.bdB);
      const ts=`<span class="t">R${rd.round} ${tk.clock}</span>`; let downed=false;
      if(tk.ka>pKa){ line(`${ts}<span class="kd">⬇ ${tk.downBodyA?`DOWN to the body! ${A.name} sinks to the canvas clutching his ribs!`:`DOWN! ${A.name} hits the canvas! The count is on…`}</span>`); flashHurt('A',true); downed=true; }
      if(tk.kb>pKb){ line(`${ts}<span class="kd">⬇ ${tk.downBodyB?`DOWN to the body! ${B.name} sinks to the canvas clutching his ribs!`:`DOWN! ${B.name} hits the canvas! The count is on…`}</span>`); flashHurt('B',true); downed=true; }
      if(tk.foul){ const fl=tk.foul, fr=fl.who===0?A:B, op=fl.who===0?B:A;
        if(fl.dq) line(`${ts}<span class="foul">⚠ ${fr.name} disqualified for ${fl.type}!</span>`);
        else if(fl.deduct) line(`${ts}<span class="foul">⚠ ${fr.name} — ${fl.type}. Point deducted by the referee.</span>`);
        else line(`${ts}<span class="foul">⚠ ${fr.name} warned for ${fl.type}.</span>`);
        if(fl.butt) line(`${ts}<span class="cut">The clash of heads has opened a cut on ${op.name}.</span>`);
      }
      if(!downed && !tk.foul) line(ts + commentary(tk,dLa,dLb,A,B));
      if(tk.handA) line(`${ts}<span class="inj">${A.name} may have hurt his hand on that shot — he's shaking it out.</span>`);
      if(tk.handB) line(`${ts}<span class="inj">${B.name} may have hurt his hand on that shot — he's shaking it out.</span>`);
      if((tk.rockA>=2||tk.rockB>=2) && Math.random()<0.45) line(`${ts}<span class="crowd">${pick(DRAMA)}</span>`);
      if(tk.rockB) flashHurt('B',tk.rockB>=2); if(tk.rockA) flashHurt('A',tk.rockA>=2);
      pLa=tk.la; pLb=tk.lb; pKa=tk.ka; pKb=tk.kb;
      if(tk.fin){ await finish(res, tk.fin); return; }
      if(speed) await sleep(speed);
    }
    if(rd.cutNewB) line(`<span class="t">R${rd.round}</span><span class="cut">A cut has opened ${rd.cutLocB||'on the face'} for ${B.name} — ${rd.cutB>=0.7?'and it looks deep.':'the corner will need to work on that.'}</span>`);
    if(rd.cutNewA) line(`<span class="t">R${rd.round}</span><span class="cut">A cut has opened ${rd.cutLocA||'on the face'} for ${A.name} — ${rd.cutA>=0.7?'a nasty gash.':'blood trickling now.'}</span>`);
    if(rd.cutB>=0.7 && !rd.cutNewB) line(`<span class="t">R${rd.round}</span><span class="doc">The ringside doctor takes a look at ${B.name}'s cut between rounds.</span>`);
    if(rd.cutA>=0.7 && !rd.cutNewA) line(`<span class="t">R${rd.round}</span><span class="doc">The ringside doctor takes a look at ${A.name}'s cut between rounds.</span>`);
    if(rd.swNewB) line(`<span class="t">R${rd.round}</span><span class="cut">${B.name}'s eye is starting to swell shut.</span>`);
    if(rd.swNewA) line(`<span class="t">R${rd.round}</span><span class="cut">${A.name}'s eye is starting to swell shut.</span>`);
    if(rd.hurtEndA||rd.hurtEndB) line(`<span class="t">R${rd.round}</span><span class="kd">…and ${rd.hurtEndA?A.name:B.name} is saved by the bell!</span>`);
    if(rd.sa>rd.sb) wonA++; else if(rd.sb>rd.sa) wonB++;
    if(rd.jr) addScoreRow(rd.round, rd.jr);
    line(`<span class="t">R${rd.round}</span>scored ${rd.sa}-${rd.sb} ${rd.sa>rd.sb?A.name:rd.sb>rd.sa?B.name:'even'}`);
    $('scorebug').textContent=`Rounds: ${lastN(A.name)} ${wonA} – ${wonB} ${lastN(B.name)}`;
    if(rd.round<res.scheduled && Math.random()<0.35) line(`<span class="t">corner</span><span class="corner">${pick(CORNER)}</span>`);
    if(speed) await sleep(Math.min(speed,1200));
  }
  await finish(res, null);
}
async function finish(res, fin){
  const A=res.A, B=res.B; let msg;
  const cardsHtml = res.cards.length ? 'Judges &mdash; '+res.cards.map((c,i)=>`J${i+1} ${c[0]}&ndash;${c[1]}`).join(' &middot; ') : '';
  if(fin && fin.method==='DQ'){ const w=fin.winner===0?A:B, l=fin.winner===0?B:A; msg=`${w.name} def. ${l.name} by disqualification, round ${res.endRound}`; line(`<div class="res">🚫 ${msg}</div>`); }
  else if(fin && fin.method==='cut'){ const w=fin.winner===0?A:B, l=fin.winner===0?B:A; msg=`${w.name} def. ${l.name} by TKO (cut), round ${res.endRound}`; line(`<div class="res">🩸 The cut is too severe — the doctor waves it off! ${msg}</div>`); }
  else if(fin && fin.method==='KO'){ const w=fin.winner===0?A:B, l=fin.winner===0?B:A; msg=`${w.name} def. ${l.name} by ${fin.body?'KO to the body':'knockout'}, round ${res.endRound}`; line(`<div class="res">🥊 ${msg}</div>`); }
  else if(fin && fin.method==='TKO'){ const w=fin.winner===0?A:B, l=fin.winner===0?B:A; msg=`${w.name} def. ${l.name} by TKO${fin.body?' (body)':''}, round ${res.endRound}`; line(`<div class="res">🥊 ${msg}</div>`); }
  else {
    line(`<div class="rdh">📋 We go to the judges' scorecards…</div>`);
    line(`<span class="t">JUDGES</span>${cardsHtml}`);
    if(res.winner===null){ msg=`The bout is a draw — ${res.method}`; line(`<div class="res">🤝 ${msg}</div>`); }
    else { const w=res.winner===0?A:B, l=res.winner===0?B:A; const dn={UD:'unanimous',MD:'majority',SD:'split'}[res.method]||''; msg=`${w.name} def. ${l.name} by ${dn} decision`; line(`<div class="res">🥊 ${msg}</div>`); }
  }
  let after='';
  if(res.war) line(`<span class="crowd">That was an absolute war — both men left everything in the ring tonight.</span>`);
  if(res.injuries.length){ res.injuries.forEach(i=>line(`<span class="inj">🏥 ${i.name} suffered ${i.type} — ${layoffStr(i.layoffDays)}${i.retires?'. He announces his retirement.':'.'}</span>`));
    after = 'Aftermath — '+res.injuries.map(i=>`${lastN(i.name)}: ${i.type} (${layoffStr(i.layoffDays)})`).join(' · '); }
  else line(`<span class="inj">🏥 Both men come through it without serious injury.</span>`);
  if(res.lasting && res.lasting.length){ res.lasting.forEach(x=>line(`<span class="inj">📉 ${x.name} carries it forward: ${x.note} (${x.attr} ${x.delta})</span>`));
    after += (after?'  ||  ':'')+'Going forward — '+res.lasting.map(x=>`${lastN(x.name)} ${x.attr} ${x.delta}`).join(', '); }
  banner.innerHTML=`<div class="m">${msg}</div>${cardsHtml?`<div class="cards">${cardsHtml}</div>`:''}${after?`<div class="aftermath">${after}</div>`:''}`;
  banner.classList.add('show'); $('go').disabled=false; running=false;
}
$('go').onclick=runFight;
</script>
</body>
</html>
""";
}
