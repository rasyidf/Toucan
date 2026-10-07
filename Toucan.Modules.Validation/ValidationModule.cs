using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts;
using Toucan.Core.Plugins;
using Toucan.Core.Services.Validation;

namespace Toucan.Modules;

public static class ValidationModule
{
    public const string Id = "toucan.validation";

    /// <summary>Adds the built-in validation rules as the <c>toucan.validation</c> module.</summary>
    public static IServiceCollection AddToucanValidationModule(this IServiceCollection services) =>
        services.AddToucanModule(new BuiltInModule(Id, "Built-in validation rules", "Missing, placeholder, duplicate, untranslated, empty and whitespace checks", typeof(ValidationModule).Assembly), s =>
        {
            s.AddSingleton<IValidationRule, MissingTranslationRule>();
            s.AddSingleton<IValidationRule, PlaceholderMismatchRule>();
            s.AddSingleton<IValidationRule, DuplicateKeyRule>();
            s.AddSingleton<IValidationRule, UntranslatedCopyRule>();
            s.AddSingleton<IValidationRule, EmptyValueRule>();
            s.AddSingleton<IValidationRule, WhitespaceMismatchRule>();
        });
}
