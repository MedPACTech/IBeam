using IBeam.Communications.Abstractions.Options;
using Microsoft.Extensions.Options;

namespace IBeam.Communications.Abstractions.Validation;

public sealed class SmsOptionsValidator : IValidateOptions<SmsOptions>
{
    public ValidateOptionsResult Validate(string? name, SmsOptions options)
    {
        if (options is null) return ValidateOptionsResult.Fail("Sms defaults options are missing.");

        if (string.IsNullOrWhiteSpace(options.FromPhoneNumber))
            return ValidateOptionsResult.Fail("IBeam Communications Sms Defaults: FromPhoneNumber is required.");

        return ValidateOptionsResult.Success;
    }
}
