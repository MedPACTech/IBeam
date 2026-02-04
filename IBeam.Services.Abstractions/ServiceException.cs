using System;

namespace IBeam.Services.Abstractions
{
    public class ServiceException : Exception
    {
        public string ServiceName { get; }
        public string Operation { get; }

        public ServiceException(Exception inner, string operation, string serviceName)
            : base($"Service error in {serviceName}.{operation}", inner)
        {
            ServiceName = serviceName;
            Operation = operation;
        }
    }
}
