using AlaraERP.Application.Common.Security;
using FluentValidation;

namespace AlaraERP.Application.Identity;

internal static class IdentityRules
{
    public const int MinPasswordLength = 10;

    public static IRuleBuilderOptions<T, string> ValidEmail<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(256).EmailAddress();

    public static IRuleBuilderOptions<T, string> ValidName<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(100);

    /// <summary>Mirrors the Identity password options so the UI gets field-level errors before Identity runs.</summary>
    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MinimumLength(MinPasswordLength)
            .MaximumLength(128)
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");

    public static IRuleBuilderOptions<T, IReadOnlyList<string>> KnownRoles<T>(this IRuleBuilder<T, IReadOnlyList<string>> rule) =>
        rule.NotNull()
            .Must(r => r.Count > 0).WithMessage("Select at least one role.")
            .Must(r => r.All(n => SystemRoles.Find(n) is not null)).WithMessage("Unknown role.")
            .Must(r => r.Distinct(StringComparer.OrdinalIgnoreCase).Count() == r.Count).WithMessage("Duplicate role.");
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).StrongPassword()
            .NotEqual(x => x.CurrentPassword).WithMessage("The new password must be different from the current one.");
    }
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.FirstName).ValidName();
        RuleFor(x => x.LastName).ValidName();
        RuleFor(x => x.TemporaryPassword).StrongPassword();
        RuleFor(x => x.Roles).KnownRoles();
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.FirstName).ValidName();
        RuleFor(x => x.LastName).ValidName();
    }
}

public sealed class AssignRolesRequestValidator : AbstractValidator<AssignRolesRequest>
{
    public AssignRolesRequestValidator() => RuleFor(x => x.Roles).KnownRoles();
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator() => RuleFor(x => x.TemporaryPassword).StrongPassword();
}
