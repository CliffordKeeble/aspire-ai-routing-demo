namespace AspireDemo.Web.Components.Pages;

public partial class SendMessage
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to publish support message {Id}")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, Guid id);
}
