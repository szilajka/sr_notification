namespace SrNotification.Api.Auth;

/// <summary>
/// Cross-site request forgery protection for the cookie-authenticated API. Every state-changing request
/// must carry a custom header. Browsers only let other sites send custom headers after a CORS preflight,
/// which this API never approves, so a forged request from another site is rejected.
/// </summary>
public sealed class CsrfHeaderFilter : IEndpointFilter
{
    public const string HeaderName = "X-SRN-CSRF";

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method))
        {
            return next(context);
        }

        if (!request.Headers.ContainsKey(HeaderName))
        {
            return ValueTask.FromResult<object?>(TypedResults.Problem(
                title: $"The {HeaderName} header is required.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        return next(context);
    }
}
