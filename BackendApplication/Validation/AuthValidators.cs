using BackendApplication.DTOs.Auth;
using FluentValidation;

namespace BackendApplication.Validation;

/// <summary>
/// Password and profile rules for registration.
/// </summary>
/// <remarks>
/// The length floor is the rule that matters most - length beats character-class
/// requirements for real-world strength. The class rules are here because most
/// coursework and corporate policies still ask for them, not because they add much.
/// </remarks>
public sealed class RegisterDtoValidator : AbstractValidator<RegisterDto>
{
    public RegisterDtoValidator()
    {
        RuleFor(x => x.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("That does not look like a valid email address.")
            .MaximumLength(256);

        RuleFor(x => x.FullName)
            .NotEmpty()
            .Length(2, 150);

        RuleFor(x => x.Password)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .MaximumLength(100)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");

        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.Password).WithMessage("Password confirmation does not match.");
    }
}

/// <summary>
/// Shape rules for login.
/// </summary>
/// <remarks>
/// Deliberately minimal. Applying the registration password rules here would tell an
/// attacker which passwords could not possibly be correct, and would lock out any user
/// whose password predates the current policy.
/// </remarks>
public sealed class LoginDtoValidator : AbstractValidator<LoginDto>
{
    public LoginDtoValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();

        RuleFor(x => x.Password)
            .NotEmpty();
    }
}
