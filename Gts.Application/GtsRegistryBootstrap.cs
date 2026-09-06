using System.Text.Json;
using System.Text.Json.Nodes;
using Gts.Extraction;
using Gts.Store;

namespace Gts.Application;

/// <summary>Loads JSON entities from disk into an in-memory registry (similar to <c>gts-go</c> <c>-path</c>).</summary>
public static class GtsRegistryBootstrap
{
    private static readonly HashSet<string> ExcludeDirs =
        new(StringComparer.OrdinalIgnoreCase) { "node_modules", "dist", "build" };

    private static readonly HashSet<string> AllowedExt =
        new(StringComparer.OrdinalIgnoreCase) { ".json", ".jsonc", ".gts" };

    /// <summary>Splits a comma-separated path list and expands leading <c>~/</c>.</summary>
    public static IReadOnlyList<string> ParsePathSpec(string? pathSpec)
    {
        if (string.IsNullOrWhiteSpace(pathSpec))
            return Array.Empty<string>();

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var parts = pathSpec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var paths = new List<string>();
        foreach (var p in parts)
        {
            if (string.IsNullOrEmpty(p))
                continue;
            if (p.StartsWith("~/", StringComparison.Ordinal) || p.StartsWith("~\\", StringComparison.Ordinal))
            {
                if (!string.IsNullOrEmpty(home))
                    paths.Add(Path.Combine(home, p[2..]));
                else
                    paths.Add(p);
            }
            else
            {
                paths.Add(p);
            }
        }

        return paths;
    }

    /// <summary>Reads optional GTS config JSON (<c>entity_id_fields</c>, <c>schema_id_fields</c>) into <see cref="GtsExtractOptions"/>.</summary>
    public static GtsExtractOptions LoadExtractOptionsFromConfig(string? configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            return GtsExtractOptions.Default;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = doc.RootElement;
            string[]? entityFields = null;
            string[]? schemaFields = null;
            if (root.TryGetProperty("entity_id_fields", out var e) && e.ValueKind == JsonValueKind.Array)
                entityFields = e.EnumerateArray().Select(x => x.GetString()).OfType<string>().ToArray();
            if (root.TryGetProperty("schema_id_fields", out var s) && s.ValueKind == JsonValueKind.Array)
                schemaFields = s.EnumerateArray().Select(x => x.GetString()).OfType<string>().ToArray();

            if (entityFields is null && schemaFields is null)
                return GtsExtractOptions.Default;

            return new GtsExtractOptions
            {
                EntityIdPropertyNames = entityFields ?? GtsExtractOptions.Default.EntityIdPropertyNames,
                SchemaIdPropertyNames = schemaFields ?? GtsExtractOptions.Default.SchemaIdPropertyNames
            };
        }
        catch
        {
            return GtsExtractOptions.Default;
        }
    }

    /// <summary>Recursively collects <c>.json</c>, <c>.jsonc</c>, and <c>.gts</c> files from paths (files or directories).</summary>
    public static IReadOnlyList<string> CollectJsonFiles(IEnumerable<string> paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var collected = new List<string>();

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            string abs;
            try
            {
                abs = Path.GetFullPath(path);
            }
            catch
            {
                continue;
            }

            if (Directory.Exists(abs))
                WalkDir(abs, collected, seen);
            else if (File.Exists(abs) && AllowedExt.Contains(Path.GetExtension(abs)))
                TryAddFile(abs, collected, seen);
        }

        return collected;
    }

    /// <summary>Parses JSON files and merges entities into <paramref name="registry"/> (skips broken files).</summary>
    public static async Task LoadIntoRegistryAsync(
        GtsRegistry registry,
        string? pathSpec,
        GtsExtractOptions? extractOptions = null,
        CancellationToken cancellationToken = default)
    {
        var paths = ParsePathSpec(pathSpec);
        if (paths.Count == 0)
            return;

        var files = CollectJsonFiles(paths);
        var opt = extractOptions ?? GtsExtractOptions.Default;
        var docOptions = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Allow,
            AllowTrailingCommas = true
        };

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(file);
            JsonNode? root;
            try
            {
                using var doc = await JsonDocument.ParseAsync(stream, docOptions, cancellationToken).ConfigureAwait(false);
                root = JsonNode.Parse(doc.RootElement.GetRawText());
            }
            catch
            {
                continue;
            }

            switch (root)
            {
                case JsonArray arr:
                    foreach (var item in arr)
                    {
                        if (item is JsonObject jo)
                        {
                            var clone = (JsonObject)jo.DeepClone()!;
                            GtsJsonKeyNormalizer.Apply(clone);
                            await GtsEntityOperations.TryAddAsync(registry, clone, validate: false, opt, cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }

                    break;
                case JsonObject obj:
                {
                    var clone = (JsonObject)obj.DeepClone()!;
                    GtsJsonKeyNormalizer.Apply(clone);
                    await GtsEntityOperations.TryAddAsync(registry, clone, validate: false, opt, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                }
            }
        }
    }

    private static void WalkDir(string dir, List<string> collected, HashSet<string> seen)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
        {
            if (Directory.Exists(entry))
            {
                var name = Path.GetFileName(entry);
                if (ExcludeDirs.Contains(name))
                    continue;
                WalkDir(entry, collected, seen);
            }
            else if (File.Exists(entry) && AllowedExt.Contains(Path.GetExtension(entry)))
            {
                TryAddFile(entry, collected, seen);
            }
        }
    }

    private static void TryAddFile(string filePath, List<string> collected, HashSet<string> seen)
    {
        try
        {
            var real = Path.GetFullPath(filePath);
            if (seen.Add(real))
                collected.Add(real);
        }
        catch
        {
            if (seen.Add(filePath))
                collected.Add(filePath);
        }
    }
}
