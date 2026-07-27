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
    private static MediaPlayer? _bed;
    private static readonly MediaPlayer[] _oneShots = new MediaPlayer[6];   // a pool, so rapid hits overlap
    private static int _next;

    private static double _bedBase;      // the occasion's resting level
    private static double _bedLevel;     // where it is right now, including any swell
    private static DispatcherTimer? _decay;

    private static readonly Dictionary<string, string> _files = new();

    /// <summary>Set the room before the first bell.</summary>
    public static void SetOccasion(Occasion o)
    {
        _bedBase = o switch
        {
            Occasion.Club => 0.10,          // a few hundred people, mostly quiet
            Occasion.Ranked => 0.22,
            Occasion.Title => 0.38,
            Occasion.Unification => 0.55,   // a full arena, humming before a punch is thrown
            _ => 0.2
        };
        _bedLevel = _bedBase;
        if (_bed is not null) _bed.Volume = _bedLevel;
    }

    /// <summary>Bring the crowd up and keep it there until StopBed.</summary>
    public static void StartBed(Occasion o)
    {
        if (!_enabled) return;
        SetOccasion(o);
        try
        {
            // A club room is sparse claps and murmur; an arena is a wall of noise. Two different beds, not
            // just two volumes — a quiet arena still sounds like an arena.
            string key = _bedBase >= 0.3 ? "bed-big" : "bed-small";
            _bed ??= new MediaPlayer();
            _bed.MediaEnded -= LoopBed;
            _bed.MediaEnded += LoopBed;
            _bed.Open(new Uri(FileFor(key, () => CrowdBed(dense: _bedBase >= 0.3))));
            _bed.Volume = _bedLevel;
            _bed.Play();

            _decay ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
            _decay.Tick -= Settle;
            _decay.Tick += Settle;
            _decay.Start();
        }
        catch { /* no audio device — the fight carries on in silence */ }
    }

    public static void StopBed()
    {
        try { _decay?.Stop(); _bed?.Stop(); } catch { }
    }

    private static void LoopBed(object? s, EventArgs e)
    {
        try { if (_bed is not null) { _bed.Position = TimeSpan.Zero; _bed.Play(); } } catch { }
    }

    /// <summary>Ease the crowd back down to its resting level after a swell.</summary>
    private static void Settle(object? s, EventArgs e)
    {
        if (_bed is null) return;
        if (Math.Abs(_bedLevel - _bedBase) < 0.005) return;
        _bedLevel += (_bedBase - _bedLevel) * 0.08;
        try { _bed.Volume = Math.Clamp(_bedLevel, 0, 1); } catch { }
    }

    /// <summary>Lift the crowd. The amount is the event's weight, so a hard shot stirs them and a knockdown
    /// takes the roof off.</summary>
    private static void Swell(double amount)
    {
        _bedLevel = Math.Clamp(_bedLevel + amount, 0, 1);
        try { if (_bed is not null) _bed.Volume = _bedLevel; } catch { }
    }

    // ---- one-shots ----

    public static void Bell() => Fire("bell", () => BellTone(0.9, 1), 0.55);
    public static void FinalBell() => Fire("bell3", () => BellTone(1.9, 3), 0.6, swell: 0.30);
    public static void Thud(double force = 1.0) => Fire("thud", Punch, 0.30 + 0.25 * force, swell: 0.03 * force);
    public static void Ooh() => Fire("ooh", CrowdOoh, 0.6, swell: 0.16);
    public static void Roar() => Fire("roar", CrowdRoar, 0.75, swell: 0.35);

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

    /// <summary>A crowd that carries on underneath everything. Layers of filtered noise at different rates give
    /// it movement so it doesn't sit as a flat hiss, and in a small hall you hear individual claps over the top
    /// rather than a wall of sound.</summary>
    private static byte[] CrowdBed(bool dense)
    {
        int n = (int)(Rate * 8.0);          // eight seconds, looped
        var buf = new double[n];
        var rng = new Random(dense ? 11 : 29);
        double lp = 0, lp2 = 0, slow = 0;

        for (int i = 0; i < n; i++)
        {
            double white = rng.NextDouble() * 2 - 1;
            lp += (white - lp) * (dense ? 0.05 : 0.035);
            lp2 += (lp - lp2) * 0.10;
            // A slow wander so the level breathes instead of sitting flat.
            slow += ((rng.NextDouble() * 2 - 1) - slow) * 0.00004;
            buf[i] = lp2 * (dense ? 3.0 : 1.7) * (1.0 + slow * 2.2);
        }

        // A thin hall: you pick out individual claps. A full arena swallows them.
        int claps = dense ? 40 : 130;
        for (int c = 0; c < claps; c++)
        {
            int at = rng.Next(n - 3000);
            double amp = (dense ? 0.10 : 0.34) * (0.5 + rng.NextDouble());
            for (int i = 0; i < 2200; i++)
            {
                double t = i / (double)Rate;
                buf[at + i] += (rng.NextDouble() * 2 - 1) * Math.Exp(-95 * t) * amp;
            }
        }

        // Fade the seam so the loop doesn't click.
        int fade = Rate / 4;
        for (int i = 0; i < fade; i++)
        {
            double g = i / (double)fade;
            buf[i] *= g;
            buf[n - 1 - i] *= g;
        }
        return Wav(buf);
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
