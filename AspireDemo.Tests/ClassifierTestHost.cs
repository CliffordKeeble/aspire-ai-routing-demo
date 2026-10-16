using AspireDemo.Classifier;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace AspireDemo.Tests;

// A host built the way the classifier's Program builds it (AddMessageClassifier),
// from in-memory settings, and never started: it exists to resolve the registration.
internal static class ClassifierTestHost
{
    public static IHost Build(params (string Key, string Value)[] settings)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)));
        builder.AddMessageClassifier();
        return builder.Build();
    }
}
