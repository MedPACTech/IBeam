using Microsoft.Extensions.Options;

namespace IBeam.Communications.Abstractions.Validation;

public sealed class EmailOptionsValidator : IValidateOptions<EmailOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        if (options is null) return ValidateOptionsResult.Fail("Email defaults options are missing.");

        if (string.IsNullOrWhiteSpace(options.FromAddress))
            return ValidateOptionsResult.Fail("IBeam Communications Email Defaults: FromAddress is required.");

        return ValidateOptionsResult.Success;
    }
}
