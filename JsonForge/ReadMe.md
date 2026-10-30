# Getting Started

Welcome to the Uno Platform!

To discover how to get started with your new app: https://aka.platform.uno/get-started

For more information on how to use the Uno.Sdk or upgrade Uno Platform packages in your solution: https://aka.platform.uno/using-uno-sdk

## Local (self-hosted) AI setup (recommended)

JsonForge supports any **OpenAI-compatible** endpoint. By default it will **auto-detect a local endpoint** (Ollama / LM Studio)
on startup, so you usually don't need to edit configuration.

The easiest local setup on Windows is **Ollama**.

1) Install Ollama

```powershell
winget install --id Ollama.Ollama -e
```

2) Pull a model (recommended default)

```powershell
ollama pull qwen2.5-coder:7b
```

If that is too heavy for your machine, use a smaller one:

```powershell
ollama pull qwen2.5-coder:3b
```

3) Run JsonForge (Debug uses `appsettings.development.json`)

```powershell
dotnet run --project .\JsonForge\JsonForge.csproj -c Debug -f net10.0-desktop
```

### Configuration

AI settings live under the `Ai` section in:

- `JsonForge/appsettings.development.json` (Debug)
- `JsonForge/appsettings.json` (Production)

The recommended local endpoint is:

- `http://localhost:11434/v1`

If you want to force AI on/off rather than auto-detecting, set `Ai:Enabled` (explicit) and/or `Ai:AutoEnable` (auto).
