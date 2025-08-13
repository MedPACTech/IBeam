using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;

namespace IBeam.Utilities
{
    [Serializable]
    public sealed class RepositoryException : Exception
    {
        public string Repository { get; }
        public string Action { get; }
        public IList<string> Parameters { get; }
        public string JsonData { get; }

        private static string _defaultExceptionMessage() =>
            "Repository exception occurred. View custom exception properties for more details.";

        public RepositoryException() : base(_defaultExceptionMessage()) { }

        public RepositoryException(Exception innerException, string repository, string action, object dataObject = null, params object[] inputParameters)
            : base(_defaultExceptionMessage(), innerException)
        {
            Repository = repository;
            Action = action;

            if (dataObject != null)
            {
                try
                {
                    JsonData = JsonSerializer.Serialize(dataObject, new JsonSerializerOptions
                    {
                        MaxDepth = 6,
                        WriteIndented = false
                    });
                }
                catch (Exception ex)
                {
                    JsonData = $"[Serialization failed: {ex.Message}]";
                }
            }

            if (inputParameters?.Any() == true)
            {
                Parameters = inputParameters
                    .Where(p => p != null)
                    .Select(p => p.ToString())
                    .ToList();
            }
            else
            {
                Parameters = Array.Empty<string>();
            }
        }

        private RepositoryException(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt) { }
    }
}
