using LiteDB;
using Foundry.Models;

namespace Foundry.Services;

/// <summary>
/// Manages a single LiteDB database instance for all Foundry ML pipeline persistence.
/// </summary>
public sealed class FoundryDatabase : IDisposable
{
    private readonly LiteDatabase _db;
    private bool _disposed;

    /// <summary>
    /// Opens (or creates) the LiteDB database at <c>&lt;stateRootPath&gt;/foundry.db</c>.
    /// The directory is created if it does not exist.
    /// </summary>
    /// <param name="stateRootPath">
    /// Root of the Foundry state directory. When empty or whitespace, defaults to
    /// <c>%LOCALAPPDATA%/Foundry</c> (Windows) or the platform equivalent.
    /// </param>
    public FoundryDatabase(string stateRootPath)
    {
        var root = string.IsNullOrWhiteSpace(stateRootPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Foundry"
            )
            : Path.GetFullPath(stateRootPath);
        Directory.CreateDirectory(root);

        var dbPath = Path.Combine(root, "foundry.db");
        _db = new LiteDatabase($"Filename={dbPath};Connection=shared");

        EnsureIndexes();
    }

    /// <summary>LiteDB collection for practice attempt records.</summary>
    public ILiteCollection<TrainingAttemptRecord> PracticeAttempts =>
        _db.GetCollection<TrainingAttemptRecord>("training_practice_attempts");

    /// <summary>LiteDB collection for operator daily-run plan templates.</summary>
    public ILiteCollection<DailyRunTemplate> DailyRuns =>
        _db.GetCollection<DailyRunTemplate>("operator_daily_runs");

    /// <summary>LiteDB collection for async job records.</summary>
    public ILiteCollection<FoundryJob> Jobs =>
        _db.GetCollection<FoundryJob>("jobs");

    /// <summary>LiteDB collection for recurring job schedule definitions.</summary>
    public ILiteCollection<JobSchedule> JobSchedules =>
        _db.GetCollection<JobSchedule>("job_schedules");

    /// <summary>LiteDB collection for reusable operator workflow templates.</summary>
    public ILiteCollection<WorkflowTemplate> WorkflowTemplates =>
        _db.GetCollection<WorkflowTemplate>("workflow_templates");

    /// <summary>LiteDB collection holding the latest persisted ML analytics result.</summary>
    public ILiteCollection<PersistedMLResult> MLAnalyticsResults =>
        _db.GetCollection<PersistedMLResult>("ml_analytics");

    /// <summary>LiteDB collection holding the latest persisted ML forecast result.</summary>
    public ILiteCollection<PersistedMLResult> MLForecastResults =>
        _db.GetCollection<PersistedMLResult>("ml_forecast");

    /// <summary>LiteDB collection holding the latest persisted ML embeddings result.</summary>
    public ILiteCollection<PersistedMLResult> MLEmbeddingsResults =>
        _db.GetCollection<PersistedMLResult>("ml_embeddings");

    /// <summary>LiteDB collection for indexed knowledge document records.</summary>
    public ILiteCollection<IndexedDocumentRecord> KnowledgeIndex =>
        _db.GetCollection<IndexedDocumentRecord>("knowledge_index");

    private void EnsureIndexes()
    {
        PracticeAttempts.EnsureIndex(x => x.CompletedAt);
        DailyRuns.EnsureIndex(x => x.DateKey);
        Jobs.EnsureIndex(x => x.Id);
        Jobs.EnsureIndex(x => x.Status);
        Jobs.EnsureIndex(x => x.CreatedAt);
        JobSchedules.EnsureIndex(x => x.Id);
        JobSchedules.EnsureIndex(x => x.Enabled);
        WorkflowTemplates.EnsureIndex(x => x.Id);
        KnowledgeIndex.EnsureIndex(x => x.DocumentPath);
    }

    /// <summary>
    /// Checks whether the database has been migrated from JSON files.
    /// Uses a metadata collection to track migration status.
    /// </summary>
    public bool HasMigrated(string storeName)
    {
        var meta = _db.GetCollection("_migration_meta");
        return meta.Exists(Query.EQ("_id", storeName));
    }

    /// <summary>
    /// Marks a store as migrated from JSON.
    /// </summary>
    public void MarkMigrated(string storeName)
    {
        var meta = _db.GetCollection("_migration_meta");
        meta.Upsert(new BsonDocument
        {
            ["_id"] = storeName,
            ["migratedAt"] = DateTime.UtcNow,
        });
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _db.Dispose();
        }
    }
}
