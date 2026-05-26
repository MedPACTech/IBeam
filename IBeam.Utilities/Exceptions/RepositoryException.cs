using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IBeam.Utilities.Exceptions
{
    [Serializable]
    public sealed class RepositoryException : Exception, IBaseException
    {
        public string Repository { get; }
        public string Action { get; }
        public IList<string> Parameters { get; } = new List<string>();
        public string JsonData { get; }

        // ---- IBaseException ----
        public string Code => $"REPOSITORY.{(Action ?? "UNKNOWN").ToUpperInvariant()}";
        public HttpStatusCode StatusCode => MapStatus(InnerException ?? this);
        public string UserMessage => CurateMessage(Action);

        public IReadOnlyDictionary<string, object?> GetClientExtensions() => new Dictionary<string, object?>
        {
            ["repository"] = Repository,
            ["action"] = Action,
            ["parameters"] = Parameters,
            ["jsonData"] = JsonData
        };

        private static string _defaultExceptionMessage() =>
            "Repository exception occurred. View custom exception properties for more details.";

        public RepositoryException() : base(_defaultExceptionMessage()) { }

        public RepositoryException(
            Exception innerException,
            string repository,
            string action,
            object? dataObject = null,
            params object[] inputParameters)
            : base(_defaultExceptionMessage(), innerException)
        {
            Repository = repository;
            Action = action;

            if (dataObject is not null)
            {
                try
                {
                    JsonData = JsonSerializer.Serialize(
                        dataObject,
                        new JsonSerializerOptions
                        {
                            MaxDepth = 6,
                            WriteIndented = false,
                            ReferenceHandler = ReferenceHandler.IgnoreCycles
                        });
                }
                catch (Exception ex)
                {
                    JsonData = $"[Serialization failed: {ex.Message}]";
                }
            }

            // Portable, null-safe build of Parameters
            Parameters = (
                inputParameters?.Where(p => p is not null).Select(p => p!.ToString())
                ?? Enumerable.Empty<string>()
            ).ToList();
        }

        private RepositoryException(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt) { }

        // ---- Helpers for status + message ----
        private static HttpStatusCode MapStatus(Exception inner) => inner switch
        {
            ArgumentNullException or ArgumentException => HttpStatusCode.BadRequest,
            KeyNotFoundException => HttpStatusCode.NotFound,
            TimeoutException => HttpStatusCode.GatewayTimeout,
            System.Data.Common.DbException => HttpStatusCode.ServiceUnavailable,
            InvalidOperationException => HttpStatusCode.BadRequest,
            _ => HttpStatusCode.InternalServerError
        };

        private static string CurateMessage(string? action) => (action ?? "").ToLowerInvariant() switch
        {
            "save" or "saveall" => "We couldn’t save your changes. Please review the input and try again.",
            "getbyid" => "We couldn’t find the requested item.",
            "getbyids" => "Some items could not be retrieved.",
            "getall" => "We couldn’t load the list right now. Please try again.",
            "archive" or "archiveall" => "We couldn’t archive the item(s). Please try again.",
            "unarchive" or "unarchiveall" => "We couldn’t unarchive the item(s). Please try again.",
            "delete" or "deletebyid" => "We couldn’t delete the item. Please try again.",
            _ => "A data error occurred while processing your request."
        };
    }
}
