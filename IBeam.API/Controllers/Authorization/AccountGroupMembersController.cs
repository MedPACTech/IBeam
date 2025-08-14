using Microsoft.AspNetCore.Mvc;
using System;
using IBeam.Scaffolding.Models;
using IBeam.Scaffolding.Models.API;
using IBeam.Scaffolding.Services.Authorization;

namespace IBeam.API.Controllers
{

	[ApiController]
	[Route("[controller]")]
	public class AccountGroupMembersController : ControllerBase
	{
  
        private IAccountGroupMemberService _AccountGroupMemberService;

        public AccountGroupMembersController(IAccountGroupMemberService AccountGroupMemberService)
        {
            _AccountGroupMemberService = AccountGroupMemberService;
        }

        [HttpGet("{id}")]
        public ActionResult Get(Guid id)
        {
            return Ok(_AccountGroupMemberService.Fetch(id));
        }

        [HttpGet("/Accountgroups/{AccountGroupId}/Accountgroupmembers")]
        [HttpGet("/Accountgroups/{AccountGroupId}/members")]
        public ActionResult GetByAccountGroup(Guid AccountGroupId)
        {
            return Ok(_AccountGroupMemberService.FetchByAccountGroup(AccountGroupId));
        }

        [HttpPost]
        public ActionResult Post(AccountGroupMember AccountGroupMember)
        {
            _AccountGroupMemberService.Save(AccountGroupMember);
            return Ok();
        }

        [HttpPost("remove")]
        public ActionResult RemoveAccountFromGroup(RemoveAccountFromGroupRequest req)
        {
            _AccountGroupMemberService.RemoveAccountFromGroup(req.AccountId, req.AccountGroupId);
            return Ok();
        }

	}
}
