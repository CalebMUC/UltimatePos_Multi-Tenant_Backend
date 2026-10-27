namespace UltimatePos.Domain.Entities;

/// <summary>
/// The name a product is sold under (Soko, Pembe, Jogoo), so the shelf's many versions of one item can be told apart and filtered.
/// Optional on a product, and separate from the category: the category says what it is (Maize Flour), the brand whose it is.
/// </summary>
public class Brand : ITenantEntity
{
    public Guid BrandId { get; set; }
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
    public DateTime? LastUpdatedAt { get; set; }
    public Guid? LastUpdatedBy { get; set; }
}
