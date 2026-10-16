# AspireDemo — Architecture

**Status:** v0.2 (16 September 2026). v0.1 was 9 April 2026; the sections below were corrected against the code on 16 September and the decisions log gained entries 11 to 13.
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
4. A cache-aware design that treats model calls as the expensive resource they are.
   **Not built.** The cache is deferred (decision 12); what stands in for it today is the
   keyword default and the fallback path, which keep the model off the hot path unless asked for.

This is an architect's portfolio piece, not a developer's. The README and this
document are part of the deliverable.

## Stack

- .NET 10, C#
- .NET Aspire 13.4 (orchestration, ServiceDefaults, dashboard)
- Blazor Server + SignalR (front end + live updates)
- Azure Service Bus (topics/subscriptions; emulator locally, real in Azure)
- `Microsoft.Extensions.AI` over a local Ollama model or OpenAI, chosen from configuration
  behind `IMessageClassifier`. **Not Azure OpenAI** (decision 11)
- ~~Redis (classification cache)~~ — **deferred, commented out in AppHost and Web** (decision 12)
- xUnit + Aspire.Hosting.Testing (integration tests against the running AppHost)
- `azd` with a container-app host via `azure.yaml`. **No Bicep is authored in this repository** —
  Aspire generates the deployment manifest at publish time, and nothing here has been deployed

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
depends on this interface, not on a model client directly.

**As built (16 September 2026):** `ClassifierRegistration` chooses the implementation from
`Classifier:Provider` — `keyword` (the default, and what the integration suite runs), `ollama`
(a local model, and what F5 runs from the AppHost launch profile), or `openai`. An unrecognised
value throws at startup rather than quietly running the keyword matcher. The two model providers
share one `ChatClientClassifier` over `IChatClient`, so adding a provider is a registration line
and not a class.

The seam is what makes the integration suite free: it runs on `keyword`, so no model is pulled
and no quota is spent, while the pipeline under test is the same one.

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

⛔ **DEFERRED AND NOT WIRED. This section describes an intention, not the build.** The resource is
commented out in `AppHost.cs`, `AddRedisOutputCache` and `UseOutputCache` are commented out in the web
project, and the package references are left in place. A reviewer will see no `cache` in the dashboard,
and that is correct.

The design, kept for when it returns: key = hash of message text, value = category; first message pays
the full model round-trip and a repeat is a cache hit with no model span.

## Cross-cutting

### Observability

Aspire's `ServiceDefaults` adds OpenTelemetry tracing, metrics, and structured
logging to every project that calls `AddServiceDefaults()`. Web and the workers
all opt in.

The model hop is traced rather than assumed, and the mechanism is worth stating because it is one
line: `ServiceDefaults` registers `builder.Environment.ApplicationName` as a tracing source;
`ClassifierTelemetry.TelemetryName` is that same name; and the chat client is registered with
`.UseOpenTelemetry(sourceName: ClassifierTelemetry.TelemetryName)`. So the `classify message` activity
and the model's own span both export with no further wiring. The dashboard renders Blazor click,
Service Bus hop, model call, handler, and back to the live view.

