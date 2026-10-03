namespace SrNotification.Api.Endpoints;

internal static class ApiResults
{
    /// <summary>400 with a message for one field, in the ValidationProblem format the SPA understands.</summary>
    public static IResult Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    public static IResult Problem(int statusCode, string title, string? detail = null) =>
        TypedResults.Problem(title: title, detail: detail, statusCode: statusCode);
}

/// <summary>Page / pageSize query parameters, clamped to sane values.</summary>
internal readonly record struct Paging(int Page, int PageSize)
{
    public static Paging From(int? page, int? pageSize) =>
        new(Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 25, 1, 100));

    public int Skip => (Page - 1) * PageSize;
}
