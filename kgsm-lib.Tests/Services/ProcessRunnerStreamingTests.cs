using System.Text;

namespace TheKrystalShip.KGSM.Tests.Services;

/// <summary>
/// The streaming and stdin paths of <see cref="ProcessRunner"/>, against real child processes
/// (<c>cat</c>, <c>sh</c>) that the OS provides.
/// </summary>
public class ProcessRunnerStreamingTests
{
    private readonly ProcessRunner _runner = new(new Mock<ILogger<ProcessRunner>>().Object);

    /// <summary>Produces a deterministic stream of <c>Length</c> bytes without ever holding them.</summary>
    private sealed class GeneratedStream(long length) : Stream
    {
        private long _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = (int)Math.Min(count, length - _position);
            for (int i = 0; i < n; i++)
                buffer[offset + i] = (byte)((_position + i) % 251);
            _position += n;
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Counts and checksums what it is given and keeps none of it.</summary>
    private sealed class CountingSink : Stream
    {
        public long Count { get; private set; }
        public long LargestWrite { get; private set; }
        public bool Matches { get; private set; } = true;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => Count; set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (buffer[offset + i] != (byte)((Count + i) % 251))
                    Matches = false;
            }

            Count += count;
            LargestWrite = Math.Max(LargestWrite, count);
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Streaming_CopiesALargeStreamThroughTheProcessInOrderWithoutHoldingItWhole()
    {
        const long size = 256L * 1024 * 1024;
        GeneratedStream source = new(size);
        CountingSink sink = new();

        StreamResult result = await _runner.ExecuteStreamingAsync("cat", [], null, source, sink);

        Assert.True(result.IsSuccess);
        Assert.Equal(size, sink.Count);
        Assert.True(sink.Matches);
        // Every write the sink saw was one pipe-sized chunk, never the part.
        Assert.True(sink.LargestWrite <= 1024 * 1024);
    }

    [Fact]
    public async Task Streaming_NonZeroExit_ReturnsTheCodeAndStderr()
    {
        StreamResult result = await _runner.ExecuteStreamingAsync(
            "sh", ["-c", "echo no such part >&2; exit 25"], null, null, null);

        Assert.False(result.IsSuccess);
        Assert.Equal(25, result.ExitCode);
        Assert.Equal("no such part", result.Stderr);
    }

    [Fact]
    public async Task Streaming_StdoutOnly_WritesOnlyTheProcessBytesToTheDestination()
    {
        using MemoryStream destination = new();

        StreamResult result = await _runner.ExecuteStreamingAsync(
            "sh", ["-c", "printf 'tar-bytes'; echo log >&2"], null, null, destination);

        Assert.True(result.IsSuccess);
        Assert.Equal("tar-bytes", Encoding.UTF8.GetString(destination.ToArray()));
        Assert.Equal("log", result.Stderr);
    }

    [Fact]
    public async Task Streaming_ProcessThatIgnoresItsInput_StillReturnsItsExitCode()
    {
        StreamResult result = await _runner.ExecuteStreamingAsync(
            "sh", ["-c", "exit 63"], null, new GeneratedStream(64L * 1024 * 1024), null);

        Assert.Equal(63, result.ExitCode);
    }

    [Fact]
    public async Task Streaming_PassesEnvironmentToTheProcess()
    {
        using MemoryStream destination = new();

        await _runner.ExecuteStreamingAsync(
            "sh", ["-c", "printf '%s' \"$KGSM_EVENT_ACTOR\""],
            new Dictionary<string, string> { ["KGSM_EVENT_ACTOR"] = "claude" }, null, destination);

        Assert.Equal("claude", Encoding.UTF8.GetString(destination.ToArray()));
    }

    /// <summary>Signals once the process has written its pid, and keeps what it wrote.</summary>
    private sealed class PidSink : Stream
    {
        private readonly MemoryStream _bytes = new();
        public TaskCompletionSource<int> Pid { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _bytes.Length;
        public override long Position { get => _bytes.Position; set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _bytes.Write(buffer, offset, count);
            string text = Encoding.UTF8.GetString(_bytes.ToArray());
            if (text.EndsWith('\n') && int.TryParse(text.Trim(), out int pid))
                Pid.TrySetResult(pid);
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Streaming_Cancellation_KillsTheProcessAndThrows()
    {
        using CancellationTokenSource cts = new();
        PidSink sink = new();

        Task<StreamResult> run = _runner.ExecuteStreamingAsync(
            "sh", ["-c", "echo $$; exec sleep 600"], null, null, sink, cts.Token);

        int pid = await sink.Pid.Task.WaitAsync(TimeSpan.FromMinutes(2));
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        // The task has returned, so the process has been waited for: its pid no longer exists.
        Assert.False(Directory.Exists($"/proc/{pid}"));
    }

    [Fact]
    public async Task Streaming_AlreadyCancelled_StartsNothing()
    {
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _runner.ExecuteStreamingAsync("cat", [], null, null, null, cts.Token));
    }

    [Fact]
    public async Task Streaming_SourceThatFails_KillsTheProcessAndSurfacesTheFailure()
    {
        FailingStream source = new();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            _runner.ExecuteStreamingAsync("cat", [], null, source, Stream.Null));
    }

    private sealed class FailingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidDataException("source failed");
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void ExecuteWithInput_WritesThePayloadToStdinAndBuffersStdout()
    {
        string payload = new string('x', 300_000) + "\n{\"ok\":true}";

        ProcessResult result = _runner.ExecuteWithInput("cat", TimeSpan.FromMinutes(2), null, payload, []);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(payload, result.Stdout.Replace("\r", ""));
    }

    [Fact]
    public void ExecuteWithInput_NonZeroExit_KeepsStdoutAndTheCode()
    {
        ProcessResult result = _runner.ExecuteWithInput(
            "sh", TimeSpan.FromMinutes(2), null, "ignored", ["-c", "echo '{\"ok\":false}'; exit 62"]);

        Assert.Equal(62, result.ExitCode);
        Assert.Equal("{\"ok\":false}", result.Stdout);
    }
}
