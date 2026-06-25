using System.Security.Cryptography;
using System.Text;

namespace backend.main.shared.utilities
{
    internal static class CryptoHelper
    {
        internal static string HashToken(string rawToken)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
            return Convert.ToHexString(bytes);
        }

        internal static bool FixedTimeEquals(string left, string right)
        {
            var leftBytes = Encoding.UTF8.GetBytes(left);
            var rightBytes = Encoding.UTF8.GetBytes(right);
            if (leftBytes.Length != rightBytes.Length)
                return false;

            return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
    }
}
