using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using NAudio.Wave;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace SpeechTestApp;

public partial class MainWindow : Window
{
    private enum Mode { None, Streaming, Rest }

    private const int RestMaxSeconds = 60;

    private SpeechRecognizer? _recognizer;
    private AudioConfig? _audioConfig;

    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _tempWav;
    private readonly object _writeLock = new();
    private DispatcherTimer? _restTimer;
    private bool _restStopping;
    private bool _streamStopping;

    private Mode _mode = Mode.None;

    private long _lastLatencyMs = -1;
    private long _latencySumMs;
    private int _latencyCount;

    public MainWindow()
    {
        InitializeComponent();
        UpdateLatencyLabel();
        SetMode(Mode.None);
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Debug.WriteLine(line);
        Dispatcher.Invoke(() =>
        {
            TxtLog.Text += line + Environment.NewLine;
            TxtLog.ScrollToEnd();
        });
    }

    private void UpdateLatencyLabel()
    {
        var last = _lastLatencyMs >= 0 ? $"{_lastLatencyMs} ms" : "-- ms";
        var avg = _latencyCount > 0 ? $"{_latencySumMs / _latencyCount} ms" : "-- ms";
        TxtLatency.Text = $"Latency: {last} | avg: {avg}";
    }

    private void SetMode(Mode mode)
    {
        _mode = mode;
        switch (mode)
        {
            case Mode.None:
                BtnStreaming.Content = "Start Streaming";
                BtnStreaming.IsEnabled = true;
                BtnRest.Content = "Start REST API";
                BtnRest.IsEnabled = true;
                break;
            case Mode.Streaming:
                BtnStreaming.Content = "Stop Streaming";
                BtnStreaming.IsEnabled = true;
                BtnRest.IsEnabled = false;
                break;
            case Mode.Rest:
                BtnRest.Content = "Stop REST API";
                BtnRest.IsEnabled = true;
                BtnStreaming.IsEnabled = false;
                break;
        }
    }

    private static (string key, string region, string endpoint, string language) ReadConfig()
    {
        var config = App.Configuration.GetSection("Speech");
        var key = config["Key"] ?? Environment.GetEnvironmentVariable("SPEECH_KEY") ?? "";
        var region = config["Region"] ?? Environment.GetEnvironmentVariable("SPEECH_REGION") ?? "";
        var endpoint = config["Endpoint"] ?? Environment.GetEnvironmentVariable("SPEECH_ENDPOINT") ?? "";
        var language = config["Language"] ?? "en-US";
        return (key, region, endpoint, language);
    }

    private void ResetSession()
    {
        TxtLog.Clear();
        TxtPartial.Clear();
        TxtFinal.Clear();
        _lastLatencyMs = -1;
        _latencySumMs = 0;
        _latencyCount = 0;
        UpdateLatencyLabel();
    }

    private void ShowConfigWarning(string message)
    {
        Log("ERROR: " + message);
        MessageBox.Show(message, "Configuration", MessageBoxButton.OK, MessageBoxImage.Warning);
        SetMode(Mode.None);
    }

