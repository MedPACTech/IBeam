// IBeam.Utilities/IBaseException.cs
using System.Collections.Generic;
using System.Net;

namespace IBeam.Utilities
{
    /// <summary>
    /// App-level exception contract used by the API to produce a consistent ProblemDetails response.
    /// </summary>
    public interface IBaseException
    {
        /// A short, stable machine code (e.g., "REPOSITORY.SAVE", "DOMAIN.RULE").
        string Code { get; }

        /// Curated, user-safe message for clients.
        string UserMessage { get; }

        /// HTTP status the API should return.
        HttpStatusCode StatusCode { get; }

        /// Safe metadata to include in ProblemDetails.Extensions.
        IReadOnlyDictionary<string, object?> GetClientExtensions();
    }
}
