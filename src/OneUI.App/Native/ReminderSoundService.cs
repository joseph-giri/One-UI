using NAudio.Wave;

namespace OneUI.App.Native;

/// <summary>
/// Synthesizes and plays warm, Liquid-Glass-inspired chime tones (Crystal, Aurora, Marimba, Zen)
/// completely in-memory with zero external audio files.
/// </summary>
public static class ReminderSoundService
{
    private const int SampleRate = 44100;

    public static void Play(string? preset = "Crystal")
    {
        if (string.Equals(preset, "Mute", StringComparison.OrdinalIgnoreCase))
            return;

        string chosen = string.IsNullOrWhiteSpace(preset) ? "Crystal" : preset;

        _ = Task.Run(() =>
        {
            try
            {
                byte[] pcm = SynthesizeStereoPcm16(chosen);
                using var ms = new MemoryStream(pcm);
                using var raw = new RawSourceWaveStream(ms, new WaveFormat(SampleRate, 16, 2));
                using var waveOut = new WaveOutEvent();
                using var done = new ManualResetEventSlim(false);

                waveOut.PlaybackStopped += (_, _) => done.Set();
                waveOut.Init(raw);
                waveOut.Play();
                done.Wait(TimeSpan.FromSeconds(4));
            }
            catch
            {
                try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
            }
        });
    }

    private static byte[] SynthesizeStereoPcm16(string preset)
    {
        double durationSec = preset.ToLowerInvariant() switch
        {
            "zen" => 2.6,
            "aurora" => 2.1,
            "marimba" => 1.5,
            _ => 1.9 // Crystal
        };

        int totalSamples = (int)(SampleRate * durationSec);
        var left = new double[totalSamples];
        var right = new double[totalSamples];

        switch (preset.ToLowerInvariant())
        {
            case "aurora":
                // Warm swelling major-7th glass pad bloom
                AddTone(left, right, 523.25, 0.00, 1.30, 0.20, attack: 0.06, decay: 2.2, pan: -0.25, harmonics: true); // C5
                AddTone(left, right, 659.25, 0.05, 1.30, 0.18, attack: 0.06, decay: 2.2, pan: 0.20, harmonics: true);  // E5
                AddTone(left, right, 783.99, 0.10, 1.35, 0.17, attack: 0.06, decay: 2.1, pan: -0.15, harmonics: true); // G5
                AddTone(left, right, 987.77, 0.24, 1.65, 0.22, attack: 0.08, decay: 1.9, pan: 0.15, harmonics: true);  // B5
                AddTone(left, right, 1318.51, 0.34, 1.60, 0.14, attack: 0.08, decay: 2.0, pan: 0.0, harmonics: false); // E6 shimmer
                break;

            case "marimba":
                // Crisp warm wooden marimba 3-note motif (E5 -> G5 -> C6)
                AddTone(left, right, 659.25, 0.00, 0.55, 0.34, attack: 0.004, decay: 7.5, pan: -0.20, harmonics: false, wood: true);
                AddTone(left, right, 783.99, 0.14, 0.55, 0.34, attack: 0.004, decay: 7.2, pan: 0.20, harmonics: false, wood: true);
                AddTone(left, right, 1046.50, 0.28, 1.05, 0.38, attack: 0.004, decay: 4.2, pan: 0.0, harmonics: false, wood: true);
                AddTone(left, right, 1567.98, 0.30, 0.90, 0.12, attack: 0.008, decay: 4.8, pan: 0.12, harmonics: false, wood: false);
                break;

            case "zen":
                // Peaceful singing-bowl bell with gentle slow beating shimmer
                AddTone(left, right, 587.33, 0.00, 2.50, 0.28, attack: 0.015, decay: 1.5, pan: -0.10, harmonics: true); // D5
                AddTone(left, right, 589.10, 0.00, 2.50, 0.14, attack: 0.020, decay: 1.6, pan: 0.10, harmonics: false); // subtle chorus beat
                AddTone(left, right, 880.00, 0.04, 2.20, 0.16, attack: 0.020, decay: 1.9, pan: 0.15, harmonics: false); // A5 fifth
                AddTone(left, right, 1174.66, 0.08, 1.90, 0.10, attack: 0.025, decay: 2.4, pan: -0.15, harmonics: false); // D6 overtone
                break;

            default:
                // "Crystal": Shimmering Liquid Glass harp chime (C6 -> E6 -> G6 -> B6)
                AddTone(left, right, 1046.50, 0.00, 1.35, 0.26, attack: 0.008, decay: 3.1, pan: -0.28, harmonics: true); // C6
                AddTone(left, right, 1318.51, 0.11, 1.35, 0.24, attack: 0.008, decay: 3.0, pan: 0.25, harmonics: true);  // E6
                AddTone(left, right, 1567.98, 0.22, 1.45, 0.25, attack: 0.008, decay: 2.7, pan: -0.12, harmonics: true); // G6
                AddTone(left, right, 1975.53, 0.34, 1.50, 0.18, attack: 0.010, decay: 2.4, pan: 0.16, harmonics: false); // B6 sparkle
                AddTone(left, right, 523.25, 0.00, 1.50, 0.15, attack: 0.025, decay: 2.5, pan: 0.0, harmonics: false);   // warm C5 root
                break;
        }

        // Convert to 16-bit interleaved stereo PCM with soft limiter
        var bytes = new byte[totalSamples * 4];
        for (int i = 0; i < totalSamples; i++)
        {
            short l = ToInt16Soft(left[i]);
            short r = ToInt16Soft(right[i]);
            int idx = i * 4;
            bytes[idx] = (byte)(l & 0xFF);
            bytes[idx + 1] = (byte)((l >> 8) & 0xFF);
            bytes[idx + 2] = (byte)(r & 0xFF);
            bytes[idx + 3] = (byte)((r >> 8) & 0xFF);
        }

        return bytes;
    }

