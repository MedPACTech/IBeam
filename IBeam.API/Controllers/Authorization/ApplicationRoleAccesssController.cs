using Microsoft.AspNetCore.Mvc;
using IBeam.Models;
using IBeam.Services;
using IBeam.Services.Authorization;
using IBeam.Services.Interfaces;
using System;

namespace IBeam.API.Controllers
{

	[ApiController]
	[Route("[controller]")]
	public class ApplicationRoleAccesssController : ControllerBase
	{
  
        private IApplicationRoleAccessService _applicationRoleAccessService;

        public ApplicationRoleAccesssController(IApplicationRoleAccessService applicationRoleAccessService)
        {
            _applicationRoleAccessService = applicationRoleAccessService;
        }

        [HttpGet("{id}")]
        public ActionResult Get(Guid id)
        {
            return Ok(_applicationRoleAccessService.Fetch(id));
        }

        [HttpPost]
        public ActionResult Post(ApplicationRoleAccess applicationRoleAccess)
        {
            _applicationRoleAccessService.Save(applicationRoleAccess);
            return Ok();
        }

	}
}
