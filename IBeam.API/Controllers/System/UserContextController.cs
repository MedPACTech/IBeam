using Microsoft.AspNetCore.Mvc;
using IBeam.DataModels;
using IBeam.Models.API;
using IBeam.Services.Authorization;
using IBeam.Services.Interfaces;
using System;

namespace IBeam.API.Controllers
{

	[ApiController]
	[Route("[controller]")]
	public class AccountContextController : ControllerBase
	{
  
        private IAccountContextService _AccountContextService;
        private IAccountService _AccountService;

        public AccountContextController(IAccountContextService AccountContextService, IAccountService AccountService)
        {
            _AccountContextService = AccountContextService;
            _AccountService = AccountService;
        }

        [HttpGet("{AccountId}")]
        [HttpGet("/Accounts/{AccountId}/Accountcontext")]
        [HttpGet("/Accounts/{AccountId}/context")]
        public ActionResult Get(Guid AccountId)
        {
            return Ok(_AccountService.FetchContextByAccount(AccountId));
        }

        [HttpPost]
        public ActionResult Post(AccountContext AccountContext)
        {
            _AccountContextService.Save(AccountContext);
            return Ok();
        }

        [HttpPost("baseContext")]
        public ActionResult Post(AccountContextDTO AccountContext)
        {
            _AccountContextService.SaveBaseContext(AccountContext);
            return Ok();
        }

        [HttpGet("getBaseContext/{AccountId}")]
        public ActionResult GetBase(Guid AccountId)
        {
            var result = _AccountContextService.GetBaseContext(AccountId);
            return Ok(result);
        }
    }
}
