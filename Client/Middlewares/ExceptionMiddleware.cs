using Grains.Exception;
using Orleans.Runtime;

namespace Client.Middlewares;

internal sealed class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context) 
    {
        try
        {
            await next(context);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Exception");
            await HandleException(context, e);
        }
    }

    private static Task HandleException(HttpContext context, Exception exception)
    {
        context.Response.StatusCode = exception switch
        {
            SessionNotFoundException => StatusCodes.Status404NotFound,
            SessionExpiredException => StatusCodes.Status409Conflict,
            SiloUnavailableException => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };

        var message = "Error";

        return context.Response.WriteAsJsonAsync(new
        {
            Message = message
        });
    }
}