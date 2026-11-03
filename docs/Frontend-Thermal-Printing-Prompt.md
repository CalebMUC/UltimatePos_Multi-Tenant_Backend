# Frontend Task: 80 mm Thermal Receipt Printing

> **For:** the coding agent working on the Ultimate POS frontend.
> **Depends on:** nothing. There is no API change. The API runs on a server and cannot reach a USB printer, so the till's browser prints.
> Before you start, read `src/features/sales/Receipt.tsx`, `src/features/deliveries/DeliveryNote.tsx`, `src/components/ui/overlays.tsx`,
> `src/stores/preferences.ts`, the `@media print` block in `src/app/globals.css` and the "Point of sale" card in
> `src/app/(app)/settings/page.tsx`. If the code and this prompt disagree, the code wins.
> The working tree has unfinished dashboard changes. Leave them alone, and do not commit them with this work.

## Goal

The tills use an **Xprinter XP-T80A** (80 mm, USB), installed in Windows as an ordinary printer. Cashiers must be able to:

1. Print a complete receipt on one 80 mm page. Today it does not print correctly (see below).
2. Print without the browser's dialog. This is till setup (Chrome's `--kiosk-printing`), so no code is needed for it.
3. Optionally have the receipt print by itself when a sale completes.

**Out of scope:** raw ESC/POS, QZ Tray and WebUSB. The business has **no cash drawer**, so add no drawer code. Do not change anything about
eTIMS beyond waiting for its signature as described below.

## What is wrong today

Both were reproduced in Chromium by generating PDFs from a copy of the dialog with the same styles:

- **The receipt is cut off.** The global print rule makes `.print-area` `position: absolute`, and `.no-print` hides the dialog's header and
  footer. Nothing is left in flow, so the `DialogContent` (`fixed`, `overflow-y-auto`, `max-h-[92vh]`) collapses to about 40 px and clips the
  receipt. Only the top strip prints, and it repeats on a second page because the page behind the dialog still takes up space.
- **The paper size is ignored.** `size: 80mm auto` is invalid CSS. `size` takes two lengths, so the browser drops the declaration and falls
  back to the default paper.

The delivery note has both faults, because it uses the same dialog and the same `@page` rule.

## Changes

### 1. `src/app/globals.css`

Keep the existing `@media print` rules and add these inside the block, after them:

```css
/* A dialog is fixed and scrolls: print it in the page flow, or it clips what it holds. Nothing else takes up space. */
body:has(.print-area) > :not(:has(.print-area)) { display: none !important; }
[role='dialog']:has(.print-area), [role='dialog']:has(.print-area) div:has(> .print-area) {
  position: static !important; display: block !important; width: auto !important; max-width: none !important;
  max-height: none !important; overflow: visible !important; transform: none !important;
  padding: 0 !important; border: 0 !important; box-shadow: none !important;
}
[role='dialog'] .print-area { position: static; }
```

### 2. New file `src/lib/print.ts`

```ts
import type { ReceiptPaper } from '@/stores/preferences';

/**
 * Prints the open .print-area. @page needs two lengths ("80mm auto" is invalid), so an 80 mm page is sized to the rendered receipt. The
 * style goes once printing ends: it would otherwise apply to the next report printed.
 */
export function printReceipt(paper: ReceiptPaper) {
  const receipt = document.querySelector('.print-area');
  if (!receipt) return;
  document.getElementById('receipt-page')?.remove();
  if (paper === '80mm') {
    const heightMm = Math.ceil((receipt.getBoundingClientRect().height * 25.4) / 96) + 6;
    const style = document.createElement('style');
    style.id = 'receipt-page';
    style.textContent = `@media print { @page { size: 80mm ${heightMm}mm; margin: 2mm; } }`;
    document.head.appendChild(style);
    window.addEventListener('afterprint', () => style.remove(), { once: true });
  }
  window.print();
}
```

### 3. `Receipt.tsx` and `DeliveryNote.tsx`

- Call `printReceipt(paper)` from the Print button in place of `window.print()`. Leave `ReportLayout.tsx` as it is.
- `printReceipt` now owns the 80 mm page, so the inline `<style>` is only for A4. In both files render it only when `!narrow`:
  `@media print { @page { size: A4; margin: 12mm; } }`.

### 4. Auto-print (per device, off by default)

- **`src/stores/preferences.ts`:** add `autoPrintReceipt: boolean`, default `false`.
- **Settings → Point of sale:** add a row **"Print receipt automatically"** with the hint *"Prints when a sale completes. Set up silent
  printing on this till, or the print dialog opens after every sale."* Use the same control style as the rows around it.
- **`ReceiptDialog`:** when `autoPrintReceipt`, `live`, `open` and `sale.status === SaleStatus.Completed` are all true, print once:
  - Call `printReceipt(paper)` after the next frame, so the dialog is on screen and can be measured.
  - Keep the printed `saleId` in a ref so that re-renders and StrictMode never print the same sale twice. **A second copy wastes paper and
    looks like a second sale.**
  - **Only `live` prints by itself.** Opening a receipt from a sale's detail page never does.
- **Businesses with eTIMS:** a receipt without KRA's signature is not valid for KRA, so never auto-print one. When `useHasModule('etims')` is
  true, read the same `['etims-receipt', saleId]` query `KraBlock` uses, and print only when its status is
  `EtimsSubmissionStatus.Sent` or it fails with `isNotFound` (the sale will never be reported). In any other state do not print: the cashier
  sees "KRA signature pending" and uses the Print button. With eTIMS off, print straight away.

The Print button and the 80 mm / A4 switch stay as they are, and they remain the fallback whenever auto-print is off or does not fire.

## Tests

Follow the existing Vitest setup (`src/features/sales/Receipt.test.tsx`, `renderWithProviders`). Stub `window.print`.

**`src/lib/print.test.ts`:**
- 80 mm: adds a `#receipt-page` style with two lengths, then calls `window.print` once.
- A4: adds no style.
- Printing twice leaves one style.
- The style is gone after an `afterprint` event.
- With no `.print-area`, nothing is printed.

**`ReceiptDialog`:**
- Preference off: no print.
- Preference on with `live`: prints once, including after a re-render.
- Without `live` (a reprint from the sale's detail page): no auto-print.
- eTIMS on and `Pending`: no print. It prints when the status becomes `Sent`, and on a 404. It does not print when `Rejected`.

## Check by hand

jsdom has no print layout, so these need a real browser. Sign in at a till and make a sale of 1 item and one of 40 items:

- [ ] In Chrome, click Print and choose **Save as PDF**. Every item, the total and "Thank you" appear, on **one** page 80 mm wide.
- [ ] The same with the paper set to A4: the sheet is A4, and a long receipt runs onto a second page without losing lines.
- [ ] A delivery note prints in full.
- [ ] A report printed straight after a receipt is on normal paper, not 80 mm.
- [ ] On the XP-T80A: the right edge is not cut. If it is, change the receipt's `w-[72mm]` to `w-[70mm]` in both files.
- [ ] On the XP-T80A: long receipts are not shrunk or split, and the paper does not run on far past the receipt.
- [ ] With `--kiosk-printing` and auto-print on, completing a sale prints one receipt, with no dialog.
- [ ] `npm run typecheck`, `npm run lint` and `npm run test` pass.
