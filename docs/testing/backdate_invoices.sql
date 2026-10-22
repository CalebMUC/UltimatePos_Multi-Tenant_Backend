-- Move two credit invoices into the past so the ageing buckets have something to show.
-- INV-000002 becomes 75 days old, INV-000003 becomes 40 days old. Run only against a TEST database.
UPDATE "CustomerLedgerEntries" SET "CreatedAt" = now() - interval '75 days'
 WHERE "EntryType" = 'Invoice' AND "ReferenceId" = (SELECT "SaleId" FROM "Sales" WHERE "SaleNumber" = 'INV-000002');
UPDATE "Sales" SET "CreatedAt" = now() - interval '75 days' WHERE "SaleNumber" = 'INV-000002';

UPDATE "CustomerLedgerEntries" SET "CreatedAt" = now() - interval '40 days'
 WHERE "EntryType" = 'Invoice' AND "ReferenceId" = (SELECT "SaleId" FROM "Sales" WHERE "SaleNumber" = 'INV-000003');
UPDATE "Sales" SET "CreatedAt" = now() - interval '40 days' WHERE "SaleNumber" = 'INV-000003';
