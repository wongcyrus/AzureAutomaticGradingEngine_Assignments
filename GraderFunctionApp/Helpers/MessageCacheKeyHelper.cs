using System.Security.Cryptography;
using System.Text;

namespace GraderFunctionApp.Helpers;

internal static class MessageCacheKeyHelper
{
    public static string CreateNpcKey(
        string originalMessage,
        int age,
        string gender,
        string background)
    {
        return $"npc_{age}_{ComputeHash(gender)}_{ComputeHash(background)}_{ComputeHash(originalMessage)}";
    }

    public static string ComputeHash(string input)
    {
        var hashedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hashedBytes)
            .Replace("/", "_")
            .Replace("+", "-")
            .Replace("=", "");
    }
}
