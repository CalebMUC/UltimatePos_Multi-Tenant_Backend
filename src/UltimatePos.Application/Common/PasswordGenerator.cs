using System.Security.Cryptography;

namespace UltimatePos.Application.Common
{
    /// <summary>Temporary passwords for accounts created or reset by someone else. The user is made to change it at first sign-in.</summary>
    public static class PasswordGenerator
    {
        // No look-alikes (0/O, 1/l/I) — these get read out over the phone.
        private const string Letters = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ";
        private const string Digits = "23456789";

        public static string Generate(int length = 12)
        {
            var all = Letters + Digits;
            var chars = new char[length];
            chars[0] = Letters[RandomNumberGenerator.GetInt32(Letters.Length)];
            chars[1] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
            for (var i = 2; i < length; i++)
                chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
            RandomNumberGenerator.Shuffle(chars.AsSpan());
            return new string(chars);
        }
    }
}
