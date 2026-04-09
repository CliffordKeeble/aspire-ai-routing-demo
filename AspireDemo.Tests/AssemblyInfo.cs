using Xunit;

// Aspire integration tests spin up the full distributed application (Service Bus
// emulator + workers). Two of these in parallel race for ports and resources.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
