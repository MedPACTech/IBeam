using System;
using System.Security.Cryptography;
using System.Text;

namespace IBeam.Utilities
{
    public static class TokenGenerator
    {
        private const string LOWERCASE_LETTERS = "abcdefghijklmnopqrstuvwxyz";
        private const string UPPERCASE_LETTERS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private const string DIGITS = "0123456789";
        private const string CHARACTERS = LOWERCASE_LETTERS + UPPERCASE_LETTERS + DIGITS;

        public static string GenerateRandomToken(int length, string characters = CHARACTERS)
        {
            using var rand = new RNGCryptoServiceProvider();
            var data = new byte[4 * length];
            rand.GetBytes(data);

            var sb = new StringBuilder();
            for (var i = 0; i < length; i++)
            {
                var x = BitConverter.ToUInt32(data, i * 4);
                var index = x % (uint)characters.Length;
                sb.Append(characters[(int)index]);
            }

            return sb.ToString();
        }

        public static string GenerateTwoFactorCode()
        {
            return GenerateRandomToken(6, DIGITS);
        }
    }
}