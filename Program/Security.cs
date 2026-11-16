using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Career24;

internal sealed record Session(string FullName, string Role);
internal sealed record PasswordDigest(byte[] Hash, byte[] Salt, int Iterations);
internal static class Security
{
    public const string Manager = "manager";
    public const string Administrator = "admin";
    private const int WorkFactor = 600_000;

    public static PasswordDigest CreateDigest(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        return new PasswordDigest(Derive(password, salt, WorkFactor), salt, WorkFactor);
    }

    public static bool Verify(string password, byte[] hash, byte[] salt, int iterations) =>
        CryptographicOperations.FixedTimeEquals(hash, Derive(password, salt, iterations));

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);

    public static string NormalizeLogin(string login) => login.Trim().ToLowerInvariant();
    public static bool ValidLogin(string login) => Regex.IsMatch(login, @"\A[a-z0-9_.-]{4,32}\z");
}
