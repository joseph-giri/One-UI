using System.Speech.Recognition;
using WinRtSpeech = Windows.Media.SpeechRecognition;

namespace OneUI.App.Native;

public sealed record VoiceRecognitionResult(
    bool Success,
    string Text,
    string? ErrorMessage = null);

public sealed class VoiceRecognitionService : IDisposable
{
    private SpeechRecognitionEngine? _sapiEngine;
    private WinRtSpeech.SpeechRecognizer? _winrtRecognizer;
    private CancellationTokenSource? _cts;
    private bool _isListening;

    public bool IsListening => _isListening;

    public event Action<string>? HypothesisReceived;

    public async Task<VoiceRecognitionResult> ListenOnceAsync()
    {
        if (_isListening)
        {
            Cancel();
            return new VoiceRecognitionResult(false, string.Empty, "Cancelled");
        }

        _isListening = true;
        _cts = new CancellationTokenSource();

        try
        {
            // 1. Primary: 100% silent, offline in-process Windows SAPI dictation (no popups, no external app, no cloud privacy wall)
            var sapiResult = await ListenWithSapiAsync(_cts.Token);
            if (sapiResult.Success || sapiResult.ErrorMessage is "Cancelled" or "No speech detected")
                return sapiResult;

            // 2. Silent in-process fallback: WinRT SpeechRecognizer (still completely silent, no UI)
            var winrtResult = await ListenWithWinRtAsync(_cts.Token);
            return winrtResult.Success ? winrtResult : sapiResult;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _isListening = false;
        }
    }

    private Task<VoiceRecognitionResult> ListenWithSapiAsync(CancellationToken ct)
    {
        return Task.Run(() =>
        {
            SpeechRecognitionEngine? engine = null;
            try
            {
                var recognizers = SpeechRecognitionEngine.InstalledRecognizers();
                if (recognizers.Count == 0)
                    return new VoiceRecognitionResult(false, string.Empty, "No speech recognizer installed");

                var info = recognizers.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase))
                           ?? recognizers[0];

                engine = new SpeechRecognitionEngine(info);
                _sapiEngine = engine;

                engine.LoadGrammar(new DictationGrammar());
                engine.SetInputToDefaultAudioDevice();
                engine.InitialSilenceTimeout = TimeSpan.FromSeconds(6);
                engine.BabbleTimeout = TimeSpan.FromSeconds(15);
                engine.EndSilenceTimeout = TimeSpan.FromMilliseconds(1100);
                engine.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(1300);

                var tcs = new TaskCompletionSource<VoiceRecognitionResult>(TaskCreationOptions.RunContinuationsAsynchronously);

                engine.SpeechHypothesized += (_, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Result?.Text))
                        HypothesisReceived?.Invoke(args.Result.Text);
                };

                engine.SpeechRecognized += (_, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Result?.Text))
                        tcs.TrySetResult(new VoiceRecognitionResult(true, args.Result.Text.Trim()));
                };

                engine.RecognizeCompleted += (_, args) =>
                {
                    if (args.Cancelled)
                        tcs.TrySetResult(new VoiceRecognitionResult(false, string.Empty, "Cancelled"));
                    else if (args.Error is not null)
                        tcs.TrySetResult(new VoiceRecognitionResult(false, string.Empty, "Microphone unavailable"));
                    else if (args.InitialSilenceTimeout)
                        tcs.TrySetResult(new VoiceRecognitionResult(false, string.Empty, "No speech detected"));
                    else if (args.Result is not null && !string.IsNullOrWhiteSpace(args.Result.Text))
                        tcs.TrySetResult(new VoiceRecognitionResult(true, args.Result.Text.Trim()));
                    else
                        tcs.TrySetResult(new VoiceRecognitionResult(false, string.Empty, "Didn't catch that — try again"));
                };

                using var reg = ct.Register(() =>
                {
                    try { engine.RecognizeAsyncCancel(); } catch { }
                    tcs.TrySetResult(new VoiceRecognitionResult(false, string.Empty, "Cancelled"));
                });

                engine.RecognizeAsync(RecognizeMode.Single);
                return tcs.Task.GetAwaiter().GetResult();
            }
            catch (InvalidOperationException)
            {
                return new VoiceRecognitionResult(false, string.Empty, "No microphone detected");
            }
            catch
            {
                return new VoiceRecognitionResult(false, string.Empty, "Speech engine unavailable");
            }
            finally
            {
                if (engine is not null)
                {
                    try { engine.RecognizeAsyncCancel(); } catch { }
                    try { engine.Dispose(); } catch { }
                }
                _sapiEngine = null;
            }
        }, ct);
    }

    private async Task<VoiceRecognitionResult> ListenWithWinRtAsync(CancellationToken ct)
    {
        try
        {
            _winrtRecognizer?.Dispose();
            _winrtRecognizer = new WinRtSpeech.SpeechRecognizer();
            _winrtRecognizer.Constraints.Add(
                new WinRtSpeech.SpeechRecognitionTopicConstraint(WinRtSpeech.SpeechRecognitionScenario.Dictation, "reminder"));

            var compilation = await _winrtRecognizer.CompileConstraintsAsync();
            if (compilation.Status != WinRtSpeech.SpeechRecognitionResultStatus.Success)
                return new VoiceRecognitionResult(false, string.Empty, "Speech engine unavailable");

            _winrtRecognizer.Timeouts.InitialSilenceTimeout = TimeSpan.FromSeconds(6);
            _winrtRecognizer.Timeouts.EndSilenceTimeout = TimeSpan.FromSeconds(1.2);
            _winrtRecognizer.Timeouts.BabbleTimeout = TimeSpan.FromSeconds(15);
            _winrtRecognizer.HypothesisGenerated += OnWinRtHypothesis;

            var op = _winrtRecognizer.RecognizeAsync();
            using var reg = ct.Register(() =>
            {
                try { op.Cancel(); } catch { }
            });

            var result = await op;
            if (result.Status == WinRtSpeech.SpeechRecognitionResultStatus.Success && !string.IsNullOrWhiteSpace(result.Text))
                return new VoiceRecognitionResult(true, result.Text.Trim());

            if (result.Status == WinRtSpeech.SpeechRecognitionResultStatus.TimeoutExceeded)
                return new VoiceRecognitionResult(false, string.Empty, "No speech detected");

            return new VoiceRecognitionResult(false, string.Empty, "Didn't catch that — try again");
        }
        catch (OperationCanceledException)
        {
            return new VoiceRecognitionResult(false, string.Empty, "Cancelled");
        }
        catch
        {
            return new VoiceRecognitionResult(false, string.Empty, "Voice input unavailable");
        }
        finally
        {
            if (_winrtRecognizer is not null)
            {
                _winrtRecognizer.HypothesisGenerated -= OnWinRtHypothesis;
                try { _winrtRecognizer.Dispose(); } catch { }
                _winrtRecognizer = null;
            }
        }
    }

    private void OnWinRtHypothesis(WinRtSpeech.SpeechRecognizer sender, WinRtSpeech.SpeechRecognitionHypothesisGeneratedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(args.Hypothesis?.Text))
            HypothesisReceived?.Invoke(args.Hypothesis.Text);
    }

    public void Cancel()
    {
        try { _cts?.Cancel(); } catch { }
        try { _sapiEngine?.RecognizeAsyncCancel(); } catch { }
    }

    public void Dispose()
    {
        Cancel();
        try { _sapiEngine?.Dispose(); } catch { }
        try { _winrtRecognizer?.Dispose(); } catch { }
        _sapiEngine = null;
        _winrtRecognizer = null;
    }
}
