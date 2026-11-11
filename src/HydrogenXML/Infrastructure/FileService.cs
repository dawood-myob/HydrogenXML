using System.IO;
using System.Text;

namespace HydrogenXML.Infrastructure;

public sealed record LoadedText(string Text, Encoding Encoding, bool IsPreview, long TotalBytes);

public static class FileService
{
    /// <summary>Files bigger than this are previewed read-only; transformations still use the whole file from disk.</summary>
    public const long LargeFileBytes = 20L * 1024 * 1024;
    private const int PreviewBytes = 512 * 1024;

    public static LoadedText Read(string path, bool allowPreview)
    {
        var info = new FileInfo(path);
        if (allowPreview && info.Length > LargeFileBytes)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[PreviewBytes];
            var read = fs.Read(buffer, 0, buffer.Length);
            var (enc, bom) = Detect(buffer, read);
            var text = enc.GetString(buffer, bom, read - bom);
            var cut = text.LastIndexOf('\n');
            if (cut > 0) text = text[..cut];
            return new LoadedText(text, enc, true, info.Length);
        }

        var bytes = ReadAllBytesShared(path);
        var (encoding, preamble) = Detect(bytes, bytes.Length);
        string content;
        try
        {
            content = (encoding is UTF8Encoding
                ? new UTF8Encoding(false, true)
                : encoding).GetString(bytes, preamble, bytes.Length - preamble);
        }
        catch (DecoderFallbackException)
        {
            // Not valid UTF-8; treat as a legacy single-byte code page.
            encoding = Encoding.Latin1;
            content = encoding.GetString(bytes, 0, bytes.Length);
        }

        return new LoadedText(content, encoding, false, bytes.Length);
    }

    private static byte[] ReadAllBytesShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bytes = new byte[fs.Length];
        fs.ReadExactly(bytes);
        return bytes;
    }

    private static (Encoding, int) Detect(byte[] b, int len)
    {
        if (len >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return (new UTF8Encoding(true), 3);
        if (len >= 2 && b[0] == 0xFF && b[1] == 0xFE) return (new UnicodeEncoding(false, true), 2);
        if (len >= 2 && b[0] == 0xFE && b[1] == 0xFF) return (new UnicodeEncoding(true, true), 2);
        return (new UTF8Encoding(false), 0);
    }

    public static void Write(string path, string text, Encoding encoding)
    {
        var temp = path + ".hydrogen.tmp";
        File.WriteAllText(temp, text, encoding);
        File.Move(temp, path, true);
    }

    public static Encoding DetectOutputEncoding(string file)
    {
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[256];
            var read = fs.Read(buffer, 0, buffer.Length);
            var (enc, _) = Detect(buffer, read);
            if (read >= 2 && (buffer[0] == 0xFF || buffer[0] == 0xFE)) return enc;
            var head = Encoding.ASCII.GetString(buffer, 0, read);
            var m = System.Text.RegularExpressions.Regex.Match(head, "encoding\\s*=\\s*[\"']([^\"']+)[\"']");
            if (m.Success)
            {
                try { return Encoding.GetEncoding(m.Groups[1].Value); }
                catch (ArgumentException) { }
            }

            return enc;
        }
        catch
        {
            return new UTF8Encoding(false);
        }
    }

    public static LoadedText ReadOutput(string file, long maxBytes)
    {
        var info = new FileInfo(file);
        var enc = DetectOutputEncoding(file);
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var truncated = info.Length > maxBytes;
        var buffer = new byte[Math.Min(info.Length, maxBytes)];
        fs.ReadExactly(buffer);
        var preamble = enc.GetPreamble();
        var skip = preamble.Length > 0 && buffer.Length >= preamble.Length && buffer.AsSpan(0, preamble.Length).SequenceEqual(preamble)
            ? preamble.Length
            : 0;
        var text = enc.GetString(buffer, skip, buffer.Length - skip);
        if (truncated)
        {
            var cut = text.LastIndexOf('\n');
            if (cut > 0) text = text[..cut];
        }

        return new LoadedText(text, enc, truncated, info.Length);
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024:0.#} MB";
        return $"{bytes / 1024.0 / 1024 / 1024:0.##} GB";
    }
}
