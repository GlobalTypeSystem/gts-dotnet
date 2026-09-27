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
