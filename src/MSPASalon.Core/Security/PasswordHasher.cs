using System.Security.Cryptography;

namespace MSPASalon.Core.Security;

/// <summary>
/// PBKDF2-HMAC-SHA256 password hashing. The legacy application stored passwords in plain text;
/// imported passwords are hashed on import and the user is forced to change them.
/// </summary>
public static class PasswordHasher
{
    public const int DefaultIterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public sealed record HashedPassword(string Hash, string Salt, int Iterations);

    public static HashedPassword Hash(string password, int iterations = DefaultIterations)
    {
        ArgumentNullException.ThrowIfNull(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashSize);
        return new HashedPassword(Convert.ToBase64String(hash), Convert.ToBase64String(salt), iterations);
    }

    public static bool Verify(string password, string hash, string salt, int iterations)
    {
        if (password is null || string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(salt) || iterations <= 0) return false;
        byte[] expected, saltBytes;
        try
        {
            expected = Convert.FromBase64String(hash);
            saltBytes = Convert.FromBase64String(salt);
        }
        catch (FormatException)
        {
            return false;
        }
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
