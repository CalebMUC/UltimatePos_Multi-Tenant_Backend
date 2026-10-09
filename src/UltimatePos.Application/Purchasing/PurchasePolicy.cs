using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Domain.Entities;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Purchasing
{
    /// <summary>
    /// Which catalog item types a business may purchase, by its BusinessKind. Pure and DB-free.
    /// Changing the rule, or adding a kind (e.g. a Hybrid agrovet that also resells third-party products),
    /// is an edit to the table below and nothing else.
    /// </summary>
    public static class PurchasePolicy
    {
        private static readonly IReadOnlyDictionary<BusinessKind, ItemType[]> Allowed =
            new Dictionary<BusinessKind, ItemType[]>
            {
                // Packaging is included on purpose: production needs boxes/packets (a box = 12 x 1kg packets).
                [BusinessKind.Manufacturer] = new[] { ItemType.RawMaterial, ItemType.Packaging },
                [BusinessKind.Trader] = new[] { ItemType.FinishedGood },
            };

        /// <summary>Allowed item types for the kind. Unknown kinds get none (fail closed).</summary>
        public static IReadOnlyList<ItemType> AllowedItemTypes(BusinessKind kind) =>
            Allowed.TryGetValue(kind, out var types) ? types : Array.Empty<ItemType>();

        public static bool CanPurchase(BusinessKind kind, ItemType itemType) =>
            AllowedItemTypes(kind).Contains(itemType);
    }
}
