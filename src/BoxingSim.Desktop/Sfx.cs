using System.IO;
using System.Media;

namespace BoxingSim.Desktop;

/// <summary>The sounds of fight night, synthesised rather than shipped.
///
/// Every tone here is generated as a PCM waveform at startup, so the app carries no audio files, adds nothing
/// to its download, and has no licensing to worry about. A bell is a handful of inharmonic partials decaying
/// together; a knockdown is a low sine with a noise transient on the front.
///
/// Sound is deliberately sparse — a bell between rounds, a thud when a man goes down, the final bell at the
/// end. Anything more and it would be noise over a text feed.</summary>
public static class Sfx
{
    private const int Rate = 44100;

    public static bool Enabled { get; set; } = true;

    private static readonly Lazy<byte[]> RoundBell = new(() => Bell(0.9, 1.0));
    private static readonly Lazy<byte[]> FinalBell = new(() => Bell(1.9, 1.0, strikes: 3));
    private static readonly Lazy<byte[]> Thud = new(() => Knockdown());

    private static SoundPlayer? _player;

    public static void Bell() => Play(RoundBell.Value);
    public static void FinalBellSound() => Play(FinalBell.Value);
    public static void Knockdown_() => Play(Thud.Value);

    private static void Play(byte[] wav)
    {
        if (!Enabled) return;
        try
        {
            // One player, reused: a new sound replaces the last rather than layering, which is what you want
            // when a knockdown lands while a bell is still ringing.
            _player?.Stop();
            _player = new SoundPlayer(new MemoryStream(wav));
            _player.Play();   // asynchronous; never blocks the call
        }
        catch
        {
            // A machine with no audio device must not take the fight down with it.
        }
    }

    /// <summary>A struck bell: inharmonic partials, each decaying at its own rate.</summary>
    private static byte[] Bell(double seconds, double gain, int strikes = 1)
    {
        int n = (int)(Rate * seconds);
        var buf = new double[n];
        // Ratios that read as "bell" rather than "organ note" — a bell's overtones are not whole multiples.
        double[] partials = { 1.0, 2.76, 5.40, 8.93 };
        double[] weights = { 1.0, 0.55, 0.32, 0.18 };
        double strikeGap = seconds / Math.Max(1, strikes) * 0.72;

        for (int s = 0; s < strikes; s++)
        {
            int offset = (int)(s * strikeGap * Rate);
            for (int i = 0; i + offset < n; i++)
            {
                double t = i / (double)Rate;
                double env = Math.Exp(-3.1 * t);
                double v = 0;
                for (int k = 0; k < partials.Length; k++)
                    v += weights[k] * Math.Sin(2 * Math.PI * 640 * partials[k] * t) * Math.Exp(-1.4 * k * t);
                buf[i + offset] += v * env * 0.22 * gain;
            }
        }
        return Wav(buf);
    }

    /// <summary>A knockdown: a short noise transient over a low body that drops away fast.</summary>
    private static byte[] Knockdown()
    {
        int n = (int)(Rate * 0.55);
        var buf = new double[n];
        var rng = new Random(1);
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)Rate;
            double body = Math.Sin(2 * Math.PI * 72 * t) * Math.Exp(-7.5 * t);
            double snap = (rng.NextDouble() * 2 - 1) * Math.Exp(-55 * t);   // the impact itself
            buf[i] = (body * 0.85 + snap * 0.5) * 0.5;
        }
        return Wav(buf);
    }

    /// <summary>Wrap a mono sample buffer as a 16-bit PCM WAV, clipped rather than allowed to wrap round.</summary>
    private static byte[] Wav(double[] samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataBytes = samples.Length * 2;

        w.Write("RIFF"u8); w.Write(36 + dataBytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(dataBytes);

        foreach (var d in samples)
            w.Write((short)(Math.Clamp(d, -1.0, 1.0) * short.MaxValue));

        w.Flush();
        return ms.ToArray();
    }
}
