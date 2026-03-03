using Azure;
using IBeam.Identity.Exceptions;
using Microsoft.AspNetCore.Identity;
using System;
using System.Linq;
using System.Security;

namespace IBeam.Identity.Repositories.AzureTable.Types;

internal static class IdentityExceptionTranslator
{
    /// <summary>
    /// Converts provider/internal exceptions (Azure Tables, Microsoft Identity) into Abstractions exceptions.
    /// Keeps all provider details inside this assembly.
    /// </summary>
    public static Exception ToProviderException(Exception ex)
    {
        // Already translated
        if (ex is IdentityValidationException ||
            ex is IdentityUnauthorizedException ||
            ex is IdentityProviderException)
            return ex;

        // Cancellation should flow through unchanged
        if (ex is OperationCanceledException)
            return ex;

        // Microsoft Identity failures (rarely thrown directly, but can happen)
        if (ex is IdentityResultException ire)
            return new IdentityValidationException(ire.Message, inner: ire);

        // Unauthorized-style cases
        if (ex is SecurityException || ex is UnauthorizedAccessException)
            return new IdentityUnauthorizedException("Unauthorized.", ex);

        // Azure Tables / Storage errors
        if (ex is RequestFailedException rfe)
        {
            // Auth problems
            if (rfe.Status is 401 or 403)
                return new IdentityUnauthorizedException("Azure Table authorization failure.", rfe);

            // Concurrency conflict (ETag mismatch)
            if (rfe.Status == 412)
                return new IdentityProviderException("Azure Table concurrency conflict.", inner: rfe);

            // Not found (often safe to treat as validation depending on call site)
            if (rfe.Status == 404)
                return new IdentityProviderException("Azure Table resource not found.", inner: rfe);

            // Conflict (e.g., duplicate insert)
            if (rfe.Status == 409)
                if (rfe.Status == 409)
                    return new IdentityValidationException("Resource already exists.", inner: rfe);


            // Default
            return new IdentityProviderException($"Azure Table request failed ({rfe.Status}).", inner: rfe);
        }

        // Conservative default: provider failure
        return new IdentityProviderException("Identity provider failure.", inner: ex);
    }

    /// <summary>
    /// Optional helper if you want to throw on IdentityResult.Succeeded == false.
    /// Internal so Microsoft Identity never leaks.
    /// </summary>
    internal static void ThrowIfFailed(IdentityResult result, string messagePrefix)
    {
        if (result.Succeeded) return;

        // Keep message safe and developer-friendly
        var msg = messagePrefix;

        var details = string.Join("; ",
            result.Errors.Select(e =>
                string.IsNullOrWhiteSpace(e.Code)
                    ? e.Description
                    : $"{e.Code}: {e.Description}"));

        if (!string.IsNullOrWhiteSpace(details))
            msg = $"{msg} {details}";

        throw new IdentityResultException(msg);
    }

    internal sealed class IdentityResultException : Exception
    {
        public IdentityResultException(string message) : base(message) { }
        public IdentityResultException(string message, Exception inner) : base(message, inner) { }
    }
}
