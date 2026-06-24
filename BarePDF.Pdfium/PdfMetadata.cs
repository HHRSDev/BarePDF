namespace BarePDF.Pdfium;

public sealed record PdfMetadata(
    string? Title,
    string? Author,
    string? Subject,
    string? Keywords,
    string? Creator,
    string? Producer,
    DateTimeOffset? CreationDate,
    DateTimeOffset? ModificationDate,
    string PdfVersion);
