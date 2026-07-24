using System.IO;

namespace PublisherRip.App.Models;

public sealed class DocumentSource
{
    public DocumentSource(string fileName, byte[] content)
    {
        FileName = fileName;
        Content = content;
        Extension = Path.GetExtension(fileName);
    }

    public string FileName { get; }

    public byte[] Content { get; }

    public string Extension { get; }
}
