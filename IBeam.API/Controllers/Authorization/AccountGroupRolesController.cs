using Microsoft.AspNetCore.Mvc;
using IBeam.Models;
using IBeam.Scaffolding.Services;
using IBeam.Scaffolding.Services.Authorization;
using IBeam.Scaffolding.Services.Interfaces;
using System;
using IBeam.Scaffolding.Models;

namespace IBeam.API.Controllers
{

	[ApiController]
	[Route("[controller]")]
	public class AccountGroupRolesController : ControllerBase
	{
  
        private IAccountGroupRoleService _AccountGroupRoleService;

        public AccountGroupRolesController(IAccountGroupRoleService AccountGroupRoleService)
        {
            _AccountGroupRoleService = AccountGroupRoleService;
        }

        [HttpGet("{id}")]
        public ActionResult Get(Guid id)
        {
            return Ok(_AccountGroupRoleService.Fetch(id));
        }

        [HttpGet("/Accountgroups/{AccountGroupId}/Accountgrouproles")]
        [HttpGet("/Accountgroups/{AccountGroupId}/roles")]
        public ActionResult GetByAccountGroup(Guid AccountGroupId)
        {
            return Ok(_AccountGroupRoleService.FetchByAccountGroup(AccountGroupId));
        }

        [HttpPost]
        public ActionResult Post(AccountGroupRole AccountGroupRole)
        {
            _AccountGroupRoleService.Save(AccountGroupRole);
            return Ok();
        }

	}
}
