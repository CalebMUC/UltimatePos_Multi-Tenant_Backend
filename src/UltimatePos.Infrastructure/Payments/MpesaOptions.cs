using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Infrastructure.Payments
{
    public class MpesaOptions
    {
        public string BaseUrl { get; set; } = "https://sandbox.safaricom.co.ke";
        public string ConsumerKey { get; set; } = string.Empty;
        public string ConsumerSecret { get; set; } = string.Empty;
        public string ShortCode { get; set; } = string.Empty;
        public string Passkey { get; set; } = string.Empty;
        public string StkCallbackUrl { get; set; } = string.Empty;
        public string C2bConfirmationUrl { get; set; } = string.Empty;
        public string C2bValidationUrl { get; set; } = string.Empty;

        // Empty = open (so sandbox testing isn't blocked before this is configured). Populate with CIDR ranges
        // (e.g. "196.201.214.0/24") from your own Daraja production traffic or Safaricom support before going live —
        // callbacks are unsigned, so this is the only real gate on who can call these endpoints.
        public string[] CallbackIpAllowlist { get; set; } = Array.Empty<string>();
    }
}
