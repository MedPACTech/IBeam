using Microsoft.AspNetCore.Mvc;
using IBeam.Models;
using IBeam.Services.Interfaces;
using System;

namespace IBeam.API.Controllers
{

	[ApiController]
	[Route("[controller]")]
	public class NotificationsController : ControllerBase
	{
  
        private INotificationService _notificationService;

        public NotificationsController(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        [HttpGet("{id}")]
        public ActionResult Get(Guid id)
        {
            return Ok(_notificationService.Fetch(id));
        }

        [HttpPost("delete/{id}")]
        public ActionResult Delete(Guid id)
        {
            return Ok(_notificationService.Delete(id)); 
        }

        [HttpGet("Account/{AccountId}")]
        public ActionResult GetByAccount(Guid AccountId)
        {
            return Ok(_notificationService.FetchByAccount(AccountId));
        }


        [HttpPost("read/{id}")]
        public ActionResult Post(Guid id)
        {
            _notificationService.SaveAsRead(id);
            return Ok();
        }
    }
}
