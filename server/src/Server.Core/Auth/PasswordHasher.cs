using System.Security.Cryptography;

namespace Realmforge.Server.Core.Auth;

/// <summary>PBKDF2-SHA256 hashes in the self-describing format "v1.{iterations}.{salt}.{hash}".</summary>
public static class PasswordHasher
{
    private const string FormatVersion = "v1";
    private const int Iterations = 120_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    public const int MinPasswordLength = 8;

    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return string.Join('.', FormatVersion, Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public static bool Verify(string password, string encoded)
    {
        string[] parts = encoded.Split('.');
        if (parts.Length != 4 || parts[0] != FormatVersion || !int.TryParse(parts[1], out int iterations))
        {
            return false;
        }
        byte[] salt = Convert.FromBase64String(parts[2]);
        byte[] expected = Convert.FromBase64String(parts[3]);
        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
