using System.Collections.Concurrent;
using Npgsql;

namespace Realmforge.Server.Gateway.Accounts;

public sealed record AccountRecord(Guid Id, string Email, string PasswordHash, DateTimeOffset CreatedAt);

/// <summary>Login credentials only; everything gameplay-related lives in the AccountGrain keyed by the same id.</summary>
public interface IAccountStore
{
    Task<AccountRecord?> FindByEmail(string normalizedEmail);
    Task<bool> TryCreate(AccountRecord account);
}

public static class AccountEmail
{
    public const int MaxLength = 254;

    public static string? Normalize(string? email)
    {
        string trimmed = (email ?? string.Empty).Trim().ToLowerInvariant();
        int at = trimmed.IndexOf('@');
        bool looksValid = at > 0 && at < trimmed.Length - 1 && trimmed.Length <= MaxLength && !trimmed.Contains(' ');
        return looksValid ? trimmed : null;
    }
}

public sealed class InMemoryAccountStore : IAccountStore
{
    private readonly ConcurrentDictionary<string, AccountRecord> _byEmail = new();

    public Task<AccountRecord?> FindByEmail(string normalizedEmail) =>
        Task.FromResult(_byEmail.GetValueOrDefault(normalizedEmail));

    public Task<bool> TryCreate(AccountRecord account) => Task.FromResult(_byEmail.TryAdd(account.Email, account));
}

public sealed class PostgresAccountStore : IAccountStore
{
    private const string FindSql = "SELECT id, email, password_hash, created_at FROM accounts WHERE email = @email";
    private const string InsertSql = """
        INSERT INTO accounts (id, email, password_hash, created_at) VALUES (@id, @email, @hash, @created)
        ON CONFLICT (email) DO NOTHING
        """;

    private readonly NpgsqlDataSource _dataSource;

    public PostgresAccountStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<AccountRecord?> FindByEmail(string normalizedEmail)
    {
        await using NpgsqlCommand command = _dataSource.CreateCommand(FindSql);
        command.Parameters.AddWithValue("email", normalizedEmail);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }
        return new AccountRecord(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3));
    }

    public async Task<bool> TryCreate(AccountRecord account)
    {
        await using NpgsqlCommand command = _dataSource.CreateCommand(InsertSql);
        command.Parameters.AddWithValue("id", account.Id);
        command.Parameters.AddWithValue("email", account.Email);
        command.Parameters.AddWithValue("hash", account.PasswordHash);
        command.Parameters.AddWithValue("created", account.CreatedAt);
        return await command.ExecuteNonQueryAsync() == 1;
    }
}
