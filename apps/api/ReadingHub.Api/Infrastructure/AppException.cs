namespace ReadingHub.Api.Infrastructure;

public sealed class AppException : Exception
{
    public AppException(string title, int statusCode, string? detail = null, IDictionary<string, object?>? extensions = null)
        : base(detail ?? title)
    {
        Title = title;
        StatusCode = statusCode;
        Extensions = extensions;
    }

    public string Title { get; }
    public int StatusCode { get; }
    public IDictionary<string, object?>? Extensions { get; }

    public static AppException NotFound(string detail) => new("Not found", StatusCodes.Status404NotFound, detail);

    public static AppException Conflict(string detail, IDictionary<string, object?>? extensions = null) =>
        new("Conflict", StatusCodes.Status409Conflict, detail, extensions);

    public static AppException BadRequest(string detail) => new("Bad request", StatusCodes.Status400BadRequest, detail);

    public static AppException Unauthorized(string detail = "Authentication is required.") =>
        new("Unauthorized", StatusCodes.Status401Unauthorized, detail);

    public static AppException Forbidden(string detail = "Registration is closed.") =>
        new("Forbidden", StatusCodes.Status403Forbidden, detail);
}
