using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameResource.Package;
using Embedded = GameResource.Package.Internal.Lz4.LZ4Codec;
using EmbeddedLevel = GameResource.Package.Internal.Lz4.LZ4Level;
using Original = K4os.Compression.LZ4.LZ4Codec;
using OriginalLevel = K4os.Compression.LZ4.LZ4Level;

string output = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/lz4-embedded/results.json");
if (File.Exists(output)) throw new IOException("Use a new result path.");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
using var process = Process.GetCurrentProcess();
if (OperatingSystem.IsWindows()) process.ProcessorAffinity = (nint)1;
int[] lengths = [0, 1, 4, 12, 13, 15, 16, 31, 32, 255, 256, 1024, 4096, 65535, 65536, 65537, 131089, 1048576];
int comparisons = 0;
foreach (bool force32 in new[] { false, true })
{
    Embedded.Enforce32 = Original.Enforce32 = force32;
    foreach (int length in lengths)
    foreach (int pattern in Enumerable.Range(0, 4))
    foreach (int level in new[] { 0, 3, 9, 12 })
    {
        byte[] raw = Payload(length, pattern);
        byte[] a = new byte[Original.MaximumOutputSize(length)], b = new byte[Embedded.MaximumOutputSize(length)];
        Require(a.Length == b.Length, "Compression bound differs.");
        int countA = Original.Encode(raw, a, (OriginalLevel)level);
        int countB = Embedded.Encode(raw, b, (EmbeddedLevel)level);
        Require(countA == countB && a.AsSpan(0, countA).SequenceEqual(b.AsSpan(0, countB)), "Encoded bytes differ from upstream.");
        byte[] decodedA = new byte[length], decodedB = new byte[length];
        Require(Embedded.Decode(a.AsSpan(0, countA), decodedB) == length, "Embedded cannot read upstream output.");
        Require(Original.Decode(b.AsSpan(0, countB), decodedA) == length, "Upstream cannot read embedded output.");
        Require(raw.AsSpan().SequenceEqual(decodedA) && raw.AsSpan().SequenceEqual(decodedB), "Round-trip mismatch.");
        if (length > 0)
        {
            Require(Embedded.Decode(a.AsSpan(0, countA), decodedB.AsSpan(0, length - 1)) < 0, "Short output buffer accepted.");
            Require(Original.Encode(raw, a.AsSpan(0, 1), (OriginalLevel)level) ==
                Embedded.Encode(raw, b.AsSpan(0, 1), (EmbeddedLevel)level), "Short compression buffer behavior differs.");
        }
        comparisons++;
    }
}
Embedded.Enforce32 = Original.Enforce32 = false;

// Corrupt input differential checks, including output guards around a sliced destination.
var random = new Random(20261025);
for (int i = 0; i < 2000; i++)
{
    byte[] bytes = new byte[random.Next(1, 257)];
    random.NextBytes(bytes);
    byte[] a = new byte[160], b = new byte[160];
    a.AsSpan().Fill(0xCD); b.AsSpan().Fill(0xCD);
    int countA = Original.Decode(bytes, a.AsSpan(16, 128));
    int countB = Embedded.Decode(bytes, b.AsSpan(16, 128));
    Require(countA == countB, "Malformed block result differs.");
    if (countA >= 0) Require(a.AsSpan(16, countA).SequenceEqual(b.AsSpan(16, countB)), "Malformed accepted block output differs.");
    Require(b.AsSpan(0, 16).IndexOfAnyExcept((byte)0xCD) < 0 && b.AsSpan(144).IndexOfAnyExcept((byte)0xCD) < 0,
        "Decoder wrote past the output buffer.");
}
Parallel.For(0, 256, i =>
{
    byte[] raw = Payload(65536 + i, i % 4), encoded = new byte[Embedded.MaximumOutputSize(raw.Length)], decoded = new byte[raw.Length];
    int count = Embedded.Encode(raw, encoded);
    Require(Original.Decode(encoded.AsSpan(0, count), decoded) == raw.Length && raw.AsSpan().SequenceEqual(decoded), "Parallel interoperability failure.");
});
Console.WriteLine($"Compatibility passed: {comparisons} boundary/pattern/level/engine combinations, 2000 malformed blocks, 256 parallel operations.");

