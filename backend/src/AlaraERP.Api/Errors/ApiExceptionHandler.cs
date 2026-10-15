using System.Text.Json;
using AlaraERP.Application.Common;
using AlaraERP.Domain.Common;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AlaraERP.Api.Errors;

/// <summary>Maps exceptions to RFC 7807 problem responses. Internal details are never sent to the client.</summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem = exception switch
        {
            ValidationException v => ValidationProblem(v),
            NotFoundException nf => Problem(404, "Not found", nf.Message, "not_found"),
            ConflictException c => Problem(409, "Conflict", c.Message, c.Code),
            ForbiddenOperationException f => Problem(403, "Not allowed", f.Message, f.Code),
            DomainException d => Problem(422, "Business rule violated", d.Message, d.Code),
            DbUpdateConcurrencyException => Problem(409, "Conflict",
                "This record was changed by someone else. Reload it and try again.", "concurrency_conflict"),
            DbUpdateException { InnerException: PostgresException pg } => DatabaseProblem(pg),
            BadHttpRequestException b => Problem(b.StatusCode, "Bad request", b.Message, "bad_request"),
            _ => null!,
        };

        if (problem is null)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", http.Request.Method, http.Request.Path);
            problem = Problem(500, "Unexpected error", "An unexpected error occurred. The error has been logged.", "internal_error");
        }
        else if (problem.Status >= 500 || exception is DbUpdateException)
        {
            logger.LogWarning(exception, "Request failed with {Status}", problem.Status);
        }

        http.Response.StatusCode = problem.Status ?? 500;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem, Exception = exception });
    }

    private static ProblemDetails Problem(int status, string title, string detail, string code) => new()
    {
        Status = status, Title = title, Detail = detail, Extensions = { ["code"] = code },
    };

    private static ValidationProblemDetails ValidationProblem(ValidationException v)
    {
        var errors = v.Errors
            .GroupBy(e => ToCamelPath(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
        return new ValidationProblemDetails(errors)
        {
            Status = 400,
            Title = "Validation failed",
            Detail = errors.Count == 1 && errors.ContainsKey("") ? errors[""][0] : "One or more fields are invalid.",
            Extensions = { ["code"] = "validation_failed" },
        };
    }

    /// <summary>Last line of defence: constraint violations that slipped past application validation.</summary>
    private static ProblemDetails DatabaseProblem(PostgresException pg) => pg.SqlState switch
    {
        PostgresErrorCodes.UniqueViolation => Problem(409, "Conflict", "A record with the same unique value already exists.", $"duplicate:{pg.ConstraintName}"),
        PostgresErrorCodes.ForeignKeyViolation => Problem(409, "Conflict", "The record references, or is referenced by, other data.", $"reference:{pg.ConstraintName}"),
        PostgresErrorCodes.CheckViolation => Problem(422, "Business rule violated", "The change would break a data rule.", $"check:{pg.ConstraintName}"),
        _ => null!,
    };

    private static string ToCamelPath(string path) =>
        string.Join('.', path.Split('.').Select(segment => JsonNamingPolicy.CamelCase.ConvertName(segment)));
}
