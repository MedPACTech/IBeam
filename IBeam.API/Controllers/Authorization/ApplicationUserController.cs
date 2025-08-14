using Microsoft.AspNetCore.Mvc;
using IBeam.Models;
using IBeam.Services;
using System;
using IBeam.Scaffolding.Services.Interfaces;
using IBeam.Scaffolding.Services.Authorization;
using IBeam.Scaffolding.Services.Authorization;
using IBeam.Scaffolding.Models;

namespace IBeam.API.Controllers
{

	[ApiController]
	[Route("[controller]")]
	public class ApplicationAccountController : ControllerBase
	{
  
        private readonly IApplicationAccountService _applicationAccountService;

        public ApplicationAccountController(IApplicationAccountService applicationAccountService)
        {
            _applicationAccountService = applicationAccountService;
        }

        [HttpGet("{id}")]
        public ActionResult Get(Guid id)
        {
            return Ok(_applicationAccountService.Fetch(id));
        }

        [HttpPost]
        public ActionResult Post(ApplicationAccount applicationAccount)
        {
            _applicationAccountService.Save(applicationAccount);
            return Ok();
        }

	}
}