var legacyPackages = new List<object>();
foreach (string packagePath in args.Skip(1))
{
    using var package = ResourcePackage.OpenRead(packagePath);
    var timer = Stopwatch.StartNew();
    package.Verify();
    legacyPackages.Add(new { Path = Path.GetFullPath(packagePath), Files = package.GetStatistics().FileCount, VerifyMilliseconds = timer.Elapsed.TotalMilliseconds });
    Console.WriteLine($"Existing package verified: {packagePath}");
}

var performance = new List<object>();
long sink = 0;
for (int pattern = 0; pattern < 4; pattern++)
{
    byte[] raw = Payload(65536, pattern), encoded = new byte[Original.MaximumOutputSize(raw.Length)], decoded = new byte[raw.Length];
    int encodedLength = Original.Encode(raw, encoded);
    Action encodeOriginal = () => sink += Original.Encode(raw, encoded);
    Action encodeEmbedded = () => sink += Embedded.Encode(raw, encoded);
    Action decodeOriginal = () => sink += Original.Decode(encoded.AsSpan(0, encodedLength), decoded);
    Action decodeEmbedded = () => sink += Embedded.Decode(encoded.AsSpan(0, encodedLength), decoded);
    var actions = new[] { ("encode", "upstream", encodeOriginal), ("encode", "embedded", encodeEmbedded),
        ("decode", "upstream", decodeOriginal), ("decode", "embedded", decodeEmbedded) };
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
        counts[operation + implementation] = Math.Max(1, (int)Math.Ceiling(count * 150 / ms));
    }
    for (int round = 1; round <= 7; round++)
    {
        var order = actions.ToArray(); random.Shuffle(order);
        foreach (var (operation, implementation, action) in order)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            int count = counts[operation + implementation];
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++) action();
            double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            performance.Add(new { Pattern = pattern, Operation = operation, Implementation = implementation,
                Round = round, Iterations = count, Milliseconds = ms, MicrosecondsPerBlock = ms * 1000 / count,
                MiBPerSecond = count * 65536d / 1048576 / (ms / 1000) });
        }
    }
    Require(decoded.AsSpan().SequenceEqual(raw), "Performance output mismatch.");
}

var library = typeof(ResourcePackage).Assembly;
Require(!library.GetReferencedAssemblies().Any(a => a.Name == "K4os.Compression.LZ4"), "Library still references upstream DLL.");
Require(!library.GetExportedTypes().Any(t => t.Namespace?.Contains("Lz4", StringComparison.Ordinal) == true), "Embedded codec leaked into public API.");
var report = new
{
    Completed = true, Time = DateTimeOffset.Now, Runtime = RuntimeInformation.FrameworkDescription,
    OS = RuntimeInformation.OSDescription, ProcessorAffinity = OperatingSystem.IsWindows() ? process.ProcessorAffinity.ToInt64() : 0,
    TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
    UpstreamVersion = Original.Version, EmbeddedVersion = Embedded.Version,
    UpstreamAssemblySha256 = Hash(typeof(Original).Assembly), EmbeddedAssemblySha256 = Hash(library),
    Comparisons = comparisons, MalformedBlocks = 2000, ParallelOperations = 256,
    PerformanceProtocol = "7 rounds, seeded randomized order, calibrated ~150 ms per case; 64 KiB blocks; reused buffers; no disk I/O; upstream package 1.3.8 vs embedded source; patterns: 0=random, 1=zeros, 2=text, 3=BGRA-like colors.",
    ExistingPackages = legacyPackages, Performance = performance, Sink = sink
};
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"RESULT {output}");

static string Hash(Assembly assembly) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location)));
static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
static byte[] Payload(int length, int pattern)
{
    byte[] bytes = new byte[length];
    if (pattern == 0) new Random(1234 + length).NextBytes(bytes);
    else if (pattern == 2)
    {
        byte[] text = Encoding.UTF8.GetBytes("{\"path\":\"game/monster/cat/stand/0.png\",\"width\":128,\"offsetX\":-16,\"enabled\":true}\n");
        for (int i = 0; i < length; i++) bytes[i] = text[i % text.Length];
        for (int i = 0; i + 4 <= length; i += 1024) BitConverter.TryWriteBytes(bytes.AsSpan(i, 4), i);
    }
    else if (pattern == 3)
        for (int i = 0; i < length; i++) bytes[i] = i % 4 == 3 ? (byte)255 : (byte)((i / 256 + i / 16) % 32 * 8);
    return bytes;
}
