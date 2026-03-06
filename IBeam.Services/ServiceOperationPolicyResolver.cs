using Microsoft.Extensions.Options;
using System;
using System.Linq;

namespace IBeam.Services.Abstractions
{
    public sealed class ServiceOperationPolicyResolver : IServiceOperationPolicyResolver
    {
        private readonly IOptionsMonitor<ServicePolicyOptions> _options;

        public ServiceOperationPolicyResolver(IOptionsMonitor<ServicePolicyOptions> options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public bool IsAllowed(Type serviceType, ServiceOperation operation, bool fallback)
        {
            if (serviceType is null)
                return fallback;

            // 1) Attribute override on service type.
            var attr = serviceType
                .GetCustomAttributes(typeof(ServiceOperationPolicyAttribute), inherit: true)
                .OfType<ServiceOperationPolicyAttribute>()
                .LastOrDefault(a => a.Operation == operation);
            if (attr is not null)
                return attr.Allowed;

            // 2) Options override by type full name or short name.
            var options = _options.CurrentValue;
            if (options.Services.TryGetValue(serviceType.FullName ?? string.Empty, out var full))
            {
                var byFull = full.GetValue(operation);
                if (byFull.HasValue)
                    return byFull.Value;
            }

            if (options.Services.TryGetValue(serviceType.Name, out var shortName))
            {
                var byShort = shortName.GetValue(operation);
                if (byShort.HasValue)
                    return byShort.Value;
            }

            // 3) Legacy in-code fallback.
            return fallback;
        }
    }
}
