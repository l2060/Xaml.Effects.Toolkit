using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Compression.Benchmarks;
using K4os.Compression.LZ4;

string root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
string outputPath = Path.GetFullPath(args.Length > 1 ? args[1] : "artifacts/compression-benchmark/results.json");
int rounds = args.Length > 2 ? int.Parse(args[2]) : 7;
double targetMilliseconds = args.Length > 3 ? double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 200;
if (rounds < 3 || targetMilliseconds < 20) throw new ArgumentOutOfRangeException(nameof(args));
if (File.Exists(outputPath)) throw new IOException("Use a new output path to preserve prior results.");
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
using var process = Process.GetCurrentProcess();
process.ProcessorAffinity = (nint)1; // This process only: keep all cases on logical CPU 0.
var corpora = Corpora.Load(root);
var prepared = new List<Prepared>();
var cases = new List<DecodeCase>();
Console.WriteLine($"Corpora: {corpora.Length}; {corpora.Sum(x => (long)x.Raw.Length):N0} raw bytes; CPU affinity=1");

foreach (var corpus in corpora)
foreach (var layout in new[] { "whole", "blocks64k" })
foreach (var codec in new[] { Codec.Lz4, Codec.ZlibFastest, Codec.ZlibOptimal })
{
    var compressed = Prepared.Encode(corpus, codec, layout);
    prepared.Add(compressed);
    cases.Add(new(compressed, false));
    if (layout == "blocks64k") cases.Add(new(compressed, true));
    Console.WriteLine($"PREP {corpus.Name}/{layout}/{codec}: {compressed.EncodedBytes:N0}/{corpus.Raw.Length:N0} bytes; raw fallback blocks={compressed.FallbackBlocks}");
}

// Verify every case before timing; decompression writes into a preallocated caller-owned output array.
foreach (var item in cases) { item.Decode(); item.Verify(); }
foreach (var item in cases) item.Calibrate(targetMilliseconds);
long sink = 0;
var measurements = new List<Measurement>();
DateTimeOffset started = DateTimeOffset.Now;
for (int round = 1; round <= rounds; round++)
{
    var order = cases.ToArray();
    new Random(77123 + round).Shuffle(order);
    foreach (var item in order)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        item.Decode(); // Warm code and buffers again after GC; not timed.
        long allocated = GC.GetTotalAllocatedBytes(true);
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        TimeSpan cpuBefore = process.TotalProcessorTime;
        long begin = Stopwatch.GetTimestamp();
        for (int iteration = 0; iteration < item.Iterations; iteration++) item.Decode();
        double milliseconds = Stopwatch.GetElapsedTime(begin).TotalMilliseconds;
        long allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated;
        double cpuMilliseconds = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
        long rawBytes = (long)item.Prepared.Corpus.Raw.Length * item.Iterations;
        measurements.Add(new(item.Prepared.Corpus.Name, item.Prepared.Layout, item.Prepared.Codec.ToString(),
            item.Auto ? "auto_fallback" : "always_decode", round, item.Iterations, milliseconds,
            milliseconds / item.Iterations, rawBytes / 1048576d / (milliseconds / 1000), allocatedBytes,
            cpuMilliseconds, GC.CollectionCount(0) - g0, GC.CollectionCount(1) - g1, GC.CollectionCount(2) - g2));
        item.Verify(); // Full byte-for-byte validation after each round, outside the timed region.
        sink += item.Output[0] + item.Output[^1];
    }
    Save();
    Console.WriteLine($"ROUND {round}/{rounds} complete; {cases.Count} verified cases.");
}
Save();
Console.WriteLine($"RESULT {outputPath}");

