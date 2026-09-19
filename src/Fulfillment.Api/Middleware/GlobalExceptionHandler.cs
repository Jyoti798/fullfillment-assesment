using Fulfillment.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Fulfillment.Api.Middleware;

/// <summary>
/// The single place that turns exceptions into HTTP responses. Expected failures (validation, not found, conflict)
/// are logged without stack traces; anything else is a 500 with a generic message, so internals never leak.
/// It only decides the status and the message: the body is written through <see cref="IProblemDetailsService"/>,
/// the same path the framework's own validation and routing errors use, so every error has one consistent shape
/// (see <c>AddProblemDetails</c> in Program.cs).
/// </summary>
internal sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug("Request was cancelled by the client");
            httpContext.Response.StatusCode = 499; // client closed request
            return true;
        }

        var problem = ToProblem(exception, httpContext);
        httpContext.Response.StatusCode = problem.Status!.Value;

        await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
        return true;
    }

    private ProblemDetails ToProblem(Exception exception, HttpContext httpContext)
    {
        switch (exception)
        {
            case DomainValidationException validation:
                logger.LogInformation("Validation failed on {Fields}", string.Join(", ", validation.Errors.Keys));
                return new ValidationProblemDetails(validation.Errors)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "One or more validation errors occurred."
                };

            case NotFoundException notFound:
                logger.LogInformation("Not found: {Message}", notFound.Message);
                return new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Resource not found.",
                    Detail = notFound.Message
                };

            case ConflictException conflict:
                logger.LogInformation("Conflict: {Message}", conflict.Message);
                return new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "The request conflicts with the current state of the resource.",
                    Detail = conflict.Message
                };

            default:
                logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
                return new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "An unexpected error occurred.",
                    Detail = "The error has been logged. Quote the traceId when contacting support."
                };
        }
    }
}
