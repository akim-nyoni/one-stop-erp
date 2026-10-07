namespace One4All.Application.Attachments;

/// <summary>
/// What may be attached (brief §4 attachments): documents and images by extension, the content matching the extension for the formats that
/// carry a signature, nothing executable, and within the size limit. Pure and unit-tested.
/// </summary>
public static class AttachmentRules
{
    /// <summary>The default size limit (10 MB).</summary>
    public const long DefaultMaxBytes = 10L * 1024 * 1024;

    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf", [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".tif"] = "image/tiff", [".tiff"] = "image/tiff",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document", [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".csv"] = "text/csv", [".txt"] = "text/plain", [".eml"] = "message/rfc822",
    };

    /// <summary>The media type for a file, or why it cannot be attached.</summary>
    /// <param name="fileName">The file name.</param>
    /// <param name="content">The bytes.</param>
    /// <param name="maxBytes">The size limit.</param>
    /// <returns>(media type, null) when allowed; (null, reason) when not.</returns>
    public static (string? ContentType, string? Problem) Check(string fileName, ReadOnlySpan<byte> content, long maxBytes = DefaultMaxBytes)
    {
        string extension = Path.GetExtension(fileName ?? string.Empty);
        if (content.Length == 0)
        {
            return (null, "The file is empty.");
        }

        if (content.Length > maxBytes)
        {
            return (null, $"The file is {content.Length / 1024} KB; the limit is {maxBytes / 1024} KB.");
        }

        if (!Types.TryGetValue(extension, out string? type))
        {
            return (null, $"Files of type '{extension}' cannot be attached (allowed: {string.Join(", ", Types.Keys)}).");
        }

        if (StartsWith(content, "MZ"u8) || StartsWith(content, [0x7F, (byte)'E', (byte)'L', (byte)'F']))
        {
            return (null, "Executable files cannot be attached.");
        }

        bool matches = extension.ToLowerInvariant() switch
        {
            ".pdf" => StartsWith(content, "%PDF"u8),
            ".png" => StartsWith(content, [0x89, (byte)'P', (byte)'N', (byte)'G']),
            ".jpg" or ".jpeg" => StartsWith(content, [0xFF, 0xD8, 0xFF]),
            ".tif" or ".tiff" => StartsWith(content, "II*\0"u8) || StartsWith(content, "MM\0*"u8),
            ".docx" or ".xlsx" => StartsWith(content, "PK"u8),
            _ => true,
        };
        return matches ? (type, null) : (null, $"The content is not a real {extension} file.");
    }

    private static bool StartsWith(ReadOnlySpan<byte> content, ReadOnlySpan<byte> signature) => content.Length >= signature.Length && content[..signature.Length].SequenceEqual(signature);
}
