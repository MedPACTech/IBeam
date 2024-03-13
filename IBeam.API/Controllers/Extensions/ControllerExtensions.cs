using Microsoft.AspNetCore.Mvc;
using IBeam.Services.System;
using System;
using System.Linq;
using System.Security.Claims;

namespace IBeam.API.Controllers.Extensions
{
    public static class ControllerExtensions
    {
        public static Guid GetRequestAccountID(this ControllerBase controller)
        {
            return Guid.Parse(controller.User.Claims.First(x => x.Type == "AccountId").Value);
        }

        public static string GetRequestAccountRole(this ControllerBase controller)
        {
            return controller.User.Claims.First(x => x.Type == ClaimTypes.Role).Value;
        }

        public static void SetAccountContext(this BaseController controller, BaseService baseService)
        {
            
            baseService.SetAccountContext(controller._AccountContext);
        }

    }
}
