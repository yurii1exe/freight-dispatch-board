using System.Globalization;

namespace FreightDispatch.Core.Transport;

/// <summary>
/// Where the drop directories live and how patiently they are watched.
/// </summary>
public sealed class FileDropOptions
{
    /// <summary>The root directory. The four working directories are created underneath it.</summary>
    public string Root { get; init; } = "edi-drop";

    /// <summary>Files a partner drops here are ingested. Defaults to <c>&lt;root&gt;/in</c>.</summary>
    public string? InboundDirectory { get; init; }

    /// <summary>Generated 997s, 214s and 210s land here. Defaults to <c>&lt;root&gt;/out</c>.</summary>
    public string? OutboundDirectory { get; init; }

    /// <summary>Inbound files that were dealt with are moved here. Defaults to <c>&lt;root&gt;/processed</c>.</summary>
    public string? ProcessedDirectory { get; init; }

    /// <summary>Inbound files nobody could make sense of. Defaults to <c>&lt;root&gt;/error</c>.</summary>
    public string? ErrorDirectory { get; init; }

    /// <summary>How often the inbound directory is listed.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Extensions treated as EDI. Anything else in the directory is left alone, which is
    /// what you want the first time somebody drops a spreadsheet in there.
    /// </summary>
    public IReadOnlyList<string> Extensions { get; init; } = new[] { ".edi", ".x12", ".txt", ".dat" };
}

/// <summary>
/// A transport built on two directories: one a partner writes into, one this board writes
/// into.
/// </summary>
/// <remarks>
/// <para>This is not a toy. A watched directory is exactly what an SFTP mount, a VAN's
/// download folder and most managed file transfer products look like from the application's
/// side, and a large share of freight EDI in production is a directory somebody else's
/// process writes into.</para>
/// <para><b>It polls, and that is on purpose.</b> <c>FileSystemWatcher</c> is the obvious
/// choice and the wrong one: it silently drops events when its internal buffer overflows,
/// it does not fire at all on a good number of network shares, and — worst — it tells you a
/// file <em>appeared</em>, which is not the same as a file having finished arriving.
/// Listing a directory once a second is unglamorous and has never lost a tender.</para>
/// <para><b>A file appearing is not a file being complete.</b> A partner's upload shows up
/// as a zero-byte entry and grows for however long the transfer takes; reading it on sight
/// gets you the first eight kilobytes of a load tender and a parse error that blames the
/// sender. Two defences are used here, and both are what a real integration does:</para>
/// <list type="number">
/// <item><description>A file is only read once its length and last-write time have been
/// unchanged for a full poll, so a file still being written is skipped until it settles.</description></item>
/// <item><description>It is then opened with <see cref="FileShare.None"/>. On Windows that
/// fails outright while the writer still holds the handle, which turns a race into a
/// retry.</description></item>
/// </list>
/// <para>Outbound files get the same courtesy in reverse: each is written to a
/// <c>.tmp</c> name and renamed into place, because a rename within a volume is atomic and
/// the partner's watcher therefore never sees a half-written 214.</para>
/// </remarks>
public sealed class FileDropTransport : ITransport
{
    private readonly FileDropOptions _options;
    private readonly Dictionary<string, FileFingerprint> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    private CancellationTokenSource? _stopping;
    private Task? _loop;
    private Func<InboundDocument, CancellationToken, Task<InboundResult>>? _handler;

    /// <summary>Creates a transport and creates its directories.</summary>
    /// <param name="options">Where the directories are. Defaults to <c>edi-drop</c> under the working directory.</param>
    public FileDropTransport(FileDropOptions? options = null)
    {
        _options = options ?? new FileDropOptions();

        string root = Path.GetFullPath(_options.Root);
        InboundDirectory = Path.GetFullPath(_options.InboundDirectory ?? Path.Combine(root, "in"));
        OutboundDirectory = Path.GetFullPath(_options.OutboundDirectory ?? Path.Combine(root, "out"));
        ProcessedDirectory = Path.GetFullPath(_options.ProcessedDirectory ?? Path.Combine(root, "processed"));
        ErrorDirectory = Path.GetFullPath(_options.ErrorDirectory ?? Path.Combine(root, "error"));

        foreach (string directory in new[] { InboundDirectory, OutboundDirectory, ProcessedDirectory, ErrorDirectory })
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <inheritdoc />
    public string Name => "file-drop";

    /// <inheritdoc />
    public string Endpoint => $"{InboundDirectory} → board → {OutboundDirectory}";

    /// <inheritdoc />
    public bool IsRunning => _loop is { IsCompleted: false };

    /// <summary>The directory a partner drops tenders into.</summary>
    public string InboundDirectory { get; }

    /// <summary>The directory generated interchanges are written to.</summary>
    public string OutboundDirectory { get; }

    /// <summary>Where inbound files go once they have been dealt with.</summary>
    public string ProcessedDirectory { get; }

    /// <summary>Where inbound files go when nothing could be made of them.</summary>
    public string ErrorDirectory { get; }

    /// <inheritdoc />
    public Task StartAsync(
        Func<InboundDocument, CancellationToken, Task<InboundResult>> handler,
        CancellationToken cancellationToken = default)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        _handler = handler;
        _stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = Task.Run(() => WatchAsync(_stopping.Token), CancellationToken.None);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_stopping is null)
        {
            return;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);

