# Changelog

All notable changes to Foundry will be documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- Renamed the legacy `EngineHandoff` concept to `AgentHandoff` throughout the
  codebase. `EngineHandoff` was never a serialized C# type — it existed only
  as a conceptual label in the Copilot instructions — so no wire-compat shim
  is required. The new `AgentHandoff` type is the canonical input contract for
  all future Foundry agents.
- Refactored `FoundryOrchestrator` from a single 20 KB file into focused
  partial-class files with no behavior changes:
  - `FoundryOrchestrator.cs` — constructor, fields, public property accessors
    (~6.5 KB, down from ~20 KB).
  - `FoundryOrchestrator.Lifecycle.cs` — health checks, broker state snapshot,
    and internal initialization helpers.
  - `FoundryOrchestrator.Knowledge.cs` — knowledge indexing and library import.
  - `FoundryOrchestrator.Workflow.cs` — daily workflow execution.
  - `FoundryOrchestrator.Dispatch.cs` — agent handoff dispatch (delegates to
    `AgentDispatcher`).
- Removed dead fields `_mlGate` and `_learningProfile` from
  `FoundryOrchestrator` (no callers, marked for removal in pass-1 TODOs).

### Added

- `IAgent` interface and `AgentResult` record under `Foundry.Core.Agents`.
  Establishes the contract for future specialized agents (`dep-reviewer`,
  `toolkit-bumper`, etc.).
- `AgentHandoff` — immutable handoff payload type produced by event sources
  (GitHub webhooks, Discord commands, scheduler) and consumed by agents.
- `AgentDispatcher` — routes an `AgentHandoff` to the first registered
  `IAgent` whose `CanHandle` returns `true`. Fails open with a
  `AgentResult { Success = false }` when no agent matches (never throws).
- `services.AddFoundryAgent<TAgent>()` DI extension method
  (`AgentServiceCollectionExtensions`) for registering concrete agents.
  `FoundryOrchestrator` now accepts `IEnumerable<IAgent>` via its constructor
  and delegates dispatch to `AgentDispatcher`.
- Five unit tests in `Foundry.Core.Tests` pinning the dispatch contract:
  `Orchestrator_Dispatches_To_Matching_Agent`,
  `Orchestrator_Returns_Failure_When_No_Agent_Handles`,
  `Orchestrator_Dispatches_To_First_Matching_Agent_In_Registration_Order`,
  `Orchestrator_Returns_Failure_When_No_Agents_Registered`, and
  `Orchestrator_Result_Contains_Timing_Info`.
