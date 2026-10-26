using System.Security.Cryptography;
using Game.Asset.Cli.Build;
using Game.Asset.Cli.Configuration;
using Game.Asset.Cli.Sources;
using Game.Asset.Cli.Tags;
using Game.Asset.Package;
using Game.Asset.Package.Internal;

internal static class Suite
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var tests = new List<(string Name, Func<Task> Body)>();
        void Test(string name, Func<Task> body) => tests.Add((name, body));
        void Sync(string name, Action body) => Test(name, () => { body(); return Task.CompletedTask; });
        Sync("all eight metadata types and deterministic property encoding", () =>
        {
            var m = new AssetMetadata(PropertyMetadata.Parse("name=hero\ni=-9223372036854775808\nd=1.25\nb=true\ns=[\"a\",\"中\"]\nints=[1,2]\nds=[0.5,2.0]\nbs=[true,false]\nempty:int64[]=[]\ncode:string=001\nurl=a=b#c"));
            Check.Equal("hero", m.GetString("name")); Check.Equal(long.MinValue, m.GetInt64("i")); Check.Equal(1.25, m.GetDouble("d")); Check.True(m.GetBoolean("b"));
            Check.Equal("中", m.GetStringArray("s")[1]); Check.True(m.GetInt64Array("ints").SequenceEqual([1L, 2L]));
            Check.True(m.GetDoubleArray("ds").SequenceEqual([0.5, 2.0])); Check.True(m.GetBooleanArray("bs").SequenceEqual([true, false]));
            Check.Equal(0, m.GetInt64Array("empty").Length); Check.Equal("001", m.GetString("code")); Check.Equal("a=b#c", m.GetString("url"));
            Check.True(!m.TryGetInt64("name", out _)); Check.Throws<InvalidCastException>(() => m.GetInt64("name"));
            Check.Bytes(PropertyMetadata.Parse("a=1\nb=test"), PropertyMetadata.Parse("# comment\nb = test\n\na = 1\n"));
        });
        foreach (string invalid in new[] { "a=1\na=2", "a=9223372036854775808", "a=[1,\"b\"]", "a=[[1]]", "a=[null]", "a:double=NaN", "a:double=Infinity", "{\"a\":1}", "a:unknown=1", "a=[9007199254740993,1.5]" })
            Sync("invalid metadata: " + invalid.Replace('\n', '/'), () => Check.Throws<InvalidDataException>(() => PropertyMetadata.Parse(invalid)));

        Test("YAML paths resolve relative to project; strict schema", async () =>
        {
            using var f = new Fixture(); f.Put("a.bin", [1, 2, 3]); var p = f.Config();
            Check.Equal(f.Input, p.Packages[0].Sources[0].Directory);
            Check.Equal("common/data/a.bin", (await SourceScanner.ScanAsync(p, 4, default)).Single().Path);
            Check.Throws<InvalidDataException>(() => ProjectYaml.Parse("a: 1\na: 2")); Check.Throws<InvalidDataException>(() => ProjectYaml.Parse("a: &anchor x"));
            File.AppendAllText(f.ProjectPath, "metadata:\n  format: json\n"); Check.Throws<InvalidDataException>(() => f.Config());
        });
        Test("ignore rules, negation, nested rules and orphan metadata", async () =>
        {
            using var f = new Fixture(); f.Put("keep.bin", [1]); f.Put("skip.tmp", [2]); f.Put("keep.tmp", [3]); f.Put("sub/no.bin", [4]);
            f.Text(".assetignore", "*.tmp\n!keep.tmp\n"); f.Text("sub/.assetignore", "no.bin\n");
            Check.Equal(2, (await SourceScanner.ScanAsync(f.Config(), 4, default)).Length);
            f.Text("orphan.bin.meta", "a=1"); await Check.ThrowsAsync<InvalidDataException>(() => SourceScanner.ScanAsync(f.Config(), 4, default));
            f.Text(".assetignore", "*.tmp\n!keep.tmp\norphan.bin*\n"); Check.Equal(2, (await SourceScanner.ScanAsync(f.Config(), 4, default)).Length);
        });
        foreach (bool compression in new[] { false, true })
        foreach (bool encryption in new[] { false, true })
        {
            bool c = compression, e = encryption;
            Test($"full build/read compression={c} encryption={e}", async () =>
            {
                using var f = new Fixture(c, e); byte[] expected = new byte[700_123]; new Random(17).NextBytes(expected); expected.AsSpan(300000, 200000).Clear();
                f.Put("body.bin", expected); f.Text("body.bin.meta", "name=unique-secret-metadata\noffset=-16\nweights=[1.5,2.5]"); f.Put("empty.bin", []); f.Text("empty.bin.meta", "");
                var tag = await f.Build(); Check.Equal(2, tag.ContentChanges); Check.Equal(1, tag.Outputs.Length);
                using var set = await AssetPackageSet.OpenDirectoryAsync(f.Output, f.ReadOptions);
                Check.Equal(tag.TagId, set.Packages.Single().TagId); Check.Equal(2, set.Enumerate().Count());
                using var data = await set.ReadAsync("common/data/body.bin"); Check.Bytes(expected, data.Data.Span); Check.Equal(-16L, data.Metadata.GetInt64("offset"));
                Check.True(set.GetInfo("common/data/empty.bin").HasMetadata); using var empty = set.Read("common/data/empty.bin"); Check.Equal(0, empty.Data.Length);
                byte[] range = new byte[5000]; Check.Equal(range.Length, await set.ReadAtAsync("common/data/body.bin", 262140, range)); Check.Bytes(expected.AsSpan(262140, range.Length), range);
                using var stream = set.OpenRead("common/data/body.bin"); stream.Seek(-17, SeekOrigin.End); Check.Equal((int)expected[^17], stream.ReadByte());
                await set.VerifyAsync();
                using var paged = AssetPackageSet.OpenDirectory(f.Output, f.ReadOptions with { MaxResidentIndexBytes = 0, PageCacheBytes = 0, MetadataCacheBytes = 0 });
                using var again = paged.Read("common/data/body.bin"); Check.Bytes(expected, again.Data.Span);
                byte[] disk = File.ReadAllBytes(Path.Combine(f.Root, tag.Outputs.Single())); var h = PackageHeader.Decode(disk.AsSpan(0, 4096), disk.Length);
                Check.True(disk.AsSpan(0, (int)h.DataOffset).IndexOf("common/data/body.bin"u8) < 0); Check.True(disk.AsSpan(0, (int)h.DataOffset).IndexOf("unique-secret-metadata"u8) < 0);
                if (e) Check.Throws<InvalidDataException>(() => AssetPackageSet.OpenDirectory(f.Output, new AssetReadOptions { Password = "incorrect" }));
                Check.Equal(0, Directory.GetFiles(f.Output, "*.tmp", SearchOption.AllDirectories).Length);
            });
        }
        Test("metadata-only overlay, no deletion, full replacement resets metadata", async () =>
        {
            using var f = new Fixture(); f.Put("a", [1, 2]); f.Text("a.meta", "x=1\nold=true"); f.Put("gone", [3]);
            var a = await f.Build(); f.Text("a.meta", "x=2"); var b = await f.Build(a.TagId); Check.Equal(0, b.ContentChanges); Check.Equal(1, b.MetadataChanges);
            using (var only = AssetPackageSet.OpenDirectory(Path.Combine(f.Output, b.TagId.ToString("N")))) { Check.True(!only.Contains("common/data/a")); only.Verify(); }
            using (var all = AssetPackageSet.OpenDirectory(f.Output)) { using var r = all.Read("common/data/a"); Check.Bytes([1, 2], r.Data.Span); Check.Equal(2L, r.Metadata.GetInt64("x")); Check.True(!r.Metadata.Contains("old")); }
            File.Delete(Path.Combine(f.Input, "a.meta")); File.Delete(Path.Combine(f.Input, "gone")); var noDelete = await f.Build(b.TagId); Check.Equal(0, noDelete.Outputs.Length);
            using (var all = AssetPackageSet.OpenDirectory(f.Output)) { Check.True(all.Contains("common/data/gone")); Check.Equal(2L, all.ReadMetadata("common/data/a").GetInt64("x")); }
            Check.Equal(2, TagStore.LoadFiles(f.Config().StateDirectory, noDelete.TagId).Count);
            f.Put("a", [9]); await f.Build(noDelete.TagId);
            using var final = AssetPackageSet.OpenDirectory(f.Output); Check.True(!final.GetInfo("common/data/a").HasMetadata); using var data = final.Read("common/data/a"); Check.Bytes([9], data.Data.Span);
        });
        Test("header time ordering and relative filename tie break", async () =>
        {
            using var f = new Fixture(); f.Put("a", [1]); var a = await f.Build(); f.Put("a", [2]); var b = await f.Build();
            string one = Path.Combine(f.Root, a.Outputs[0]), two = Path.Combine(f.Root, b.Outputs[0]);
            File.SetLastWriteTimeUtc(one, DateTime.UtcNow.AddYears(1)); File.SetLastWriteTimeUtc(two, DateTime.UtcNow.AddYears(-1));
            using (var set = AssetPackageSet.OpenDirectory(f.Output)) { using var d = set.Read("common/data/a"); Check.Bytes([2], d.Data.Span); }
            string dir = Path.Combine(f.Root, "ordered"); Directory.CreateDirectory(dir); File.Copy(one, Path.Combine(dir, "z.asset")); File.Copy(two, Path.Combine(dir, "a.asset"));
            Fixture.RewriteHeader(Path.Combine(dir, "a.asset"), h => h with { BuildTicks = a.BuildTimeUtc.UtcTicks });
            Directory.CreateDirectory(Path.Combine(dir, ".incomplete.tmp")); File.WriteAllText(Path.Combine(dir, ".incomplete.tmp", "bad.asset"), "unfinished");
            using var tied = AssetPackageSet.OpenDirectory(dir); using var data = tied.Read("common/data/a"); Check.Bytes([1], data.Data.Span);
        });
        Test("paged path index and multi-page metadata", async () =>
        {
            using var f = new Fixture(); for (int i = 0; i < 550; i++) f.Put($"file{i:D4}", [(byte)i]); f.Text("file0000.meta", "text=" + new string('中', 50000)); await f.Build();
            using var set = AssetPackageSet.OpenDirectory(f.Output, new AssetReadOptions { MaxResidentIndexBytes = 0, PageCacheBytes = 196608 });
            Check.Equal(550, set.Enumerate().Count()); Check.Equal(50000, set.ReadMetadata("common/data/file0000").GetString("text").Length);
            using var data = set.Read("common/data/file0549"); Check.Bytes([(byte)(549 % 256)], data.Data.Span);
        });
        Test("parallel reads, cancellation recovery and reader lifetimes", async () =>
        {
            using var f = new Fixture(true, true); byte[] expected = new byte[1_100_123]; new Random(7).NextBytes(expected); f.Put("a", expected); f.Text("a.meta", "n=42"); await f.Build();
            var set = await AssetPackageSet.OpenDirectoryAsync(f.Output, f.ReadOptions);
            await Parallel.ForEachAsync(Enumerable.Range(0, 128), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (i, ct) =>
            { byte[] b = new byte[4096]; int offset = i * 7919 % (expected.Length - b.Length); Check.Equal(b.Length, await set.ReadAtAsync("common/data/a", offset, b, ct)); Check.Bytes(expected.AsSpan(offset, b.Length), b); });
            Parallel.For(0, 64, i => { byte[] b = new byte[16]; set.ReadAt("common/data/a", i * 1000, b); Check.Bytes(expected.AsSpan(i * 1000, 16), b); });
            using var cancel = new CancellationTokenSource(); cancel.Cancel(); await Check.ThrowsAsync<OperationCanceledException>(async () => { using var ignored = await set.ReadAsync("common/data/a", cancel.Token); });
            var buffered = await set.ReadAsync("common/data/a"); var stream = set.OpenRead("common/data/a");
            await Check.ThrowsAsync<OperationCanceledException>(async () => { _ = await stream.ReadAsync(new byte[1], cancel.Token); }); Check.Equal((int)expected[0], stream.ReadByte());
            set.Dispose(); Check.Throws<ObjectDisposedException>(() => set.GetInfo("common/data/a")); Check.Equal((int)expected[1], stream.ReadByte()); Check.Bytes(expected, buffered.Data.Span); Check.Equal(42L, buffered.Metadata.GetInt64("n"));
            stream.Dispose(); buffered.Dispose(); Check.Throws<ObjectDisposedException>(() => { _ = buffered.Data; });
        });
        Test("buffer limit and mapped backend correctness", async () =>
        {
            using var f = new Fixture(); byte[] data = Enumerable.Range(0, 70000).Select(i => (byte)i).ToArray(); f.Put("a", data); await f.Build();
            using var set = AssetPackageSet.OpenDirectory(f.Output, new AssetReadOptions { MaxBufferedAssetBytes = 8, ReadMode = AssetReadMode.MemoryMapped });
            Check.Throws<InvalidOperationException>(() => set.Read("common/data/a")); byte[] range = new byte[128]; await set.ReadAtAsync("common/data/a", 65530, range); Check.Bytes(data.AsSpan(65530, 128), range);
            using var output = new MemoryStream(); await set.CopyToAsync("common/data/a", output); Check.Bytes(data, output.ToArray());
        });
        Test("header, metadata, content corruption and truncation", async () =>
        {
            using var f = new Fixture(false); f.Put("a", [1, 2, 3, 4]); f.Text("a.meta", "name=hidden"); var tag = await f.Build(); string file = Path.Combine(f.Root, tag.Outputs[0]); byte[] original = File.ReadAllBytes(file);
            var header = PackageHeader.Decode(original.AsSpan(0, 4096), original.Length); var damaged = original.ToArray(); damaged[32] ^= 1; File.WriteAllBytes(file, damaged);
            Check.Throws<InvalidDataException>(() => AssetPackageSet.OpenDirectory(f.Output));
            damaged = original.ToArray(); damaged[(int)header.MetadataOffset] ^= 1; File.WriteAllBytes(file, damaged);
            using (var set = AssetPackageSet.OpenDirectory(f.Output)) { Check.True(set.Contains("common/data/a")); Check.Throws<CryptographicException>(() => set.ReadMetadata("common/data/a")); }
            damaged = original.ToArray(); damaged[^1] ^= 1; File.WriteAllBytes(file, damaged);
            using (var set = AssetPackageSet.OpenDirectory(f.Output)) { Check.Throws<InvalidDataException>(() => set.Read("common/data/a")); Check.Throws<InvalidDataException>(() => set.Verify()); }
            File.WriteAllBytes(file, original[..^1]); Check.Throws<InvalidDataException>(() => AssetPackageSet.OpenDirectory(f.Output));
        });
        Test("changed source fails without publishing", async () =>
        {
            using var f = new Fixture(); f.Put("a", [1, 2, 3]); var source = (await SourceScanner.ScanAsync(f.Config(), 2, default)).Single(); f.Put("a", [7, 8, 9]); string file = Path.Combine(f.Root, "failed.asset");
            await Check.ThrowsAsync<Exception>(() => PackageWriter.WriteAsync(file, Path.Combine(f.Root, "scratch"), Guid.NewGuid(), DateTime.UtcNow.Ticks, [new BuildEntry(source, false)], null, f.BuildOptions, default));
            Check.True(!File.Exists(file)); Check.True(!File.Exists(file + ".tmp"));
        });
        Test("cancellation does not publish Tag; missing baseline rejected", async () =>
        {
            using var f = new Fixture(); f.Put("a", [1]); using var cancel = new CancellationTokenSource(); cancel.Cancel();
            await Check.ThrowsAsync<OperationCanceledException>(() => ProjectBuilder.BuildAsync(f.Config(), null, f.BuildOptions, cancel.Token)); Check.Equal(0, TagStore.List(f.Config().StateDirectory).Count());
            await Check.ThrowsAsync<FileNotFoundException>(() => f.Build(Guid.NewGuid()));
        });
        Test("encoding configuration changes rewrite unchanged resources", async () =>
        {
            using var f = new Fixture(false); f.Put("a", new byte[1000]); var a = await f.Build(); File.WriteAllText(f.ProjectPath, File.ReadAllText(f.ProjectPath).Replace("compression: false", "compression: true"));
            Check.Equal(1, (await f.Build(a.TagId)).ContentChanges);
        });
        Sync("zero third-party assembly dependencies and embedded licenses", () =>
        {
            foreach (var assembly in new[] { typeof(AssetPackageSet).Assembly, typeof(ProjectBuilder).Assembly })
            foreach (var r in assembly.GetReferencedAssemblies()) Check.True(r.Name == "Game.Asset.Package" || r.Name!.StartsWith("System", StringComparison.Ordinal) || r.Name == "Microsoft.CSharp", r.FullName);
            Check.Equal(2, typeof(AssetPackageSet).Assembly.GetManifestResourceNames().Count(x => x.Contains("LICENSE", StringComparison.Ordinal)));
        });
        Test("multiple packages share Tag/time; duplicate source mapping fails", async () =>
        {
            using var f = new Fixture(); f.Put("a", [1]);
            File.AppendAllText(f.ProjectPath, "  - name: second\n    sources:\n      - directory: ./input\n        prefix: other/data\n");
            var tag = await f.Build(); Check.Equal(2, tag.Outputs.Length);
            using (var set = AssetPackageSet.OpenDirectory(f.Output))
            { Check.Equal(2, set.Packages.Count); Check.True(set.Packages.All(p => p.TagId == tag.TagId && p.BuildTimeUtc == tag.BuildTimeUtc)); Check.True(set.Contains("other/data/a")); }
            File.WriteAllText(f.ProjectPath, File.ReadAllText(f.ProjectPath).Replace("prefix: common/data", "prefix: common/data\n      - directory: ./input\n        prefix: common/data"));
            await Check.ThrowsAsync<InvalidDataException>(() => f.Build()); Check.Equal(1, TagStore.List(f.Config().StateDirectory).Count());
        });
        Test("Unicode path sorting, source-root project state exclusion and empty Meta", async () =>
        {
            using var f = new Fixture(); f.Put("\ue000", [1]); f.Put("😀", [2]); f.Text("😀.meta", "a=1"); var a = await f.Build();
            using (var set = AssetPackageSet.OpenDirectory(f.Output))
            { using var x = set.Read("common/data/😀"); Check.Bytes([2], x.Data.Span); Check.Equal(2, set.Enumerate().Count()); }
            f.Text("😀.meta", ""); var b = await f.Build(a.TagId); Check.Equal(1, b.MetadataChanges);
            using (var set = AssetPackageSet.OpenDirectory(f.Output)) { Check.True(set.GetInfo("common/data/😀").HasMetadata); Check.Equal(0, set.ReadMetadata("common/data/😀").Count); }
            File.WriteAllText(f.ProjectPath, File.ReadAllText(f.ProjectPath).Replace("directory: ./input", "directory: ."));
            var current = await SourceScanner.ScanAsync(f.Config(), 4, default);
            Check.True(current.All(x => !x.Path.Contains("/.asset/", StringComparison.Ordinal) && !x.Path.Contains("/dist/", StringComparison.Ordinal)));
        });
        Test("array TryGet methods and exact mixed numeric conversion", () =>
        {
            var m = new AssetMetadata(PropertyMetadata.Parse("n=[9007199254740992,1.5]\ni=[1]\nb=[true]\ns=[\"x\"]"));
            Check.True(m.TryGetDoubleArray("n", out var d) && d[0] == 9007199254740992d);
            Check.True(m.TryGetInt64Array("i", out var i) && i[0] == 1);
            Check.True(m.TryGetBooleanArray("b", out var b) && b[0]); Check.True(m.TryGetStringArray("s", out var s) && s![0] == "x");
            Check.True(!m.TryGetInt64Array("missing", out _)); return Task.CompletedTask;
        });
        Test("encrypted content corruption", async () =>
        {
            using var f = new Fixture(true, true); f.Put("a", new byte[400000]); var tag = await f.Build();
            string file = Path.Combine(f.Root, tag.Outputs[0]); byte[] disk = File.ReadAllBytes(file);
            disk[^1] ^= 1; File.WriteAllBytes(file, disk);
            using var set = AssetPackageSet.OpenDirectory(f.Output, f.ReadOptions);
            await Check.ThrowsAsync<CryptographicException>(async () => { using var ignored = await set.ReadAsync("common/data/a"); });
        });
        if (args.Contains("--large"))
        foreach (bool compression in new[] { false, true })
        foreach (bool encryption in new[] { false, true })
        {
            bool encrypted = encryption, compressed = compression;
            Test($"actual >4GiB resource streaming, LZ4={compressed}, AES={encrypted}", async () =>
            {
                using var f = new Fixture(compressed, encrypted); const long length = (1L << 32) + 12345;
                long[] positions = [0, (1L << 31) - 11, (1L << 31) + 7, (1L << 32) - 9, (1L << 32) + 3, length - 1];
                using (var source = new FileStream(Path.Combine(f.Input, "huge.bin"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { SparseFile.Enable(source.SafeFileHandle); source.SetLength(length); for (int i = 0; i < positions.Length; i++) { source.Position = positions[i]; source.WriteByte((byte)(i + 41)); } source.Flush(true); }
                await f.Build(); using var set = AssetPackageSet.OpenDirectory(f.Output, f.ReadOptions with { MaxResidentIndexBytes = 0, PageCacheBytes = 196608 });
                Check.Equal(length, set.GetInfo("common/data/huge.bin").Length);
                for (int i = 0; i < positions.Length; i++) { byte[] b = new byte[1]; Check.Equal(1, await set.ReadAtAsync("common/data/huge.bin", positions[i], b)); Check.Equal((byte)(i + 41), b[0]); }
                using var stream = set.OpenRead("common/data/huge.bin"); stream.Seek(-1, SeekOrigin.End); Check.Equal(46, stream.ReadByte()); Check.Equal(-1, stream.ReadByte());
                using var mapped = AssetPackageSet.OpenDirectory(f.Output, f.ReadOptions with { ReadMode = AssetReadMode.MemoryMapped });
                byte[] tail = new byte[1]; Check.Equal(1, await mapped.ReadAtAsync("common/data/huge.bin", length - 1, tail)); Check.Equal((byte)46, tail[0]);
            });
        }
        int failed = 0; var total = System.Diagnostics.Stopwatch.StartNew();
        foreach (var test in tests)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try { await test.Body(); Console.WriteLine($"PASS {test.Name} ({watch.ElapsedMilliseconds} ms)"); }
            catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL {test.Name}\n{ex}"); }
        }
        Console.WriteLine($"{tests.Count - failed}/{tests.Count} passed in {total.Elapsed.TotalSeconds:F1}s"); return failed == 0 ? 0 : 1;
    }
}
