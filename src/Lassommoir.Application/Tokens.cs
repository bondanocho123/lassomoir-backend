using System.Security.Cryptography;
using System.Text;

namespace Lassommoir.Application;

/// <summary>Token akses opaque. Hanya hash SHA-256-nya yang disimpan; token mentah ditampilkan sekali.</summary>
public static class Tokens
{
    public static string Generate() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Perbandingan waktu-konstan, untuk token yang tidak dicari lewat indeks (Auth:AdminToken).</summary>
    public static bool Matches(string token, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(token)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
