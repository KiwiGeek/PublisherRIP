using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using PublisherRip.App.Models;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace PublisherRip.App.Services;

internal static class PageBitmapFactory
{
    private static readonly Lazy<PopplerTools?> PopplerTools = new(ResolvePopplerTools);

    public static bool IsPdf(DocumentSource source)
    {
        if (string.Equals(source.Extension, ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return source.Content.Length >= 4
            && source.Content[0] == '%'
            && source.Content[1] == 'P'
            && source.Content[2] == 'D'
            && source.Content[3] == 'F';
    }

    public static int GetPdfPageCount(DocumentSource source)
    {
        try
        {
            using var stream = CreateRandomAccessStream(source.Content);
            var document = LoadPdfDocument(stream);
            return checked((int)document.PageCount);
        }
        catch (Exception ex)
        {
            return GetPdfPageCountWithPopplerFallback(source, ex);
        }
    }

    public static BitmapSource CreatePreview(DocumentSource source, int pageIndex, bool isPdf)
    {
        return isPdf
            ? RenderPdfPage(source, pageIndex, new Size(220, 280), 144)
            : LoadImageBitmap(source.Content, 220, 280);
    }

    public static BitmapSource CreatePrintableBitmap(DocumentSource source, int pageIndex, bool isPdf, Size targetSize)
    {
        return isPdf
            ? RenderPdfPage(source, pageIndex, targetSize, 300)
            : LoadImageBitmap(source.Content, null, null);
    }

    private static BitmapSource RenderPdfPage(DocumentSource source, int pageIndex, Size targetSize, double dpi)
    {
        try
        {
            using var sourceStream = CreateRandomAccessStream(source.Content);
            var document = LoadPdfDocument(sourceStream);
            using var page = document.GetPage((uint)pageIndex);
            var renderStream = new InMemoryRandomAccessStream();

            var pixelBounds = CalculateFittedPixelBounds(page.Size.Width, page.Size.Height, targetSize, dpi);
            var renderOptions = new PdfPageRenderOptions
            {
                DestinationWidth = pixelBounds.Width,
                DestinationHeight = pixelBounds.Height
            };

            page.RenderToStreamAsync(renderStream, renderOptions).AsTask().GetAwaiter().GetResult();
            renderStream.Seek(0);
            return CreateBitmapSource(renderStream.AsStreamForRead());
        }
        catch (Exception ex)
        {
            return RenderPdfPageWithPopplerFallback(source, pageIndex, dpi, ex);
        }
    }

    private static PdfDocument LoadPdfDocument(IRandomAccessStream stream)
    {
        try
        {
            return PdfDocument.LoadFromStreamAsync(stream).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            var details = string.IsNullOrWhiteSpace(ex.Message)
                ? $"{ex.GetType().Name} (0x{ex.HResult:X8})"
                : $"{ex.Message} (0x{ex.HResult:X8})";
            throw new InvalidOperationException($"Windows PDF engine could not open this PDF: {details}", ex);
        }
    }

    private static int GetPdfPageCountWithPopplerFallback(DocumentSource source, Exception primaryException)
    {
        var tools = PopplerTools.Value;
        if (tools is null)
        {
            throw BuildPdfFallbackException(primaryException, "No fallback PDF renderer was found on this machine.");
        }

        using var temp = new TemporaryPdfFile(source.Content);
        var result = RunProcess(tools.PdfInfoPath, temp.PdfPath);
        var pagesLine = result.StdOut
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith("Pages:", StringComparison.OrdinalIgnoreCase));

        if (pagesLine is null)
        {
            var extra = string.IsNullOrWhiteSpace(result.StdErr) ? "" : $" Fallback output: {result.StdErr.Trim()}";
            throw BuildPdfFallbackException(primaryException, $"Fallback PDF info did not report a page count.{extra}");
        }

        var countText = pagesLine["Pages:".Length..].Trim();
        if (!int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pageCount) || pageCount <= 0)
        {
            throw BuildPdfFallbackException(primaryException, $"Fallback PDF info returned an invalid page count: '{countText}'.");
        }

        return pageCount;
    }

