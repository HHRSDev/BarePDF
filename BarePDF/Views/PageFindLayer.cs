using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace BarePDF.Views;

public sealed class PageFindLayer : FrameworkElement
{
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
        var match = new SolidColorBrush(Color.FromArgb(96, 255, 196, 0));
        match.Freeze();
        MatchBrush = match;

        var current = new SolidColorBrush(Color.FromArgb(140, 255, 132, 0));
        current.Freeze();
        CurrentBrush = current;

        var pen = new Pen(new SolidColorBrush(Color.FromArgb(220, 200, 70, 0)), 1.5);
        pen.Brush.Freeze();
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
                dc.DrawRectangle(MatchBrush, null, rect);
            }
        }

        var current = CurrentMatchRects;
        if (current is not null)
        {
            foreach (var rect in current)
            {
                if (rect.Width <= 0 || rect.Height <= 0) continue;
                dc.DrawRectangle(CurrentBrush, CurrentPen, rect);
            }
        }
    }
}
