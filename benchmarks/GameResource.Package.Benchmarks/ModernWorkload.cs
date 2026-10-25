using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameResource.Package;
using MessagePack;

sealed partial class Workload
{
    private void Modern()
    {
        _details["dataset"] = "Same 500k mixed payloads; every resource receives a MessagePack map {id,width,name,tags}; no encryption.";
        using (var editor = ResourcePackage.OpenEdit(config.PackagePath))
        {
            Measure("metadata.write_all", 0, config.Files, 0, () =>
            {
                for (int id = 0; id < config.Files; id++) editor.UpdateMetadata(_paths[id], Meta(id));
            });
            Measure("metadata.save", 0, 1, 0, () => editor.Save());
            CheckBudget();
        }
        for (int round = 1; round <= config.Rounds; round++)
        {
            Measure("metadata.open_sync", round, 1, 0, () =>
            {
                using var package = ResourcePackage.OpenRead(config.PackagePath);
                Require(package.GetStatistics().MetadataCacheBytes == 0, "Opening eagerly loaded metadata.");
            });
            Measure("metadata.open_async", round, 1, 0, () =>
            {
                using var package = ResourcePackage.OpenAsync(config.PackagePath).AsTask().GetAwaiter().GetResult();
                Require(package.GetStatistics().MetadataCacheBytes == 0, "Opening eagerly loaded metadata.");
            });
        }
        int[] ids = _shuffled.Take(Math.Min(100_000, config.Files)).ToArray();
        long bytes = ids.Sum(id => (long)Payloads.Length(id));
        _details["combinedReadSampleCount"] = ids.Length;
        _details["combinedReadCache"] = "Content and metadata OS cache warmed. Package metadata cache disabled for sync/async comparison. One buffered result disposed per read; XXH64 checked.";
        using (var package = ResourcePackage.OpenRead(config.PackagePath, new() { MetadataCacheBytes = 0 }))
        {
            // Warm disk data and both API paths before collecting timings.
            foreach (int id in ids) { using var resource = package.ReadResource<BenchMetadata>(_paths[id]); }
            ReadCombinedAsync(package, ids[..Math.Min(1000, ids.Length)], 4).GetAwaiter().GetResult();
            for (int round = 1; round <= config.Rounds; round++)
            {
                foreach (int concurrency in new[] { 1, 4, 8 })
                {
                    Measure($"combined.sync.c{concurrency}", round, ids.Length, bytes, () => ReadCombined(package, ids, concurrency));
                    Measure($"combined.async.c{concurrency}", round, ids.Length, bytes,
                        () => ReadCombinedAsync(package, ids, concurrency).GetAwaiter().GetResult());
                }
                Measure("combined.properties.sync.c1", round, ids.Length, bytes, () =>
                {
                    long sum = 0;
                    foreach (int id in ids)
                    {
                        using var resource = package.ReadResource(_paths[id]);
                        Require(resource.Metadata!.GetInt32("id") == id, "Properties mismatch.");
                        sum += resource.Data.Length;
                    }
                    _sink ^= sum;
                });
                Measure("lazy.reference_create", round, config.Files, 0, () =>
                {
                    foreach (var path in _paths) _sink ^= package.GetResource<BenchMetadata>(path).Path.Length;
                });
            }
        }
        using (var package = ResourcePackage.OpenRead(config.PackagePath))
        {
            string hotPath = _paths[ids[0]];
            package.ReadMetadata<BenchMetadata>(hotPath);
            for (int round = 1; round <= config.Rounds; round++)
                Measure("metadata.hot_dto", round, 100_000, 0, () =>
                {
                    for (int i = 0; i < 100_000; i++) _sink ^= package.ReadMetadata<BenchMetadata>(hotPath)!.Id;
                });
            _details["statisticsWithMetadata"] = package.GetStatistics();
            package.Verify();
        }
        var metadata = Meta(12345);
        var messagePackOptions = MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData.WithMaximumObjectGraphDepth(64));
        byte[] packed = MessagePackSerializer.Serialize(metadata, messagePackOptions);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(metadata);
        _details["sampleMessagePackBytes"] = packed.Length;
        _details["sampleJsonBytes"] = json.Length;
        // Serializer-only comparison. Package map validation/checksums/I/O are covered by combined reads above.
        for (int i = 0; i < 1000; i++)
        {
            _sink ^= MessagePackSerializer.Deserialize<BenchMetadata>(packed, messagePackOptions).Id;
            _sink ^= JsonSerializer.Deserialize<BenchMetadata>(json)!.Id;
        }
        for (int round = 1; round <= config.Rounds; round++)
        {
            Measure("codec.messagepack.decode", round, 1_000_000, 0, () =>
            {
                for (int i = 0; i < 1_000_000; i++) _sink ^= MessagePackSerializer.Deserialize<BenchMetadata>(packed, messagePackOptions).Id;
            });
            Measure("codec.json.decode", round, 1_000_000, 0, () =>
            {
                for (int i = 0; i < 1_000_000; i++) _sink ^= JsonSerializer.Deserialize<BenchMetadata>(json)!.Id;
            });
        }
        _details["fullPackageWithMetadataVerified"] = true;
    }
    private static BenchMetadata Meta(int id) => new() { Id = id, Width = 32 + id % 128, Name = "cat", Tags = ["sprite", "monster"] };
    private void ReadCombined(ResourcePackage package, int[] ids, int concurrency)
    {
        if (concurrency == 1) { Worker(0); return; }
        Parallel.For(0, concurrency, new ParallelOptions { MaxDegreeOfParallelism = concurrency }, Worker);
        void Worker(int worker)
        {
            long sum = 0;
            for (int i = worker; i < ids.Length; i += concurrency)
            {
                int id = ids[i];
                using var resource = package.ReadResource<BenchMetadata>(_paths[id]);
                Require(resource.Metadata!.Id == id && BinaryPrimitives.ReadInt32LittleEndian(resource.Data.Span) == id, "Combined read mismatch.");
                sum += resource.Data.Length;
            }
            Interlocked.Add(ref _sink, sum);
        }
    }
    private Task ReadCombinedAsync(ResourcePackage package, int[] ids, int concurrency) =>
        Task.WhenAll(Enumerable.Range(0, concurrency).Select(async worker =>
        {
            long sum = 0;
            for (int i = worker; i < ids.Length; i += concurrency)
            {
                int id = ids[i];
                using var resource = await package.ReadResourceAsync<BenchMetadata>(_paths[id]).ConfigureAwait(false);
                Require(resource.Metadata!.Id == id && BinaryPrimitives.ReadInt32LittleEndian(resource.Data.Span) == id, "Combined read mismatch.");
                sum += resource.Data.Length;
            }
            Interlocked.Add(ref _sink, sum);
        }));
}

[MessagePackObject]
public sealed class BenchMetadata
{
    [Key("id"), JsonPropertyName("id")] public int Id { get; set; }
    [Key("width"), JsonPropertyName("width")] public int Width { get; set; }
    [Key("name"), JsonPropertyName("name")] public string Name { get; set; } = "";
    [Key("tags"), JsonPropertyName("tags")] public string[] Tags { get; set; } = [];
}
