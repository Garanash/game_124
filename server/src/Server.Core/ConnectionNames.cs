namespace Realmforge.Server.Core;

/// <summary>Keys under "ConnectionStrings"; an empty value switches the process to its in-memory fallback.</summary>
public static class ConnectionNames
{
    public const string Postgres = "Postgres";
    public const string Redis = "Redis";
    public const string Nats = "Nats";
}
