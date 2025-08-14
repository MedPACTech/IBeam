using Microsoft.AspNetCore.Mvc;
using IBeam.Models;
using IBeam.Services;
using System;
using IBeam.Scaffolding.Services.Interfaces;
using IBeam.Scaffolding.Models;

namespace IBeam.API.Controllers
{

	[ApiController]
	[Route("[controller]")]
	public class LicenseController : ControllerBase
	{
  
        private readonly ILicenseService _licenseService;

        public LicenseController(ILicenseService licenseService)
        {
            _licenseService = licenseService;
        }

        [HttpGet("latest")]
        public ActionResult GetLatest()
        {
            return Ok(_licenseService.GetLatest());
        }

        [HttpGet("{id}")]
        public ActionResult Get(Guid id)
        {
            return Ok(_licenseService.Fetch(id));
        }

        [HttpPost]
        public ActionResult Post(License license)
        {
            _licenseService.Save(license);
            return Ok();
        }

	}
}
