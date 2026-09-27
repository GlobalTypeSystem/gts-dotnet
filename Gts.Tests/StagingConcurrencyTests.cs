using System.Text.Json.Nodes;
using Gts.Extraction;
using Gts.Store;

namespace Gts.Tests;

/// <summary>
/// Concurrency test for the staging overlay a validate=true batch relies on: an
/// entity that is staged but not committed (e.g. a batch entry that fails
/// validation and is discarded) must never be observable through the committed
/// read path. Several probe tasks hammer the committed read for a
/// deliberately-invalid id while a writer stages the whole batch and then
/// commits the valid entries but discards the invalid one - mirroring the batch
/// handler's outcome. Repeated over many cycles. <see cref="InMemoryGtsStore"/>
/// serializes map access with a lock, so the probes run genuinely concurrently
/// with the writer.
/// </summary>
public class StagingConcurrencyTests
{
    private const string Draft7 = "http://json-schema.org/draft-07/schema#";

    private static GtsJsonEntity Schema(string typeId)
    {
        var json = $$"""{"$$id":"gts://{{typeId}}","$$schema":"{{Draft7}}","type":"object"}""";
        return GtsJsonEntity.ExtractEntity(JsonNode.Parse(json)!.AsObject());
    }

    private static GtsJsonEntity Schema(string typeId, string title)
    {
        var json = $$"""{"$$id":"gts://{{typeId}}","$$schema":"{{Draft7}}","type":"object","title":"{{title}}"}""";
        return GtsJsonEntity.ExtractEntity(JsonNode.Parse(json)!.AsObject());
    }

    // Two stages that resolve to the same id must each get their own token, and a commit must never
    // silently overwrite different committed content - it reports a conflict instead. This covers both a
    // duplicated id inside one validate=true batch and two concurrent batches racing on the same id.
    [Fact]
    public async Task Staging_the_same_id_twice_yields_distinct_tokens_and_commit_does_not_overwrite()
    {
        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        var id = "gts.x.dotnetdup._.t.v1~";

        var tokenA = await registry.StageAsync(Schema(id, "a"));
        var tokenB = await registry.StageAsync(Schema(id, "b"));
        Assert.NotEqual(tokenA, tokenB);

        // First commit publishes A.
        Assert.Equal(GtsSaveOutcome.Added, await registry.CommitStagedAsync(tokenA));
        // Committing B (different content for the same id) must conflict, not clobber A.
        Assert.Equal(GtsSaveOutcome.Conflict, await registry.CommitStagedAsync(tokenB));

        var committed = await registry.GetByInstanceIdAsync(id);
        Assert.NotNull(committed);
        Assert.Equal("a", committed!.Content["title"]?.GetValue<string>());
    }

    // Committing the same content twice (e.g. an idempotent duplicate) is Unchanged, never a conflict.
    [Fact]
    public async Task Committing_identical_staged_content_twice_is_unchanged()
    {
        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        var id = "gts.x.dotnetdup._.same.v1~";

        var tokenA = await registry.StageAsync(Schema(id, "x"));
        var tokenB = await registry.StageAsync(Schema(id, "x"));
        Assert.Equal(GtsSaveOutcome.Added, await registry.CommitStagedAsync(tokenA));
        Assert.Equal(GtsSaveOutcome.Unchanged, await registry.CommitStagedAsync(tokenB));
    }

    // Discarding one staged entry must not affect another entry that happens to share the same id.
    [Fact]
    public async Task Discarding_one_token_leaves_another_staged_entry_for_the_same_id()
    {
        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        var id = "gts.x.dotnetdup._.iso.v1~";

        var tokenA = await registry.StageAsync(Schema(id, "keep"));
        var tokenB = await registry.StageAsync(Schema(id, "drop"));
        await registry.DiscardStagedAsync(tokenB);

        // A survives and can still be committed.
        Assert.Equal(GtsSaveOutcome.Added, await registry.CommitStagedAsync(tokenA));
        var committed = await registry.GetByInstanceIdAsync(id);
        Assert.Equal("keep", committed!.Content["title"]?.GetValue<string>());
    }

    [Fact]
    public async Task Staged_entity_is_never_visible_to_concurrent_committed_reads()
    {
        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        const int cycles = 10;
        const int probes = 4;
        const int validPerBatch = 60;
        long leaks = 0;

        for (var cycle = 0; cycle < cycles; cycle++)
        {
            var ns = $"gts.x.dotnetconc{cycle}._";
            var invalidId = $"{ns}.invalid.v1~";
            var validIds = Enumerable.Range(0, validPerBatch).Select(i => $"{ns}.t{i}.v1~").ToList();

            using var cts = new CancellationTokenSource();
            var probers = Enumerable.Range(0, probes).Select(_ => Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    // The invalid entry must NEVER be observable through the committed read path.
                    if (await registry.GetByInstanceIdAsync(invalidId) is not null)
                        Interlocked.Increment(ref leaks);
                }
            })).ToArray();

            // Two-phase, mirroring the batch handler: stage every entry (invisible to
            // public reads), then commit the valid ids and discard the invalid one.
            var staged = new List<(string Key, bool Valid)>();
            foreach (var id in validIds)
                staged.Add((await registry.StageAsync(Schema(id)), true));
            staged.Add((await registry.StageAsync(Schema(invalidId)), false));

            foreach (var (key, valid) in staged)
            {
                if (valid)
                    await registry.CommitStagedAsync(key);
                else
                    await registry.DiscardStagedAsync(key);
            }

            cts.Cancel();
            await Task.WhenAll(probers);

            Assert.Null(await registry.GetByInstanceIdAsync(invalidId));
            Assert.NotNull(await registry.GetByInstanceIdAsync(validIds[0]));
        }

        Assert.Equal(0, Interlocked.Read(ref leaks));
    }
}
