using BackendApplication.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;
using ValidationException = BackendApplication.Exceptions.ValidationException;

namespace BackendApplication.Filters;

/// <summary>
/// Runs the FluentValidation validator for every action argument, before the action body
/// executes.
/// </summary>
/// <remarks>
/// Registered globally, so every endpoint validates identically and no controller has to
/// remember to check <c>ModelState.IsValid</c>. It merges DataAnnotations failures (which
/// model binding has already recorded) with FluentValidation failures into one 422 body,
/// so a client never has to handle two different error shapes depending on which rule
/// happened to fire.
/// </remarks>
public sealed class ValidationFilter : IAsyncActionFilter
{
    private readonly IServiceProvider _serviceProvider;

    public ValidationFilter(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var failures = new Dictionary<string, List<string>>();

        // 1. Whatever model binding and DataAnnotations already found.
        foreach (var (field, entry) in context.ModelState)
        {
            if (entry.Errors.Count == 0)
            {
                continue;
            }

            // System.Text.Json reports paths as "$.fieldName"; strip the prefix so the
            // client sees the field name it actually sent.
            var key = Camelize(field.StartsWith("$.", StringComparison.Ordinal) ? field[2..] : field);

            failures[key] = entry.Errors
                .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage)
                    ? "The value provided is not valid for this field."
                    : e.ErrorMessage)
                .ToList();
        }

        // 2. Whatever a registered FluentValidation validator finds.
        foreach (var (argumentName, argument) in context.ActionArguments)
        {
            if (argument is null)
            {
                continue;
            }

            // Look up IValidator<TArgument> at runtime: the filter is generic over every
            // action, so the concrete type is only known here.
            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());

            if (_serviceProvider.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var validationContext = new ValidationContext<object>(argument);
            var result = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);

            if (result.IsValid)
            {
                continue;
            }

            foreach (var error in result.Errors)
            {
                var key = Camelize(error.PropertyName);

                if (!failures.TryGetValue(key, out var messages))
                {
                    failures[key] = messages = [];
                }

                messages.Add(error.ErrorMessage);
            }

            _ = argumentName;
        }

        if (failures.Count > 0)
        {
            // Thrown rather than returned, so the one exception handler produces the body
            // and this filter does not need to know the ProblemDetails shape.
            throw new ValidationException(
                failures.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.Distinct(StringComparer.Ordinal).ToArray()));
        }

        await next();
    }

    private static string Camelize(string value)
        => string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
