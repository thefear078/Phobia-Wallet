using System.Text;

namespace Umbrella.Wallet.Infrastructure;

/// <summary>
/// Whole-file writes that either happen completely or not at all.
///
/// <c>File.WriteAllText</c> over the real file empties it first: a crash, a power cut or a full disk
/// halfway through leaves a truncated file, and the next start reads "no settings" — the currency, Tor,
/// theme and language all quietly back to their defaults, or an address book that no longer opens.
/// Writing a temporary file beside it, flushing it to disk and only then moving it over the original
/// means the old file stays intact until the new one is whole.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents) =>
        WriteAllBytes(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents));

    public static void WriteAllBytes(string path, byte[] contents)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // Same directory, so the move is a rename on one volume rather than a copy across two.
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(contents);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
        }
    }

    public static Task WriteAllTextAsync(string path, string contents, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        WriteAllText(path, contents);
        return Task.CompletedTask;
    }

    public static Task WriteAllBytesAsync(string path, byte[] contents, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        WriteAllBytes(path, contents);
        return Task.CompletedTask;
    }
}
