using System.IO;
using System.Windows.Media;
using System.Windows.Threading;

namespace BoxingSim.Desktop;

/// <summary>How big the night is. Drives how many people are in the building and how loudly they react — a
/// four-rounder in a club room should not sound like a unification.</summary>
public enum Occasion { Club, Ranked, Title, Unification }

/// <summary>The sound of fight night: a crowd that is always there, and events layered over the top.
///
/// Everything is synthesised at first use — no audio files ship with the app, nothing to license. The pieces:
///
///   BED      a continuous crowd, looping. Its level is set by the occasion (a club hall murmurs, a
///            unification roars) and it SWELLS on events, then settles back.
///   THUD     every hard punch that lands.
///   OOH      the noise a crowd makes when a man gets hurt — the intake of breath, not a cheer.
///   ROAR     a knockdown or a stoppage.
///   BELL     between rounds, and three times at the end.
///
/// These genuinely layer. The previous implementation used SoundPlayer, which plays one sound and cancels
/// whatever was already going — a thud would kill the crowd bed. MediaPlayer gives one instance per layer,
/// each with its own volume, all sounding together.</summary>
public static class Sfx
{
    private const int Rate = 44100;

    public static bool Enabled
    {
        get => _enabled;
        set { _enabled = value; if (!value) StopBed(); }
    }
    private static bool _enabled = true;

    // ---- layers ----
    // Two beds run at once, not one: a calm room and a roaring one, CROSSFADED by how excited the crowd is.
    // Riding a single bed's volume only makes the same murmur louder; a real crowd changes character, not just
    // level. This is what sports-game audio does with intensity stems, and it is the difference between
    // "louder murmur" and "the place is going up".
    //
    // The two loops are different lengths (7s and 11s) so they drift against each other and the repeat never
    // becomes audible — the same trick as asynchronous ambience loops.
    private static MediaPlayer? _bedCalm, _bedHot;
    private static readonly MediaPlayer[] _oneShots = new MediaPlayer[6];   // a pool, so rapid hits overlap
    private static int _next;

    private static double _room;         // how full the building is (the occasion)
    private static double _excitement;   // 0 = between rounds in a quiet hall, 1 = a man is down
    private static DispatcherTimer? _decay;

    private static readonly Dictionary<string, string> _files = new();

    /// <summary>Set the room before the first bell.</summary>
    public static void SetOccasion(Occasion o)
    {
        _room = o switch
        {
            Occasion.Club => 0.16,          // a few hundred people, mostly quiet
            Occasion.Ranked => 0.30,
            Occasion.Title => 0.50,
            Occasion.Unification => 0.72,   // a full arena, humming before a punch is thrown
            _ => 0.3
        };
        ApplyMix();
    }

    /// <summary>Bring the crowd up and keep it there until StopBed.</summary>
    public static void StartBed(Occasion o)
    {
        if (!_enabled) return;
        SetOccasion(o);
        _excitement = 0;
        try
        {
            bool dense = _room >= 0.45;
            _bedCalm = Loop(_bedCalm, dense ? "bed-big-calm" : "bed-small-calm", () => CrowdBed(dense, 0.0));
            _bedHot = Loop(_bedHot, dense ? "bed-big-hot" : "bed-small-hot", () => CrowdBed(dense, 0.9));
            ApplyMix();

            _decay ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _decay.Tick -= Settle;
            _decay.Tick += Settle;
            _decay.Start();
        }
        catch { /* no audio device — the fight carries on in silence */ }
    }

    private static MediaPlayer Loop(MediaPlayer? p, string key, Func<byte[]> make)
    {
        p ??= new MediaPlayer();
        p.MediaEnded -= Rewind;
        p.MediaEnded += Rewind;
        p.Open(new Uri(FileFor(key, make)));
        p.Play();
        return p;
    }

    private static void Rewind(object? s, EventArgs e)
    {
        if (s is MediaPlayer p) { try { p.Position = TimeSpan.Zero; p.Play(); } catch { } }
    }

    public static void StopBed()
    {
        try { _decay?.Stop(); _bedCalm?.Stop(); _bedHot?.Stop(); } catch { }
    }

