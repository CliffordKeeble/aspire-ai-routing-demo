using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace AspireDemo.Contracts;

// How a worker reads a message body. The three workers share it so that a body
// that will not deserialise is handled the same way in each: reported as a failure
// the worker can log and dead-letter, never thrown into the processor's retry loop.
public static class MessageBody
{
    // The reason given when a message is dead-lettered because its body cannot be read.
    public const string DeadLetterReason = "UnreadableBody";

    public static bool TryRead<T>(ReadOnlyMemory<byte> body, [NotNullWhen(true)] out T? message, out string? failure)
        where T : class
    {
        message = null;
        failure = null;

        if (body.IsEmpty)
        {
            failure = "The body is empty.";
            return false;
        }

        try
        {
            message = JsonSerializer.Deserialize<T>(body.Span);
        }
        catch (JsonException ex)
        {
            failure = $"The body is not valid {typeof(T).Name} JSON: {ex.Message}";
            return false;
        }

        if (message is null)
        {
            failure = "The body is JSON null.";
            return false;
        }

        return true;
    }
}
