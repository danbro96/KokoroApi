# KokoroApi

Small internal text-to-speech web API around the Kokoro 82M TTS model, built on .NET 10 and [KokoroSharp](https://github.com/Lyrcaxis/KokoroSharp).

Three endpoints:
- `GET /options` — what the server accepts: voice list (id, friendly name, language, gender), speed bounds, max text length.
- `POST /tts` — discrete synthesis. JSON in, `audio/wav` out.
- `WS /tts/stream` — bidirectional streaming. Client sends text deltas as the user types; server streams int16-LE PCM @ 24 kHz mono back as soon as each sentence is ready.

## Quick start (local)

```pwsh
dotnet run --project src/KokoroApi
```

First run downloads the Kokoro `kokoro.onnx` model (~320 MB) into the working directory. Subsequent runs reuse it.

The dev API key in `appsettings.json` is `REPLACE_ME_DEV_ONLY_64HEX` — change it before exposing the service.

## Endpoints

### `GET /options`
```bash
curl -H "X-API-Key: <key>" http://localhost:8080/options
```
Returns `{ defaultVoice, speed: { min, max, default }, maxTextLength, voices: [{ id, name, language, gender }], languages: [...], genders: [...] }`. `language` and `gender` are emitted as enum names ("AmericanEnglish", "Female") rather than integers.

### `POST /tts`
```bash
curl -X POST http://localhost:8080/tts \
  -H "X-API-Key: <key>" \
  -H "content-type: application/json" \
  -d '{"text":"Hello world.","voice":"af_heart","speed":1.0}' \
  -o out.wav
```

`voice` and `speed` are optional; defaults come from `Kokoro:DefaultVoice` / `Kokoro:DefaultSpeed`.

### `WS /tts/stream`
Connect to `ws://host:8080/tts/stream?api_key=<key>`.

Client → server (text frames, JSON):
- `{"type":"config","voice":"af_heart","speed":1.0}` — once, optional
- `{"type":"text","delta":"Hello wo"}` — append to per-connection buffer
- `{"type":"flush"}` — synthesize whatever's in the buffer
- `{"type":"cancel"}` — drop pending segments + clear buffer

Server → client:
- Binary frames: int16 LE PCM @ 24 kHz mono
- JSON frames: `{"type":"segment_start","id":N,"text":"..."}`, `{"type":"segment_end","id":N}`, `{"type":"error","message":"..."}`

## Auth & rate limits

API keys live in `Auth:ApiKeys`. In production, override via env:
```
Auth__ApiKeys__0__Key=$(openssl rand -hex 32)
Auth__ApiKeys__0__Name=prod
```
Both endpoints require auth; `/healthz` does not.

Rate limit is a token bucket of `RateLimit:RequestsPerMinute` (default 60) **partitioned by API-key name** (or remote IP for unauthenticated calls).

## Configuration

Anything under the keys below can be overridden via env vars (`__` is the section delimiter).

```jsonc
{
  "Kokoro": {
    "DefaultVoice": "af_heart",     // any voice from Kokoro 82M v1.0
    "DefaultSpeed": 1.0,
    "MinSegmentChars": 30,          // soft-flush clauses on commas after this length
    "MaxBufferChars": 400,          // hard-flush threshold
    "MaxTextLength": 4000           // /tts request limit
  },
  "Auth": {
    "ApiKeys": [{ "Key": "...", "Name": "default" }],
    "AllowedOrigins": []            // CORS allow-list; empty = none
  },
  "RateLimit": { "RequestsPerMinute": 60 }
}
```

## Browser test client

Open **http://localhost:8080/** in a browser — the demo page is served from the API itself (so no CORS friction), at [src/KokoroApi/wwwroot/demo/](C:\Users\danie\Github\KokoroApi\src\KokoroApi\wwwroot\demo\). Paste an API key, click **Connect** to start a session immediately, or click **Load options** first to populate the voice dropdown. Sentences fire on `.`, `!`, `?`, and on plain Enter (Shift+Enter for a newline). Audio plays through WebAudio in-order.

## Docker / TrueNAS

```bash
docker run --rm -p 8080:8080 \
  -e Auth__ApiKeys__0__Key=$(openssl rand -hex 32) \
  -e Auth__ApiKeys__0__Name=prod \
  -v kokoro-cache:/app/cache \
  danbro96/kokoro-api:latest
```

To build locally instead of pulling: `docker build -t kokoro-api . && docker run ... kokoro-api`. CI publishes both `latest` and `sha-<short>` tags on every push to `main`; see [`deploy/compose.yaml`](deploy/compose.yaml) for the TrueNAS Custom App definition.

Notes for TrueNAS Scale (Linux app):
- The image installs `libopenal1` so KokoroSharp's playback subsystem initializes cleanly even on a headless host (the API never plays audio, but the library still touches the OpenAL stub).
- The `kokoro.onnx` model (~320 MB) downloads on first run into the container's working dir. Mount a named volume there if you don't want to redownload after recreates.
- TLS is expected to terminate upstream (Cloudflare / NPM / Caddy / TrueNAS reverse proxy). Kestrel binds plain HTTP inside the container.

## Future
- Intel A380 acceleration via ONNX Runtime + OpenVINO Execution Provider.
- Per-key usage metering.
- Opus/Ogg wire format for off-LAN clients (~10× smaller than raw PCM).
- Per-request voice mixing (`KokoroVoiceManager.Mix`).

## Licenses
- This project: pick one (default unspecified).
- KokoroSharp: MIT.
- Kokoro-82M model + voices: Apache 2.0.
- eSpeak NG (bundled by KokoroSharp): GPLv3.
