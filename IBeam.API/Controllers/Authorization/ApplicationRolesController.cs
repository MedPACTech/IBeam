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
	public class ApplicationRolesController : ControllerBase
	{
  
        private IApplicationRoleService _applicationRoleService;

        public ApplicationRolesController(IApplicationRoleService applicationRoleService)
        {
            _applicationRoleService = applicationRoleService;
        }

        [HttpGet("{id}")]
        public ActionResult Get(Guid id)
        {
            return Ok(_applicationRoleService.Fetch(id));
        }

        [HttpGet("/application/{id}/roles")]
        [HttpGet("/application/{id}/applicationRoles")]
        public ActionResult GetByApplicationId(Guid id)
        {
            return Ok(_applicationRoleService.FetchByApplicationId(id));
        }

        [HttpPost]
        public ActionResult Post(ApplicationRole applicationRole)
        {
            _applicationRoleService.Save(applicationRole);
            return Ok();
        }

	}
}
