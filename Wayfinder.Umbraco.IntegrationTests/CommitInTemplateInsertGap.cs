using System.Data.Common;
using Microsoft.Data.Sqlite;
using NPoco;
using SQLitePCL;
using Umbraco.Cms.Infrastructure.Persistence;
using UmbracoConstants = Umbraco.Cms.Core.Constants;

namespace Wayfinder.Umbraco.IntegrationTests;

/// <summary>
/// Just before Umbraco inserts a template's node row, commits a write from another connection.
/// Umbraco's template service checks the alias with a read, then inserts, inside a deferred SQLite
/// transaction. A commit landing between the two leaves the insert failing with SQLITE_BUSY_SNAPSHOT
/// on every retry, and the boot stalls for about ten minutes. A seeder that takes a write lock first
/// owns the database from its first statement, so this commit simply waits for it. Doing this on
/// every template insert turns a rare CI timing into a failure on every run.
/// </summary>
public sealed class CommitInTemplateInsertGap(string connectionString) : IProviderSpecificExecutingInterceptor
{
    private static readonly string TemplateObjectType = UmbracoConstants.ObjectTypes.Template.ToString();

    public string ProviderName => UmbracoConstants.ProviderNames.SQLLite;

    public void OnExecutingCommand(IDatabase database, DbCommand cmd)
    {
        if (!cmd.CommandText.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase)
            || !cmd.CommandText.Contains("umbracoNode", StringComparison.OrdinalIgnoreCase)
            || !cmd.Parameters.Cast<DbParameter>().Any(p => string.Equals(p.Value?.ToString(), TemplateObjectType, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        // One attempt, no waiting: when nothing holds the write lock this commits now; when the
        // seeder holds it, it fails at once instead of landing later in some unrelated gap.
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder(connectionString) { Cache = SqliteCacheMode.Private, Pooling = false }.ToString());
        connection.Open();
        raw.sqlite3_exec(connection.Handle,
            "PRAGMA busy_timeout = 0; BEGIN IMMEDIATE; CREATE TABLE IF NOT EXISTS testConcurrentWrite(at TEXT); " +
            "INSERT INTO testConcurrentWrite VALUES(datetime('now')); COMMIT;");
    }

    public void OnExecutedCommand(IDatabase database, DbCommand cmd)
    {
    }
}
