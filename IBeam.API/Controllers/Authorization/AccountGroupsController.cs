using Microsoft.AspNetCore.Mvc;
using IBeam.Models;
using IBeam.Services;
using IBeam.Scaffolding.Services.Interfaces;
using System;
using IBeam.Scaffolding.Services.Authorization;
using IBeam.Scaffolding.Models;

namespace IBeam.API.Controllers
{

	[ApiController]
	[Route("[controller]")]
	public class AccountGroupsController : ControllerBase
	{
  
        private IAccountGroupService _AccountGroupService;

        public AccountGroupsController(IAccountGroupService AccountGroupService)
        {
            _AccountGroupService = AccountGroupService;
        }

        [HttpGet("{id}")]
        public ActionResult Get(Guid id)
        {
            return Ok(_AccountGroupService.Fetch(id));
        }

        [HttpGet("")]
        public ActionResult GetAll()
        {
            return Ok(_AccountGroupService.FetchAll());
        }

        [HttpGet("/Accounts/{AccountId}/Accountgroups")]
        [HttpGet("/Accounts/{AccountId}/groups")]
        public ActionResult GetByAccount(Guid AccountId)
        {
            return Ok(_AccountGroupService.FetchByAccount(AccountId));
        }

        [HttpPost]
        public ActionResult Post(AccountGroup AccountGroup)
        {
            _AccountGroupService.Save(AccountGroup);
            return Ok();
        }

        [HttpDelete("{id}")]
        public ActionResult Delete(Guid id)
        {
            _AccountGroupService.Delete(id);
            return Ok();
        }

    }
}
