using System.IO;
using System.Windows;
using PublisherRip.App.Models;

namespace PublisherRip.App.Services;

internal sealed class DropImportService
{
    public static bool CanImport(IDataObject dataObject)
    {
        return dataObject.GetDataPresent(DataFormats.FileDrop)
            || OutlookVirtualFileReader.CanRead(dataObject);
    }

    public async Task<DropImportResult> ImportAsync(IDataObject dataObject)
    {
        var localPaths = Array.Empty<string>();
        if (dataObject.GetDataPresent(DataFormats.FileDrop))
        {
            localPaths = dataObject.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
        }

        var virtualFiles = OutlookVirtualFileReader.CanRead(dataObject)
            ? OutlookVirtualFileReader.ReadFiles(dataObject)
            : Array.Empty<VirtualFileData>();

        return await Task.Run(() => ImportInternal(localPaths, virtualFiles));
    }

    private static DropImportResult ImportInternal(IReadOnlyList<string> localPaths, IReadOnlyList<VirtualFileData> virtualFiles)
    {
        var pages = new List<PrintPageItem>();
        var warnings = new List<string>();

        foreach (var path in localPaths.Where(File.Exists))
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                AddSourcePages(new DocumentSource(Path.GetFileName(path), bytes), pages, warnings);
            }
            catch (Exception ex)
            {
                warnings.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        foreach (var file in virtualFiles)
        {
            try
            {
                AddSourcePages(new DocumentSource(file.FileName, file.Content), pages, warnings);
            }
            catch (Exception ex)
            {
                warnings.Add($"{file.FileName}: {ex.Message}");
            }
        }

        return new DropImportResult(pages, warnings);
    }

    private static void AddSourcePages(DocumentSource source, ICollection<PrintPageItem> pages, ICollection<string> warnings)
    {
        if (PageBitmapFactory.IsPdf(source))
        {
            var pageCount = PageBitmapFactory.GetPdfPageCount(source);
            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                var preview = PageBitmapFactory.CreatePreview(source, pageIndex, true);
                pages.Add(new PrintPageItem(source, pageIndex, pageCount, preview, true));
            }

            return;
        }

        try
        {
            var preview = PageBitmapFactory.CreatePreview(source, 0, false);
            pages.Add(new PrintPageItem(source, 0, 1, preview, false));
        }
        catch (Exception imageException)
        {
            warnings.Add($"{source.FileName}: unsupported image or PDF format ({imageException.Message})");
        }
    }
}

internal sealed record DropImportResult(IReadOnlyList<PrintPageItem> Pages, IReadOnlyList<string> Warnings);
