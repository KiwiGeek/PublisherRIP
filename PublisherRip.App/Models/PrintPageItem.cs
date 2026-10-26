using System.ComponentModel;
using System.IO;
using System.Windows.Media.Imaging;

namespace PublisherRip.App.Models;

public sealed class PrintPageItem : INotifyPropertyChanged
{
    private int _rotationAngle;

    public PrintPageItem(DocumentSource source, int pageIndex, int pageCount, BitmapSource preview, bool isPdf)
    {
        Source = source;
        PageIndex = pageIndex;
        PageCount = pageCount;
        Preview = preview;
        IsPdf = isPdf;

        var baseName = Path.GetFileName(source.FileName);
        DisplayName = isPdf ? $"{baseName} - Page {pageIndex + 1}" : baseName;
        PageLabel = isPdf ? $"PDF page {pageIndex + 1} of {pageCount}" : "Single image page";
        SourceLabel = isPdf ? "Rendered from PDF" : "Rendered from image";
    }

    public DocumentSource Source { get; }

    public int PageIndex { get; }

    public int PageCount { get; }

    public BitmapSource Preview { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int RotationAngle => _rotationAngle;

    public void Rotate(bool clockwise)
    {
        _rotationAngle = (_rotationAngle + (clockwise ? 90 : 270)) % 360;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RotationAngle)));
    }

    public bool IsPdf { get; }

    public string DisplayName { get; }

    public string PageLabel { get; }

    public string SourceLabel { get; }
}
