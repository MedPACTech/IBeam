using IBeam.Models;
using System;
using System.Collections.Generic;

namespace IBeam.Services.System
{
    public interface IServiceAuthorizationService
    {
        IEnumerable<IServiceAuthorization> Fetch();
        IEnumerable<IServiceAuthorization> FetchByApplication(Guid applicationId);
    }
}