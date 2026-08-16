using System.Collections.Concurrent;
using FreightDispatch.Core.Transport;
using Xunit;

namespace FreightDispatch.Tests;

public class FileDropTransportTests
{
    [Fact]
    public async Task A_file_dropped_into_the_inbound_directory_reaches_the_handler()
    {
        using var drop = new TempDrop();
        var received = new ConcurrentQueue<InboundDocument>();

        await drop.Transport.StartAsync((document, _) =>
        {
            received.Enqueue(document);
            return Task.FromResult(new InboundResult(true, "ok"));
        });

        drop.Drop("tender.edi", Samples.Read(Samples.DryVan));

        Assert.True(await TempDrop.WaitUntil(() => received.Count == 1), "the file was never picked up");
        Assert.Contains("ISA*00*", received.Single().Edi, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_handled_file_is_moved_out_of_the_inbound_directory_and_not_read_twice()
    {
        using var drop = new TempDrop();
        int calls = 0;

        await drop.Transport.StartAsync((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new InboundResult(true, "ok"));
        });

        drop.Drop("tender.edi", Samples.Read(Samples.DryVan));

        Assert.True(await TempDrop.WaitUntil(() => TempDrop.Files(drop.Transport.ProcessedDirectory).Count == 1));
        Assert.Empty(TempDrop.Files(drop.Transport.InboundDirectory));

        // Give the loop several more polls to prove it does not come back for it.
        await Task.Delay(200);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task A_file_nobody_could_make_sense_of_goes_to_the_error_directory()
    {
        using var drop = new TempDrop();

        await drop.Transport.StartAsync((_, _) =>
            Task.FromResult(InboundResult.Failed("not an interchange")));

        drop.Drop("junk.edi", "this is not EDI");

        Assert.True(await TempDrop.WaitUntil(() => TempDrop.Files(drop.Transport.ErrorDirectory).Count == 1));
        Assert.Empty(TempDrop.Files(drop.Transport.ProcessedDirectory));
    }

    [Fact]
    public async Task A_file_that_is_still_being_written_is_left_alone_until_it_settles()
    {
        // The reason this transport polls and fingerprints rather than trusting a create
        // event: a partner's upload appears as a short file and grows. Reading it on sight
        // gets you half a load tender and a parse error that blames the sender.
        using var drop = new TempDrop();
        var received = new ConcurrentQueue<InboundDocument>();

        await drop.Transport.StartAsync((document, _) =>
        {
            received.Enqueue(document);
            return Task.FromResult(new InboundResult(true, "ok"));
        });

        string edi = Samples.Read(Samples.DryVan);
        string path = Path.Combine(drop.Transport.InboundDirectory, "slow.edi");

        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        using (var writer = new StreamWriter(stream))
        {
            await writer.WriteAsync(edi.AsMemory(0, 200));
            await writer.FlushAsync();

            // Several poll intervals with the file open and incomplete.
            await Task.Delay(200);
            Assert.Empty(received);

            await writer.WriteAsync(edi.AsMemory(200));
            await writer.FlushAsync();
        }

        Assert.True(await TempDrop.WaitUntil(() => received.Count == 1), "the completed file was never picked up");
        Assert.Equal(edi, received.Single().Edi);
    }

    [Fact]
    public async Task Anything_that_is_not_an_EDI_file_is_ignored()
    {
        using var drop = new TempDrop();
        var received = new ConcurrentQueue<InboundDocument>();

        await drop.Transport.StartAsync((document, _) =>
        {
            received.Enqueue(document);
            return Task.FromResult(new InboundResult(true, "ok"));
        });

        File.WriteAllText(Path.Combine(drop.Transport.InboundDirectory, "notes.xlsx"), "not for you");
        File.WriteAllText(Path.Combine(drop.Transport.InboundDirectory, "partial.filepart"), "still uploading");
        drop.Drop("real.edi", Samples.Read(Samples.DryVan));

        Assert.True(await TempDrop.WaitUntil(() => received.Count == 1));
        await Task.Delay(200);

        Assert.Single(received);
        Assert.Equal(2, TempDrop.Files(drop.Transport.InboundDirectory).Count);
    }

    [Fact]
    public async Task An_outbound_document_is_written_under_a_name_that_sorts_and_searches()
    {
        using var drop = new TempDrop();

        var document = new OutboundDocument(
            "214",
            "000004070",
            "DEMOCARRIER",
            "DEMOBROKER",
            "ISA…",
            Guid.NewGuid(),
            "LD10041872",
            new DateTimeOffset(2026, 8, 20, 14, 5, 9, 250, TimeSpan.Zero));

        string path = await drop.Transport.SendAsync(document);

        Assert.Equal("20260820-140509.250-000004070-214.edi", Path.GetFileName(path));
        Assert.Equal("ISA…", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(drop.Transport.OutboundDirectory, "*.tmp"));
    }

    [Fact]
    public async Task Two_documents_generated_in_the_same_millisecond_do_not_collide()
    {
        // Leaving an intermediate stop emits two 214s back to back, and on a fast machine
        // they carry the same timestamp.
        using var drop = new TempDrop();

        var at = new DateTimeOffset(2026, 8, 20, 14, 5, 9, 250, TimeSpan.Zero);
        var first = new OutboundDocument("214", "000004070", "A", "B", "one", null, "LD1", at);
        var second = first with { Edi = "two" };

        string a = await drop.Transport.SendAsync(first);
        string b = await drop.Transport.SendAsync(second);

        Assert.NotEqual(a, b);
        Assert.Equal(2, TempDrop.Files(drop.Transport.OutboundDirectory).Count);
    }

    [Fact]
    public async Task Stopping_the_transport_stops_the_watching()
    {
        using var drop = new TempDrop();
        var received = new ConcurrentQueue<InboundDocument>();

        await drop.Transport.StartAsync((document, _) =>
        {
            received.Enqueue(document);
            return Task.FromResult(new InboundResult(true, "ok"));
        });

        Assert.True(drop.Transport.IsRunning);

        await drop.Transport.StopAsync();
        Assert.False(drop.Transport.IsRunning);

        drop.Drop("tender.edi", Samples.Read(Samples.DryVan));
        await Task.Delay(200);

        Assert.Empty(received);
        Assert.Single(TempDrop.Files(drop.Transport.InboundDirectory));
    }
}
