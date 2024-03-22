using Microsoft.AspNetCore.Mvc.Filters;
using IBeam.Models.API;
using IBeam.Models.Interfaces;
using IBeam.Services.System;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IBeam.API.Controllers.Extensions
{
    public class HandleContextAttribute : ActionFilterAttribute
    {

        private readonly bool _ignoreContext;

        public HandleContextAttribute(bool ignoreContext = false)
        {
            _ignoreContext = ignoreContext;
        }

        public override void OnActionExecuting(ActionExecutingContext actionExecutingContext)
        {
            if (_ignoreContext == false)
            { 
                var controller = (BaseController)actionExecutingContext.Controller;
                controller.AssignAccountContext();
               // SetContextForServices(controller._servicesWithContext, controller._AccountContext);
            }
            base.OnActionExecuting(actionExecutingContext);
        }

        //private static void SetContextForServices(List<IBaseService> servicesCollection, IAccountContext AccountContext)
        //{
        //    foreach(var service in servicesCollection)
        //    {
        //        service.SetAccountContext(AccountContext);
        //    }
        //}
    }
}
