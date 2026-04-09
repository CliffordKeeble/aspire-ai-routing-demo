using AspireDemo.Contracts;

namespace AspireDemo.Classifier;

public interface IMessageClassifier
{
    Task<ClassifiedMessage> ClassifyAsync(SupportMessage message, CancellationToken cancellationToken = default);
}