    private static void AddTone(
        double[] left,
        double[] right,
        double freqHz,
        double startSec,
        double durationSec,
        double amplitude,
        double attack,
        double decay,
        double pan,
        bool harmonics,
        bool wood = false)
    {
        int startIdx = (int)(startSec * SampleRate);
        int len = Math.Min((int)(durationSec * SampleRate), left.Length - startIdx);
        if (len <= 0) return;

        double gainL = Math.Sqrt(0.5 * (1.0 - pan));
        double gainR = Math.Sqrt(0.5 * (1.0 + pan));

        for (int i = 0; i < len; i++)
        {
            double t = i / (double)SampleRate;
            double env = t < attack
                ? (t / attack)
                : Math.Exp(-decay * (t - attack));

            // Smooth tail fade-out in final 40ms to guarantee zero click
            double remain = (len - 1 - i) / (double)SampleRate;
            if (remain < 0.04)
                env *= remain / 0.04;

            double wave = Math.Sin(2.0 * Math.PI * freqHz * t);
            if (harmonics)
            {
                // Gentle crystalline 2nd & 3rd overtones that decay faster than fundamental
                wave += 0.24 * Math.Exp(-decay * 1.4 * t) * Math.Sin(2.0 * Math.PI * (freqHz * 2.0) * t);
                wave += 0.08 * Math.Exp(-decay * 2.2 * t) * Math.Sin(2.0 * Math.PI * (freqHz * 3.0) * t);
            }
            else if (wood)
            {
                // Characteristic 4th harmonic transient of a marimba bar
                wave += 0.18 * Math.Exp(-24.0 * t) * Math.Sin(2.0 * Math.PI * (freqHz * 4.0) * t);
            }

            double sample = wave * env * amplitude;
            left[startIdx + i] += sample * gainL;
            right[startIdx + i] += sample * gainR;
        }
    }

    private static short ToInt16Soft(double value)
    {
        // Soft tanh saturation prevents any harsh clipping
        double shaped = Math.Tanh(value * 1.15);
        return (short)Math.Clamp(shaped * 32760.0, short.MinValue, short.MaxValue);
    }
}
