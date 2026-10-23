using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace StonesHotspot.Network.Bandwidth;

/// <summary>Where the WAN speed test downloads from and uploads to (configuration section <c>Bandwidth</c>).</summary>
public sealed class SpeedTestOptions
{
    public string TestDownloadUrl { get; set; } = "https://speed.cloudflare.com/__down?bytes=100000000";
    public string TestUploadUrl { get; set; } = "https://speed.cloudflare.com/__up";
    public TimeSpan DownloadDuration { get; set; } = TimeSpan.FromSeconds(8);
    public TimeSpan UploadDuration { get; set; } = TimeSpan.FromSeconds(6);
    public int Streams { get; set; } = 4;
}

/// <param name="DownKbps">Measured download speed (kbit/s), null when the test failed.</param>
/// <param name="UpKbps">Measured upload speed (kbit/s), null when the test failed.</param>
public sealed record SpeedTestResult(int? DownKbps, int? UpKbps, DateTime MeasuredAt, string? Error);

/// <summary>
/// Measures the internet line from the PC itself (not through the gate): several parallel streams for a few
/// seconds each way; the result can fill in the fair-share totals.
/// </summary>
public sealed class WanSpeedTest(HttpClient http, SpeedTestOptions options, TimeProvider clock, ILogger<WanSpeedTest> logger)
{
    private readonly SemaphoreSlim _running = new(1, 1);

    public SpeedTestResult? Last { get; private set; }

    public bool IsRunning => _running.CurrentCount == 0;

    public async Task<SpeedTestResult> RunAsync(CancellationToken ct)
    {
        if (!await _running.WaitAsync(0, ct))
            return Last ?? new SpeedTestResult(null, null, clock.GetUtcNow().UtcDateTime, "A test is already running.");
        try
        {
            string? error = null;
            int? down = null, up = null;
            try
            {
                down = await MeasureAsync(DownloadOnceAsync, options.DownloadDuration, ct);
                up = await MeasureAsync(UploadOnceAsync, options.UploadDuration, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "WAN speed test failed");
                error = ex.Message;
            }
            Last = new SpeedTestResult(down, up, clock.GetUtcNow().UtcDateTime, error);
            return Last;
        }
        finally
        {
            _running.Release();
        }
    }

    /// <summary>Runs <see cref="SpeedTestOptions.Streams"/> loops of <paramref name="once"/> until the time is up; kbit/s.</summary>
    private async Task<int> MeasureAsync(Func<CancellationToken, Action<long>, Task> once, TimeSpan duration, CancellationToken ct)
    {
        long total = 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(duration);
        var watch = Stopwatch.StartNew();
        void Count(long bytes) => Interlocked.Add(ref total, bytes);

        var streams = Enumerable.Range(0, Math.Max(1, options.Streams)).Select(async _ =>
        {
            try
            {
                while (!timeout.IsCancellationRequested)
                    await once(timeout.Token, Count);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // time is up
            }
        });
        await Task.WhenAll(streams);
        ct.ThrowIfCancellationRequested();

        var seconds = Math.Max(watch.Elapsed.TotalSeconds, 0.1);
        if (Interlocked.Read(ref total) == 0)
            throw new HttpRequestException("No data could be transferred.");
        return (int)(Interlocked.Read(ref total) * 8 / 1000 / seconds);
    }

    private async Task DownloadOnceAsync(CancellationToken ct, Action<long> count)
    {
        using var response = await http.GetAsync(options.TestDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            count(read);
    }

    private static readonly byte[] UploadChunk = new byte[2 * 1024 * 1024];

    private async Task UploadOnceAsync(CancellationToken ct, Action<long> count)
    {
        using var content = new ByteArrayContent(UploadChunk);
        using var response = await http.PostAsync(options.TestUploadUrl, content, ct);
        response.EnsureSuccessStatusCode();
        count(UploadChunk.Length);
    }
}
