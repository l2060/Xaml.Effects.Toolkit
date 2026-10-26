namespace Game.Asset.Cli.Build;

internal sealed record BuildOptions
{
    internal int Jobs { get; init; } = Math.Min(Environment.ProcessorCount, 8);
    internal int IoConcurrency { get; init; } = 4;
    internal int WriteConcurrency { get; init; } = 1;
    internal int MemoryMiB { get; init; } = 128;
    internal int BlockSize { get; init; } = 256 * 1024;
    internal void Validate()
    {
        if (Jobs is < 1 or > 256 || IoConcurrency is < 1 or > 256 || WriteConcurrency is < 1 or > 64 || MemoryMiB is < 8 or > 65536 ||
            BlockSize is < 65536 or > 1048576 || (BlockSize & (BlockSize - 1)) != 0)
            throw new ArgumentException("Invalid build concurrency, memory or block-size setting.");
        if (((long)Jobs * 4 + IoConcurrency + 4) * BlockSize > (long)MemoryMiB * 1024 * 1024)
            throw new ArgumentException("Memory budget is too small for the selected jobs, I/O concurrency and block size.");
    }
}
