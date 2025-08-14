using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using IBeam.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using IBeam.Scaffolding.Models.API;

namespace IBeam.API.Controllers
{
    //
    // Summary:
    //     A base class for an MVC controller without view support.
    [Authorize]
    [Controller]
    public abstract class BaseController : ControllerBase
    {
        internal AccountContext _AccountContext;
        //internal List<IBaseService> _servicesWithContext;

        protected BaseController()
        {
        //    _servicesWithContext = new List<IBaseService>();
        }

        //internal protected void AddServiceWithContext(IBaseService service)
        //{
        //    try
        //    {
        //        var baseService = service;
           //     _servicesWithContext.Add(baseService);
        //    }
        //    catch (Exception e)
        //    {
        //        throw new Exception("Controller can only add IBaseService derrived classes to ServiceCollection", e);
        //    }

        //}

        //TODO: Account.Identity research
        internal void AssignAccountContext()
        {
            _AccountContext = new AccountContext();
            var AccountTest = User;



            var httpContext = HttpContext;
            if (httpContext != null)
            {
                var Account = httpContext.User;

                if (Account != null && Account.Claims != null)
                {
                    var AccountRoles = Account.Claims.Where(x => x.Type == ClaimTypes.Role).Select(y => y.Value);
                    var AccountRoleIds = Account.Claims.Where(x => x != null && x.Type == "RoleId").Select(y => Guid.Parse(y.Value));

                    var AccountIdClaim = Account.Claims.Where(x => x != null && x.Type == "AccountId").FirstOrDefault();
                    var AccountId = AccountIdClaim == null ? Guid.Empty : Guid.Parse(AccountIdClaim.Value);

                    _AccountContext.Roles = AccountRoles;
                    _AccountContext.RoleIds = AccountRoleIds;
                    _AccountContext.AccountId = AccountId;
                }
            }
        }
    }
}
