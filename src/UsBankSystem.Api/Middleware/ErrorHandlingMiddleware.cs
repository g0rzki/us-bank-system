using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using UsBankSystem.Api.Integrations;

namespace UsBankSystem.Api.Middleware;

public class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteProblemDetailsAsync(context, ex);
        }
    }

    private static async Task WriteProblemDetailsAsync(HttpContext context, Exception ex)
    {
        var (status, title) = ex switch
        {
            KeyNotFoundException => (HttpStatusCode.NotFound, "Resource not found"),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "Unauthorized"),
            GatewayUnavailableException => (HttpStatusCode.ServiceUnavailable, "Service unavailable"),
            InvalidOperationException => (HttpStatusCode.Conflict, "Conflict"),
            ArgumentException => (HttpStatusCode.BadRequest, "Bad request"),
            PostgresException { SqlState: "40001" } => (HttpStatusCode.Conflict, "Concurrent modification — please retry"),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred")
        };

        var problem = new ProblemDetails
        {
            Status = (int)status,
            Title = title,
            Detail = ex.Message,
            Instance = context.Request.Path
        };

        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}
