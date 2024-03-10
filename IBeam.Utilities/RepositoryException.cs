using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Threading.Tasks;

namespace IBeam.API.Utilities
{
    //
    // Summary:
    //     Represents errors that occur within Repositories.
    //     Any Exceptions should bypass all try catches and be proccessed to help identify exception locations easier
    //
    [Serializable]
    public class RepositoryException : Exception
    {

        private static string _defaultExceptionmessage()
        {
            return "Repository exception occured.View custom exception properties for more details.";
        }

        public string Repository { get; set; }
        public string Action { get; set; }
        public IList<string> Paramaters { get; set; }
        public string JsonData { get; set; }

        // Constructors
        public RepositoryException()
            : base(_defaultExceptionmessage())
        {
        }

        public RepositoryException(Exception innerException, object dataObject = null)
            : base(_defaultExceptionmessage(), innerException)
        {
            GenerateException(innerException, dataObject);
        }

        public RepositoryException(Exception innerException, object dataObject = null, params object[] inputParamaters)
            : base(_defaultExceptionmessage(), innerException)
        {
            GenerateException(innerException, dataObject, inputParamaters);
        }

        public RepositoryException(Exception innerException, string repository, string action, object dataObject = null, params object[] inputParamaters)
                   : base(_defaultExceptionmessage(), innerException)
        {
            Repository = repository;
            Action = action;
            GenerateException(innerException, dataObject, inputParamaters);
        }

        private void GenerateException(Exception innerException, object dataObject = null, params object[] inputParamaters)
        {
            if (dataObject != null)
                JsonData = JsonSerializer.Serialize(dataObject);

            if (inputParamaters != null)
                Paramaters = inputParamaters.Select(i => i.ToString()).ToList();
        }

        // Ensure Exception is Serializable
        protected RepositoryException(SerializationInfo info, StreamingContext ctxt)
            : base(info, ctxt)
        { }
    }
}
