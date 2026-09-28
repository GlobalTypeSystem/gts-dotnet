using Gts.Extraction;

namespace Gts.Store;

/// <summary>Outcome of an atomic conditional save (<see cref="IGtsStore.TrySaveAsync"/>).</summary>
public enum GtsSaveOutcome
{
    /// <summary>The entity was inserted (no prior entity with the same id).</summary>
    Added,

    /// <summary>An entity with the same id already existed with identical content; the store was left unchanged.</summary>
    Unchanged,

    /// <summary>An entity with the same id already existed with different content; the store was left unchanged.</summary>
    Conflict
}

/// <summary>Storage abstraction for GTS JSON entities keyed by GTS ID.</summary>
public interface IGtsStore
{
    /// <summary>Stores or overwrites the entity (keyed by its GTS ID).</summary>
    ValueTask SaveAsync(GtsJsonEntity entity);

    /// <summary>
    /// Atomically stores the entity unless an entity with the same id already exists with different content.
    /// This is a single compare-and-swap that closes the check-then-save race: concurrent callers writing
    /// different content for the same id cannot both succeed.
    /// </summary>
    ValueTask<GtsSaveOutcome> TrySaveAsync(GtsJsonEntity entity);

    /// <summary>Retrieves an entity by GTS ID, or null if not found.</summary>
    ValueTask<GtsJsonEntity?> GetAsync(GtsId id);

    /// <summary>
    /// Looks up an instance by GTS instance id or by an opaque id (e.g. UUID for anonymous instances).
    /// </summary>
    ValueTask<GtsJsonEntity?> GetByInstanceIdAsync(string instanceId);

    /// <summary>Returns all stored entities, including instances without a <see cref="GtsJsonEntity.GtsId"/> (e.g. opaque ids).</summary>
    ValueTask<IList<GtsJsonEntity>> GetAllAsync();

    /// <summary>
    /// Returns a read-only snapshot of all stored entities <strong>without</strong> deep-cloning their content,
    /// for internal read-only consumers (e.g. validation). Committed entities are overlaid with the staging
    /// entries of the <strong>single staging session</strong> named by <paramref name="stagingSessionId"/> (see
    /// <see cref="StageAsync"/>) so a schema being validated as part of that batch can resolve its
    /// not-yet-committed siblings. When <paramref name="stagingSessionId"/> is <c>null</c> the overlay is empty
    /// and the snapshot is committed-only, so single-entity/standalone validation never observes another
    /// request's unvalidated staged entries. The returned entities and their <see cref="GtsJsonEntity.Content"/>
    /// must not be mutated. Prefer this over <see cref="GetAllAsync"/> on hot paths that only read.
    /// </summary>
    ValueTask<IReadOnlyList<GtsJsonEntity>> SnapshotForReadAsync(string? stagingSessionId = null);

    /// <summary>
    /// Like <see cref="SnapshotForReadAsync"/> but committed-only: the staging overlay is never
    /// included. This is the read path for public/API consumers (e.g. <c>/query</c>), so a
    /// staged-but-not-yet-committed entity is never exposed.
    /// </summary>
    ValueTask<IReadOnlyList<GtsJsonEntity>> SnapshotCommittedForReadAsync();

    /// <summary>
    /// Stages an entity WITHOUT publishing it, returning a <strong>unique staging token</strong> that
    /// identifies this staged entry (never its registry key). The entry is associated with the staging
    /// <paramref name="stagingSessionId"/> (a batch id); when omitted, the entry gets its own private session
    /// so it is visible to no other snapshot. A staged entity is visible to internal validation only via
    /// <see cref="SnapshotForReadAsync"/> called with the <em>same</em> session id (so a batch resolves its own
    /// intra-batch references regardless of order, and never sees another request's staged entries) and is
    /// invisible to public reads (<see cref="GetAsync"/>, <see cref="GetByInstanceIdAsync"/>,
    /// <see cref="GetAllAsync"/>, <see cref="SnapshotCommittedForReadAsync"/>) until it is committed.
    /// Because each stage yields its own token, two entries that resolve to the same id - whether from a
    /// duplicated batch entry or two concurrent batches - never clobber each other's staged content, and a
    /// commit/discard only ever affects the entry it names. Callers MUST eventually commit or discard the token.
    /// </summary>
    ValueTask<string> StageAsync(GtsJsonEntity entity, string? stagingSessionId = null);

    /// <summary>
    /// Publishes a previously staged entity by its staging <paramref name="token"/>, making it visible to
    /// public reads. The publish is atomic with a conflict check against the committed store (the same
    /// compare-and-swap as <see cref="TrySaveAsync"/>): <see cref="GtsSaveOutcome.Added"/> when it was
    /// inserted, <see cref="GtsSaveOutcome.Unchanged"/> when an identical entity already held the id, and
    /// <see cref="GtsSaveOutcome.Conflict"/> when a different entity already held the id (nothing is
    /// published in that case). An unknown/already-resolved token yields <see cref="GtsSaveOutcome.Conflict"/>.
    /// </summary>
    ValueTask<GtsSaveOutcome> CommitStagedAsync(string token);

    /// <summary>
    /// Atomically publishes a whole set of staged <paramref name="tokens"/> as one all-or-nothing unit: every
    /// token's target is checked against the committed store (and against its batch siblings) under a single
    /// lock, and if <em>any</em> target would conflict - a concurrent commit won the id with different content,
    /// an intra-batch duplicate disagrees, or a token is unknown - <strong>nothing</strong> is published. This
    /// closes the gap where a per-entry commit loop could publish a dependent schema against a parent or
    /// <c>$ref</c> target whose content changed after the dependent was validated. Returns one
    /// <see cref="GtsSaveOutcome"/> per token, positionally aligned with <paramref name="tokens"/>; on a batch
    /// conflict the non-conflicting entries report <see cref="GtsSaveOutcome.Conflict"/> as well because the
    /// batch as a whole was not published. Committed tokens are removed from the staging area; on conflict the
    /// tokens remain staged for the caller to discard.
    /// </summary>
    ValueTask<IReadOnlyList<GtsSaveOutcome>> CommitStagedBatchAsync(IReadOnlyList<string> tokens);

    /// <summary>Drops a staged entity by its staging token. The committed state is untouched.</summary>
    ValueTask DiscardStagedAsync(string token);

    /// <summary>Returns the number of stored entities.</summary>
    ValueTask<int> CountAsync();
}
