using Grains.Exception;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Orleans.Runtime;

namespace Client.Middlewares;

internal sealed class ExceptionInterceptor(
        ILogger<ExceptionInterceptor> logger)
    : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            var response = await base.UnaryServerHandler(request, context, continuation);
            return response;
        }
        catch (Exception e) when (e is not RpcException)
        {
            logger.LogError(e, "Exception");
            throw GetException(e);
        }
    }
    
    private static RpcException GetException(Exception exception)
    {
        var statusCode = exception switch
        {
            SessionExpiredException => StatusCode.NotFound,
            ConcurrencyException => StatusCode.FailedPrecondition,
            SiloUnavailableException => StatusCode.Unavailable,
            _ => StatusCode.Internal
        };

        var message = statusCode == StatusCode.Internal
            ? "Error"
            : exception.Message;

        return new RpcException(new Status(statusCode, message));
    }
}