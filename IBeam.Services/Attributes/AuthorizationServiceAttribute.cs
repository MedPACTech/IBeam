using System;

namespace IBeam.Services
{
    [AttributeUsage(AttributeTargets.Class)]
    internal class AuthorizationServiceAttribute : Attribute
    {
        private string v1;
        private string v2;

        public AuthorizationServiceAttribute(string serviceId, string serviceName)
        {
            this.v1 = serviceId;
            this.v2 = serviceName;
        }
    }
}