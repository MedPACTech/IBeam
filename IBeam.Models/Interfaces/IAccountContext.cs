using System;
using System.Collections.Generic;

namespace IBeam.Models.API
{
    public interface IAccountContext
    {
        Guid Id { get; set; }
        Guid AccountId { get; set; }
        IEnumerable<string> Roles { get; set; }
        IEnumerable<Guid> RoleIds { get; set; }
        string ApplicationContext { get; set; }
        string ApplicationSettings { get; set; }
        string Demographics { get; set; }

    }
}