using Microsoft.AspNetCore.Mvc;
using IBeam.Models;
using IBeam.Repositories.Interfaces;
using IBeam.Scaffolding.Services.Authorization;
using IBeam.Scaffolding.Services.Interfaces;
using System;
using IBeam.Scaffolding.Models;

namespace IBeam.API.Controllers
{

	[ApiController]
	[Route("[controller]")]
	public class AccountRolesController : ControllerBase
	{
        private IAccountRoleService _AccountRoleService;

        public AccountRolesController(IAccountRoleService AccountRoleService)
        {
            _AccountRoleService = AccountRoleService;
        }

        [HttpGet("{id}")]
        public ActionResult Get(Guid id)
        {
            return Ok(_AccountRoleService.Fetch(id));
        }

        [HttpGet("/Accounts/{AccountId}/Accountroles")]
        [HttpGet("/Accounts/{AccountId}/roles")]
        public ActionResult GetByAccount(Guid AccountId)
        {
            return Ok(_AccountRoleService.FetchByAccount(AccountId));
        }

        [HttpPost]
        public ActionResult Post(AccountRole AccountRole)
        {
            _AccountRoleService.Save(AccountRole);
            return Ok();
        }

	}
}