void Save()
{
    var summary = measurements.GroupBy(m => (m.Corpus, m.Layout, m.Codec, m.Policy)).Select(g =>
    {
        var row = prepared.Single(p => p.Corpus.Name == g.Key.Corpus && p.Layout == g.Key.Layout && p.Codec.ToString() == g.Key.Codec);
        return new
        {
            g.Key.Corpus, g.Key.Layout, g.Key.Codec, g.Key.Policy, Samples = g.Count(),
            RawBytes = row.Corpus.Raw.Length, EncodedBytes = row.EncodedBytes, AutoStoredBytes = row.AutoStoredBytes,
            row.FallbackBlocks, Blocks = row.Blocks.Length,
            MedianMillisecondsPerOperation = Median(g.Select(x => x.MillisecondsPerOperation)),
            MinMillisecondsPerOperation = g.Min(x => x.MillisecondsPerOperation),
            MaxMillisecondsPerOperation = g.Max(x => x.MillisecondsPerOperation),
            MedianMiBPerSecond = Median(g.Select(x => x.MiBPerSecond)),
            MedianAllocatedBytesPerOperation = Median(g.Select(x => (double)x.AllocatedBytes / x.Iterations))
        };
    }).OrderBy(x => x.Corpus).ThenBy(x => x.Layout).ThenBy(x => x.Policy).ThenBy(x => x.Codec).ToArray();
    var report = new
    {
        Started = started, Updated = DateTimeOffset.Now, Completed = measurements.Count == rounds * cases.Count,
        Environment = new
        {
            Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(), GCSettings.IsServerGC,
            ProcessorAffinity = process.ProcessorAffinity.ToInt64(), LogicalProcessors = Environment.ProcessorCount,
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            Lz4Assembly = typeof(LZ4Codec).Assembly.FullName,
            CompressionAssembly = typeof(ZLibStream).Assembly.FullName,
            BenchmarkSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Prepared).Assembly.Location)))
        },
        Protocol = new
        {
            Rounds = rounds, TargetMillisecondsPerCase = targetMilliseconds,
            Input = "Compressed bytes already in memory; reused caller-owned output buffers; no disk, PNG decoding, encryption or path lookup in timing.",
            Zlib = "System.IO.Compression.ZLibStream; Fastest and Optimal; construct/dispose one stream per compressed unit; ReadExactly plus EOF/trailer check.",
            Lz4 = "K4os.Compression.LZ4 1.3.8; LZ4Level.L00_FAST; raw independent blocks; verify decoded byte count.",
            Framing = "Whole corpus or independent <=65536-byte blocks. Ratio includes Zlib wrapper/checksum; excludes package block tables for both codecs.",
            Policies = "always_decode: force codec even for incompressible blocks. auto_fallback: block stored raw when compressed length >= raw, and decoded via memory copy.",
            Order = "Seeded randomized case order every round; single logical CPU; calibrated iteration count per case; every round byte-for-byte verified."
        },
        Corpora = corpora.Select(x => x.Describe()), Summary = summary, Measurements = measurements, Sink = sink
    };
    File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
}
static double Median(IEnumerable<double> values)
{
    var ordered = values.Order().ToArray();
    return ordered.Length % 2 == 0 ? (ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2 : ordered[ordered.Length / 2];
}

internal enum Codec { Lz4, ZlibFastest, ZlibOptimal }
internal sealed record Block(int Offset, int RawLength, byte[] Encoded);
internal sealed record Prepared(Corpus Corpus, Codec Codec, string Layout, Block[] Blocks)
{
    internal long EncodedBytes => Blocks.Sum(b => (long)b.Encoded.Length);
    internal long AutoStoredBytes => Blocks.Sum(b => (long)Math.Min(b.RawLength, b.Encoded.Length));
    internal int FallbackBlocks => Blocks.Count(b => b.Encoded.Length >= b.RawLength);
    internal static Prepared Encode(Corpus corpus, Codec codec, string layout)
    {
        var blocks = new List<Block>();
        int blockSize = layout == "whole" ? corpus.Raw.Length : 65536;
        for (int offset = 0; offset < corpus.Raw.Length; offset += blockSize)
        {
            int length = Math.Min(blockSize, corpus.Raw.Length - offset);
            var raw = corpus.Raw.AsSpan(offset, length);
            byte[] encoded;
            if (codec == Codec.Lz4)
            {
                byte[] target = new byte[LZ4Codec.MaximumOutputSize(length)];
                int stored = LZ4Codec.Encode(raw, target, LZ4Level.L00_FAST);
                if (stored <= 0) throw new InvalidDataException("LZ4 compression failed.");
                encoded = target.AsSpan(0, stored).ToArray();
            }
            else
            {
                using var buffer = new MemoryStream();
                using (var stream = new ZLibStream(buffer, codec == Codec.ZlibFastest ? CompressionLevel.Fastest : CompressionLevel.Optimal, leaveOpen: true))
                    stream.Write(raw);
                encoded = buffer.ToArray();
            }
            blocks.Add(new(offset, length, encoded));
        }
        return new(corpus, codec, layout, blocks.ToArray());
    }
}

internal sealed class DecodeCase(Prepared prepared, bool auto)
{
    internal Prepared Prepared { get; } = prepared;
    internal bool Auto { get; } = auto;
    internal byte[] Output { get; } = new byte[prepared.Corpus.Raw.Length];
    internal int Iterations { get; private set; } = 1;
    internal void Decode()
    {
        foreach (var block in Prepared.Blocks)
        {
            var destination = Output.AsSpan(block.Offset, block.RawLength);
            if (Auto && block.Encoded.Length >= block.RawLength)
                Prepared.Corpus.Raw.AsSpan(block.Offset, block.RawLength).CopyTo(destination);
            else if (Prepared.Codec == Codec.Lz4)
            {
                if (LZ4Codec.Decode(block.Encoded, destination) != block.RawLength) throw new InvalidDataException("LZ4 output length mismatch.");
            }
            else
            {
                using var input = new MemoryStream(block.Encoded, writable: false);
                using var stream = new ZLibStream(input, CompressionMode.Decompress);
                stream.ReadExactly(destination);
                if (stream.ReadByte() != -1) throw new InvalidDataException("Zlib output exceeded expected length.");
            }
        }
    }
    internal void Verify()
    {
        if (!Output.AsSpan().SequenceEqual(Prepared.Corpus.Raw)) throw new InvalidDataException($"Content mismatch: {Prepared.Corpus.Name}/{Prepared.Codec}");
    }
    internal void Calibrate(double targetMilliseconds)
    {
        for (int i = 0; i < 5; i++) Decode();
        int iterations = 1;
        double milliseconds;
        do
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) Decode();
            milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (milliseconds < 30) iterations = checked(iterations * 2);
        } while (milliseconds < 30);
        Iterations = Math.Clamp((int)Math.Ceiling(targetMilliseconds / milliseconds * iterations), 1, 5_000_000);
    }
}

internal sealed record Measurement(string Corpus, string Layout, string Codec, string Policy, int Round, int Iterations,
    double Milliseconds, double MillisecondsPerOperation, double MiBPerSecond, long AllocatedBytes,
    double CpuMilliseconds, int Gen0, int Gen1, int Gen2);
