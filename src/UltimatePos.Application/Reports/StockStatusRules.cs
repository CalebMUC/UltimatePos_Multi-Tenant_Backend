namespace UltimatePos.Application.Reports
{
    public enum StockStatus { InStock, LowStock, OutOfStock }

    public static class StockStatusRules
    {
        /// <summary>
        /// At or below zero is out of stock. Above zero but at or below a reorder level is low. A reorder level of 0 means
        /// "never set", so it never makes a product low. The dashboard counts use the same rules.
        /// </summary>
        public static StockStatus Classify(decimal quantityOnHand, decimal reorderLevel) =>
            quantityOnHand <= 0 ? StockStatus.OutOfStock
            : reorderLevel > 0 && quantityOnHand <= reorderLevel ? StockStatus.LowStock
            : StockStatus.InStock;
    }
}
