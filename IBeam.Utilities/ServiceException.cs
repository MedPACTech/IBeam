using System;
using System.Collections.Generic;
using System.Net;

namespace IBeam.Utilities
{
    [Serializable]
    public sealed class ServiceException : Exception, IBaseException
    {
        public string Service { get; }
        public string Action { get; }
        public IList<object?> Parameters { get; }

        public ServiceException(Exception inner, string service, string action, params object?[] parameters)
            : base("A service error occurred while processing your request.", inner)
        {
            Service = service;
            Action = action;
            Parameters = parameters is null ? new List<object?>() : new List<object?>(parameters);
        }

        // IBaseException
        public string Code => $"SERVICE.{Action?.ToUpperInvariant() ?? "UNKNOWN"}";
        public string UserMessage => "We couldn’t complete the operation. Please try again.";
        public HttpStatusCode StatusCode => InnerException switch
        {
            ArgumentNullException or ArgumentException => HttpStatusCode.BadRequest,
            KeyNotFoundException => HttpStatusCode.NotFound,
            InvalidOperationException => HttpStatusCode.BadRequest,
            _ => HttpStatusCode.InternalServerError
        };

        public IReadOnlyDictionary<string, object?> GetClientExtensions() => new Dictionary<string, object?>
        {
            ["service"] = Service,
            ["action"] = Action,
            ["parameters"] = Parameters
        };
    }
}
