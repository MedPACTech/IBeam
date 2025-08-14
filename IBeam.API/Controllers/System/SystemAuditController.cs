using System;
using Microsoft.AspNetCore.Mvc;
using IBeam.Models;
using IBeam.Services.Abstractions;

namespace IBeam.API.Controllers
{

    [ApiController]
	[Route("[controller]")]
	public class SystemAuditController : ControllerBase
	{
  
       // private ISystemAuditService _systemAuditService;

       // public SystemAuditController(ISystemAuditService systemAuditService)
       // {
      //      _systemAuditService = systemAuditService;
      //  }

        //[HttpGet("{id}")]
        //public ActionResult Get(Guid id)
        //{
        //    return Ok(_systemAuditService.Fetch(id));
        //}

       // [HttpGet]
       // public ActionResult GetAll()
       // {
     //       var data = _systemAuditService.Fetch();
     //       return Ok(data);
     //   }

	}
}