    /// <summary>Crossfade the two beds by excitement, and scale the pair by how full the building is.</summary>
    private static void ApplyMix()
    {
        try
        {
            double e = Math.Clamp(_excitement, 0, 1);
            // Equal-power, so the total loudness stays steady through the crossfade instead of dipping.
            if (_bedCalm is not null) _bedCalm.Volume = Math.Clamp(_room * Math.Cos(e * Math.PI / 2), 0, 1);
            if (_bedHot is not null) _bedHot.Volume = Math.Clamp(_room * Math.Sin(e * Math.PI / 2) * 1.25, 0, 1);
        }
        catch { }
    }

    /// <summary>Let the crowd come back down after a swell.</summary>
    private static void Settle(object? s, EventArgs e)
    {
        if (_excitement <= 0.001) return;
        _excitement *= 0.955;
        if (_excitement < 0.005) _excitement = 0;
        ApplyMix();
    }

    /// <summary>Lift the crowd, by an amount matched to what just happened.</summary>
    private static void Swell(double amount)
    {
        _excitement = Math.Clamp(_excitement + amount, 0, 1);
        ApplyMix();
    }

    // ---- one-shots ----

    public static void Bell() => Fire("bell", () => BellTone(0.9, 1), 0.55);
    public static void FinalBell() => Fire("bell3", () => BellTone(1.9, 3), 0.6, swell: 0.8);
    public static void Thud(double force = 1.0) => Fire("thud", Punch, 0.30 + 0.25 * force, swell: 0.10 * force);
    public static void Ooh() => Fire("ooh", CrowdOoh, 0.6, swell: 0.40);
    public static void Roar() => Fire("roar", CrowdRoar, 0.75, swell: 1.0);

    private static void Fire(string key, Func<byte[]> make, double volume, double swell = 0)
    {
        if (!_enabled) return;
        try
        {
            var p = _oneShots[_next] ??= new MediaPlayer();
            _next = (_next + 1) % _oneShots.Length;
            p.Open(new Uri(FileFor(key, make)));
            p.Volume = Math.Clamp(volume, 0, 1);
            p.Play();
            if (swell > 0) Swell(swell);
        }
        catch { }
    }

