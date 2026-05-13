namespace BarePDF.Settings;

public sealed class AppSettings
{
    public InstanceMode? InstanceMode { get; set; }
    public AppTheme? Theme { get; set; }
    public Views.ZoomMode? LastZoomMode { get; set; }
    public double? LastZoomScale { get; set; }
    public bool? AutoFitWindowWidth { get; set; }
    public bool? ShowThumbnails { get; set; }
    public System.Collections.Generic.List<string>? RecentFiles { get; set; }
    public System.Collections.Generic.Dictionary<string, int>? PerDocumentRotation { get; set; }
    public TitleBarFilenameMode? TitleBarFilenameMode { get; set; }
}
