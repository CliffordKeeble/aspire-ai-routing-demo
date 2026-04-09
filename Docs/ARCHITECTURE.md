# AspireDemo — Architecture

**Status:** v0.1 (April 9, 2026)
**Owner:** Architect (Claude, in collaboration with Cliff as product owner)
**Implementer:** Claude Code

## Purpose

A small, opinionated demonstration of AI-assisted message routing inside an
enterprise messaging topology. The point of the demo is **not** to be a chatbot.
The point is to show:

1. .NET Aspire as the orchestration layer for a distributed system that runs
   identically on a laptop and in Azure.
2. A clean seam between domain code and the AI provider, so the classifier can
   swap implementations without touching the pipeline.
3. End-to-end observability — every message traced from Blazor click to handler
   response, including the AI hop.
4. A cache-aware design that treats LLM calls as the expensive resource they are.

This is an architect's portfolio piece, not a developer's. The README and this
document are part of the deliverable.

## Stack

- .NET 9, C#
- .NET Aspire 9.x (orchestration, ServiceDefaults, dashboard)
- Blazor Server + SignalR (front end + live updates)
- Azure Service Bus (topics/subscriptions; emulator locally, real in Azure)
- Azure OpenAI (classifier; swappable via `IMessageClassifier`)
- Redis (classification cache)
- xUnit + Aspire.Hosting.Testing (integration tests against the running AppHost)
- Bicep via `azd up` (Azure deployment)

## Solution structure

```
AspireDemo.sln
├── AspireDemo.AppHost           Orchestration. Wires resources and projects.
├── AspireDemo.ServiceDefaults   Shared OTel, health, logging, resilience.
├── AspireDemo.Contracts         Message records and topic/subscription names.
├── AspireDemo.Web               Blazor Server. Publishes to messaging topic.
├── AspireDemo.Classifier        Worker. Reads incoming, classifies, republishes.
├── AspireDemo.Handler           Worker. Reads classified, generates response.
└── AspireDemo.Tests             xUnit integration tests using Aspire.Hosting.Testing.
```

**Why no Api project.** Considered and rejected for this demo. Blazor Server
publishes directly to the Service Bus topic via an injected `ServiceBusClient`.
Removing the Api hop keeps the architecture lean and the dashboard readable.
In a production system the Api would return — it's where you'd put auth,
validation, throttling, and ingestion-side observability. Documented here so a
reviewer can see the choice was deliberate.

**Why Contracts as a class library.** Three projects need the same record
shapes and the same topic/subscription names. Without a shared library, those
definitions duplicate and drift. With it, there is one source of truth and the
serialisation contract is explicit. Six lines of code, large architectural
payoff.

## The seams

### `IMessageClassifier`

Defined in `AspireDemo.Classifier/IMessageClassifier.cs`. The classifier worker
depends on this interface, not on `OpenAIClient` directly. Today's
implementation uses Azure OpenAI; an Anthropic implementation can be added
later by registering a different concrete type in `Program.cs`. Tests register
a stub implementation so the integration suite never burns OpenAI quota.

This seam exists because the demo's pitch is "AI as force multiplier" and that
pitch is only credible if the AI provider is treated as a pluggable resource,
not a hardcoded dependency.

### Contracts as shared language

`SupportMessage` → `ClassifiedMessage` → `HandledMessage` are the three stages
of the pipeline. Each worker accepts the previous stage's output and produces
the next. The contract is the data; the workers are stateless transformers.
This makes each worker independently testable and independently scalable.

## Resources

### Service Bus (`messaging`)

- `support.incoming` topic with `classifier-sub` subscription.
- `support.classified` topic with `handler-sub` subscription.
- Local: emulator container via `RunAsEmulator()`.
- Azure: real Service Bus namespace provisioned by `azd up`.
- Connection strings injected automatically by Aspire.

### Redis (`cache`)

- Classification cache. Key = hash of message text. Value = category.
- First message: full LLM round-trip (~600ms, visible in trace).
- Repeat message: cache hit (~5ms, no LLM span).
- Local: container via `RunAsContainer()`.
- Azure: provisioned by `azd up`.

The cache is wired into AppHost from day one but only consumed by the
classifier worker. A reviewer scanning the dashboard early in the build will
see `cache` idle — that's expected, not dead code.

## Cross-cutting

### Observability

Aspire's `ServiceDefaults` adds OpenTelemetry tracing, metrics, and structured
logging to every project that calls `AddServiceDefaults()`. Web and the workers
all opt in. The dashboard renders end-to-end traces including the Service Bus
hop and the OpenAI call.

### Resilience

`WaitFor` in AppHost is a dev-only orchestration hint — it sequences container
startup so workers don't crash trying to reach an unavailable Service Bus. In
production, the same robustness comes from the Service Bus SDK's built-in
retry policy and the worker's startup health probe. Both layers exist on
purpose.

## Decisions log

| # | Date | Decision | Rationale |
|---|------|----------|-----------|
| 1 | 2026-04-09 | No Api project | Lean demo; Blazor publishes directly. |
| 2 | 2026-04-09 | Azure OpenAI for classifier | Lower bug risk for first build; Anthropic option preserved via `IMessageClassifier`. |
| 3 | 2026-04-09 | Redis as classification cache | Demonstrates cost-aware design; visible in dashboard traces. |
| 4 | 2026-04-09 | One xUnit project, integration-first | `Aspire.Hosting.Testing` boots the whole AppHost in-process; one good test beats many shallow ones. |
| 5 | 2026-04-09 | `Guid Id`, dot-namespaced topics | Idiomatic for Service Bus; reads well in the dashboard. |
| 6 | 2026-04-09 | Contracts as a separate class library | Single source of truth for message shapes and topic names. |

## Open items

- Worker health checks (parked — not blocking the demo).
- Anthropic implementation of `IMessageClassifier` (deferred — proves the seam earns its keep).
- README with screenshots and the design-notes section (Day 2).

## What this document is not

This is not exhaustive API documentation, not a user guide, not a tutorial. It
is the architect's working notes — what exists, why it exists, what was
considered and rejected. If a reviewer wants to understand the *shape* of the
system in five minutes, this is the file they read.