    private async void BtnStreaming_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == Mode.None)
            await StartStreamingAsync();
        else if (_mode == Mode.Streaming)
            await StopStreamingAsync();
    }

    private async void BtnRest_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == Mode.None)
            await StartRestAsync();
        else if (_mode == Mode.Rest)
            await StopRestAsync();
    }

    // ----- Streaming (Speech SDK) -----

    private async Task StartStreamingAsync()
    {
        try
        {
            ResetSession();
            TxtStatus.Text = "Starting...";

            var (key, region, endpoint, language) = ReadConfig();

            Log("Mode: Streaming (Speech SDK)");
            Log($"Language: {language}");
            Log($"Using endpoint: {!string.IsNullOrWhiteSpace(endpoint)}");
            Log($"Region: {region}");
            Log($"Key configured: {!string.IsNullOrWhiteSpace(key)} (length: {key.Length})");

            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(region))
            {
                ShowConfigWarning("Set Speech Key and Region in appsettings.json or environment variables.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(endpoint) && !endpoint.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) && !endpoint.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            {
                ShowConfigWarning("Endpoint must start with wss:// (e.g. wss://YOUR_REGION.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1). " +
                                  "If you don't have a custom endpoint, leave Endpoint empty and fill only Key and Region.");
                return;
            }

            var speechConfig = string.IsNullOrWhiteSpace(endpoint)
                ? SpeechConfig.FromSubscription(key, region)
                : SpeechConfig.FromEndpoint(new Uri(endpoint), key);

            speechConfig.SpeechRecognitionLanguage = language;
            speechConfig.SetProperty(PropertyId.SpeechServiceResponse_RequestDetailedResultTrueFalse, "true");

            _audioConfig = AudioConfig.FromDefaultMicrophoneInput();
            _recognizer = new SpeechRecognizer(speechConfig, _audioConfig);

            _recognizer.Recognizing += (s, e) =>
            {
                if (e.Result.Reason == ResultReason.RecognizingSpeech)
                {
                    Dispatcher.Invoke(() => TxtPartial.Text = e.Result.Text);
                }
            };

            _recognizer.Recognized += (s, e) =>
            {
                if (e.Result.Reason == ResultReason.RecognizedSpeech)
                {
                    var text = e.Result.Text;
                    var latencyStr = e.Result.Properties.GetProperty(PropertyId.SpeechServiceResponse_RecognitionLatencyMs);
                    var hasLatency = long.TryParse(latencyStr, out var latencyMs);

                    Dispatcher.Invoke(() =>
                    {
                        TxtFinal.Text += text + Environment.NewLine;
                        TxtPartial.Clear();

                        if (hasLatency)
                        {
                            _lastLatencyMs = latencyMs;
                            _latencySumMs += latencyMs;
                            _latencyCount++;
                        }
                        UpdateLatencyLabel();
                    });

                    Log($"Recognized: {text} (latency {(hasLatency ? latencyMs + "ms" : "n/a")})");
                }
                else if (e.Result.Reason == ResultReason.NoMatch)
                {
                    Dispatcher.Invoke(() => TxtStatus.Text = "No speech detected.");
                    Log("No speech detected (NoMatch)");
                }
            };

            _recognizer.Canceled += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    Log($"CANCELED: {e.ErrorDetails}");
                    TxtStatus.Text = $"Canceled: {e.ErrorDetails}";
                    SetMode(Mode.None);
                });
            };

            _recognizer.SessionStarted += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    Log("Session started");
                    TxtStatus.Text = "Listening... speak into the microphone";
                });
            };

            _recognizer.SessionStopped += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    Log("Session ended");
                    SetMode(Mode.None);
                    TxtStatus.Text = "Session ended.";
                });
            };

            await _recognizer.StartContinuousRecognitionAsync();

            SetMode(Mode.Streaming);
        }
        catch (Exception ex)
        {
            Log($"EXCEPTION on streaming start: {ex.Message}");
            if (ex.InnerException != null)
                Log($"Inner: {ex.InnerException.Message}");
            MessageBox.Show($"Error starting streaming: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            SetMode(Mode.None);
        }
    }

    private async Task StopStreamingAsync()
    {
        if (_streamStopping)
            return;
        _streamStopping = true;
        try
        {
            BtnStreaming.IsEnabled = false;
            Log("Stopping streaming...");

            if (_recognizer != null)
                await _recognizer.StopContinuousRecognitionAsync();

            _recognizer?.Dispose();
            _recognizer = null;

            _audioConfig?.Dispose();
            _audioConfig = null;

            TxtStatus.Text = "Stopped.";
            Log("Streaming stopped.");
        }
        catch (Exception ex)
        {
            Log($"EXCEPTION on streaming stop: {ex.Message}");
        }
        finally
        {
            _streamStopping = false;
            SetMode(Mode.None);
        }
    }

    // ----- REST API (short audio) -----

    private Task StartRestAsync()
    {
        try
        {
            ResetSession();
            TxtStatus.Text = "Recording (REST)...";

            var (key, region, _, language) = ReadConfig();

            Log("Mode: REST (short audio)");
            Log($"Language: {language}");
            Log($"Region: {region}");
            Log($"Key configured: {!string.IsNullOrWhiteSpace(key)} (length: {key.Length})");

            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(region))
            {
                ShowConfigWarning("Set Speech Key and Region in appsettings.json or environment variables.");
                return Task.CompletedTask;
            }

            TxtPartial.Text = $"(REST mode: recording, auto-stops at {RestMaxSeconds}s)";

            _tempWav = Path.Combine(Path.GetTempPath(), $"stt_{Guid.NewGuid():N}.wav");
            var format = new WaveFormat(16000, 16, 1);
            _writer = new WaveFileWriter(_tempWav, format);
            _waveIn = new WaveInEvent { WaveFormat = format };
            _waveIn.DataAvailable += OnRestDataAvailable;
            _waveIn.RecordingStopped += (s, e) =>
            {
                if (e.Exception != null)
                    Log($"Capture error: {e.Exception.Message}");
            };
            _waveIn.StartRecording();

            _restTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(RestMaxSeconds) };
            _restTimer.Tick += async (s, e) =>
            {
                Log($"Reached {RestMaxSeconds}s limit, stopping automatically.");
                await StopRestAsync();
            };
            _restTimer.Start();

            SetMode(Mode.Rest);
            TxtStatus.Text = "Recording (REST)... speak, then press Stop.";
            Log("Recording started (16 kHz mono WAV).");
        }
        catch (Exception ex)
        {
            Log($"EXCEPTION on REST start: {ex.Message}");
            if (ex.InnerException != null)
                Log($"Inner: {ex.InnerException.Message}");
            MessageBox.Show($"Error starting REST recording: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            CleanupRestCapture();
            SetMode(Mode.None);
        }

        return Task.CompletedTask;
    }

    private void OnRestDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_writeLock)
        {
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        }
    }

    private async Task StopRestAsync()
    {
        if (_mode != Mode.Rest || _restStopping)
            return;

        _restStopping = true;
        try
        {
            _restTimer?.Stop();
            _restTimer = null;
            Log("Stopping recording...");
            TxtStatus.Text = "Stopping...";

            CleanupRestCapture();

            var (key, region, _, language) = ReadConfig();

            if (string.IsNullOrEmpty(_tempWav) || !File.Exists(_tempWav))
            {
                Log("No audio captured.");
                return;
            }

            var sizeBytes = new FileInfo(_tempWav).Length;
            Log($"Captured {sizeBytes} bytes. Sending to REST API...");
            TxtStatus.Text = "Transcribing (REST)...";

            var (text, elapsedMs) = await TranscribeRestAsync(key, region, language);

            _lastLatencyMs = elapsedMs;
            _latencySumMs += elapsedMs;
            _latencyCount++;
            UpdateLatencyLabel();

            TxtPartial.Clear();

            if (!string.IsNullOrWhiteSpace(text))
            {
                TxtFinal.Text += text + Environment.NewLine;
                Log($"REST result: {text} (round-trip {elapsedMs} ms)");
            }
            else
            {
                TxtStatus.Text = "No speech recognized.";
                Log($"No speech recognized (round-trip {elapsedMs} ms)");
            }
        }
        catch (Exception ex)
        {
            Log($"REST error: {ex.Message}");
            TxtStatus.Text = $"REST error: {ex.Message}";
        }
        finally
        {
            try
            {
                if (_tempWav != null && File.Exists(_tempWav))
                    File.Delete(_tempWav);
            }
            catch
            {
                // ignore cleanup errors
            }

            _tempWav = null;
            _restStopping = false;
            SetMode(Mode.None);
            TxtStatus.Text = "Stopped.";
            Log("Recording stopped.");
        }
    }

    private void CleanupRestCapture()
    {
        if (_waveIn != null)
        {
            try { _waveIn.StopRecording(); } catch { /* ignore */ }
            _waveIn.Dispose();
            _waveIn = null;
        }

        lock (_writeLock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private async Task<(string? text, long elapsedMs)> TranscribeRestAsync(string key, string region, string language)
    {
        var url = $"https://{region}.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1" +
                  $"?language={Uri.EscapeDataString(language)}&format=simple";

        var bytes = await File.ReadAllBytesAsync(_tempWav!);

        using var client = new HttpClient();
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("codecs", "audio/pcm"));
        content.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("samplerate", "16000"));

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Add("Ocp-Apim-Subscription-Key", key);

        var sw = Stopwatch.StartNew();
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        sw.Stop();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var status = root.TryGetProperty("RecognitionStatus", out var st) ? st.GetString() : null;

        if (!string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase))
        {
            Log($"RecognitionStatus: {status}");
            return (null, sw.ElapsedMilliseconds);
        }

        var text = root.TryGetProperty("DisplayText", out var dt) ? dt.GetString() : null;
        return (text, sw.ElapsedMilliseconds);
    }

    protected override void OnClosed(EventArgs e)
    {
        _restTimer?.Stop();
        _restTimer = null;

        CleanupRestCapture();

        _recognizer?.Dispose();
        _audioConfig?.Dispose();

        base.OnClosed(e);
    }
}
