namespace AspireDemo.Web.Components.Pages;

public partial class Departments
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Departments page failed to refresh after a message arrived")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception);
}
