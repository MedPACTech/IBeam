using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using AutoMapper;
using IronPdf;
using IBeam.Scaffolding.DataModels;
using IBeam.Scaffolding.Models.Interfaces;
using IBeam.Repositories.Interfaces;
using IBeam.Scaffolding.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using IBeam.Scaffolding.Repositories.Interfaces;
using IBeam.Scaffolding.Models;

namespace IBeam.Scaffolding.Services
{
    public class DocumentService : IDocumentService
    {
        private readonly IMapper _mapper;
        private readonly IDocumentRepository _repository;

        public DocumentService(IMapper mapper,
            IDocumentRepository repository)
        {
            _mapper = mapper;
            _repository = repository;
        }

        public Guid Save(Document image)
        {
            if (image.Id == Guid.Empty)
            {
                var id = Guid.NewGuid();
                image.Id = id;
            }
            _repository.Save(_mapper.Map<DocumentDTO>(image));
            return image.Id;
        }

        public IEnumerable<Document> GetByAssociatedId(Guid associatedId)
        {
            return _mapper.Map<IEnumerable<Document>>(_repository.GetByAssociatedId(associatedId));
        }

        public bool Archive(Document image)
        {
            var documentDTO = _mapper.Map<DocumentDTO>(image);
            _repository.Archive(documentDTO);
            return true;
        }

        public Guid SaveUploadedPdf(AdditionalData additionalData, IFormFile pdf)
        {
            var doc = new Document(pdf, JsonSerializer.Serialize(additionalData));
            var docId = Guid.NewGuid();
            doc.Id = docId;
            Save(doc);
            return docId;
        }

        public Document GetDocument(Guid id)
        {
            var doc = _repository.GetById(id);
            return _mapper.Map<Document>(doc);
        }

        public MemoryStream GetDocumentAsStream(Guid id)
        {
            var document = GetDocument(id);
            MemoryStream memStream = new MemoryStream();
            memStream.Write(document.Content, 0, document.Content.Length);
            memStream.Seek(0, SeekOrigin.Begin);
            return memStream;
        }

        public Guid SaveGeneratedPdf(string name, byte[] data)
        {
            var document = new Document
            {
                Id = Guid.NewGuid(),
                Name = name,
                Content = data,
                ContentType = "application/pdf",
                IsArchived = false
            };
            Save(document);
            return document.Id;
        }

        public void CombinePDFs(Guid certId, Guid[] ids, string name, byte[] certContent)
        {
            var certBinary = new IronPdf.PdfDocument(certContent);
            var pdfs = new List<IronPdf.PdfDocument>();
            pdfs.Add(certBinary);

            foreach (var id in ids)
            {
                var doc = GetDocument(id);

                if (doc.ContentType == "application/pdf")
                {
                    pdfs.Add(new IronPdf.PdfDocument(GetContentById(id)));
                }
                else
                {
                    MemoryStream ms = new MemoryStream(doc.Content);
                    Image image = Image.FromStream(ms);
                    var pdf = ImageToPdfConverter.ImageToPdf(image);
                    pdfs.Add(pdf);
                }
            }
            var mergedPDF = IronPdf.PdfDocument.Merge(pdfs);

            var mergedDocument = new Document
            {
                Id = certId,
                Name = name,
                Content = mergedPDF.BinaryData,
                ContentType = "application/pdf",
                IsArchived = false
            };
            Save(mergedDocument);
        }


        public byte[] GetContentById(Guid id)
        {
            var result = _repository.GetById(id);
            if (result != null)
            {
                return result.Content;
            }
            return new byte[0];
        }

        public IEnumerable<Document> GetByIds(List<Guid> ids)
        {
            var result = _repository.GetByIds(ids);
            return _mapper.Map<List<Document>>(result);
        }
    }
}