namespace IBeam.AccessControl;

/// <summary>
/// Marks a stretch of work as originating from a verified machine callback rather than a signed-in
/// caller, so service-operation authorization is not demanded of it.
/// <para>
/// Service-operation authorization asks two things: which principal is acting, and in which tenant.
/// Both are answerable for a person clicking a button. Neither is answerable for a provider
/// callback — a payment processor cannot sign in to the consuming app, and the tenant is not known
/// until the payload has been parsed. Demanding them of such a caller fails every time, which is
/// what happened to Stripe webhooks: recording the provider event threw
/// "tenantId is required for service operation authorization" before any payload could be handled.
/// </para>
/// <para>
/// This is deliberately narrower than marking those operations <c>Permission = false</c> outright.
/// The same services are also reached by authenticated admin and support tooling, which should keep
/// being authorized; only the callback path is exempt, and only for as long as the scope is held.
/// </para>
/// <para>
/// <b>Opening a scope is not a substitute for authenticating the caller.</b> It only suppresses a
/// check that cannot apply. Whatever opens one is responsible for having already established that
/// the request is genuine by other means — for provider webhooks that is signature verification
/// over the raw body, which must happen first.
/// </para>
/// </summary>
public interface IServiceOperationSystemContext
{
    /// <summary>Whether a system scope is currently open.</summary>
    bool IsActive { get; }

    /// <summary>Why the innermost open scope was entered, for audit. Null when none is open.</summary>
    string? Reason { get; }

    /// <summary>
    /// Opens a system scope until the returned handle is disposed. Nesting is supported; the scope
    /// closes when the outermost handle is disposed.
    /// </summary>
    /// <param name="reason">
    /// Short, greppable description of the caller, recorded on audit entries — for example
    /// "billing.provider-webhook". Required, so an exemption can never appear unexplained.
    /// </param>
    IDisposable Enter(string reason);
}

/// <summary>
/// Default <see cref="IServiceOperationSystemContext"/>. Registered scoped, so a scope cannot
/// outlive the request that opened it even if something forgets to dispose the handle.
/// </summary>
public sealed class ServiceOperationSystemContext : IServiceOperationSystemContext
{
    private readonly Stack<string> _reasons = new();

    public bool IsActive => _reasons.Count > 0;

    public string? Reason => _reasons.Count > 0 ? _reasons.Peek() : null;

    public IDisposable Enter(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required so an authorization exemption is never unexplained.", nameof(reason));
        }

        _reasons.Push(reason.Trim());
        return new Scope(this);
    }

    private void Exit()
    {
        if (_reasons.Count > 0)
        {
            _reasons.Pop();
        }
    }

    private sealed class Scope(ServiceOperationSystemContext owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            // Guarded: a double dispose (a using inside a try/finally that also disposes, say) must
            // not pop someone else's scope and silently exempt work that was meant to be checked.
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.Exit();
        }
    }
}
