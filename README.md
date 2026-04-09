# AspireDemo — AI-Assisted Message Routing on .NET Aspire

A small demonstration of using .NET Aspire to orchestrate an enterprise message-routing pipeline where an AI classifier dispatches customer-support messages across Service Bus topics to downstream handlers, with full OpenTelemetry tracing of the end-to-end flow including the AI hop.

**Status:** Work in progress — under active development.

## Stack

- .NET 9
- .NET Aspire 9.x
- Blazor Server + SignalR
- Azure Service Bus
- Azure OpenAI
- Redis
- xUnit with Aspire.Hosting.Testing
- Bicep via azd

## Architecture

Architecture notes and decisions log live in `docs/ARCHITECTURE.md`.

## Quick start

```bash
dotnet build
```

Open `AspireDemo.AppHost` in Visual Studio and press F5.

> A fuller quickstart with screenshots lands on Day 2.