    /// <summary>MediaPlayer needs a URI, so each synthesised tone is written once to temp and reused.</summary>
    private static string FileFor(string key, Func<byte[]> make)
    {
        if (_files.TryGetValue(key, out var existing) && File.Exists(existing)) return existing;
        var dir = Path.Combine(Path.GetTempPath(), "BoxingSim.sfx");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, key + ".wav");
        if (!File.Exists(path)) File.WriteAllBytes(path, make());
        _files[key] = path;
        return path;
    }

    // ---- synthesis ----

    /// <summary>A crowd, built the way a crowd actually is: out of VOICES.
    ///
    /// The first attempt filtered white noise, which is why it sounded like rain. Noise has no formant
    /// structure, and formants are the whole reason the ear hears "people" rather than "hiss". Real games
    /// sidestep this by using recordings and multiplying them with pitch-shift, chorus and unison plugins —
    /// with no recordings to hand, the nearest honest equivalent is to synthesise the individuals and let a
    /// crowd emerge from the pile.
    ///
    /// Each voice is a glottal pulse train at its own pitch, pushed through two formant resonators tuned to a
    /// vowel. Hundreds of them at random pitches and entry times give the beating, uneven texture of a room.
    /// A thin noise floor underneath is the air and the shuffling; the voices sit on top of it.</summary>
    private static byte[] CrowdBed(bool dense, double excitement = 0.0)
    {
        double seconds = dense ? 11.0 : 7.0;   // co-prime-ish lengths so two layers rarely repeat together
        int n = (int)(Rate * seconds);
        var buf = new double[n];
        var rng = new Random(dense ? 11 : 29);

        // The air in the room. A whisper under the voices, not a rumble over them — the first attempt put
        // half its energy below 120 Hz, which the ear reads as weather rather than people.
        double lp = 0;
        for (int i = 0; i < n; i++)
        {
            lp += ((rng.NextDouble() * 2 - 1) - lp) * 0.08;
            buf[i] += lp * 0.05;
        }

        // The people. More of them in an arena; more of them shouting rather than talking when excited.
        int voices = (int)((dense ? 340 : 120) * (1.0 + excitement * 0.8));
        for (int v = 0; v < voices; v++)
        {
            int at = rng.Next(n);
            double dur = 0.25 + rng.NextDouble() * (excitement > 0.4 ? 1.1 : 0.55);
            int len = Math.Min((int)(dur * Rate), n - at);
            if (len < 400) continue;

            // A voice: pitch, a vowel, and how hard it is being used.
            double f0 = (excitement > 0.4 ? 150 : 105) * (0.72 + rng.NextDouble() * 0.85);
            var (f1, f2) = Vowel(rng, excitement);
            double amp = (0.05 + rng.NextDouble() * 0.10) * (dense ? 1.0 : 1.5);

            var r1 = new Resonator(f1, 90);
            var r2 = new Resonator(f2, 130);
            double phase = rng.NextDouble();
            for (int i = 0; i < len; i++)
            {
                double t = i / (double)Rate;
                // Glottal pulses: a train of narrow impulses, which is what gives a voice its buzz.
                phase += f0 / Rate;
                double drive = 0;
                if (phase >= 1.0) { phase -= 1.0; drive = 1.0; }
                drive += (rng.NextDouble() * 2 - 1) * 0.03;      // breath

                double voiced = r1.Next(drive) * 0.7 + r2.Next(drive) * 0.45;
                // Ease in and out so nobody starts or stops mid-shout.
                double env = Math.Sin(Math.PI * i / len);
                buf[at + i] += voiced * env * amp;
            }
        }

        // Claps, which is what you actually pick out in a small hall. An arena swallows them.
        int claps = dense ? 55 : 150;
        for (int c = 0; c < claps; c++)
        {
            int at = rng.Next(n - 3000);
            double amp = (dense ? 0.09 : 0.30) * (0.5 + rng.NextDouble());
            for (int i = 0; i < 2200; i++)
            {
                double t = i / (double)Rate;
                buf[at + i] += (rng.NextDouble() * 2 - 1) * Math.Exp(-95 * t) * amp;
            }
        }

        // Strip everything under ~170 Hz. A voice's energy lives in its formants, not its fundamental, and
        // sub-bass in a crowd bed only ever muddies it — this is the single biggest step from "noise" to
        // "people". Measured: the voice band goes from a quarter of the signal to the majority of it.
        HighPass(buf, 170);
        Normalise(buf, dense ? 0.72 : 0.55);
        FadeSeam(buf);
        return Wav(buf);
    }

    /// <summary>Vowel formant pairs. A calm room is talking ("uh", "er"); an excited one is open-mouthed
    /// ("ah", "oh"), which is both louder and brighter — the shift is audible even at the same volume.</summary>
    private static (double F1, double F2) Vowel(Random rng, double excitement)
    {
        (double, double)[] calm = { (500, 1500), (450, 1100), (600, 1000), (400, 900) };      // uh / er / o
        (double, double)[] loud = { (730, 1100), (700, 1200), (640, 1200), (800, 1150) };     // ah / aw
        var set = rng.NextDouble() < excitement ? loud : calm;
        var (f1, f2) = set[rng.Next(set.Length)];
        // Nobody's throat is identical; the spread is what stops it sounding like one person multiplied.
        return (f1 * (0.9 + rng.NextDouble() * 0.2), f2 * (0.9 + rng.NextDouble() * 0.2));
    }

    /// <summary>A two-pole resonator — the cheapest thing that behaves like a vocal tract formant.</summary>
    private sealed class Resonator
    {
        private readonly double _a0, _b1, _b2;
        private double _y1, _y2;
        public Resonator(double freq, double bandwidth)
        {
            double r = Math.Exp(-Math.PI * bandwidth / Rate);
            double theta = 2 * Math.PI * freq / Rate;
            _b1 = 2 * r * Math.Cos(theta);
            _b2 = -r * r;
            _a0 = (1 - r) * Math.Sqrt(1 - 2 * r * Math.Cos(2 * theta) + r * r);
        }
        public double Next(double x)
        {
            double y = _a0 * x + _b1 * _y1 + _b2 * _y2;
            _y2 = _y1; _y1 = y;
            return y;
        }
    }

    /// <summary>A one-pole high-pass. Cheap, and enough to take the rumble out from under a crowd.</summary>
    private static void HighPass(double[] buf, double cutoffHz)
    {
        double a = 1.0 / (1.0 + 2 * Math.PI * cutoffHz / Rate);
        double prevIn = 0, prevOut = 0;
        for (int i = 0; i < buf.Length; i++)
        {
            double x = buf[i];
            double y = a * (prevOut + x - prevIn);
            prevIn = x; prevOut = y;
            buf[i] = y;
        }
    }

    /// <summary>Scale to a target peak. Hundreds of summed voices land anywhere, so fixing the level after the
    /// fact is more reliable than guessing per-voice gains.</summary>
    private static void Normalise(double[] buf, double target)
    {
        double peak = 0;
        foreach (var d in buf) peak = Math.Max(peak, Math.Abs(d));
        if (peak <= 1e-9) return;
        double g = target / peak;
        for (int i = 0; i < buf.Length; i++) buf[i] *= g;
    }

    /// <summary>Fade both ends to silence so a looping bed doesn't click on the seam.</summary>
    private static void FadeSeam(double[] buf)
    {
        int fade = Rate / 4;
        for (int i = 0; i < fade && i < buf.Length / 2; i++)
        {
            double g = i / (double)fade;
            buf[i] *= g;
            buf[buf.Length - 1 - i] *= g;
        }
    }

    /// <summary>The "ooooh" — a crowd drawing breath. Voiced, not noisy: a low cluster of tones sliding up
    /// slightly, which is what a room full of people sounds like reacting together.</summary>
    private static byte[] CrowdOoh()
    {
        int n = (int)(Rate * 1.35);
        var buf = new double[n];
        var rng = new Random(5);
        // A spread of nearby pitches — a crowd is never in unison, and the spread is what makes it a crowd.
        double[] f = { 138, 146, 152, 161, 174, 183 };
        var detune = f.Select(_ => 0.985 + rng.NextDouble() * 0.03).ToArray();
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)Rate;
            double rise = 1.0 + 0.06 * Math.Min(1, t / 0.5);        // the pitch lifts as it swells
            double env = Math.Min(1, t / 0.18) * Math.Exp(-1.7 * Math.Max(0, t - 0.35));
            double v = 0;
            for (int k = 0; k < f.Length; k++)
                v += Math.Sin(2 * Math.PI * f[k] * detune[k] * rise * t) / f.Length;
            // A breath of noise on top stops it sounding like an organ chord.
            v += (rng.NextDouble() * 2 - 1) * 0.16;
            buf[i] = v * env * 0.75;
        }
        return Wav(buf);
    }

    private static byte[] CrowdRoar()
    {
        int n = (int)(Rate * 2.4);
        var buf = new double[n];
        var rng = new Random(7);
        double lp = 0, lp2 = 0;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)Rate;
            double white = rng.NextDouble() * 2 - 1;
            lp += (white - lp) * 0.055;
            lp2 += (lp - lp2) * 0.14;
            double env = Math.Min(1.0, t / 0.30) * Math.Exp(-1.0 * Math.Max(0, t - 0.5));
            buf[i] = lp2 * env * 3.4;
        }
        return Wav(buf);
    }

    /// <summary>A punch landing: the crack of the glove over a short low thump.</summary>
    private static byte[] Punch()
    {
        int n = (int)(Rate * 0.30);
        var buf = new double[n];
        var rng = new Random(3);
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)Rate;
            double body = Math.Sin(2 * Math.PI * 88 * t) * Math.Exp(-16 * t);
            double crack = (rng.NextDouble() * 2 - 1) * Math.Exp(-70 * t);
            buf[i] = (body * 0.75 + crack * 0.55) * 0.65;
        }
        return Wav(buf);
    }

    private static byte[] BellTone(double seconds, int strikes)
    {
        int n = (int)(Rate * seconds);
        var buf = new double[n];
        double[] partials = { 1.0, 2.76, 5.40, 8.93 };
        double[] weights = { 1.0, 0.55, 0.32, 0.18 };
        double gap = seconds / Math.Max(1, strikes) * 0.72;

        for (int s = 0; s < strikes; s++)
        {
            int offset = (int)(s * gap * Rate);
            for (int i = 0; i + offset < n; i++)
            {
                double t = i / (double)Rate;
                double v = 0;
                for (int k = 0; k < partials.Length; k++)
                    v += weights[k] * Math.Sin(2 * Math.PI * 640 * partials[k] * t) * Math.Exp(-1.4 * k * t);
                buf[i + offset] += v * Math.Exp(-3.1 * t) * 0.22;
            }
        }
        return Wav(buf);
    }

    /// <summary>Wrap a mono buffer as 16-bit PCM WAV, clipped rather than allowed to wrap round.</summary>
    private static byte[] Wav(double[] samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataBytes = samples.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + dataBytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(dataBytes);
        foreach (var d in samples) w.Write((short)(Math.Clamp(d, -1.0, 1.0) * short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }
}
