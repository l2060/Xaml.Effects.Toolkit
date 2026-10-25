using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Compression.Benchmarks;

internal sealed record InputFile(string Path, long Bytes, string Sha256);
internal sealed record Corpus(string Name, string Description, byte[] Raw, int? Width, int? Height, InputFile[] Inputs)
{
    internal object Describe() => new
    {
        Name, Description, RawBytes = Raw.Length, Width, Height,
        RawSha256 = Convert.ToHexString(SHA256.HashData(Raw)), Inputs
    };
}

internal static class Corpora
{
    internal static Corpus[] Load(string root) =>
    [
        Source(root), Json(),
        Image(root, "AssetsEditor/Assets/Icon.png", "image.icon"),
        Image(root, "AssetsEditor/Assets/Images/background.png", "image.editor_background"),
        Image(root, "Xaml.Effect.Demo/Assets/Images/background.png", "image.demo_background"),
        Sprites(), Texture()
    ];

    private static Corpus Source(string root)
    {
        string[] directories = ["GameResource.Package", "AssetsEditor", "src"];
        var files = directories.SelectMany(directory => Directory.EnumerateFiles(Path.Combine(root, directory), "*", SearchOption.AllDirectories))
            .Where(path => (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                && !path.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj"))
            .Order(StringComparer.Ordinal).ToArray();
        using var raw = new MemoryStream();
        var inputs = new List<InputFile>();
        foreach (string path in files)
        {
            byte[] bytes = File.ReadAllBytes(path);
            inputs.Add(DescribeInput(root, path, bytes));
            raw.Write(bytes);
            raw.WriteByte((byte)'\n');
        }
        return new("text.source", "Real repository C# and XAML, concatenated once with newlines, no padding/repetition.", raw.ToArray(), null, null, inputs.ToArray());
    }

    private static Corpus Json()
    {
        using var raw = new MemoryStream();
        using (var json = new Utf8JsonWriter(raw, new() { Indented = true }))
        {
            json.WriteStartArray();
            var random = new Random(20261025);
            for (int i = 0; i < 8192; i++)
            {
                json.WriteStartObject();
                json.WriteNumber("id", i);
                json.WriteString("path", $"game/monster/m{i % 173:D3}/stand/{i % 12}.png");
                json.WriteString("name", $"怪物资源_{i:D5}");
                json.WriteNumber("width", random.Next(32, 513));
                json.WriteNumber("height", random.Next(32, 513));
                json.WriteNumber("offsetX", random.Next(-128, 129));
                json.WriteNumber("offsetY", random.Next(-128, 129));
                json.WriteNumber("health", random.Next(100, 100000));
                json.WriteBoolean("enabled", i % 7 != 0);
                json.WriteStartArray("tags"); json.WriteStringValue("monster"); json.WriteStringValue(i % 2 == 0 ? "fire" : "ice"); json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
        }
        return new("text.json", "Synthetic UTF-8 indented JSON with 8192 varied resource records, seed 20261025.", raw.ToArray(), null, null, []);
    }

    private static Corpus Image(string root, string relativePath, string name)
    {
        string path = Path.Combine(root, relativePath);
        byte[] encoded = File.ReadAllBytes(path);
        using var input = new MemoryStream(encoded, false);
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var image = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        int stride = checked(image.PixelWidth * 4);
        byte[] pixels = new byte[checked(stride * image.PixelHeight)];
        image.CopyPixels(pixels, stride, 0);
        return new(name, "Real repository PNG decoded to straight BGRA32 before timing; no PNG headers/filtering in tested bytes.",
            pixels, image.PixelWidth, image.PixelHeight, [DescribeInput(root, path, encoded)]);
    }

    private static Corpus Sprites()
    {
        const int size = 1024;
        byte[] pixels = new byte[size * size * 4];
        // A 16 x 16 atlas of 64 x 64 transparent, palette-colored sprite silhouettes.
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int tile = (y / 64) * 16 + x / 64, u = x % 64 - 32, v = y % 64 - 32;
            int radius = 19 + tile % 6;
            if (u * u + v * v > radius * radius) continue;
            int shade = ((u + 32) / 8 + (v + 32) / 8 + tile) % 4;
            int offset = (y * size + x) * 4;
            pixels[offset] = (byte)(40 + tile % 5 * 25 + shade * 8);
            pixels[offset + 1] = (byte)(55 + tile % 7 * 18 + shade * 9);
            pixels[offset + 2] = (byte)(60 + tile % 6 * 21 + shade * 7);
            pixels[offset + 3] = u * u + v * v > (radius - 1) * (radius - 1) ? (byte)128 : (byte)255;
        }
        return new("image.synthetic_sprites", "Synthetic transparent palette sprite atlas; not sampled from a shipped game.", pixels, size, size, []);
    }

    private static Corpus Texture()
    {
        const int size = 1024;
        byte[] pixels = new byte[size * size * 4];
        var random = new Random(20261025);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int offset = (y * size + x) * 4;
            int baseColor = 65 + (int)(30 * Math.Sin(x / 71d) + 20 * Math.Cos(y / 47d));
            pixels[offset] = (byte)Math.Clamp(baseColor + random.Next(-24, 25), 0, 255);
            pixels[offset + 1] = (byte)Math.Clamp(baseColor + 40 + random.Next(-24, 25), 0, 255);
            pixels[offset + 2] = (byte)Math.Clamp(baseColor + 20 + random.Next(-24, 25), 0, 255);
            pixels[offset + 3] = 255;
        }
        return new("image.synthetic_texture", "Synthetic opaque gradient with independent RGB noise, seed 20261025; no row predictor/filter.", pixels, size, size, []);
    }

    private static InputFile DescribeInput(string root, string path, byte[] bytes) =>
        new(Path.GetRelativePath(root, path).Replace('\\', '/'), bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
}
