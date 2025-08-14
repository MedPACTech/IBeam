using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IBeam.Scaffolding.Models.API
{
    public class AccountContext : IAccountContext
    {
        public Guid Id { get; set; }
        public Guid AccountId { get; set; }
        public string Demographics { get; set; }
        public string ApplicationSettings { get; set; }
        public string ApplicationContext { get; set; }
        public IEnumerable<string> Roles { get; set; }
        public IEnumerable<Guid> RoleIds { get; set; }
    }
}
