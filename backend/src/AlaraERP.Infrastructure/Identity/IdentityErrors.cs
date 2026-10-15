using AlaraERP.Application.Common;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;

namespace AlaraERP.Infrastructure.Identity;

internal static class IdentityErrors
{
    /// <summary>Translates Identity failures into the API's validation / conflict errors.</summary>
    public static void ThrowIfFailed(this IdentityResult result, string passwordField = "password")
    {
        if (result.Succeeded) return;

        if (result.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail"))
            throw new ConflictException("users.duplicate_email", "An employee with this email already exists.");

        var failures = result.Errors.Select(e => new ValidationFailure(FieldFor(e.Code, passwordField), e.Description));
        throw new ValidationException(failures);
    }

    private static string FieldFor(string code, string passwordField) => code switch
    {
        "PasswordMismatch" => "currentPassword",
        _ when code.StartsWith("Password", StringComparison.Ordinal) => passwordField,
        "InvalidEmail" or "InvalidUserName" => "email",
        _ => "",
    };
}
