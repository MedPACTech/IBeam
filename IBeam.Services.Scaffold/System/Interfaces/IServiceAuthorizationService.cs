using IBeam.Scaffolding.Models.Interfaces;
using IBeam.Scaffolding.Models;
using System;
using System.Collections.Generic;

namespace IBeam.Scaffolding.Services.System
{
    public interface IServiceAuthorizationService
    {
        IEnumerable<IServiceAuthorization> Fetch();
        IEnumerable<IServiceAuthorization> FetchByApplication(Guid applicationId);
    }
}