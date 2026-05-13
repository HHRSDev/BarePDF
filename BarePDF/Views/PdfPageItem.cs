using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Imaging;

namespace BarePDF.Views;

public sealed class PdfPageItem : INotifyPropertyChanged
{
    private const double LogicalPerPoint = 96.0 / 72.0;

    private double _scale = 1.0;
    private int _rotation;
    private BitmapSource? _image;

    public PdfPageItem(int pageNumber, double widthPoints, double heightPoints)
    {
        PageNumber = pageNumber;
        WidthPoints = widthPoints;
        HeightPoints = heightPoints;
    }

    public int PageNumber { get; }
    public double WidthPoints { get; }
    public double HeightPoints { get; }

    public double DisplayWidth =>
        ((_rotation == 1 || _rotation == 3) ? HeightPoints : WidthPoints) * LogicalPerPoint * _scale;
    public double DisplayHeight =>
        ((_rotation == 1 || _rotation == 3) ? WidthPoints : HeightPoints) * LogicalPerPoint * _scale;

    public int Rotation
    {
        get => _rotation;
        internal set
        {
            var normalized = ((value % 4) + 4) % 4;
            if (_rotation == normalized) return;
            _rotation = normalized;
            OnPropertyChanged(nameof(Rotation));
            OnPropertyChanged(nameof(DisplayWidth));
            OnPropertyChanged(nameof(DisplayHeight));
        }
    }

    public double Scale
    {
        get => _scale;
        internal set
        {
            if (_scale == value) return;
            _scale = value;
            OnPropertyChanged(nameof(DisplayWidth));
            OnPropertyChanged(nameof(DisplayHeight));
        }
    }

    public BitmapSource? Image
    {
        get => _image;
        internal set
        {
            if (ReferenceEquals(_image, value)) return;
            _image = value;
            OnPropertyChanged(nameof(Image));
        }
    }

    private IReadOnlyList<Rect>? _selectedRects;
    public IReadOnlyList<Rect>? SelectedRects
    {
        get => _selectedRects;
        internal set
        {
            if (ReferenceEquals(_selectedRects, value)) return;
            _selectedRects = value;
            OnPropertyChanged(nameof(SelectedRects));
        }
    }

    public int SelectionStart { get; internal set; } = -1;
    public int SelectionEnd { get; internal set; } = -1;

    private System.Windows.Visibility _itemVisibility = System.Windows.Visibility.Visible;
    public System.Windows.Visibility ItemVisibility
    {
        get => _itemVisibility;
        internal set
        {
            if (_itemVisibility == value) return;
            _itemVisibility = value;
            OnPropertyChanged(nameof(ItemVisibility));
        }
    }

    private IReadOnlyList<Rect>? _matchRects;
    public IReadOnlyList<Rect>? MatchRects
    {
        get => _matchRects;
        internal set
        {
            if (ReferenceEquals(_matchRects, value)) return;
            _matchRects = value;
            OnPropertyChanged(nameof(MatchRects));
        }
    }

    private IReadOnlyList<Rect>? _currentMatchRects;
    public IReadOnlyList<Rect>? CurrentMatchRects
    {
        get => _currentMatchRects;
        internal set
        {
            if (ReferenceEquals(_currentMatchRects, value)) return;
            _currentMatchRects = value;
            OnPropertyChanged(nameof(CurrentMatchRects));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
