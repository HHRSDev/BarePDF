namespace BarePDF.Pdfium;

public abstract record PdfLinkTarget;

public sealed record PdfUriLinkTarget(string Url) : PdfLinkTarget;

public sealed record PdfGoToLinkTarget(int PageIndex) : PdfLinkTarget;
