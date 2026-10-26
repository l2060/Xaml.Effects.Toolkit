using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Game.Asset.Cli.Build;
using Game.Asset.Cli.Configuration;
using Game.Asset.Cli.Tags;
using Game.Asset.Package;
using Game.Asset.Package.Internal;

internal static class Check
{
    internal static void True(bool value, string? message = null) { if (!value) throw new Exception(message ?? "Assertion failed."); }
    internal static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}."); }
    internal static void Bytes(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual) => True(expected.SequenceEqual(actual), "Byte sequences differ.");
    internal static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }
    internal static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }
}

internal sealed class Fixture : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "asset-tests-" + Guid.NewGuid().ToString("N"));
    internal string Input => Path.Combine(Root, "input");
    internal string Output => Path.Combine(Root, "dist");
    internal string ProjectPath => Path.Combine(Root, "asset-project.yml");
    private readonly string _variable = "ASSET_TEST_KEY_" + Guid.NewGuid().ToString("N");
    internal AssetReadOptions ReadOptions { get; }
    internal BuildOptions BuildOptions { get; } = new() { Jobs = 4, IoConcurrency = 4 };
    internal Fixture(bool compression = true, bool encryption = false)
    {
        Directory.CreateDirectory(Input); Environment.SetEnvironmentVariable(_variable, "test password; secret");
        ReadOptions = new() { Password = encryption ? "test password; secret" : null };
        File.WriteAllText(ProjectPath, $"version: 1\noutput: ./dist\ncompression: {compression.ToString().ToLowerInvariant()}\nencryption:\n  enabled: {encryption.ToString().ToLowerInvariant()}\n  key-environment: {_variable}\npackages:\n  - name: main\n    sources:\n      - directory: ./input\n        prefix: common/data\n");
    }
    internal ProjectConfig Config() => ProjectConfig.Load(ProjectPath);
    internal Task<BuildTag> Build(Guid? baseline = null) => ProjectBuilder.BuildAsync(Config(), baseline, BuildOptions, default);
    internal void Put(string path, byte[] bytes) { string full = Path.Combine(Input, path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllBytes(full, bytes); }
    internal void Text(string path, string text) => Put(path, AssetPath.Utf8.GetBytes(text));
    internal static void RewriteHeader(string file, Func<PackageHeader, PackageHeader> change)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        byte[] bytes = new byte[4096]; stream.ReadExactly(bytes); var h = PackageHeader.Decode(bytes, stream.Length);
        byte[] key = BlockCipher.DeriveKey(h, null); stream.Position = 0; stream.Write(change(h).Encode(key)); CryptographicOperations.ZeroMemory(key);
    }
    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_variable, null);
        string root = Path.GetFullPath(Root), temp = Path.GetFullPath(Path.GetTempPath());
        if (!ProjectConfig.Within(root, temp) || !Path.GetFileName(root).StartsWith("asset-tests-", StringComparison.Ordinal)) throw new Exception("Unsafe test cleanup.");
        Directory.Delete(root, true);
    }
}
internal static class SparseFile
{
    internal static void Enable(SafeFileHandle handle)
    { if (OperatingSystem.IsWindows() && !DeviceIoControl(handle, 0x900c4, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint control, IntPtr input, uint inputSize, IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);
}