        if (_loop is { } loop)
        {
            try
            {
                await loop.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Stopping was cancelled or the loop ended in cancellation. Either way the
                // watcher is no longer running, which is what was asked for.
            }
        }

        _stopping.Dispose();
        _stopping = null;
        _loop = null;
    }

    /// <inheritdoc />
    public async Task<string> SendAsync(OutboundDocument document, CancellationToken cancellationToken = default)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        // Two documents generated in the same millisecond would collide on the suggested
        // name. The gate makes the uniqueness check and the write one operation rather than
        // two, which matters because leaving a stop emits a 214 pair back to back.
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            string path = Unique(Path.Combine(OutboundDirectory, document.SuggestedFileName));
            string staging = path + ".tmp";

            await File.WriteAllTextAsync(staging, document.Edi, cancellationToken).ConfigureAwait(false);

            // Rename rather than write in place. Within a volume this is atomic, so a
            // partner polling this directory sees the file either not at all or complete —
            // never the first half of an interchange.
            File.Move(staging, path);

            return path;
        }
        finally
        {
            _sendGate.Release();
        }
    }

    /// <summary>
    /// Reads whatever is already in the inbound directory and then keeps listing it.
    /// </summary>
    private async Task WatchAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // A directory that has momentarily gone away, a file locked by a virus
                // scanner, a permission that changed underneath us: none of these are
                // reasons to stop watching. The next sweep will find whatever is still
                // there, which is the entire advantage of polling over events.
            }

            try
            {
                await Task.Delay(_options.PollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One pass over the inbound directory.</summary>
    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(InboundDirectory))
        {
            return;
        }

        var candidates = Directory
            .EnumerateFiles(InboundDirectory)
            .Where(IsEdiFile)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        var present = new HashSet<string>(candidates, StringComparer.OrdinalIgnoreCase);
        foreach (string gone in _seen.Keys.Where(k => !present.Contains(k)).ToList())
        {
            _seen.Remove(gone);
        }

        foreach (string path in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileInfo info;
            try
            {
                info = new FileInfo(path);
                if (!info.Exists)
                {
                    continue;
                }
            }
            catch (IOException)
            {
                continue;
            }

            var fingerprint = new FileFingerprint(info.Length, info.LastWriteTimeUtc);

            // First sighting: remember it and come back next time. A file that is still
            // being uploaded will have grown by then and gets another poll to finish.
            if (!_seen.TryGetValue(path, out FileFingerprint previous))
            {
                _seen[path] = fingerprint;
                continue;
            }

            if (previous != fingerprint)
            {
                _seen[path] = fingerprint;
                continue;
            }

            _seen.Remove(path);
            await IngestAsync(path, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Reads one settled file, hands it to the board and files it away.</summary>
    private async Task IngestAsync(string path, CancellationToken cancellationToken)
    {
        string text;

        try
        {
            // FileShare.None: if anything still holds a write handle this throws, and the
            // file simply gets picked up on a later sweep instead of being read half-written.
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.None, bufferSize: 8192, useAsync: true);
            using var reader = new StreamReader(stream);
            text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        InboundResult result;

        try
        {
            result = await _handler!(
                new InboundDocument(path, text, DateTimeOffset.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = InboundResult.Failed(ex.Message);
        }

        string destination = result.Handled ? ProcessedDirectory : ErrorDirectory;
        Archive(path, destination);
    }

    /// <summary>Moves an ingested file out of the inbound directory so it is not read twice.</summary>
    private static void Archive(string path, string destination)
    {
        try
        {
            string target = Unique(Path.Combine(destination, Path.GetFileName(path)));
            File.Move(path, target);
        }
        catch (IOException)
        {
            // If the file cannot be moved it will be seen again on the next sweep. That is
            // preferable to deleting it: a duplicate tender is a phone call, a lost one is a
            // truck that never gets dispatched.
        }
    }

    private bool IsEdiFile(string path)
    {
        string extension = Path.GetExtension(path);

        // Two conventions for "still uploading" that partners actually use. Both mean the
        // same thing: not yet.
        if (extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".filepart", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return _options.Extensions.Any(e => extension.Equals(e, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Finds a name that is not taken, by appending a counter.</summary>
    private static string Unique(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);

        for (int counter = 2; counter < 10_000; counter++)
        {
            string candidate = Path.Combine(
                directory,
                string.Create(CultureInfo.InvariantCulture, $"{name}-{counter}{extension}"));

            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{name}-{Guid.NewGuid():N}{extension}");
    }

    /// <summary>Length and last-write time — enough to tell "still arriving" from "settled".</summary>
    private readonly record struct FileFingerprint(long Length, DateTime LastWriteUtc);
}
