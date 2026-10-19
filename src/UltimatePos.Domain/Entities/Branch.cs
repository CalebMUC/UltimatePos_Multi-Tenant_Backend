using System;

namespace UltimatePos.Domain.Entities
{
    /// <summary>
    /// A place inside a business where stock is held and sales happen — a shop, a store, a factory. Every business has a head office
    /// branch (number 00), created with it. Catalogue, prices, customers and suppliers are shared across branches; stock, sales,
    /// purchase orders, production and (optionally) expenses belong to a branch.
    /// </summary>
    public class Branch : ITenantEntity
    {
        public Guid BranchId { get; set; }
        public Guid BusinessId { get; set; }
        /// <summary>
        /// The branch's number within the business, 0–99, as KRA eTIMS numbers branches ("bhfId"): 0 is the head office. Unique per
        /// business and never reused once assigned. See <see cref="KraBranchId"/>.
        /// </summary>
        public int BranchNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        /// <summary>Short label for documents and lists, e.g. "NBO". Optional; unique per business when set.</summary>
        public string? Code { get; set; }
        public string? PhysicalAddress { get; set; }
        public string? County { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        /// <summary>The branch's own M-Pesa till. Till payments to it are routed to this branch.</summary>
        public string? MpesaShortCode { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public Guid? LastUpdatedBy { get; set; }

        public bool IsHeadOffice => BranchNumber == 0;

        /// <summary>The two-character branch id KRA eTIMS expects: "00", "01", ...</summary>
        public string KraBranchId => FormatKraBranchId(BranchNumber);

        public static string FormatKraBranchId(int branchNumber) => branchNumber.ToString("00");

        public const int MaxBranchNumber = 99;
    }

    /// <summary>
    /// Which branches a member of a business may work in. Users holding Branches.AccessAll (owners, managers) reach every branch and
    /// need no rows here; everyone else works only in the branches listed.
    /// </summary>
    public class UserBranch
    {
        public Guid UserId { get; set; }
        public Guid BranchId { get; set; }
        /// <summary>The branch signed in to when the user does not pick one.</summary>
        public bool IsDefault { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }

        public User User { get; set; } = null!;
        public Branch Branch { get; set; } = null!;
    }

    /// <summary>
    /// A record that happens at one branch (a sale, a stock row, a movement...). The DbContext stamps the active branch on insert and
    /// limits every query to the branches the caller may see.
    /// </summary>
    public interface IBranchEntity
    {
        Guid BranchId { get; set; }
    }
}
