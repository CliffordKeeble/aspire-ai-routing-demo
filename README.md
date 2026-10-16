# AspireDemo — message routing on .NET Aspire, with a real classifier behind a seam

A small distributed system that routes customer-support messages across Azure Service Bus topics to
downstream workers, with a **Blazor Server** front end showing the flow live and **OpenTelemetry tracing
of the whole path, including the model call**.

The point of it is the seam. Classification sits behind `IMessageClassifier`, and the implementation is
chosen from configuration rather than from code — a keyword matcher, a local model, or OpenAI — so the
pipeline, the tests and the traces are identical whichever is running.

## Stack

- .NET 10
- .NET Aspire 13.6.1
- Blazor Server (interactive server render mode, over the SignalR circuit)
- Azure Service Bus — three topics, run locally against the emulator container
- `Microsoft.Extensions.AI` over Ollama (local model) or OpenAI
- OpenTelemetry through Aspire's ServiceDefaults
- xUnit v3 with `Aspire.Hosting.Testing`, and bUnit for the Blazor pages

## The three classifiers

`Classifier:Provider` picks one. An unrecognised value stops the host at startup rather than quietly
running something else.

| value | what it does | what it needs |
|---|---|---|
| `keyword` | `KeywordClassifier`. The default when nothing is set, and what the integration suite runs | nothing — no network, no model, no cost |
| `ollama` | a local model through `IChatClient`. **This is what F5 runs**, from the AppHost launch profile | pulls `llama3.2:3b` on first start; a named volume keeps it, so only the first run pays |
| `openai` | OpenAI through `IChatClient`, `gpt-4o-mini` by default | an API key in user secrets or the environment, never in the repository |

**The keyword matcher is first match wins, in the order Billing, Account, Cancellation, then Other, and it
matches substrings** — so "cancel my bill" is Billing, and "charger" is Billing too. Both are pinned by tests,
not endorsed.

To set the OpenAI key, from the `AspireDemo.AppHost` folder:

```bash
dotnet user-secrets set "Parameters:openai-api-key" "<key>"
```

### Other settings

Set these where you set the provider: the AppHost's configuration (its launch profile's environment
variables, its user secrets, or the command line). The AppHost passes them on to the classifier.

| setting | default | what it does |
|---|---|---|
| `Classifier:TimeoutSeconds` | `30` | how long a model call may take before it falls back to the keyword matcher. Must be greater than 0, or the host stops at startup |
| `Classifier:OllamaModel` | `llama3.2:3b` | the model the AppHost pulls and the classifier asks for, in `ollama` mode |
| `OpenAI:Model` | `gpt-4o-mini` | the model to ask OpenAI for, in `openai` mode |

Model calls fall back to the keyword matcher on no match, timeout or exception. Every classification
increments a counter tagged with the provider and whether it fell back — **the fallback share is the
instrument that says whether this is still doing what it claims.**

**No accuracy comparison has been run, and the model is not claimed to be better.** Nothing in this
repository measures whether `ollama` or `openai` classifies more correctly than `KeywordClassifier`:
there is no evaluation set, no scored run, and the counter above records the *fallback share*, not
correctness. The model is here to demonstrate the seam, the provider switch and the telemetry — so
read its presence as an integration, not as a result. **An evaluation set is the next honest step,
and until it exists the keyword matcher is the one whose behaviour is fully known.**

## What is traced

`ServiceDefaults` registers each application's own name as a tracing source. The classifier's telemetry
source carries that same name and the chat client is wrapped with `UseOpenTelemetry` under it, so the
`classify message` activity and the model's own span both reach the dashboard with no extra wiring —
Blazor click, Service Bus hop, model call, handler, back to the live view.

## Quick start

Requires:

- **The .NET SDK, 10.0.400 or later** (the 10.0.4xx band or newer). `global.json` asks for it, because on an
  earlier band (10.0.112 was seen) the OllamaSharp source generator is refused with CS9057.
- **Docker Desktop, or Podman** with `ASPIRE_CONTAINER_RUNTIME=podman` set. The Service Bus emulator runs in a
  container, so the tests and the AppHost both need one running.
- The Aspire CLI is **optional**. F5 and `dotnet run --project AspireDemo.AppHost` need only the SDK.

```bash
dotnet build
dotnet test                 # starts the whole AppHost against the emulator; runs on 'keyword', so no model is pulled
```

Then open `AspireDemo.AppHost` in Visual Studio and press F5, or `dotnet run --project AspireDemo.AppHost`
(`aspire run` works too, if you have the CLI). The launch profile sets `Classifier__Provider=ollama`, so the
first start pulls a model. To run on `keyword` instead, change that value in
`AspireDemo.AppHost/Properties/launchSettings.json` (it appears in both profiles).

Once it is running, the Aspire dashboard shows the topology and the traces. Open the `webfrontend` resource's
link, go to **Send Message**, send one, then watch **Departments**.

## Architecture

`Docs/ARCHITECTURE.md` carries the solution structure, the seams, and the decisions log with its
superseded entries kept rather than deleted.

## Not in it, deliberately

- **No Redis.** The Redis packages are removed. The commented-out cache code in `AppHost.cs` and the web
  project, and the decisions log, keep the intent. It is a future slice, not a feature of this build.
- **No authentication and no throttling.** The web front end is open to anyone who can reach it. With `openai`,
  anyone who reaches the page spends on your key.
- **No Bicep in the repository.** `azure.yaml` configures `azd` with a container-app host, and Aspire
  generates the deployment manifest at publish time. Nothing here has been deployed to Azure.
- **No API project.** Blazor publishes to the topic directly. `Docs/ARCHITECTURE.md` gives the reasoning.

MIT licensed.
