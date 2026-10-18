# Speech Test - STT with Azure Speech (.NET 8 + WPF)

A simple Windows app for Speech-to-Text. Press a button, speak into the microphone, and see the transcription on screen.

Two recognition modes are available:

- **Streaming (Speech SDK)**: real-time WebSocket recognition with partial results.
- **REST API (short audio)**: records the microphone to a WAV file and sends it to the synchronous Speech-to-text REST API (final result only, max 60 seconds per request).

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) installed
- An Azure **Speech** resource (free or paid tier)

## How to get your Azure credentials

1. Open the Azure portal.
2. Create a **Speech** resource (or use an existing one).
3. In the left menu, go to **Keys and Endpoint**.
4. Copy:
   - **Key 1** or **Key 2** (`Speech Key`)
   - **Region** (e.g. `eastus`, `brazilsouth`)
   - **Endpoint URL** — must start with `wss://`

### Endpoint format

The Endpoint must use the `wss://` scheme (secure WebSocket), not `https://`:

```
wss://YOUR_REGION.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1
```

**Tip:** if you don't have a custom endpoint, leave the `Endpoint` field empty in `appsettings.json` and fill in only `Key` and `Region`. The app will build the correct URL automatically.

### Configuration

### Option A - appsettings.json (local)

Edit `src/SpeechTestApp/appsettings.json`:

```json
{
  "Speech": {
    "Key": "YOUR_KEY_HERE",
    "Region": "YOUR_REGION",
    "Endpoint": "wss://YOUR_REGION.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1",
    "Language": "en-US"
  }
}
```

If you don't want to use a custom endpoint, leave it empty:

```json
{
  "Speech": {
    "Key": "YOUR_KEY_HERE",
    "Region": "YOUR_REGION",
    "Endpoint": "",
    "Language": "en-US"
  }
}
```

> ⚠️ This file is ignored by git (`.gitignore`). Never commit your keys.

### Option B - Environment variables

```powershell
$env:SPEECH_KEY="YOUR_KEY"
$env:SPEECH_REGION="YOUR_REGION"
$env:SPEECH_ENDPOINT="https://YOUR_REGION.api.cognitive.microsoft.com/"
```

## How to run

```powershell
dotnet run --project src/SpeechTestApp/SpeechTestApp.csproj
```

Or build and run:

```powershell
dotnet build src/SpeechTestApp/SpeechTestApp.csproj
dotnet src/SpeechTestApp/bin/Debug/net8.0-windows/SpeechTestApp.exe
```

## How to use

The app has two buttons side by side: **Start Streaming** (Speech SDK) and **Start REST API** (short-audio REST). Starting one disables the other until you stop it.

### Streaming mode

1. Run the app.
2. Click **Start Streaming**.
3. Speak into the default microphone of the PC.
4. Follow the text:
   - **Real-time (partial)**: in-progress hypothesis, updated while you speak.
   - **Final result**: confirmed result of each phrase (accumulates).
5. The **Latency** label shows the latency of the last recognition and the session average.
6. Click **Stop Streaming** to stop.

### REST API mode

1. Click **Start REST API**. The app records the microphone to a temporary WAV file (16 kHz, mono).
2. Speak, then click **Stop REST API** — or let it auto-stop after 60 seconds.
3. On stop, the WAV is sent to the REST API and the recognized text is shown in the **Final result** box.
4. The **Latency** label shows the round-trip time (upload + recognition) of the last request and the session average. The temporary WAV file is deleted after each request.

> REST mode has no partial results and is limited to 60 seconds of audio per request.


## Language

Recognition uses the fixed language `en-US`. To change it, edit `Language` in `appsettings.json` (e.g. `pt-BR`, `ko-KR`) using a BCP-47 code. This value is applied via `speechConfig.SpeechRecognitionLanguage` in Streaming mode and via the `language` query parameter in REST mode.

## Latency and log

- **Latency (Streaming)**: Azure `RecognitionLatencyMs` (time between the audio reaching the SDK and the final result coming back). Shown on screen (last value + average) and in the log.
- **Latency (REST)**: round-trip time measured from pressing Stop until the HTTP response is received.
- **Simple log**: the "Log" panel with configuration, session, recognized results, and error messages.

## REST API details

- Endpoint: `https://{region}.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1?language={lang}&format=simple`
- Auth header: `Ocp-Apim-Subscription-Key`
- Content-Type: `audio/wav; codecs=audio/pcm; samplerate=16000`
- Audio: PCM WAV, 16 kHz, mono (captured with [NAudio](https://github.com/naudio/NAudio)).
- Limit: 60 seconds of audio per request (auto-stop enforced).


## Troubleshooting

| Problem | Likely cause | Solution |
|---------|--------------|----------|
| "Invalid key" | Wrong Key/Region | Check `appsettings.json` or environment variables |
| Microphone not found | Permission or device | Connect a microphone and grant the app permission |
| No text appears | Network or endpoint | Check the Endpoint URL and internet connection |
| Build error | .NET 8 not installed | Install the 8.0 SDK |

## Project structure

```
src/SpeechTestApp/
├── SpeechTestApp.csproj
├── appsettings.json
├── App.xaml / App.xaml.cs
├── MainWindow.xaml / MainWindow.xaml.cs
```

## License

Free to use for learning and local testing.
