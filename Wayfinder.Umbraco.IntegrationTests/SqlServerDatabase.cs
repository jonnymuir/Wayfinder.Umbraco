using Microsoft.Data.SqlClient;

namespace Wayfinder.Umbraco.IntegrationTests;

/// <summary>
/// A uniquely named, throwaway database on the SQL Server <see cref="ReferenceAppFactory"/> was
/// pointed at, so a run never meets tables left by an earlier one.
/// </summary>
internal sealed class SqlServerDatabase
{
    private readonly string _server;
    private readonly string _name = "wu_integration_" + Guid.NewGuid().ToString("N");

    public SqlServerDatabase(string server)
    {
        _server = server;
        ConnectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = _name }.ConnectionString;
    }

    public string ConnectionString { get; }

    public void Create() => Execute($"CREATE DATABASE [{_name}]");

    public void Drop()
    {
        // The host's pooled connections would otherwise hold the database open.
        SqlConnection.ClearAllPools();
        try
        {
            Execute($"ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_name}]");
        }
        catch (SqlException)
        {
            // Best effort: the server is a throwaway CI container.
        }
    }

    private void Execute(string sql)
    {
        using var connection = new SqlConnection(_server);
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }
}
