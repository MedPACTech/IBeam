using IBeam.Communications.Core.Options;
using Microsoft.Extensions.Options;

namespace IBeam.Communications.Core.Validation;

public sealed class EmailDefaultsOptionsValidator : IValidateOptions<EmailDefaultsOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailDefaultsOptions options)
    {
        if (options is null) return ValidateOptionsResult.Fail("Email defaults options are missing.");

        if (string.IsNullOrWhiteSpace(options.FromAddress))
            return ValidateOptionsResult.Fail("IBeam Communications Email Defaults: FromAddress is required.");

        return ValidateOptionsResult.Success;
    }
}
