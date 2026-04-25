using System.ComponentModel;
using System.Windows.Media.Imaging;

namespace BarePDF.Views;

public sealed class PdfPageItem : INotifyPropertyChanged
{
    private BitmapSource? _image;

    public PdfPageItem(int pageNumber, double widthPoints, double heightPoints)
    {
        PageNumber = pageNumber;
        DisplayWidth = widthPoints * 96.0 / 72.0;
        DisplayHeight = heightPoints * 96.0 / 72.0;
    }

    public int PageNumber { get; }
    public double DisplayWidth { get; }
    public double DisplayHeight { get; }

    public BitmapSource? Image
    {
        get => _image;
        internal set
        {
            if (ReferenceEquals(_image, value)) return;
            _image = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