Alongside it, a counter tagged with the provider and whether the call fell back to the keyword
matcher. **The fallback share is the instrument that says whether this is still doing what it
claims**, and it can go red.

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
| 2 | 2026-04-09 | Azure OpenAI for classifier | **Superseded by #11.**  Lower bug risk for first build; Anthropic option preserved via `IMessageClassifier`. |
| 3 | 2026-04-09 | Redis as classification cache | **Superseded by #12.**  Demonstrates cost-aware design; visible in dashboard traces. |
| 4 | 2026-04-09 | One xUnit project, integration-first | `Aspire.Hosting.Testing` boots the whole AppHost in-process; one good test beats many shallow ones. |
| 5 | 2026-04-09 | `Guid Id`, dot-namespaced topics | Idiomatic for Service Bus; reads well in the dashboard. |
| 6 | 2026-04-09 | Contracts as a separate class library | Single source of truth for message shapes and topic names. |
| 7 | 2026-04-09 | Aspire resource names vs Service Bus topic names | Aspire resource names must be valid identifiers (no dots); Service Bus topic names can contain dots. The `AddServiceBusTopic(resourceName, topicName)` overload separates the two. Contracts constants stay dot-namespaced as the on-the-wire names; a local `Slug()` helper in `AppHost.cs` derives the resource name by replacing dots with hyphens. |
| 8 | 2026-04-09 | Service Bus emulator EULA handled automatically | `Aspire.Hosting.Azure.ServiceBus` 9.5.0's `RunAsEmulator()` handles the emulator licence acceptance internally. No explicit `ACCEPT_EULA` environment variable or fluent call needed. Verified by the dashboard coming up green on first F5. |
| 9 | 2026-04-09 | Aspire client libraries version independently | **Superseded by #10.** `Aspire.Azure.Messaging.ServiceBus` (client integration) is on 13.x while `Aspire.Hosting.*` packages are on 9.x. Both belong to the Aspire 9.x release train; the version numbers have different lineages. Packages are compatible by contract, not by version match. No pinning required. |
| 10 | 2026-07-17 | Aspire upgraded to 13.4.6; AppHost migrated to the SDK-attribute project format | Supersedes #9 — hosting and client packages now share one version, so the "different lineages" reasoning no longer applies. `aspire update` moved the AppHost from `<Project Sdk="Microsoft.NET.Sdk">` plus a separate `<Sdk Name="Aspire.AppHost.Sdk" Version="9.5.0" />` element to the single-line `<Project Sdk="Aspire.AppHost.Sdk/13.4.6">` form, and dropped `Aspire.Hosting.AppHost` — the SDK now brings it in, so an explicit reference is obsolete. Note the 9.5.0 SDK element had been left behind by the 13.2.2 bump (`540f4ba`), which updated `PackageReference` lines only; the project ran regardless. `net9.0` restores cleanly against 13.4.6, so no retarget was forced (see open items). Verified: build green, 15/15 tests, full topology reaching `Running` under `aspire run`. |
| 11 | 2026-09-13 | Classifier implemented, and **not** on Azure OpenAI | Supersedes #2, which named a provider that was never wired. `Classifier:Provider` selects `keyword`, `ollama` or `openai`; the two model providers share one `ChatClientClassifier` over `IChatClient`. Azure OpenAI appears nowhere in the code. The default is `keyword` so the suite and CI cost nothing, and an unknown value throws at startup rather than falling through to it silently. |
| 12 | 2026-09-13 | Redis deferred | Supersedes #3. The cache was described as wired from day one and never was. Commented out in AppHost and Web, package references retained, ~£12/month if provisioned. Recorded as deferred rather than deleted so the intention is not lost. |
| 13 | 2026-09-13 | Unify every project on `net10.0` | Closes the target-framework split in open items. `Contracts` drops its multi-target; `Aspire.Hosting.Testing` moves 13.2.2 → 13.4.6 to match the hosting SDK; the orphaned `ApiService` scaffold and its azd boilerplate are removed. |


## Open items

- Worker health checks (parked — not blocking the demo).
- Anthropic implementation of `IMessageClassifier` (deferred — proves the seam earns its keep).
- Screenshots of the dashboard trace. Wanted, not promised.
- ~~**Target framework split.**~~ **CLOSED by decision 13.** All seven projects target `net10.0` and `Contracts` no longer multi-targets. The orphaned `ApiService` scaffold went with it and is in neither the solution nor git.
- **`aspire update` writes a broken `nuget.config`.** It emits a `packageSourceMapping` keyed on the source URL while restore resolves against a source named `nuget.org`, so every package becomes unresolvable (`NU1100`) and restore fails. Deleting the generated file fixes it — nothing here needs it. Expect this to recur on the next `aspire update`.

## What this document is not

This is not exhaustive API documentation, not a user guide, not a tutorial. It
is the architect's working notes — what exists, why it exists, what was
considered and rejected. If a reviewer wants to understand the *shape* of the
system in five minutes, this is the file they read.
