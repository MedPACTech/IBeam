using System;

namespace IBeam.Services.Abstractions
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class ServiceOperationPolicyAttribute : Attribute
    {
        public ServiceOperationPolicyAttribute(ServiceOperation operation, bool allowed)
        {
            Operation = operation;
            Allowed = allowed;
        }

        public ServiceOperation Operation { get; }
        public bool Allowed { get; }
    }
}
