using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using GameResource.Package;
using InternalHash = GameResource.Package.Internal.XxHash64;
using OriginalHash = System.IO.Hashing.XxHash64;

string output = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/xxh64-internal/results.json");
if (File.Exists(output)) throw new IOException("Use a new result path.");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var random = new Random(20261025);
int comparisons = 0, snapshots = 0;
int[] lengths = [.. Enumerable.Range(0, 257), 511, 512, 513, 1023, 1024, 1025,
    4095, 4096, 4097, 65535, 65536, 65537, 1048575, 1048576, 1048577];
foreach (int length in lengths.Concat(Enumerable.Range(0, 1000).Select(_ => random.Next(0, 131073))))
foreach (int pattern in Enumerable.Range(0, 4))
{
    byte[] backing = new byte[length + 17];
    random.NextBytes(backing);
    Span<byte> data = backing.AsSpan(7, length);
    if (pattern == 1) data.Clear();
    else if (pattern == 2) data.Fill(255);
    else if (pattern == 3)
        for (int i = 0; i < data.Length; i++) data[i] = (byte)i;
    ulong expected = OriginalHash.HashToUInt64(data);
    Require(expected == InternalHash.HashToUInt64(data), $"One-shot mismatch at {length}/{pattern}.");
    var original = new OriginalHash();
    var internalHash = new InternalHash();
    for (int offset = 0; offset < length;)
    {
        int count = Math.Min(length - offset, random.Next(1, 8193));
        original.Append(data.Slice(offset, count));
        internalHash.Append(data.Slice(offset, count));
        Require(original.GetCurrentHashAsUInt64() == internalHash.GetCurrentHashAsUInt64(), "Snapshot mismatch.");
        internalHash.Append([]);
        Require(original.GetCurrentHashAsUInt64() == internalHash.GetCurrentHashAsUInt64(), "Empty append changed snapshot.");
        offset += count;
        snapshots++;
    }
    Require(expected == internalHash.GetCurrentHashAsUInt64(), "Incremental mismatch.");
    comparisons++;
}

// Exercise every possible split for multiple stripes against the independent oracle.
int splitComparisons = 0;
byte[] splitData = new byte[129];
random.NextBytes(splitData);
for (int length = 0; length <= splitData.Length; length++)
for (int split = 0; split <= length; split++)
{
    var hash = new InternalHash();
    hash.Append(splitData.AsSpan(0, split));
    Require(hash.GetCurrentHashAsUInt64() == OriginalHash.HashToUInt64(splitData.AsSpan(0, split)), "Split snapshot mismatch.");
    hash.Append(splitData.AsSpan(split, length - split));
    Require(hash.GetCurrentHashAsUInt64() == OriginalHash.HashToUInt64(splitData.AsSpan(0, length)), "Split final mismatch.");
    splitComparisons++;
}

// More than 4 GiB of streaming input, reusing 64 KiB rather than allocating a large file/array.
byte[] block = new byte[65536];
random.NextBytes(block);
var largeOriginal = new OriginalHash();
var largeInternal = new InternalHash();
largeOriginal.Append(block.AsSpan(0, 13));
largeInternal.Append(block.AsSpan(0, 13));
for (int i = 0; i < 65537; i++)
{
    largeOriginal.Append(block);
    largeInternal.Append(block);
    if (i % 1024 == 0)
        Require(largeOriginal.GetCurrentHashAsUInt64() == largeInternal.GetCurrentHashAsUInt64(), "Long stream snapshot mismatch.");
}
largeOriginal.Append(block.AsSpan(0, 31));
largeInternal.Append(block.AsSpan(0, 31));
Require(largeOriginal.GetCurrentHashAsUInt64() == largeInternal.GetCurrentHashAsUInt64(), "Long stream final mismatch.");
long streamedBytes = 13L + 65537L * block.Length + 31;
Parallel.For(0, 256, i =>
{
    byte[] data = new byte[65536 + i];
    new Random(i).NextBytes(data);
    var hash = new InternalHash();
    hash.Append(data.AsSpan(0, i));
    hash.Append(data.AsSpan(i));
    Require(hash.GetCurrentHashAsUInt64() == OriginalHash.HashToUInt64(data), "Parallel mismatch.");
});
Console.WriteLine($"Compatibility passed: {comparisons} inputs, {snapshots} snapshots, {splitComparisons} splits, {streamedBytes} streamed bytes, 256 parallel instances.");

