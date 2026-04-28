using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace BarePDF.Views;

public sealed class PageFindLayer : FrameworkElement
{
    private const double PadX = 1.0;
    private const double PadY = 2.0;

    public static readonly DependencyProperty MatchRectsProperty =
        DependencyProperty.Register(
            nameof(MatchRects),
            typeof(IReadOnlyList<Rect>),
            typeof(PageFindLayer),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentMatchRectsProperty =
        DependencyProperty.Register(
            nameof(CurrentMatchRects),
            typeof(IReadOnlyList<Rect>),
            typeof(PageFindLayer),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<Rect>? MatchRects
    {
        get => (IReadOnlyList<Rect>?)GetValue(MatchRectsProperty);
        set => SetValue(MatchRectsProperty, value);
    }

    public IReadOnlyList<Rect>? CurrentMatchRects
    {
        get => (IReadOnlyList<Rect>?)GetValue(CurrentMatchRectsProperty);
        set => SetValue(CurrentMatchRectsProperty, value);
    }

    private static readonly Brush MatchBrush;
    private static readonly Brush CurrentBrush;
    private static readonly Pen CurrentPen;

    static PageFindLayer()
    {
        var match = new SolidColorBrush(Color.FromArgb(110, 255, 240, 0));
        match.Freeze();
        MatchBrush = match;

        var current = new SolidColorBrush(Color.FromArgb(180, 255, 220, 0));
        current.Freeze();
        CurrentBrush = current;

        var penBrush = new SolidColorBrush(Color.FromArgb(220, 200, 160, 0));
        penBrush.Freeze();
        var pen = new Pen(penBrush, 1.5);
        pen.Freeze();
        CurrentPen = pen;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var matches = MatchRects;
        if (matches is not null)
        {
            foreach (var rect in matches)
            {
                if (rect.Width <= 0 || rect.Height <= 0) continue;
                dc.DrawRectangle(MatchBrush, null, Pad(rect));
            }
        }

        var current = CurrentMatchRects;
        if (current is not null)
        {
            foreach (var rect in current)
            {
                if (rect.Width <= 0 || rect.Height <= 0) continue;
                dc.DrawRectangle(CurrentBrush, CurrentPen, Pad(rect));
            }
        }
    }

    private static Rect Pad(Rect r) =>
        new(r.X - PadX, r.Y - PadY, r.Width + 2 * PadX, r.Height + 2 * PadY);
}
