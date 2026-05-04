using System.ComponentModel;
using System.Windows.Media.Imaging;

namespace BarePDF.Views;

public sealed class ThumbnailItem : INotifyPropertyChanged
{
    private BitmapSource? _thumbnail;

    public ThumbnailItem(int pageNumber, double widthPoints, double heightPoints)
    {
        PageNumber = pageNumber;
        WidthPoints = widthPoints;
        HeightPoints = heightPoints;
    }

    public int PageNumber { get; }
    public double WidthPoints { get; }
    public double HeightPoints { get; }

    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        internal set
        {
            if (ReferenceEquals(_thumbnail, value)) return;
            _thumbnail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
