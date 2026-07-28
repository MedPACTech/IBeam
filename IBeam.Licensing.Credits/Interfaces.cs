namespace IBeam.Licensing.Credits;

public interface ILicenseCreditGate
{
    Task<LicenseCreditGateResult> CheckAsync(
        LicenseCreditGateRequest request,
        CancellationToken ct = default);

    Task<LicenseCreditExecutionResult<T>> ExecuteAsync<T>(
        LicenseCreditGateRequest request,
        Func<CancellationToken, Task<CreditMeasuredOperationResult<T>>> operation,
        CancellationToken ct = default);
}
