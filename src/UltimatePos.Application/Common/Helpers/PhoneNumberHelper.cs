using System.Linq;

namespace UltimatePos.Application.Common.Helpers
{
    public static class PhoneNumberHelper
    {
        public static string NormalizePhoneNumber(string? phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber))
                return phoneNumber ?? string.Empty;

            var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());

            return digits.Length switch
            {
                10 when digits.StartsWith('0') => "254" + digits[1..],
                9 when digits.StartsWith('7') || digits.StartsWith('1') => "254" + digits,
                12 when digits.StartsWith("254") => digits,
                _ => digits
            };
        }
    }
}