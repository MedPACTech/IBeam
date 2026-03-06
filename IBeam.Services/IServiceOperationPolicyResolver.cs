using System;

namespace IBeam.Services.Abstractions
{
    public interface IServiceOperationPolicyResolver
    {
        bool IsAllowed(Type serviceType, ServiceOperation operation, bool fallback);
    }
}
