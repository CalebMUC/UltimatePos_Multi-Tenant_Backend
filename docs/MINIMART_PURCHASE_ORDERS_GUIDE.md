# Minimart Demo Guide: Buying Stock with Purchase Orders

A purchase order is how the shop records what it buys. When the goods arrive and are counted in, the system adds them to stock **and**
records what the shop owes the supplier, in one step. This guide follows the maize flour shelf: 10 bales of Soko Maize Meal 2kg ordered
from a distributor.

Prices are examples for the demo; the owner uses real ones.

---

## The flow at a glance

1. **Add the supplier**: once per supplier.
2. **Create the order**: what you are buying, how many, at what cost. Stock does not change yet.
3. **Receive the goods**: count what actually arrived and enter it. Stock goes up, and the supplier's bill is recorded.
4. **Pay the supplier**: now or later. What you owe goes down.

| Order status | Means |
|---|---|
| Ordered | Created, nothing has arrived yet |
| Partially received | Some of it arrived; the rest is still expected |
| Received | Everything ordered has arrived |
| Cancelled | Called off before anything arrived |

---

## Step 1: Add the supplier

Go to **Purchasing → Suppliers** and click **Add supplier**.

| Field | Enter (example) |
|---|---|
| Supplier name | `Mwangi Distributors` |
| Contact person | `Peter Mwangi` |
| Phone | `0722 000 000` |
| KRA PIN | The supplier's PIN, if known |
| Payment terms (days) | `14`: how many days the supplier gives to pay. `0` for cash on delivery |

A supplier is added once and used for every order after that.

---

## Step 2: Create the order

Go to **Purchasing → Purchase Orders** and click **New order**.

1. **Supplier:** Mwangi Distributors.
2. **Expected delivery date:** the day the supplier promised. If it passes before everything arrives, the system reminds you.
3. **Add a line** for each item:

| Product | Unit | Quantity | Cost per unit (KES) | Line total |
|---|---|---|---|---|
| Soko Maize Meal 2kg | Bale | 10 | 1,980 | 19,800 |
| Pembe Maize Flour 2kg | Bale | 5 | 1,920 | 9,600 |

4. Save. The order gets a number such as **PO-000001**.

**Order in the unit the supplier sells in.** A bale costs 1,980, so one packet costs 1,980 ÷ 12 = **165**. The system works this
out itself, and that is the buying cost it uses for profit.

**Stock has not changed yet.** An order is only a promise until the goods are in the shop.

---

## Step 3: Receive the goods

When the delivery arrives, **count it before signing the supplier's delivery note**. Then open the order and click **Receive**.

| Field | Enter |
|---|---|
| Quantity received, per line | What you counted, e.g. `8` bales of Soko (2 short) and `5` of Pembe |
| Supplier invoice / delivery note no. | The number on the supplier's paper, e.g. `INV-4471` |
| Notes | Anything worth keeping, e.g. "2 bales short, to come Friday" |

What happens when you save:

- **Stock goes up:** 8 bales = 96 packets of Soko, 5 bales = 60 packets of Pembe.
- **The supplier's bill is recorded** for what arrived only: (8 × 1,980) + (5 × 1,920) = **25,440**, not the 29,400 ordered.
- The order shows **Partially received**. When the last 2 bales arrive, open the same order and receive again. It then shows **Received**.

The system refuses to receive more than was ordered, so a delivery cannot be counted twice.

**Items with an expiry date** (milk, yoghurt, baby food): the receive screen also asks for the **batch number** and **expiry date**
printed on the pack. The system then sells the oldest first and warns before stock expires.

---

## Step 4: Pay the supplier

Go to **Purchasing → Suppliers**, open Mwangi Distributors and click **Record payment**.

| Field | Enter |
|---|---|
| Amount | `25,440`, or part of it |
| Method | Cash, M-Pesa, Bank transfer or Cheque |
| Reference | The M-Pesa code, bank reference or cheque number (needed for anything except cash) |

The supplier's balance goes down. **Purchasing → Payables** lists every supplier the shop owes, the largest first, and each supplier
has a statement of bills and payments.

**Always pay a supplier here, never as an expense.** The cost of the flour is already counted when the flour is sold. Recording the
payment as an expense would count it twice and make profit look smaller than it is, and the supplier would still show as unpaid.

---

## Common situations

| Situation | What to do |
|---|---|
| **Van sale, paid on the spot** (bread, milk, soda vans) | Create the order and receive it straight away, then record the payment in cash or M-Pesa. Takes a minute and keeps stock and spending correct |
| **Supplier gives credit** | Receive as normal; pay later. The supplier stays on the Payables list until paid |
| **Order called off** | Open it and click **Cancel**. Only possible before anything has been received |
| **Short delivery, rest never coming** | Leave the order as Partially received and note it. Closing an order short is not available yet, so the late-delivery reminder for it will keep showing |
| **Damaged or expired goods sent back to the supplier** | Not available yet as its own step. Ask the manager to record it as a stock adjustment with the reason "Returned to supplier" |
| **Transport paid to bring the goods** | Record it as an expense under *Transport & Fuel*, not on the order |

---

## What the system keeps track of for you

- **Low stock:** when an item falls to its reorder level (e.g. 12 packets), the system warns that it is time to order.
- **Late deliveries:** an order past its expected date that has not fully arrived raises a reminder.
- **Who you owe:** Payables, per supplier.
- **Price changes:** a report shows when a supplier's price for an item has gone up or down.
- **Spending:** what was bought, from whom, in any period.

---

## Mistakes to avoid

| Mistake | Do this instead |
|---|---|
| Adding a delivery as a stock adjustment | Receive it on a purchase order, so the supplier's bill is recorded too |
| Receiving what the order says, not what was counted | Count first, then enter the counted quantity |
| Recording a supplier payment as an expense | Use **Record payment** on the supplier |
| Leaving the cost at 0 | Enter the real cost, or profit cannot be worked out |
| Entering the cost per packet on a bale line | The cost is per the unit on the line: per bale for bales |
| Not writing the delivery note number | Enter it, so the bill can be matched to the supplier's paper |
