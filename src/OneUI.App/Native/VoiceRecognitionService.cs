using System.Net.Http;
using System.Text;
using NAudio;
using NAudio.Wave;
using Whisper.net;

namespace OneUI.App.Native;

public sealed record VoiceRecognitionResult(
    bool Success,
    string Text,
    string? ErrorMessage = null);

/// <summary>
/// 100% silent, local in-process voice-to-text using NAudio microphone capture + Whisper.net (tiny.en quantized 31MB model).
/// Zero external Windows apps, zero popups, works on any Windows locale.
/// </summary>
public sealed class VoiceRecognitionService : IDisposable
{
    private const string ModelFileName = "ggml-tiny.en-q5_1.bin";
    private const string ModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.en-q5_1.bin";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private static readonly SemaphoreSlim ModelLock = new(1, 1);
    private static WhisperFactory? _sharedFactory;

    private CancellationTokenSource? _cts;
    private bool _isListening;

    public bool IsListening => _isListening;

    public event Action<string>? StatusChanged;

    private static string ModelPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OneUI", "models", ModelFileName);

    public async Task<VoiceRecognitionResult> ListenOnceAsync()
    {
        if (_isListening)
        {
            Cancel();
            return new VoiceRecognitionResult(false, string.Empty, "Cancelled");
        }

        _isListening = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            // 1. Ensure the tiny local Whisper model (~31 MB) is downloaded once
            bool modelReady = await EnsureModelAsync(ct);
            if (!modelReady)
                return new VoiceRecognitionResult(false, string.Empty, "Could not download voice model (check internet)");

            // 2. Record from default microphone with automatic speech/silence detection
            StatusChanged?.Invoke("Listening... speak your reminder");
            float[] samples = await RecordUtteranceAsync(ct);

            if (ct.IsCancellationRequested && samples.Length < 4000)
                return new VoiceRecognitionResult(false, string.Empty, "Cancelled");

            if (samples.Length < 4000) // < 0.25s of audio
                return new VoiceRecognitionResult(false, string.Empty, "No speech detected");

            // 3. Transcribe locally in-process via Whisper.net
            StatusChanged?.Invoke("Transcribing...");
            string transcript = await TranscribeAsync(samples, CancellationToken.None);
            transcript = CleanTranscript(transcript);

            if (string.IsNullOrWhiteSpace(transcript))
                return new VoiceRecognitionResult(false, string.Empty, "Didn't catch that — try again");

            return new VoiceRecognitionResult(true, transcript);
        }
        catch (OperationCanceledException)
        {
            return new VoiceRecognitionResult(false, string.Empty, "Cancelled");
        }
        catch (MmException)
        {
            return new VoiceRecognitionResult(false, string.Empty, "No microphone found");
        }
        catch (Exception ex)
        {
            return new VoiceRecognitionResult(false, string.Empty, $"Voice error: {ex.Message}");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _isListening = false;
        }
    }

    private async Task<bool> EnsureModelAsync(CancellationToken ct)
    {
        if (File.Exists(ModelPath) && new FileInfo(ModelPath).Length > 10_000_000)
            return true;

        await ModelLock.WaitAsync(ct);
        try
        {
            if (File.Exists(ModelPath) && new FileInfo(ModelPath).Length > 10_000_000)
                return true;

            Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
            string tempPath = ModelPath + ".downloading";

            StatusChanged?.Invoke("Downloading voice model (one-time, 31 MB)...");

            using var resp = await Http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            long total = resp.Content.Headers.ContentLength ?? 32_000_000;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using (var dst = File.Create(tempPath))
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                int lastPct = -1;
                int read;
                while ((read = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                    downloaded += read;
                    int pct = (int)Math.Clamp(downloaded * 100 / Math.Max(1, total), 0, 100);
                    if (pct != lastPct && pct % 5 == 0)
                    {
                        lastPct = pct;
                        StatusChanged?.Invoke($"Downloading voice model... {pct}%");
                    }
                }
            }

            File.Move(tempPath, ModelPath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            ModelLock.Release();
        }
    }

    private Task<float[]> RecordUtteranceAsync(CancellationToken ct)
    {
        return Task.Run(() =>
        {
            const int sampleRate = 16000;
            const double speechRmsThreshold = 0.015;
            const int maxInitialSilenceMs = 6000;
            const int endSilenceMs = 1300;
            const int maxTotalMs = 14000;

            var pcmSamples = new List<float>(sampleRate * 5);
            using var doneEvent = new ManualResetEventSlim(false);
            using var waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(sampleRate, 16, 1),
                BufferMilliseconds = 100
            };

            bool speechStarted = false;
            var startTime = DateTime.UtcNow;
            var lastSpeechTime = DateTime.UtcNow;

            waveIn.DataAvailable += (_, e) =>
            {
                int sampleCount = e.BytesRecorded / 2;
                double sumSq = 0;
                var chunk = new float[sampleCount];

                for (int i = 0; i < sampleCount; i++)
                {
                    short s = BitConverter.ToInt16(e.Buffer, i * 2);
                    float f = s / 32768f;
                    chunk[i] = f;
                    sumSq += f * f;
                }

                double rms = sampleCount > 0 ? Math.Sqrt(sumSq / sampleCount) : 0;
                var now = DateTime.UtcNow;

                lock (pcmSamples)
                {
                    pcmSamples.AddRange(chunk);
                }

                if (rms >= speechRmsThreshold)
                {
                    if (!speechStarted)
                    {
                        speechStarted = true;
                        StatusChanged?.Invoke("Hearing you... (click stop when done)");
                    }
                    lastSpeechTime = now;
                }

                double elapsedMs = (now - startTime).TotalMilliseconds;
                if (elapsedMs >= maxTotalMs)
                {
                    doneEvent.Set();
                }
                else if (!speechStarted && elapsedMs >= maxInitialSilenceMs)
                {
                    doneEvent.Set();
                }
                else if (speechStarted && (now - lastSpeechTime).TotalMilliseconds >= endSilenceMs)
                {
                    doneEvent.Set();
                }
            };

            waveIn.RecordingStopped += (_, _) => doneEvent.Set();

            using var reg = ct.Register(() => doneEvent.Set());

            waveIn.StartRecording();
            doneEvent.Wait();
            try { waveIn.StopRecording(); } catch { }

            if (!speechStarted)
                return [];

            lock (pcmSamples)
            {
                return pcmSamples.ToArray();
            }
        }, CancellationToken.None);
    }

    private static async Task<string> TranscribeAsync(float[] samples, CancellationToken ct)
    {
        await ModelLock.WaitAsync(ct);
        try
        {
            _sharedFactory ??= WhisperFactory.FromPath(ModelPath);
        }
        finally
        {
            ModelLock.Release();
        }

        using var processor = _sharedFactory.CreateBuilder()
            .WithLanguage("en")
            .Build();

        var sb = new StringBuilder();
        await foreach (var segment in processor.ProcessAsync(samples, ct))
        {
            if (!string.IsNullOrWhiteSpace(segment.Text))
                sb.Append(segment.Text).Append(' ');
        }

        return sb.ToString().Trim();
    }

    private static string CleanTranscript(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string s = raw.Trim();

        // Filter out common Whisper non-speech tokens like [BLANK_AUDIO], (music), etc.
        if (s.StartsWith('[') && s.EndsWith(']')) return string.Empty;
        if (s.StartsWith('(') && s.EndsWith(')')) return string.Empty;

        s = s.Replace("[BLANK_AUDIO]", "", StringComparison.OrdinalIgnoreCase).Trim();
        return s.TrimEnd('.', '!', '?').Trim();
    }

    public void Cancel()
    {
        try { _cts?.Cancel(); } catch { }
    }

    public void Dispose()
    {
        Cancel();
    }
}
