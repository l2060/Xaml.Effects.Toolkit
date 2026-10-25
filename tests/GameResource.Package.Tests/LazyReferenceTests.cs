using GameResource.Package.Internal;
using Xunit;

namespace GameResource.Package.Tests;

public sealed partial class PackageTests
{
    [Fact]
    public async Task LazyLoadsCoalesceWithIndependentMetadataAndBufferLeases()
    {
        using var package = Create();
        byte[] bytes = Data(100_000);
        using var input = new MemoryStream(bytes);
        package.WriteResource("a", input, new SpriteMetadata { Width = 42, Tags = ["original"] });
        int reads = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        package.BeforeResourceRead = async (_, token) =>
        {
            Interlocked.Increment(ref reads);
            await release.Task.WaitAsync(token);
        };
        var reference = package.GetResource<SpriteMetadata>("package://a");
        Assert.Equal("a", reference.Path);
        Assert.Equal(0, reads);
        Assert.Equal(0, package.GetStatistics().MetadataCacheEntries);
        var tasks = Enumerable.Range(0, 20).Select(_ => reference.LoadAsync().AsTask()).ToArray();
        Assert.Equal(1, reads);
        Assert.Throws<InvalidOperationException>(() => package.RemoveMetadata("a"));
        release.SetResult();
        var results = await Task.WhenAll(tasks);
        try
        {
            Assert.Equal(1, reads);
            results[0].Metadata!.Tags[0] = "changed";
            results[0].Dispose();
            for (int i = 1; i < results.Length; i++)
            {
                Assert.NotSame(results[0], results[i]);
                Assert.Equal("original", results[i].Metadata!.Tags[0]);
                Assert.Equal(bytes, results[i].Data.ToArray());
            }
            package.UpdateMetadata("a", new SpriteMetadata { Width = 99 });
            using var next = await reference.LoadAsync();
            Assert.Equal(99, next.Metadata!.Width);
            Assert.Equal(42, results[1].Metadata!.Width);
            Assert.Equal(2, reads);
        }
        finally { foreach (var result in results) result.Dispose(); }
    }

    [Fact]
    public async Task CancelOneWaiterDoesNotCancelOthersAndCancelLastWaiterReleasesReadLease()
    {
        using var package = Create();
        Put(package, "a", Data(100_000));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        package.BeforeResourceRead = async (_, token) => await release.Task.WaitAsync(token);
        var reference = package.GetResource("a");
        using var canceled = new CancellationTokenSource();
        Task<ResourceData<ResourceProperties>> first = reference.LoadAsync(canceled.Token).AsTask();
        Task<ResourceData<ResourceProperties>> second = reference.LoadAsync().AsTask();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(second.IsCompleted);
        release.SetResult();
        using var result = await second;
        Assert.Equal(Data(100_000), result.Data.ToArray());

        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        package.BeforeResourceRead = async (_, token) => await blocked.Task.WaitAsync(token);
        using var allCanceled = new CancellationTokenSource();
        var third = reference.LoadAsync(allCanceled.Token).AsTask();
        allCanceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => third);
        // Cancellation completed only after the final in-flight read released its lease.
        package.UpdateMetadata("a", new ResourceProperties(("ready", true)));
        blocked.SetResult();
        using var retry = await reference.LoadAsync();
        Assert.True(retry.Metadata!.GetBoolean("ready"));
    }

    [Fact]
    public async Task LazyReferenceFollowsItsPathAndRetriesAfterFailure()
    {
        var package = Create();
        try
        {
            Put(package, "a", [1]);
            var reference = package.GetResource("a");
            package.BeforeResourceRead = (_, _) => throw new IOException("Injected read failure.");
            await Assert.ThrowsAsync<IOException>(async () => await reference.LoadAsync());
            package.BeforeResourceRead = null;
            using (var retry = await reference.LoadAsync()) Assert.Equal(new byte[] { 1 }, retry.Data.ToArray());
            package.Move("a", "b");
            await Assert.ThrowsAsync<FileNotFoundException>(async () => await reference.LoadAsync());
            Put(package, "a", [2]);
            using (var replacement = await reference.LoadAsync()) Assert.Equal(new byte[] { 2 }, replacement.Data.ToArray());
            package.Dispose();
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await reference.LoadAsync());
        }
        finally { package.Dispose(); }
    }

    [Fact]
    public async Task BufferedResultOutlivesPackageAndCombinedReadSeesOneRevision()
    {
        var package = Create(true);
        try
        {
            Put(package, "a", [1, 2], properties: """{"width":1}""");
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            package.BeforeResourceRead = async (_, token) => await release.Task.WaitAsync(token);
            var pending = package.ReadResourceAsync("a").AsTask();
            Assert.Throws<InvalidOperationException>(() => package.UpdateMetadata("a", new ResourceProperties(("width", 2))));
            Assert.Throws<InvalidOperationException>(() => package.Dispose());
            release.SetResult();
            using var data = await pending;
            package.Dispose();
            Assert.Equal(new byte[] { 1, 2 }, data.Data.ToArray());
            Assert.Equal(1, data.Metadata!.GetInt32("width"));
            using var independent = data.Share();
            data.Dispose();
            Assert.Equal(new byte[] { 1, 2 }, independent.Data.ToArray());
        }
        finally { package.Dispose(); }
    }

    [Theory]
    [InlineData((int)CommitStage.DataFlushed, false)]
    [InlineData((int)CommitStage.FirstHeaderFlushed, true)]
    [InlineData((int)CommitStage.BothHeadersFlushed, true)]
    public void CommitRecoveryPreservesMatchingMetadataAndData(int stage, bool committed)
    {
        using (var package = Create(true))
        {
            Put(package, "a", [1], properties: """{"width":1}""");
            package.Save();
            Put(package, "a", [2], properties: """{"width":2}""");
            package.CommitObserver = current => { if ((int)current == stage) throw new IOException("Injected failure."); };
            Assert.Throws<IOException>(() => package.Save());
        }
        using var reopened = ResourcePackage.OpenEdit(PathFor(), Options(true));
        using var resource = reopened.ReadResource("a");
        Assert.Equal(committed ? 2 : 1, resource.Metadata!.GetInt32("width"));
        Assert.Equal(new byte[] { committed ? (byte)2 : (byte)1 }, resource.Data.ToArray());
        reopened.Verify();
        reopened.UpdateMetadata("a", new ResourceProperties(("width", 3)));
        reopened.Save();
        reopened.Verify();
    }
}
