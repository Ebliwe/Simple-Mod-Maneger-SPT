using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace SPTModManager;

public sealed class SharedBuild
{
    public int FormatVersion { get; set; } = 1;
    public string SptVersion { get; set; } = "4.0.13";
    public string Name { get; set; } = "SPT сборка";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<SharedBuildMod> Mods { get; set; } = new();
}

public sealed class SharedBuildMod
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
}

public sealed class InstalledModRecord
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime InstalledUtc { get; set; } = DateTime.UtcNow;
    // Relative file paths written by this mod installation. Used to detect manual deletion.
    public List<string> InstalledFiles { get; set; } = new();
}

public static class BuildShareCodec
{
    private const string Prefix = "SPTB1:";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public static string Encode(SharedBuild build)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(build, JsonOptions);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(json, 0, json.Length);
        return Prefix + Convert.ToBase64String(output.ToArray())
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static SharedBuild Decode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidDataException("Код сборки пустой.");
        string normalized = new string(code.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (!normalized.StartsWith(Prefix, StringComparison.Ordinal))
            throw new InvalidDataException("Неверный формат кода сборки. Код должен начинаться с SPTB1:");

        string payload = normalized[Prefix.Length..].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        byte[] compressed;
        try { compressed = Convert.FromBase64String(payload); }
        catch (FormatException) { throw new InvalidDataException("Код сборки повреждён или скопирован не полностью."); }

        if (compressed.Length > 2_000_000)
            throw new InvalidDataException("Код сборки слишком большой.");

        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        if (output.Length > 10_000_000)
            throw new InvalidDataException("Содержимое сборки превышает допустимый размер.");

        var build = JsonSerializer.Deserialize<SharedBuild>(output.ToArray(), JsonOptions)
            ?? throw new InvalidDataException("Не удалось прочитать сборку.");
        if (build.FormatVersion != 1)
            throw new InvalidDataException("Эта версия формата сборки не поддерживается.");
        if (build.Mods.Count == 0 || build.Mods.Count > 500)
            throw new InvalidDataException("В сборке нет модов или их количество превышает 500.");
        if (build.Mods.Any(m => m.Id <= 0 || string.IsNullOrWhiteSpace(m.Version)))
            throw new InvalidDataException("В коде есть мод с некорректным ID или версией.");
        build.Mods = build.Mods.GroupBy(m => m.Id).Select(g => g.First()).ToList();
        return build;
    }
}
