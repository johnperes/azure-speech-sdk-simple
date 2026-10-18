# AGENTS.md

Context for AIs working in this repository.

## Language (IMPORTANT)
All project artifacts must be written in **US English**, regardless of the language used in the user's instructions or requests. This includes code, identifiers, comments, UI strings, log messages, commit messages, and documentation (`README.md`, `AGENTS.md`, and any new files). If the user writes in another language (e.g. Portuguese), interpret the intent and still produce English output.

## Purpose
Simple Windows (WPF) Speech-to-Text app using the Microsoft Azure Speech API.
Two modes: **Streaming** (Speech SDK, real-time WebSocket) and **REST API** (short audio).
Press a button, speak into the PC's active microphone, and see the transcribed speech on screen.

## Stack
- .NET 8 (`net8.0-windows`)
- WPF (XAML + C#)
- NuGet: `Microsoft.CognitiveServices.Speech` (v1.40.0), `NAudio` (v2.2.1)
- Language: fixed `en-US` (configurable via `Speech:Language`)

## Structure
```
src/SpeechTestApp/
  SpeechTestApp.csproj
  appsettings.json          (secrets - DO NOT commit)
  App.xaml / App.xaml.cs
  MainWindow.xaml / MainWindow.xaml.cs   (UI + inline logic)
```

## Flow
Two side-by-side buttons (`BtnStreaming`, `BtnRest`). Starting one disables the other until stopped (`SetMode` with `enum Mode { None, Streaming, Rest }`). Both modes read `App.Configuration` (`Speech`): Key, Region, Endpoint, Language.

### Streaming mode (`BtnStreaming_Click` -> `StartStreamingAsync` / `StopStreamingAsync`)
1. `SpeechConfig` with `SpeechRecognitionLanguage = language` + `RequestDetailedResultTrueFalse = true`.
2. `AudioConfig.FromDefaultMicrophoneInput()` + `StartContinuousRecognitionAsync()`.
3. Events:
   - `Recognizing` -> `TxtPartial` (real-time text, replaced).
   - `Recognized` -> appends to `TxtFinal` and clears `TxtPartial`.
4. `RecognitionLatencyMs` (via `PropertyId.SpeechServiceResponse_RecognitionLatencyMs`) -> `TxtLatency` label (last + average) and log.
5. `StopContinuousRecognitionAsync()` when stopping.

### REST mode (`BtnRest_Click` -> `StartRestAsync` / `StopRestAsync`)
1. Records the default microphone to a temp WAV (16 kHz, mono) with NAudio (`WaveInEvent` + `WaveFileWriter`).
2. Auto-stops after 60 seconds (`DispatcherTimer`, `RestMaxSeconds`); otherwise stops on button click.
3. POSTs the WAV to `https://{region}.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1?language={lang}&format=simple` with header `Ocp-Apim-Subscription-Key` and `Content-Type: audio/wav; codecs=audio/pcm; samplerate=16000`.
4. Parses `RecognitionStatus` / `DisplayText` (JSON, `System.Text.Json`) -> `TxtFinal`. Final result only (no partials).
5. `TxtLatency` = round-trip time (Stop -> response). Temp WAV deleted in `finally`.

## Log
- `Log(msg)` writes `[HH:mm:ss] msg` to `TxtLog` via `Dispatcher.Invoke` (simple, no queues/toggles).

## Configuration (appsettings.json)
- `Endpoint` must use the `wss://` scheme (or stay empty to use Key + Region). Streaming mode only.
- `Language` uses a BCP-47 code (e.g. `en-US`, `pt-BR`); applied to both modes.
- REST mode only needs Key + Region (builds the URL from the region).

## Rules
- Never commit real keys in `appsettings.json`.
- Use environment variables (`SPEECH_KEY`, `SPEECH_REGION`, `SPEECH_ENDPOINT`) or fill the file locally.
- Handle microphone, network, invalid key, and region errors.

## Commands
```powershell
dotnet build src/SpeechTestApp/SpeechTestApp.csproj
dotnet run --project src/SpeechTestApp/SpeechTestApp.csproj
```

## Prerequisites
- .NET 8 SDK installed
- Azure Speech resource created with Key, Region, and Endpoint
