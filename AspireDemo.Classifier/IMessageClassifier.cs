namespace AspireDemo.Classifier;

public interface IMessageClassifier
{
    Task<string> ClassifyAsync(string customerText, CancellationToken ct);
}
