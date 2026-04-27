using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace BarePDF.Views;

public sealed class PageSelectionLayer : FrameworkElement
{
    public static readonly DependencyProperty SelectedRectsProperty =
        DependencyProperty.Register(
            nameof(SelectedRects),
            typeof(IReadOnlyList<Rect>),
            typeof(PageSelectionLayer),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<Rect>? SelectedRects
    {
        get => (IReadOnlyList<Rect>?)GetValue(SelectedRectsProperty);
        set => SetValue(SelectedRectsProperty, value);
    }

    private static readonly Brush HighlightBrush;

    static PageSelectionLayer()
    {
        var brush = new SolidColorBrush(Color.FromArgb(96, 70, 130, 220));
        brush.Freeze();
        HighlightBrush = brush;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var rects = SelectedRects;
        if (rects is null) return;
        foreach (var rect in rects)
        {
            if (rect.Width <= 0 || rect.Height <= 0) continue;
            dc.DrawRectangle(HighlightBrush, null, rect);
        }
    }
}
