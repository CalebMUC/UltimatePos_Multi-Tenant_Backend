using System;

namespace UltimatePos.Domain.Entities
{
    /// <summary>
    /// The prescription a sale of prescription-only products was dispensed against. One per sale. Recorded as given:
    /// the system checks that it is present and complete, never what was prescribed.
    /// </summary>
    public class SalePrescription
    {
        public Guid SaleId { get; set; }
        public string? PrescriptionNumber { get; set; }
        public string PrescriberName { get; set; } = string.Empty;
        /// <summary>The prescriber's practice registration number (e.g. KMPDC).</summary>
        public string? PrescriberRegistrationNumber { get; set; }
        public string? PrescriberFacility { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public string? PatientPhoneNumber { get; set; }
        public int? PatientAgeYears { get; set; }
        public DateOnly PrescriptionDate { get; set; }
        public string? Notes { get; set; }
        /// <summary>The user who dispensed, i.e. who recorded the sale.</summary>
        public Guid? DispensedBy { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Sale Sale { get; set; } = null!;
    }
}
