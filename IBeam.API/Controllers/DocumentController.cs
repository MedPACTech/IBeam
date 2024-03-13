using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using IBeam.Models;
using IBeam.Services.Interfaces;
using System;

namespace IBeam.API.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class DocumentController : ControllerBase
    {
        private readonly IDocumentService _documentService;
        private readonly IDocumentGenerationService _documentGenerationService;

        public DocumentController(IDocumentService documentService, IDocumentGenerationService documentGenerationService)
        {
            _documentService = documentService;
            _documentGenerationService = documentGenerationService;
        }

        [HttpPost("image/upload")]
        public ActionResult Post(
            [FromForm] IFormFile content,
            [FromForm] string additionalData)
        {
            _documentService.Save(new Document(content, additionalData));
            return Ok();
        }

        [HttpGet("pdf/{id}")]
        public ActionResult ViewStoredPdf(Guid id)
        {
            var stream = _documentService.GetDocumentAsStream(id);
            return new FileStreamResult(stream,"application/pdf");
        }

        [HttpGet("{id}")]
        public ActionResult FetchById(Guid id)
        {
            var image = _documentService.GetDocument(id);
            return Ok(image);
        }

        [HttpPost("save")]
        public ActionResult Save([FromForm] IFormFile content)
        {
            var image = _documentService.Save(new Document(content, ""));
            return Ok(image);
        }

        [HttpGet("associated/{associatedId}")]
        public ActionResult Get(Guid associatedId)
        {
            var images = _documentService.GetByAssociatedId(associatedId);
            return Ok(images);
        }

        [HttpPost("image/archive")]
        public ActionResult Post(Document image)
        {
            bool result = _documentService.Archive(image);
            return Ok();
        }
    }
}
