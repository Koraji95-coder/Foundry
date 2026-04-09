using Foundry.Services;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Foundry.Broker;

internal static class ChatEndpoints
{
    public static void MapChatEndpoints(this IEndpointRouteBuilder app, ILogger logger)
    {
        // POST /api/chat/route — validate and acknowledge the requested chat route
        app.MapPost("/api/chat/route", (ChatRouteRequest request) =>
        {
            try
            {
                var validator = new ChatRouteRequestValidator();
                var validation = validator.Validate(request);
                if (!validation.IsValid)
                {
                    return Results.BadRequest(new { errors = validation.Errors.Select(e => e.ErrorMessage) });
                }

                return Results.Ok(new { route = request.Route });
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Endpoint {Endpoint} failed", "/api/chat/route");
                return Results.Problem(
                    detail: "An unexpected error occurred. See server logs for details.",
                    statusCode: 500
                );
            }
        });

        // POST /api/chat/send — validate and acknowledge the prompt (LLM inference enqueuing is handled by the job worker)
        app.MapPost("/api/chat/send", (ChatSendRequest request) =>
        {
            try
            {
                var validator = new ChatSendRequestValidator();
                var validation = validator.Validate(request);
                if (!validation.IsValid)
                {
                    return Results.BadRequest(new { errors = validation.Errors.Select(e => e.ErrorMessage) });
                }

                return Results.Accepted(value: new { prompt = request.Prompt, status = "queued" });
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Endpoint {Endpoint} failed", "/api/chat/send");
                return Results.Problem(
                    detail: "An unexpected error occurred. See server logs for details.",
                    statusCode: 500
                );
            }
        });
    }
}

// Chat request records
internal sealed record ChatRouteRequest(string Route);
internal sealed record ChatSendRequest(string Prompt);

// Known route catalog for ChatRouteRequestValidator
internal static class ChatRouteCatalog
{
    public static readonly IReadOnlyList<string> KnownRoutes =
        new[] { "general", "knowledge", "ml-engineer" };
}

internal sealed class ChatRouteRequestValidator : AbstractValidator<ChatRouteRequest>
{
    public ChatRouteRequestValidator()
    {
        RuleFor(x => x.Route)
            .NotEmpty()
            .WithMessage("Route is required.");

        RuleFor(x => x.Route)
            .Must(r => ChatRouteCatalog.KnownRoutes.Contains(r, StringComparer.OrdinalIgnoreCase))
            .When(x => !string.IsNullOrWhiteSpace(x.Route))
            .WithMessage($"Route must be one of: {string.Join(", ", ChatRouteCatalog.KnownRoutes)}.");
    }
}

internal sealed class ChatSendRequestValidator : AbstractValidator<ChatSendRequest>
{
    public ChatSendRequestValidator()
    {
        RuleFor(x => x.Prompt)
            .NotEmpty()
            .WithMessage("Prompt is required.");
    }
}