var legacyPackages = new List<object>();
foreach (string packagePath in args.Skip(1))
{
    using var package = ResourcePackage.OpenRead(packagePath);
    var timer = Stopwatch.StartNew();
    package.Verify();
    legacyPackages.Add(new { Path = Path.GetFullPath(packagePath), Files = package.GetStatistics().FileCount,
        VerifyMilliseconds = timer.Elapsed.TotalMilliseconds });
    Console.WriteLine($"Existing package contents and Meta verified: {packagePath}");
}

using var process = Process.GetCurrentProcess();
if (OperatingSystem.IsWindows()) process.ProcessorAffinity = (nint)1;
var performance = new List<object>();
ulong sink = 0;
foreach (int length in new[] { 0, 32, 256, 4096, 65536, 1048576 })
{
    byte[] data = new byte[length];
    random.NextBytes(data);
    Action oneOriginal = () => sink = unchecked(sink + OriginalHash.HashToUInt64(data));
    Action oneInternal = () => sink = unchecked(sink + InternalHash.HashToUInt64(data));
    Action streamOriginal = () =>
    {
        var hash = new OriginalHash();
        for (int i = 0; i < data.Length; i += 65536) hash.Append(data.AsSpan(i, Math.Min(65536, data.Length - i)));
        sink = unchecked(sink + hash.GetCurrentHashAsUInt64());
    };
    Action streamInternal = () =>
    {
        var hash = new InternalHash();
        for (int i = 0; i < data.Length; i += 65536) hash.Append(data.AsSpan(i, Math.Min(65536, data.Length - i)));
        sink = unchecked(sink + hash.GetCurrentHashAsUInt64());
    };
    var actions = new[] { ("one-shot", "System.IO.Hashing", oneOriginal), ("one-shot", "internal", oneInternal),
        ("incremental", "System.IO.Hashing", streamOriginal), ("incremental", "internal", streamInternal) };
    var counts = new Dictionary<string, int>();
    foreach (var (operation, implementation, action) in actions)
    {
        for (int i = 0; i < 100; i++) action();
        int count = 1;
        double ms;
        do
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++) action();
            ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (ms < 20) count *= 2;
        } while (ms < 20);
        counts[operation + implementation] = Math.Max(1, (int)Math.Ceiling(count * 100 / ms));
    }
    for (int round = 1; round <= 7; round++)
    {
        var order = actions.ToArray();
        random.Shuffle(order);
        foreach (var (operation, implementation, action) in order)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            int count = counts[operation + implementation];
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++) action();
            double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            performance.Add(new { Length = length, Operation = operation, Implementation = implementation, Round = round,
                Iterations = count, Milliseconds = ms, NanosecondsPerHash = ms * 1000000 / count,
                AllocatedBytesPerHash = (double)allocated / count,
                MiBPerSecond = count * (double)length / 1048576 / (ms / 1000) });
        }
    }
    Console.WriteLine($"Performance measured: {length} bytes.");
}

var library = typeof(ResourcePackage).Assembly;
Require(!library.GetReferencedAssemblies().Any(a => a.Name == "System.IO.Hashing"), "Library still references System.IO.Hashing.");
var report = new
{
    Completed = true, Time = DateTimeOffset.Now, Runtime = RuntimeInformation.FrameworkDescription,
    OS = RuntimeInformation.OSDescription, ProcessorAffinity = OperatingSystem.IsWindows() ? process.ProcessorAffinity.ToInt64() : 0,
    TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
    OriginalAssemblySha256 = Hash(typeof(OriginalHash).Assembly), InternalAssemblySha256 = Hash(library),
    Comparisons = comparisons, Snapshots = snapshots, SplitComparisons = splitComparisons,
    StreamedBytes = streamedBytes, StreamedHash = largeInternal.GetCurrentHashAsUInt64().ToString("X16"), ParallelInstances = 256,
    PerformanceProtocol = "7 rounds, seeded randomized order, calibrated ~100 ms per case; reused random input; no disk I/O; delegate/loop overhead included; incremental creates one instance per hash, appending in 64 KiB chunks; affinity set for performance section only.",
    ExistingPackages = legacyPackages, Performance = performance, Sink = sink
};
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"RESULT {output}");

static string Hash(Assembly assembly) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location)));
static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
