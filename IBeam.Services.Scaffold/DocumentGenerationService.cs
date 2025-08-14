using System.IO;
using AutoMapper;
using IronPdf.Rendering;
using IBeam.Scaffolding.Services.Interfaces;

namespace IBeam.Scaffolding.Services
{
    public class DocumentGenerationService : IDocumentGenerationService
    {
        private readonly IMapper _mapper;
        private readonly IDocumentService _documentService;

        public DocumentGenerationService(IMapper mapper, IDocumentService documentService)
        {
            _mapper = mapper;
            _documentService = documentService;
        }

        public async void ViewToStream()
        {
            //var byteArray = new byte[10];
            //return new MemoryStream(byteArray);
        }

        public async void ViewToByte()
        {


            //var html = await RazorTemplateEngine.RenderAsync("~/Views/Shared/SatCertification.cshtml", cert);
            //var footerHtml = GenerateFooterHTML(cert.SupportingData.AtpForm, "Page {page} of {total-pages}", "");
            //return GeneratePdf(html, footerHtml, true);
        }

        public byte[] RenderDocumentFromObject(string html)
        {
            var pdfOutput = GeneratePdf(html, string.Empty);
            return pdfOutput;
        }

        //TODO: move to engine that handles inputs 
        public static string ReadHtmlTemplate()
        {
            var path = "Assets/Template.html";
            var docHtml = File.ReadAllText(path);
            return docHtml;
        }

        private string GenerateFooterHTML(string leftText, string centerText, string rightText)
        {
            var footerHTML = "<div class=\"footer\"><div>{0}</div><div>{1}</div><div>{2}</div></div>";
            footerHTML = string.Format(footerHTML, leftText, centerText, rightText);
            return footerHTML;
        }

        private byte[] GeneratePdf(string html, string footerHTML, bool landscape = true)
        {
            var renderer = new IronPdf.HtmlToPdf();
            renderer.PrintOptions.CssMediaType = PdfCssMediaType.Screen;

            if(landscape)
                renderer.PrintOptions.PaperOrientation = IronPdf.Rendering.PdfPaperOrientation.Landscape;
            else
                renderer.PrintOptions.PaperOrientation = IronPdf.Rendering.PdfPaperOrientation.Portrait;

            renderer.PrintOptions.MarginTop = 7;
            renderer.PrintOptions.MarginBottom = 7;
            renderer.PrintOptions.MarginLeft = 7;
            renderer.PrintOptions.MarginRight = 7;

            renderer.RenderingOptions.HtmlFooter = GeneratePDFFooter(footerHTML);

            //renderer.RenderingOptions.HtmlFooter = new IronPdf.HtmlHeaderFooter()
            //{

            //    MaxHeight = 15, //millimeters
            //    HtmlFragment = "<center><i>page {page} of {total-pages}<i></center>",
            //    DrawDividerLine = true
            //};

            IronPdf.PdfDocument document = renderer.RenderHtmlAsPdf(html, string.Empty);
            byte[] res = document.Stream.ToArray();

            return res;
        }

        public IronPdf.HtmlHeaderFooter GeneratePDFFooter(string footerHTML)
        {
            var footer = new IronPdf.HtmlHeaderFooter()
            {
                MaxHeight = 15, //millimeters
                HtmlFragment = footerHTML,
                DrawDividerLine = true,
                LoadStylesAndCSSFromMainHtmlDocument = true
            };
            return footer;
        }
    }
}