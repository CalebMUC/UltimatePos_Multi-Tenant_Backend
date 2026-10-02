using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Domain.Exceptions;

namespace UltimatePos.Application.Payments
{
    // Normalizes Kenyan mobile numbers to the 2547XXXXXXXX / 2541XXXXXXXX shape Daraja requires,
    // however the cashier or customer typed it (07.., +254 7.., 254 7.., 011.., etc).
    public static class MpesaPhoneNumber
    {
        public static string Normalize(string phoneNumber)
        {
            var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());

            if (digits.Length == 12 && digits.StartsWith("254"))
                return digits;
            if (digits.Length == 10 && digits.StartsWith("0"))
                return "254" + digits[1..];
            if (digits.Length == 9 && (digits.StartsWith("7") || digits.StartsWith("1")))
                return "254" + digits;

            throw new InvalidAssignmentException($"'{phoneNumber}' is not a valid Kenyan phone number.");
        }
    }
}
