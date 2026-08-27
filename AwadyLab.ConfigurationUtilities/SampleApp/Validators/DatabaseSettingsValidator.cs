using FluentValidation;
using SampleApp.Models;

namespace SampleApp.Validators;

/// <summary>
/// Picked up by <c>UseFluentValidation</c> via reflection once <c>AddValidatorsFromAssembly</c> registers
/// it with DI — <see cref="AwadyLab.ConfigurationUtilities.Validators.FluentValidator.FluentValidationStep{TConfigType}"/>
/// never references FluentValidation types directly, so this package stays an optional dependency.
/// </summary>
public class DatabaseSettingsValidator : AbstractValidator<DatabaseSettings>
{
    public DatabaseSettingsValidator()
    {
        RuleFor(x => x.ConnectionStringName).NotEmpty();
        RuleFor(x => x.MaxPoolSize).InclusiveBetween(1, 1000);
        RuleFor(x => x.CommandTimeoutSeconds).GreaterThan(0);
    }
}