    private static BitmapSource RenderPdfPageWithPopplerFallback(DocumentSource source, int pageIndex, double dpi, Exception primaryException)
    {
        var tools = PopplerTools.Value;
        if (tools is null)
        {
            throw BuildPdfFallbackException(primaryException, "No fallback PDF renderer was found on this machine.");
        }

        using var temp = new TemporaryPdfFile(source.Content);
        var outputPrefix = Path.Combine(temp.WorkingDirectory, "page");
        RunProcess(
            tools.PdfToPpmPath,
            "-png",
            "-singlefile",
            "-f",
            (pageIndex + 1).ToString(CultureInfo.InvariantCulture),
            "-l",
            (pageIndex + 1).ToString(CultureInfo.InvariantCulture),
            "-r",
            dpi.ToString("0", CultureInfo.InvariantCulture),
            temp.PdfPath,
            outputPrefix);

        var outputPath = outputPrefix + ".png";
        if (!File.Exists(outputPath))
        {
            throw BuildPdfFallbackException(primaryException, "Fallback PDF renderer did not produce an output image.");
        }

        var imageBytes = File.ReadAllBytes(outputPath);
        return LoadImageBitmap(imageBytes, null, null);
    }

    private static ProcessResult RunProcess(string filePath, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = filePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process '{filePath}'.");

        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var details = string.IsNullOrWhiteSpace(stdErr) ? stdOut.Trim() : stdErr.Trim();
            throw new InvalidOperationException($"Fallback PDF renderer failed with exit code {process.ExitCode}. {details}".Trim());
        }

        return new ProcessResult(stdOut, stdErr);
    }

    private static PopplerTools? ResolvePopplerTools()
    {
        var candidateDirectories = new List<string>();
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            candidateDirectories.Add(Path.Combine(userProfile, ".cache", "codex-runtimes", "codex-primary-runtime", "dependencies", "native", "poppler", "Library", "bin"));
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            candidateDirectories.AddRange(pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        }

        foreach (var directory in candidateDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var pdfInfoPath = Path.Combine(directory, "pdfinfo.exe");
                var pdfToPpmPath = Path.Combine(directory, "pdftoppm.exe");
                if (File.Exists(pdfInfoPath) && File.Exists(pdfToPpmPath))
                {
                    return new PopplerTools(pdfInfoPath, pdfToPpmPath);
                }
            }
            catch
            {
                // Ignore malformed path entries and keep searching.
            }
        }

        return null;
    }

    private static InMemoryRandomAccessStream CreateRandomAccessStream(byte[] content)
    {
        var stream = new InMemoryRandomAccessStream();
        using var writer = new DataWriter(stream);
        writer.WriteBytes(content);
        writer.StoreAsync().AsTask().GetAwaiter().GetResult();
        writer.FlushAsync().AsTask().GetAwaiter().GetResult();
        writer.DetachStream();
        stream.Seek(0);
        return stream;
    }

    private static BitmapSource LoadImageBitmap(byte[] content, int? decodePixelWidth, int? decodePixelHeight)
    {
        using var stream = new MemoryStream(content, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;

        if (decodePixelWidth.HasValue)
        {
            bitmap.DecodePixelWidth = decodePixelWidth.Value;
        }

        if (decodePixelHeight.HasValue)
        {
            bitmap.DecodePixelHeight = decodePixelHeight.Value;
        }

        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource CreateBitmapSource(Stream stream)
    {
        using (stream)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
    }

    private static InvalidOperationException BuildPdfFallbackException(Exception primaryException, string fallbackMessage)
    {
        return new InvalidOperationException($"{primaryException.Message} {fallbackMessage}".Trim(), primaryException);
    }

    private static (uint Width, uint Height) CalculateFittedPixelBounds(double sourceWidth, double sourceHeight, Size targetSize, double dpi)
    {
        var maxWidth = Math.Max(1, targetSize.Width * dpi / 96d);
        var maxHeight = Math.Max(1, targetSize.Height * dpi / 96d);
        var scale = Math.Min(maxWidth / sourceWidth, maxHeight / sourceHeight);

        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
        {
            scale = 1;
        }

        return (
            Math.Max(1u, (uint)Math.Round(sourceWidth * scale)),
            Math.Max(1u, (uint)Math.Round(sourceHeight * scale)));
    }

    private sealed record PopplerTools(string PdfInfoPath, string PdfToPpmPath);

    private sealed record ProcessResult(string StdOut, string StdErr);

    private sealed class TemporaryPdfFile : IDisposable
    {
        public TemporaryPdfFile(byte[] content)
        {
            WorkingDirectory = Path.Combine(Path.GetTempPath(), "PublisherRip-Pdf", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(WorkingDirectory);
            PdfPath = Path.Combine(WorkingDirectory, "input.pdf");
            File.WriteAllBytes(PdfPath, content);
        }

        public string WorkingDirectory { get; }

        public string PdfPath { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(WorkingDirectory))
                {
                    Directory.Delete(WorkingDirectory, true);
                }
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }
}
