using System.Security.Cryptography;
using System.Text;

namespace Init7Tv.Shared;

public static class Hash
{
    public static string GetSha256(string input)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashBytes).ToLower();
    }
}