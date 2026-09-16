# AspireDemo — message routing on .NET Aspire, with a real classifier behind a seam

A small distributed system that routes customer-support messages across Azure Service Bus topics to
downstream workers, with a **Blazor Server** front end showing the flow live and **OpenTelemetry tracing
of the whole path, including the model call**.

The point of it is the seam. Classification sits behind `IMessageClassifier`, and the implementation is
chosen from configuration rather than from code — a keyword matcher, a local model, or OpenAI — so the
pipeline, the tests and the traces are identical whichever is running.

## Stack

- .NET 10
- .NET Aspire 13.4.6
- Blazor Server (interactive server render mode, over the SignalR circuit)
- Azure Service Bus — three topics, run locally against the emulator container
- `Microsoft.Extensions.AI` over Ollama (local model) or OpenAI
- OpenTelemetry through Aspire's ServiceDefaults
- xUnit v3 with `Aspire.Hosting.Testing`

## The three classifiers

`Classifier:Provider` picks one. An unrecognised value stops the host at startup rather than quietly
running something else.

| value | what it does | what it needs |
|---|---|---|
| `keyword` | `KeywordClassifier`. The default when nothing is set, and what the integration suite runs | nothing — no network, no model, no cost |
| `ollama` | a local model through `IChatClient`. **This is what F5 runs**, from the AppHost launch profile | pulls `llama3.2:3b` on first start; a named volume keeps it, so only the first run pays |
| `openai` | OpenAI through `IChatClient`, `gpt-4o-mini` by default | an API key in user secrets or the environment, never in the repository |

Model calls fall back to the keyword matcher on no match, timeout or exception. Every classification
increments a counter tagged with the provider and whether it fell back — **the fallback share is the
instrument that says whether this is still doing what it claims.**

## What is traced

`ServiceDefaults` registers each application's own name as a tracing source. The classifier's telemetry
source carries that same name and the chat client is wrapped with `UseOpenTelemetry` under it, so the
`classify message` activity and the model's own span both reach the dashboard with no extra wiring —
Blazor click, Service Bus hop, model call, handler, back to the live view.

## Quick start

Requires the .NET 10 SDK, and Docker or Podman for the Service Bus emulator.

```bash
dotnet build
dotnet test                 # the integration suite; runs on 'keyword', so no model is pulled
```

Then open `AspireDemo.AppHost` in Visual Studio and press F5, or `aspire run`. The launch profile sets
`Classifier:Provider=ollama`, so the first start pulls a model — set it to `keyword` if you would rather
not. The Aspire dashboard shows the topology and the traces.

## Architecture

`Docs/ARCHITECTURE.md` carries the solution structure, the seams, and the decisions log with its
superseded entries kept rather than deleted.

## Not in it, deliberately

- **No Redis.** A classification cache is scaffolded and commented out in `AppHost.cs` and the web
  project; the package references are still there. It is a future slice, not a feature of this build.
- **No Bicep in the repository.** `azure.yaml` configures `azd` with a container-app host, and Aspire
  generates the deployment manifest at publish time. Nothing here has been deployed to Azure.
- **No API project.** Blazor publishes to the topic directly. `Docs/ARCHITECTURE.md` gives the reasoning.

MIT licensed.
