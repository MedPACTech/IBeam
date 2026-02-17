using IBeam.Communications.Core.Options;
using Microsoft.Extensions.Options;

namespace IBeam.Communications.Core.Validation;

public sealed class SmsDefaultsOptionsValidator : IValidateOptions<SmsDefaultsOptions>
{
    public ValidateOptionsResult Validate(string? name, SmsDefaultsOptions options)
    {
        if (options is null) return ValidateOptionsResult.Fail("Sms defaults options are missing.");

        if (string.IsNullOrWhiteSpace(options.FromPhoneNumber))
            return ValidateOptionsResult.Fail("IBeam Communications Sms Defaults: FromPhoneNumber is required.");

        return ValidateOptionsResult.Success;
    }
}
