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

    // Session isolation (regression for the cross-request staging leak): a snapshot taken for one staging
    // session must overlay only that session's staged entries, never another session's unvalidated ones, and
    // a null session must be committed-only. Without this, one batch could resolve a $ref/parent against
    // another batch's not-yet-validated entry and commit a dangling reference.
    [Fact]
    public async Task Snapshot_only_overlays_its_own_staging_session()
    {
        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        var idA = "gts.x.dotnetsess._.a.v1~";
        var idB = "gts.x.dotnetsess._.b.v1~";
        const string sessionA = "session-a";
        const string sessionB = "session-b";

        await registry.StageAsync(Schema(idA, "a"), sessionA);
        await registry.StageAsync(Schema(idB, "b"), sessionB);

        static bool Has(IReadOnlyList<GtsJsonEntity> snap, string id) =>
            snap.Any(e => string.Equals(GtsJsonEntity.ExtractId(e.Content).Id, id, StringComparison.Ordinal));

        var snapA = await registry.SnapshotForReadAsync(sessionA);
        Assert.True(Has(snapA, idA));
        Assert.False(Has(snapA, idB)); // B's staged entry must not leak into A's validation

        var snapB = await registry.SnapshotForReadAsync(sessionB);
        Assert.True(Has(snapB, idB));
        Assert.False(Has(snapB, idA));

        var committedOnly = await registry.SnapshotForReadAsync();
        Assert.False(Has(committedOnly, idA));
        Assert.False(Has(committedOnly, idB));
    }

    // Atomic batch publication (regression for the non-atomic commit loop): if any target in the survivor set
    // conflicts with committed content, the whole set is published as nothing - a dependent is never committed
    // against a target whose content changed after validation.
    [Fact]
    public async Task CommitStagedBatch_publishes_nothing_when_any_target_conflicts()
    {
        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        var conflictId = "gts.x.dotnetatomic._.parent.v1~";
        var dependentId = "gts.x.dotnetatomic._.child.v1~";
        const string session = "batch-1";

        // A concurrent writer already committed the parent id with different content.
        Assert.Equal(GtsSaveOutcome.Added, await registry.TrySaveAsync(Schema(conflictId, "committed")));

        var conflictToken = await registry.StageAsync(Schema(conflictId, "staged"), session);
        var dependentToken = await registry.StageAsync(Schema(dependentId, "ok"), session);

        var outcomes = await registry.CommitStagedBatchAsync(new[] { dependentToken, conflictToken });
        Assert.Equal(GtsSaveOutcome.Conflict, outcomes[0]);
        Assert.Equal(GtsSaveOutcome.Conflict, outcomes[1]);

        // Nothing from the batch was published: the dependent is absent and the parent keeps its committed content.
        Assert.Null(await registry.GetByInstanceIdAsync(dependentId));
        var parent = await registry.GetByInstanceIdAsync(conflictId);
        Assert.Equal("committed", parent!.Content["title"]?.GetValue<string>());
    }

    // The atomic batch commit publishes the entire dependency set when no target conflicts.
    [Fact]
    public async Task CommitStagedBatch_publishes_all_when_no_conflict()
    {
        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        var idA = "gts.x.dotnetatomic2._.a.v1~";
        var idB = "gts.x.dotnetatomic2._.b.v1~";
        const string session = "batch-2";

        var tokenA = await registry.StageAsync(Schema(idA, "a"), session);
        var tokenB = await registry.StageAsync(Schema(idB, "b"), session);

        var outcomes = await registry.CommitStagedBatchAsync(new[] { tokenA, tokenB });
        Assert.Equal(GtsSaveOutcome.Added, outcomes[0]);
        Assert.Equal(GtsSaveOutcome.Added, outcomes[1]);

        Assert.NotNull(await registry.GetByInstanceIdAsync(idA));
        Assert.NotNull(await registry.GetByInstanceIdAsync(idB));
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
