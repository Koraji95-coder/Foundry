<!-- markdownlint-disable MD013 -->
# Copilot Instructions — Foundry

> **Repo:** `chamber-19/Foundry`
> **Role:** Local agent broker for Chamber 19 dependency monitoring, GitHub/Discord operations, RAG, and Ollama-backed summaries.

Use Chamber 19 shared conventions as reference guidance, but this file is the
repo-specific source of truth.

## Current Shape

- Broker: ASP.NET Core minimal API in `src/Foundry.Broker/`.
- Core services/models: `src/Foundry.Core/`.
- Operator UI: Discord bot in `bot/`.
- Local model runtime: Ollama at `http://127.0.0.1:11434` by default.

## Build And Test

```text
dotnet restore Foundry.sln
dotnet build Foundry.sln
dotnet test Foundry.sln

cd bot
pip install -r requirements.txt
python -m py_compile foundry_bot.py
```

## Non-Goals

- Do not restore ML training, PR scoring, Suite artifact export, TensorFlow,
  TorchSharp, ML.NET, ONNX Runtime, scikit-learn, or `scripts/ml` /
  `scripts/scoring` flows.
- If a feature sounds like scoring, implement it as deterministic checks plus
  optional LLM structured extraction plus rule-based output.

## Review-Critical Rules

- Agents fail open to `needs human review` if Ollama or GitHub is unavailable.
- GitHub webhook receivers must validate signatures, but current dependency
  monitoring uses scheduled polling instead of webhooks.
- Secrets belong in `foundry.settings.local.json`, environment variables, or a
  secret manager. Never commit tokens.
- Endpoint or settings changes require README/config docs updates.

Path-specific rules live under `.github/instructions/`.
