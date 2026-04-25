namespace BarePDF.Pdfium;

public sealed class PdfException : Exception
{
    public ulong ErrorCode { get; }

    public PdfException(string message, ulong errorCode) : base(message)
    {
        ErrorCode = errorCode;
    }
}
