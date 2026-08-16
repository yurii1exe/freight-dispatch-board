using FreightDispatch.Core.Transport;

namespace FreightDispatch.Tests;

/// <summary>
/// A throwaway drop directory, polled fast enough that a test does not have to sit through
/// a production poll interval.
/// </summary>
internal sealed class TempDrop : IDisposable
{
    internal TempDrop()
    {
        Root = Path.Combine(Path.GetTempPath(), "fdb-drop-" + Guid.NewGuid().ToString("N"));

        Transport = new FileDropTransport(new FileDropOptions
        {
            Root = Root,
            PollInterval = TimeSpan.FromMilliseconds(25),
        });
    }

    internal string Root { get; }

    internal FileDropTransport Transport { get; }

    /// <summary>Writes a file into the inbound directory the way a partner would: temp name, then rename.</summary>
    internal string Drop(string name, string content)
    {
        string staging = Path.Combine(Transport.InboundDirectory, name + ".tmp");
        string path = Path.Combine(Transport.InboundDirectory, name);

        File.WriteAllText(staging, content);
        File.Move(staging, path);

        return path;
    }

    /// <summary>Files in a directory, oldest first.</summary>
    internal static IReadOnlyList<string> Files(string directory) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory).OrderBy(f => f, StringComparer.Ordinal).ToList()
            : Array.Empty<string>();

    /// <summary>
    /// Waits for a condition, polling. Every assertion in these tests is about something
    /// that happens on a background loop, and a fixed sleep is either flaky or slow.
    /// </summary>
    internal static async Task<bool> WaitUntil(Func<bool> condition, int millisecondsTimeout = 10_000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(millisecondsTimeout);

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return condition();
    }

    public void Dispose()
    {
        try
        {
            Transport.StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // The test is over; a transport that will not stop cleanly is not the assertion.
        }

        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temp directories on Windows occasionally stay locked for a moment. Leaving
            // one behind is not worth failing a test over.
        }
    }
}
