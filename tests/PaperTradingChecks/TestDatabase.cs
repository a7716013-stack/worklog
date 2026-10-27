using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Test-only connectivity; callers own a fresh database identity, never the environment.
internal static class TestDatabase
{
    public static void CheckSafety()
    {
        foreach (var name in new[] { "WorkJournal", "master", "WorkJournalPaperChecks_" + new string('x', 32), "WorkJournalPaperChecks_" + Guid.NewGuid().ToString("N") + "extra" })
        {
            try { ValidateName("WorkJournalPaperChecks_", name); }
            catch (InvalidOperationException) { continue; }
            throw new Exception("Unsafe database name accepted.");
        }
        var owned = "WorkJournalPaperChecks_" + Guid.NewGuid().ToString("N");
        var prior = Environment.GetEnvironmentVariable("WORKJOURNAL_TEST_SQL");
        try
        {
            Environment.SetEnvironmentVariable("WORKJOURNAL_TEST_SQL", "Server=example.invalid;Database=Production;Integrated Security=True");
            var connection = new SqlConnectionStringBuilder(Connection("WorkJournalPaperChecks_", owned));
            if (connection.InitialCatalog != owned || connection.DataSource != "example.invalid")
                throw new Exception("Environment must only supply connectivity, never database identity.");
            using var wrong = new DbContext(new DbContextOptionsBuilder().UseSqlServer("Server=example.invalid;Database=Production;Integrated Security=True").Options);
            try { DeleteOwnedAsync(wrong, "WorkJournalPaperChecks_", owned).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { Console.WriteLine("PASS: test database naming, environment override and cleanup guard"); return; }
            throw new Exception("Mismatched cleanup target accepted.");
        }
        finally { Environment.SetEnvironmentVariable("WORKJOURNAL_TEST_SQL", prior); }
    }

    public static string Connection(string prefix, string database)
    {
        ValidateName(prefix, database);
        var connection = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("WORKJOURNAL_TEST_SQL") ??
            @"Server=.\SQLEXPRESS;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;");
        if (!string.IsNullOrEmpty(connection.AttachDBFilename) || connection.UserInstance)
            throw new InvalidOperationException("Attached/user-instance databases are not allowed in checks.");
        connection.InitialCatalog = database;
        return connection.ConnectionString;
    }

    public static void ValidateName(string prefix, string database)
    {
        if (prefix is not ("WorkJournalPaperChecks_" or "WorkJournalStrategyChecks_" or "WorkJournalRadarChecks_" or "WorkJournalFavoritesMigration_"))
            throw new InvalidOperationException("Unknown test database prefix.");
        if (!database.StartsWith(prefix, StringComparison.Ordinal) || database.Length != prefix.Length + 32 ||
            !Guid.TryParseExact(database[prefix.Length..], "N", out _))
            throw new InvalidOperationException("Refusing a non-ephemeral database name.");
    }

    public static async Task DeleteOwnedAsync(DbContext db, string prefix, string database)
    {
        ValidateName(prefix, database);
        var actual = new SqlConnectionStringBuilder(db.Database.GetConnectionString());
        if (!string.Equals(actual.InitialCatalog, database, StringComparison.Ordinal) || !string.IsNullOrEmpty(actual.AttachDBFilename))
            throw new InvalidOperationException("Refusing to delete a database not owned by this check.");
        await db.Database.EnsureDeletedAsync();
    }

    public static async Task PrintVersionAsync(DbContext db)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT @@VERSION";
            Console.WriteLine("SQL Server: " + await command.ExecuteScalarAsync());
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
