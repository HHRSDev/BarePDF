using System.Windows;
using System.Windows.Controls;

namespace BarePDF.Views;

public partial class ThumbnailStrip : UserControl
{
    public event Action<ThumbnailItem>? ThumbnailRealized;
    public event Action<int>? PageRequested;

    public ThumbnailStrip()
    {
        InitializeComponent();
    }

    public void SetItems(IEnumerable<ThumbnailItem>? items)
    {
        ThumbList.ItemsSource = items;
    }

    private void OnThumbnailContainerDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ThumbnailItem item)
        {
            ThumbnailRealized?.Invoke(item);
        }
    }

    private void OnThumbnailSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, ThumbList)) return;
        if (ThumbList.SelectedItem is ThumbnailItem item)
        {
            PageRequested?.Invoke(item.PageNumber - 1);
        }
    }
}
