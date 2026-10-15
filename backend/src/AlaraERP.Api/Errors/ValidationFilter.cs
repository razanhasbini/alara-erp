using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AlaraERP.Api.Errors;

/// <summary>Runs the FluentValidation validator registered for each action argument before the action executes.</summary>
public sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null) continue;
            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator) continue;

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            if (!result.IsValid)
                throw new ValidationException(result.Errors);
        }
        await next();
    }
}
