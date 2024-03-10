using Microsoft.AspNetCore.Mvc;
using IBeam.Models;
using IBeam.Services.Authorization;
using System;

namespace IBeam.API.Controllers
{

	[ApiController]
	[Route("[controller]")]
	public class ApplicationController : ControllerBase
	{
  
        private readonly IApplicationService _applicationService;

        public ApplicationController(IApplicationService applicationService)
        {
            _applicationService = applicationService;
        }

        [HttpGet("{id}")]
        public ActionResult Get(Guid id)
        {
            return Ok(_applicationService.Fetch(id));
        }

        [HttpPost]
        public ActionResult Post(Application application)
        {
            _applicationService.Save(application);
            return Ok();
        }

	}
}
