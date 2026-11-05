"""Builds the demo upload files for the hardware, electrical, minimart, chemist and liquor-store demos.

Sheet 1 of each products file is the API's bulk-upload layout (columns A-J). Columns K onward are a build sheet the
upload ignores (it reads A-J only): unit cost, opening quantity, supplier and pack, used to raise the opening purchase
orders and add the packs by hand. The minimart and chemist files go on with what the upload cannot take yet (gap M2):
barcode, batch tracking and shelf life, and for medicines prescription-only, generic name, strength and dosage form. The liquor
file adds age-restricted (alcohol, tobacco) and the container a pack is sold with (a crate's deposit).

    python docs/demo-data/build_demo_catalogues.py docs/demo-data [hardware electrical minimart chemist liquor]
"""
import math
import sys
from pathlib import Path
from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill

OUT = Path(sys.argv[1])
OUT.mkdir(parents=True, exist_ok=True)
ONLY = set(sys.argv[2:])

UPLOAD_HEADERS = ["Name", "Category Code", "Item Type", "Tax Classification", "Unit Symbol", "Reorder Level",
                  "Wholesale Price", "Retail Price", "Description", "Brand"]
BUILD_HEADERS = ["Unit Cost ex VAT (build)", "Opening Qty (build)", "Supplier (build)", "Pack Unit (build)", "Pack Size (build)",
                 "Pack Retail (build)", "Pack Wholesale (build)"]
DETAIL_HEADERS = ["Barcode (build)", "Batch Tracked (build)", "Shelf Life Days (build)", "First Batch Expires In Days (build)",
                  "Prescription Only (build)", "Generic Name (build)", "Strength (build)", "Dosage Form (build)",
                  "Age Restricted (build)"]
DETAIL_KEYS = ["barcode", "batch", "life", "near", "rx", "generic", "strength", "form", "age"]
FLAGS = ("batch", "rx", "age")


def r5(x):
    """Round to a shelf price: nearest 0.5 under 20 (screws, plugs), 5 under 100, 10 under 2,000, 50 above."""
    if x < 20:
        return round(x * 2) / 2
    step = 5 if x < 100 else 10 if x < 2000 else 50
    return int(round(x / step) * step)


VAT = 0.16


def cost_of(retail, markup):
    """What the item costs with VAT, at the category's mark-up on a VAT-inclusive shelf price."""
    c = retail / (1 + markup)
    return round(c, 2) if c < 20 else float(math.floor(c))


def cost_ex_vat(cost):
    """The cost to load. Both demos are VAT-registered: the API reports revenue net of VAT and takes purchase costs as entered, so
    costs go in without the VAT the shop claims back, or every margin is about 14% too low."""
    return round(cost / (1 + VAT), 2)


def wholesale_of(retail, cost):
    # Contractors' price: cost plus 60% of the retail margin.
    return r5(cost + (retail - cost) * 0.6)


def write_categories(path, cats):
    wb = Workbook()
    ws = wb.active
    ws.title = "Categories"
    ws.append(["Name", "Code", "Parent Code"])
    for name, code, parent in cats:
        ws.append([name, code, parent or None])
    style(ws)
    wb.save(path)


def write_products(path, products, markups, suppliers, vat_registered=True, taxes=None, defaults=None, markup_by_supplier=False,
                   containers=False):
    """defaults: per-category details (DETAIL_KEYS) a product's own details (its 10th element, a dict) override. Costs go in without
    VAT only for a VAT-registered shop: one that is not registered cannot claim the VAT back, so it is part of the cost.
    containers: a pack's 4th element names the product sold with it when the customer brings none (a crate and its empties)."""
    wb = Workbook()
    ws = wb.active
    ws.title = "Products"
    details = defaults is not None
    headers = UPLOAD_HEADERS + BUILD_HEADERS + (["Pack Container (build)"] if containers else []) + (DETAIL_HEADERS if details else [])
    if not vat_registered:
        headers[len(UPLOAD_HEADERS)] = "Unit Cost (build)"
    ws.append(headers)
    total_cost = 0
    packs = []
    per_supplier = {}
    for p in products:
        name, cat, unit, reorder, retail, brand, qty, sup = p[:8]
        pack = p[8] if len(p) > 8 else None
        markup = markups[sup] if markup_by_supplier else markups[cat]
        cost = cost_of(retail, markup)
        wholesale = wholesale_of(retail, cost)
        if vat_registered and (taxes or {}).get(cat, "Standard") == "Standard":
            cost = cost_ex_vat(cost)   # an exempt deposit carries no VAT to claim back
        row = [name, cat, "FinishedGood", (taxes or {}).get(cat, "Standard"), unit, reorder, wholesale, retail, None, brand or None,
               cost, qty, suppliers[sup]]
        if pack:
            pack_unit, size, discount = pack[:3]
            pack_retail = r5(retail * size * (1 - discount))
            pack_wholesale = r5(wholesale * size * (1 - discount))
            row += [pack_unit, size, pack_retail, pack_wholesale]
            packs.append((name, unit, pack_unit, size, pack_retail, pack_wholesale))
        else:
            row += [None] * 4
        if containers:
            row.append(pack[3] if pack and len(pack) > 3 else None)
        if details:
            d = {**defaults.get(cat, {}), **(p[9] if len(p) > 9 else {})}
            if not d.get("batch"):
                d.pop("life", None), d.pop("near", None)
            row += [("Y" if d.get(k) else None) if k in FLAGS else d.get(k) for k in DETAIL_KEYS]
        ws.append(row)
        total_cost += cost * qty
        per_supplier.setdefault(suppliers[sup], [0, 0])
        per_supplier[suppliers[sup]][0] += 1
        per_supplier[suppliers[sup]][1] += cost * qty
    style(ws, build_from=len(UPLOAD_HEADERS) + 1)
    wb.save(path)
    return total_cost, packs, per_supplier


def style(ws, build_from=None):
    for cell in ws[1]:
        cell.font = Font(bold=True)
        if build_from and cell.column >= build_from:
            cell.fill = PatternFill("solid", fgColor="FFF2CC")
    for col in ws.columns:
        width = max(len(str(c.value)) if c.value is not None else 0 for c in col)
        ws.column_dimensions[col[0].column_letter].width = min(max(width + 2, 8), 48)
    ws.freeze_panes = "A2"


# ---------------------------------------------------------------- Hardware
HW_CATS = [
    ("Building Materials", "BLD", None), ("Cement & Adhesives", "CEM", "BLD"), ("Steel & Mesh", "STL", "BLD"),
    ("Sand & Ballast", "AGG", "BLD"), ("Timber & Boards", "TMB", "BLD"),
    ("Roofing", "ROF", None), ("Iron Sheets", "MAB", "ROF"), ("Roofing Accessories", "RFA", "ROF"),
    ("Nails, Fasteners & Wire", "FST", None), ("Nails", "NAI", "FST"), ("Screws, Bolts & Plugs", "SCR", "FST"),
    ("Wire & Fencing", "WRE", "FST"),
    ("Plumbing", "PLB", None), ("Pipes", "PIP", "PLB"), ("Pipe Fittings", "PFT", "PLB"), ("Taps & Valves", "TAP", "PLB"),
    ("Water Tanks", "TNK", "PLB"), ("Sanitaryware", "SAN", "PLB"),
    ("Paint & Finishes", "PNT", None), ("Emulsion & Wall Paint", "EMU", "PNT"), ("Gloss, Undercoat & Thinners", "GLS", "PNT"),
    ("Brushes, Rollers & Sundries", "PAC", "PNT"),
    ("Tools", "TLS", None), ("Hand Tools", "HTL", "TLS"), ("Power Tools & Accessories", "PTL", "TLS"),
    ("Electrical", "ELE", None), ("Cables", "ECB", "ELE"), ("Switches, Sockets & Bulbs", "ESW", "ELE"),
    ("Locks & Ironmongery", "LCK", None), ("Locks & Padlocks", "LKS", "LCK"), ("Hinges & Door Fittings", "HNG", "LCK"),
]
# Mark-up on cost by category, from the margins Kenyan hardware guides give: cement 2-6%, steel 5-10%, roofing 8-15%,
# paint 15-25%, plumbing and electrical 20-40%, nails and small hardware 30-50%.
HW_MARKUP = dict(CEM=0.045, STL=0.08, AGG=0.15, TMB=0.20, MAB=0.12, RFA=0.25, NAI=0.40, SCR=0.45, WRE=0.25, PIP=0.25,
                 PFT=0.45, TAP=0.35, TNK=0.15, SAN=0.25, EMU=0.20, GLS=0.22, PAC=0.40, HTL=0.35, PTL=0.25, ECB=0.30,
                 ESW=0.40, LKS=0.35, HNG=0.40)
HW_SUP = dict(CEM="Mavoko Cement Depot", STEEL="Kasarani Steel & Roofing Distributors", AGG="Mashuru Quarry & Sand Suppliers",
              TIM="Kitengela Timber Yard", PLB="Industrial Area Plumbing Wholesalers", PNT="Paints & Coatings Stockists Ltd",
              TOOL="Tools & Fasteners Wholesalers Ltd")
# (name, category, unit, reorder, retail, brand, opening qty, supplier, (pack unit, pack size, pack discount))
HW = [
    ("Bamburi Fundi Cement 32.5 50kg", "CEM", "bag", 100, 780, "Bamburi", 400, "CEM"),
    ("Simba Cement 32.5R 50kg", "CEM", "bag", 100, 735, "Simba", 300, "CEM"),
    ("Rhino Cement 32.5 50kg", "CEM", "bag", 60, 750, "Rhino", 200, "CEM"),
    ("Tile Adhesive 20kg", "CEM", "bag", 20, 950, None, 60, "CEM"),
    ("Y8 Deformed Bar 12m", "STL", "pcs", 40, 480, None, 200, "STEEL"),
    ("Y10 Deformed Bar 12m", "STL", "pcs", 40, 720, None, 200, "STEEL"),
    ("Y12 Deformed Bar 12m", "STL", "pcs", 40, 1050, None, 250, "STEEL"),
    ("Y16 Deformed Bar 12m", "STL", "pcs", 20, 1850, None, 80, "STEEL"),
    ("R6 Round Bar 12m", "STL", "pcs", 30, 280, None, 100, "STEEL"),
    ("BRC Mesh A142 2.4m x 4.8m", "STL", "sheet", 5, 9800, None, 20, "STEEL"),
    ("Binding Wire", "STL", "kg", 50, 220, None, 250, "STEEL", ("roll", 25, 0.06)),
    ("Ballast 3/4in (per tonne)", "AGG", "t", 10, 2600, None, 60, "AGG"),
    ("River Sand (per tonne)", "AGG", "t", 10, 2300, None, 60, "AGG"),
    ("Hardcore (per tonne)", "AGG", "t", 10, 1500, None, 40, "AGG"),
    ("Cypress 2x2 (per ft)", "TMB", "ft", 200, 35, None, 2000, "TIM"),
    ("Cypress 3x2 (per ft)", "TMB", "ft", 200, 50, None, 1500, "TIM"),
    ("Cypress 4x2 (per ft)", "TMB", "ft", 200, 70, None, 1500, "TIM"),
    ("Cypress 6x1 (per ft)", "TMB", "ft", 150, 55, None, 1000, "TIM"),
    ("Marine Plywood 18mm 8x4", "TMB", "sheet", 5, 4800, None, 20, "TIM"),
    ("Hardboard 3mm 8x4", "TMB", "sheet", 10, 1100, None, 30, "TIM"),
    ("Dumuzas G30 Corrugated 2m", "MAB", "sheet", 30, 900, "Mabati Rolling Mills", 150, "STEEL"),
    ("Dumuzas G30 Corrugated 2.5m", "MAB", "sheet", 30, 1120, "Mabati Rolling Mills", 150, "STEEL"),
    ("Dumuzas G30 Corrugated 3m", "MAB", "sheet", 30, 1350, "Mabati Rolling Mills", 200, "STEEL"),
    ("Dumuzas G28 Corrugated 3m", "MAB", "sheet", 20, 1650, "Mabati Rolling Mills", 100, "STEEL"),
    ("Box Profile G30 Maroon 2m", "MAB", "sheet", 20, 1350, "Mabati Rolling Mills", 100, "STEEL"),
    ("Box Profile G30 Maroon 3m", "MAB", "sheet", 20, 2000, "Mabati Rolling Mills", 120, "STEEL"),
    ("Box Profile G30 Charcoal Grey 3m", "MAB", "sheet", 20, 2000, "Mabati Rolling Mills", 80, "STEEL"),
    ("Ridge Cap G30 Plain 1.8m", "RFA", "pcs", 20, 650, None, 60, "STEEL"),
    ("Ridge Cap G30 Maroon 1.8m", "RFA", "pcs", 20, 850, None, 40, "STEEL"),
    ("Roofing Nails", "RFA", "kg", 20, 280, None, 100, "TOOL"),
    ("PVC Gutter 110mm 4m", "RFA", "pcs", 10, 1450, None, 20, "PLB"),
    ("Wire Nails 2in", "NAI", "kg", 50, 200, None, 300, "TOOL", ("bag", 25, 0.05)),
    ("Wire Nails 3in", "NAI", "kg", 50, 200, None, 300, "TOOL", ("bag", 25, 0.05)),
    ("Wire Nails 4in", "NAI", "kg", 50, 200, None, 250, "TOOL", ("bag", 25, 0.05)),
    ("Wire Nails 6in", "NAI", "kg", 30, 210, None, 150, "TOOL", ("bag", 25, 0.05)),
    ("Clout Nails 1.5in", "NAI", "kg", 20, 260, None, 50, "TOOL"),
    ("Wood Screw 8 x 1.5in", "SCR", "pcs", 500, 3, None, 3000, "TOOL", ("box", 100, 0.17)),
    ("Drywall Screw 1.25in", "SCR", "pcs", 500, 2.5, None, 3000, "TOOL", ("box", 100, 0.10)),
    ("Rawl Plug 6mm", "SCR", "pcs", 500, 2.5, None, 3000, "TOOL", ("pkt", 100, 0.25)),
    ("Hex Bolt & Nut M12 x 100", "SCR", "pcs", 50, 60, None, 200, "TOOL"),
    ("Anchor Bolt M10 x 100", "SCR", "pcs", 30, 80, None, 100, "TOOL"),
    ("Barbed Wire G14 Roll", "WRE", "roll", 5, 4200, None, 25, "STEEL"),
    ("Chain Link 5ft x 20m Roll", "WRE", "roll", 5, 5800, None, 15, "STEEL"),
    ("Galvanised Wire G12", "WRE", "kg", 20, 280, None, 80, "STEEL"),
    ("PVC Pipe 4in (110mm) Class B 6m", "PIP", "pcs", 20, 1650, None, 60, "PLB"),
    ("PVC Pipe 2in (50mm) Class B 6m", "PIP", "pcs", 20, 650, None, 60, "PLB"),
    ("PPR Pipe 20mm PN20 4m", "PIP", "pcs", 20, 380, None, 100, "PLB"),
    ("PPR Pipe 25mm PN20 4m", "PIP", "pcs", 20, 520, None, 60, "PLB"),
    ("GI Pipe 1/2in Medium 6m", "PIP", "pcs", 10, 1450, None, 40, "PLB"),
    ("GI Pipe 3/4in Medium 6m", "PIP", "pcs", 10, 1950, None, 30, "PLB"),
    ("HDPE Pipe 20mm", "PIP", "m", 100, 45, None, 500, "PLB", ("roll", 100, 0.07)),
    ("PPR Elbow 20mm", "PFT", "pcs", 50, 30, None, 300, "PLB"),
    ("PPR Tee 20mm", "PFT", "pcs", 50, 35, None, 300, "PLB"),
    ("PPR Socket 20mm", "PFT", "pcs", 50, 25, None, 300, "PLB"),
    ("PPR Female Adaptor 20mm x 1/2in", "PFT", "pcs", 30, 180, None, 100, "PLB"),
    ("PVC Elbow 4in", "PFT", "pcs", 20, 250, None, 60, "PLB"),
    ("PVC Tee 4in", "PFT", "pcs", 20, 350, None, 40, "PLB"),
    ("GI Elbow 1/2in", "PFT", "pcs", 30, 80, None, 100, "PLB"),
    ("GI Union 1/2in", "PFT", "pcs", 20, 150, None, 60, "PLB"),
    ("Teflon Tape", "PFT", "pcs", 50, 50, None, 200, "PLB"),
    ("PVC Solvent Cement 250ml", "PFT", "pcs", 20, 450, None, 40, "PLB"),
    ("Brass Bib Tap 1/2in", "TAP", "pcs", 10, 650, None, 40, "PLB"),
    ("Chrome Pillar Tap", "TAP", "pcs", 10, 1200, None, 20, "PLB"),
    ("Ball Valve 1/2in", "TAP", "pcs", 10, 450, None, 40, "PLB"),
    ("Float Valve 1/2in", "TAP", "pcs", 10, 750, None, 20, "PLB"),
    ("Sink Mixer Tap", "TAP", "pcs", 5, 3500, None, 10, "PLB"),
    ("Water Tank 1000L", "TNK", "pcs", 2, 11500, "Kentank", 6, "PLB"),
    ("Water Tank 2300L", "TNK", "pcs", 2, 22500, "Kentank", 4, "PLB"),
    ("Water Tank 5000L", "TNK", "pcs", 1, 48000, "Kentank", 2, "PLB"),
    ("Close-Coupled WC Set White", "SAN", "pcs", 3, 8500, None, 8, "PLB"),
    ("Wash Hand Basin with Pedestal", "SAN", "pcs", 3, 4200, None, 8, "PLB"),
    ("Plastic Cistern", "SAN", "pcs", 3, 2800, None, 8, "PLB"),
    ("Stainless Kitchen Sink Single Bowl", "SAN", "pcs", 3, 3800, None, 6, "PLB"),
    ("Chrome Shower Head", "SAN", "pcs", 5, 1200, None, 10, "PLB"),
    ("Vinyl Silk Emulsion White 4L", "EMU", "pcs", 10, 3200, "Crown Paints", 30, "PNT"),
    ("Vinyl Silk Emulsion White 20L", "EMU", "pcs", 5, 14500, "Crown Paints", 10, "PNT"),
    ("Vinyl Matt Emulsion Magnolia 4L", "EMU", "pcs", 10, 2700, "Crown Paints", 20, "PNT"),
    ("Exterior Emulsion White 20L", "EMU", "pcs", 5, 12500, "Basco", 8, "PNT"),
    ("Exterior Emulsion Cream 4L", "EMU", "pcs", 10, 2900, "Basco", 20, "PNT"),
    ("Gloss Enamel White 4L", "GLS", "pcs", 10, 3700, "Crown Paints", 20, "PNT"),
    ("Gloss Enamel Black 1L", "GLS", "pcs", 10, 1050, "Crown Paints", 20, "PNT"),
    ("Wood Primer Undercoat White 4L", "GLS", "pcs", 10, 2300, "Crown Paints", 15, "PNT"),
    ("Thinners 1L", "GLS", "pcs", 20, 450, None, 40, "PNT"),
    ("Thinners 5L", "GLS", "pcs", 5, 1900, None, 10, "PNT"),
    ("Clear Wood Varnish 1L", "GLS", "pcs", 10, 950, None, 15, "PNT"),
    ("Paint Brush 2in", "PAC", "pcs", 20, 150, None, 60, "PNT"),
    ("Paint Brush 3in", "PAC", "pcs", 20, 250, None, 60, "PNT"),
    ("Paint Brush 4in", "PAC", "pcs", 20, 350, None, 40, "PNT"),
    ("Paint Roller 9in with Tray", "PAC", "pcs", 10, 650, None, 30, "PNT"),
    ("Sandpaper Sheet", "PAC", "pcs", 50, 40, None, 200, "PNT"),
    ("Masking Tape 1in", "PAC", "pcs", 20, 150, None, 60, "PNT"),
    ("Wall Filler 5kg", "PAC", "pcs", 10, 950, None, 20, "PNT"),
    ("Claw Hammer 16oz", "HTL", "pcs", 5, 650, "Ingco", 20, "TOOL"),
    ("Jembe 2lb", "HTL", "pcs", 10, 750, "Crocodile", 30, "TOOL"),
    ("Panga", "HTL", "pcs", 10, 450, "Crocodile", 30, "TOOL"),
    ("Spade", "HTL", "pcs", 5, 850, None, 20, "TOOL"),
    ("Pick Axe with Handle", "HTL", "pcs", 5, 1200, None, 10, "TOOL"),
    ("Wheelbarrow Heavy Duty", "HTL", "pcs", 2, 6500, None, 8, "TOOL"),
    ("Karai Steel Basin", "HTL", "pcs", 10, 450, None, 30, "TOOL"),
    ("Plastering Trowel", "HTL", "pcs", 5, 450, None, 20, "TOOL"),
    ("Brick Trowel", "HTL", "pcs", 5, 350, None, 20, "TOOL"),
    ("Tape Measure 5m", "HTL", "pcs", 10, 400, "Ingco", 30, "TOOL"),
    ("Spirit Level 24in", "HTL", "pcs", 5, 900, None, 10, "TOOL"),
    ("Hacksaw Frame", "HTL", "pcs", 5, 650, None, 15, "TOOL"),
    ("Hacksaw Blade", "HTL", "pcs", 20, 80, None, 100, "TOOL"),
    ("Builders Line", "HTL", "pcs", 10, 150, None, 30, "TOOL"),
    ("Impact Drill 13mm 650W", "PTL", "pcs", 2, 5500, "Ingco", 5, "TOOL"),
    ("Angle Grinder 4.5in 750W", "PTL", "pcs", 2, 4800, "Ingco", 5, "TOOL"),
    ("Cordless Drill 20V Kit", "PTL", "pcs", 2, 9500, "Total", 4, "TOOL"),
    ("Metal Cutting Disc 4.5in", "PTL", "pcs", 20, 120, None, 100, "TOOL"),
    ("Masonry Drill Bit 8mm", "PTL", "pcs", 10, 150, None, 40, "TOOL"),
    ("Welding Rods 2.5mm", "PTL", "kg", 10, 450, None, 40, "TOOL"),
    ("Single Core Cable 2.5mm", "ECB", "m", 100, 65, "East African Cables", 500, "TOOL", ("roll", 100, 0.10)),
    ("Single Core Cable 1.5mm", "ECB", "m", 100, 45, "East African Cables", 500, "TOOL", ("roll", 100, 0.10)),
    ("Twin & Earth Cable 2.5mm", "ECB", "m", 50, 150, "East African Cables", 200, "TOOL", ("roll", 100, 0.10)),
    ("Switch 1-Gang 1-Way", "ESW", "pcs", 20, 250, "Tronic", 60, "TOOL"),
    ("Socket 13A Twin Switched", "ESW", "pcs", 20, 550, "Tronic", 40, "TOOL"),
    ("LED Bulb 9W B22", "ESW", "pcs", 20, 180, "Tronic", 80, "TOOL"),
    ("Batten Lamp Holder", "ESW", "pcs", 20, 120, None, 60, "TOOL"),
    ("PVC Conduit 20mm x 2.9m", "ESW", "pcs", 50, 120, None, 200, "TOOL"),
    ("Insulation Tape", "ESW", "pcs", 20, 60, None, 100, "TOOL"),
    ("Padlock 50mm Brass", "LKS", "pcs", 10, 650, "Solex", 30, "TOOL"),
    ("Padlock 70mm Brass", "LKS", "pcs", 5, 950, "Solex", 20, "TOOL"),
    ("Mortice Door Lock 3-Lever", "LKS", "pcs", 5, 1800, None, 15, "TOOL"),
    ("Door Handle Set Chrome", "LKS", "pcs", 5, 1200, None, 15, "TOOL"),
    ("Butt Hinge 4in", "HNG", "pcs", 20, 150, None, 100, "TOOL"),
    ("Tower Bolt 6in", "HNG", "pcs", 20, 120, None, 60, "TOOL"),
    ("Hasp & Staple 4in", "HNG", "pcs", 20, 150, None, 60, "TOOL"),
    ("Window Stay 10in", "HNG", "pcs", 20, 120, None, 60, "TOOL"),
]

# ---------------------------------------------------------------- Electrical
EL_CATS = [
    ("Cables & Wires", "CBL", None), ("House Wiring Cable", "HWC", "CBL"), ("Flexible, Armoured & Data Cable", "FLX", "CBL"),
    ("Wiring Accessories", "ACC", None), ("Switches & Sockets", "SWS", "ACC"), ("Conduit & Trunking", "CDT", "ACC"),
    ("Fixings & Consumables", "FXT", "ACC"),
    ("Distribution & Protection", "DSB", None), ("Breakers & RCDs", "MCB", "DSB"), ("Consumer Units & Ready Boards", "CUN", "DSB"),
    ("Isolators & Changeovers", "CHO", "DSB"),
    ("Lighting", "LGT", None), ("LED Bulbs & Tubes", "BLB", "LGT"), ("Floodlights & Security Lights", "FLD", "LGT"),
    ("Light Fittings", "LFT", "LGT"),
    ("Solar", "SOL", None), ("Solar Panels", "PNL", "SOL"), ("Batteries", "BTY", "SOL"), ("Inverters & Controllers", "INV", "SOL"),
    ("Solar Accessories", "SAC", "SOL"),
    ("Appliances & Power", "APL", None), ("Extensions & Adaptors", "EXT", "APL"), ("Voltage Protection", "VGD", "APL"),
    ("Small Appliances", "SAP", "APL"),
    ("Tools & Earthing", "TLE", None), ("Electrician Tools", "TOL", "TLE"), ("Earthing", "ERT", "TLE"),
]
EL_MARKUP = dict(HWC=0.25, FLX=0.30, SWS=0.40, CDT=0.45, FXT=0.50, MCB=0.35, CUN=0.25, CHO=0.30, BLB=0.40, FLD=0.35, LFT=0.40,
                 PNL=0.20, BTY=0.18, INV=0.20, SAC=0.35, EXT=0.40, VGD=0.40, SAP=0.25, TOL=0.40, ERT=0.35)
EL_SUP = dict(CBL="Industrial Area Cables Distributors", ACC="Nyota Electrical Wholesalers", SOL="Jua Power Solar Distributors",
              APL="Home Appliances Distributors Ltd")
ROLL = ("roll", 100, 0.10)
EL = [
    ("EAC Single Core 1.5mm Red", "HWC", "m", 100, 45, "East African Cables", 1000, "CBL", ROLL),
    ("EAC Single Core 1.5mm Black", "HWC", "m", 100, 45, "East African Cables", 1000, "CBL", ROLL),
    ("EAC Single Core 2.5mm Red", "HWC", "m", 100, 65, "East African Cables", 1500, "CBL", ROLL),
    ("EAC Single Core 2.5mm Black", "HWC", "m", 100, 65, "East African Cables", 1500, "CBL", ROLL),
    ("EAC Single Core 2.5mm Green/Yellow Earth", "HWC", "m", 100, 65, "East African Cables", 1000, "CBL", ROLL),
    ("EAC Single Core 4mm Red", "HWC", "m", 100, 110, "East African Cables", 500, "CBL", ROLL),
    ("EAC Single Core 4mm Black", "HWC", "m", 100, 110, "East African Cables", 500, "CBL", ROLL),
    ("EAC Single Core 6mm Red", "HWC", "m", 100, 160, "East African Cables", 300, "CBL", ROLL),
    ("EAC Single Core 6mm Black", "HWC", "m", 100, 160, "East African Cables", 300, "CBL", ROLL),
    ("EAC Single Core 10mm Black", "HWC", "m", 50, 270, "East African Cables", 200, "CBL", ROLL),
    ("Kenwest Single Core 2.5mm Red", "HWC", "m", 100, 60, "Kenwest Cables", 1000, "CBL", ROLL),
    ("Kenwest Single Core 2.5mm Black", "HWC", "m", 100, 60, "Kenwest Cables", 1000, "CBL", ROLL),
    ("Kenwest Twin & Earth 1.5mm", "HWC", "m", 100, 110, "Kenwest Cables", 500, "CBL", ROLL),
    ("Kenwest Twin & Earth 2.5mm", "HWC", "m", 100, 150, "Kenwest Cables", 500, "CBL", ROLL),
    ("Flex Cable 3-Core 1.5mm", "FLX", "m", 100, 90, None, 500, "CBL", ROLL),
    ("Flex Cable 3-Core 2.5mm", "FLX", "m", 100, 140, None, 300, "CBL", ROLL),
    ("Armoured Cable 4mm 3-Core", "FLX", "m", 50, 650, None, 200, "CBL"),
    ("Armoured Cable 6mm 3-Core", "FLX", "m", 50, 850, None, 100, "CBL"),
    ("Coaxial Cable RG6", "FLX", "m", 100, 40, None, 500, "CBL", ROLL),
    ("Cat6 Network Cable", "FLX", "m", 100, 60, None, 610, "CBL", ("box", 305, 0.18)),
    ("MK 1-Gang 1-Way Switch", "SWS", "pcs", 20, 650, "MK", 60, "ACC"),
    ("MK 2-Gang 2-Way Switch", "SWS", "pcs", 20, 950, "MK", 40, "ACC"),
    ("MK 13A Twin Switched Socket", "SWS", "pcs", 20, 1350, "MK", 60, "ACC"),
    ("MK 13A Single Switched Socket", "SWS", "pcs", 20, 850, "MK", 40, "ACC"),
    ("MK 45A Cooker Control Unit", "SWS", "pcs", 5, 2800, "MK", 10, "ACC"),
    ("MK 20A DP Switch with Neon", "SWS", "pcs", 10, 1200, "MK", 20, "ACC"),
    ("Tronic 1-Gang 1-Way Switch", "SWS", "pcs", 50, 220, "Tronic", 200, "ACC"),
    ("Tronic 2-Gang 1-Way Switch", "SWS", "pcs", 50, 300, "Tronic", 150, "ACC"),
    ("Tronic 13A Twin Switched Socket", "SWS", "pcs", 50, 480, "Tronic", 200, "ACC"),
    ("Tronic 13A Single Switched Socket", "SWS", "pcs", 50, 350, "Tronic", 150, "ACC"),
    ("Ceiling Rose", "SWS", "pcs", 50, 120, None, 150, "ACC"),
    ("Batten Lamp Holder", "SWS", "pcs", 50, 110, None, 200, "ACC"),
    ("Flush Box Single 3x3in", "SWS", "pcs", 100, 40, None, 400, "ACC", ("box", 50, 0.15)),
    ("Flush Box Double", "SWS", "pcs", 50, 60, None, 200, "ACC", ("box", 50, 0.15)),
    ("Blank Plate 1-Gang", "SWS", "pcs", 20, 150, None, 60, "ACC"),
    ("PVC Conduit 20mm x 2.9m", "CDT", "pcs", 100, 120, None, 500, "ACC"),
    ("PVC Conduit 25mm x 2.9m", "CDT", "pcs", 50, 160, None, 300, "ACC"),
    ("Conduit Bend 20mm", "CDT", "pcs", 100, 25, None, 500, "ACC"),
    ("Junction Box 20mm 1-Way", "CDT", "pcs", 100, 30, None, 400, "ACC"),
    ("Junction Box 20mm 3-Way", "CDT", "pcs", 100, 35, None, 400, "ACC"),
    ("Junction Box 20mm 4-Way", "CDT", "pcs", 50, 40, None, 200, "ACC"),
    ("Conduit Saddle 20mm", "CDT", "pcs", 200, 5, None, 2000, "ACC", ("pkt", 100, 0.20)),
    ("Mini Trunking 25x16mm 2m", "CDT", "pcs", 30, 220, None, 100, "ACC"),
    ("Flexible Conduit 20mm", "CDT", "m", 50, 35, None, 200, "ACC", ("roll", 50, 0.10)),
    ("PVC Insulation Tape", "FXT", "pcs", 50, 60, None, 300, "ACC", ("pkt", 10, 0.15)),
    ("Cable Ties 200mm (pack of 100)", "FXT", "pkt", 20, 180, None, 60, "ACC"),
    ("Cable Clips 7mm (pack of 100)", "FXT", "pkt", 20, 120, None, 60, "ACC"),
    ("Terminal Connector Strip 15A", "FXT", "pcs", 20, 150, None, 80, "ACC"),
    ("Lever Connector 3-Way", "FXT", "pcs", 50, 40, "Wago", 300, "ACC", ("box", 50, 0.15)),
    ("Hager MCB 1P 10A", "MCB", "pcs", 20, 750, "Hager", 40, "ACC"),
    ("Hager MCB 1P 16A", "MCB", "pcs", 20, 750, "Hager", 60, "ACC"),
    ("Hager MCB 1P 20A", "MCB", "pcs", 20, 750, "Hager", 60, "ACC"),
    ("Hager MCB 1P 32A", "MCB", "pcs", 20, 800, "Hager", 40, "ACC"),
    ("Hager RCCB 2P 40A 30mA", "MCB", "pcs", 5, 4800, "Hager", 10, "ACC"),
    ("Chint MCB 1P 16A", "MCB", "pcs", 30, 350, "Chint", 100, "ACC"),
    ("Chint MCB 1P 32A", "MCB", "pcs", 30, 380, "Chint", 80, "ACC"),
    ("Chint MCB 3P 63A", "MCB", "pcs", 5, 1800, "Chint", 15, "ACC"),
    ("Chint RCCB 2P 63A 30mA", "MCB", "pcs", 5, 2400, "Chint", 15, "ACC"),
    ("Surge Protection Device 2P", "MCB", "pcs", 5, 2500, None, 10, "ACC"),
    ("Hager Consumer Unit 4-Way", "CUN", "pcs", 5, 4200, "Hager", 10, "ACC"),
    ("Hager Consumer Unit 8-Way", "CUN", "pcs", 3, 6500, "Hager", 6, "ACC"),
    ("Chint Consumer Unit 4-Way", "CUN", "pcs", 5, 2500, "Chint", 15, "ACC"),
    ("Ready Board 2-Socket with MCB", "CUN", "pcs", 5, 3800, None, 20, "ACC"),
    ("Isolator 63A 2P", "CHO", "pcs", 5, 1500, None, 15, "ACC"),
    ("Manual Changeover Switch 63A", "CHO", "pcs", 3, 3500, None, 8, "ACC"),
    ("Manual Changeover Switch 100A 4P", "CHO", "pcs", 2, 6800, None, 4, "ACC"),
    ("Philips LED Bulb 9W B22 Daylight", "BLB", "pcs", 30, 280, "Philips", 120, "ACC"),
    ("Philips LED Bulb 13W B22 Daylight", "BLB", "pcs", 20, 380, "Philips", 80, "ACC"),
    ("Tronic LED Bulb 9W B22", "BLB", "pcs", 50, 160, "Tronic", 300, "ACC", ("box", 10, 0.10)),
    ("Tronic LED Bulb 12W B22", "BLB", "pcs", 50, 200, "Tronic", 200, "ACC", ("box", 10, 0.10)),
    ("Tronic LED Bulb 18W B22", "BLB", "pcs", 30, 300, "Tronic", 100, "ACC", ("box", 10, 0.10)),
    ("Tronic LED Bulb 9W E27", "BLB", "pcs", 30, 160, "Tronic", 100, "ACC", ("box", 10, 0.10)),
    ("LED Tube 4ft 18W with Fitting", "BLB", "pcs", 20, 650, None, 60, "ACC"),
    ("LED Tube 2ft 9W with Fitting", "BLB", "pcs", 20, 380, None, 40, "ACC"),
    ("LED Floodlight 50W", "FLD", "pcs", 10, 1800, None, 20, "ACC"),
    ("LED Floodlight 100W", "FLD", "pcs", 10, 3200, None, 15, "ACC"),
    ("LED Floodlight 200W", "FLD", "pcs", 5, 5800, None, 8, "ACC"),
    ("Solar Floodlight 100W with Panel", "FLD", "pcs", 5, 3500, None, 15, "SOL"),
    ("Motion Sensor Security Light", "FLD", "pcs", 5, 1500, None, 15, "ACC"),
    ("Round Bulkhead Light 15W", "LFT", "pcs", 10, 850, None, 30, "ACC"),
    ("Recessed LED Panel 18W Round", "LFT", "pcs", 20, 900, None, 40, "ACC"),
    ("LED Panel 600x600 48W", "LFT", "pcs", 5, 3200, None, 10, "ACC"),
    ("Batten Fitting 5ft Single", "LFT", "pcs", 10, 900, None, 20, "ACC"),
    ("Jinko 450W Mono Solar Panel", "PNL", "pcs", 5, 16500, "Jinko", 20, "SOL"),
    ("Longi 550W Mono Solar Panel", "PNL", "pcs", 5, 19500, "Longi", 12, "SOL"),
    ("Mono Solar Panel 100W", "PNL", "pcs", 5, 6500, None, 15, "SOL"),
    ("Mono Solar Panel 200W", "PNL", "pcs", 5, 11000, None, 10, "SOL"),
    ("Gel Battery 12V 100Ah", "BTY", "pcs", 4, 18500, None, 12, "SOL"),
    ("Gel Battery 12V 200Ah", "BTY", "pcs", 2, 33000, None, 6, "SOL"),
    ("Lithium Battery 12.8V 100Ah LiFePO4", "BTY", "pcs", 2, 45000, None, 4, "SOL"),
    ("Lithium Battery 48V 100Ah LiFePO4", "BTY", "pcs", 1, 145000, None, 2, "SOL"),
    ("Hybrid Inverter 3kVA 24V", "INV", "pcs", 2, 48000, None, 5, "SOL"),
    ("Hybrid Inverter 5kVA 48V", "INV", "pcs", 1, 78000, None, 3, "SOL"),
    ("Pure Sine Inverter 1kVA 12V", "INV", "pcs", 2, 16500, None, 6, "SOL"),
    ("PWM Charge Controller 30A", "INV", "pcs", 5, 2500, None, 15, "SOL"),
    ("MPPT Charge Controller 60A", "INV", "pcs", 2, 12500, None, 6, "SOL"),
    ("Solar PV Cable 4mm", "SAC", "m", 100, 120, None, 400, "SOL", ROLL),
    ("MC4 Connector Pair", "SAC", "pcs", 20, 250, None, 100, "SOL"),
    ("Panel Mounting Bracket Set", "SAC", "pcs", 5, 2800, None, 15, "SOL"),
    ("DC Breaker 2P 63A", "SAC", "pcs", 5, 1200, None, 15, "SOL"),
    ("Battery Cable 25mm", "SAC", "m", 20, 450, None, 60, "SOL"),
    ("Tronic 4-Way Extension 3m", "EXT", "pcs", 10, 1250, "Tronic", 40, "APL"),
    ("Tronic 6-Way Extension 5m", "EXT", "pcs", 10, 1950, "Tronic", 25, "APL"),
    ("3-Way Multi Plug Adaptor", "EXT", "pcs", 20, 250, None, 80, "APL"),
    ("Cable Reel Extension 25m", "EXT", "pcs", 3, 4500, None, 6, "APL"),
    ("Fridge Guard Voltage Protector", "VGD", "pcs", 10, 1500, None, 30, "APL"),
    ("TV Guard Voltage Protector", "VGD", "pcs", 10, 1500, None, 30, "APL"),
    ("Voltage Stabiliser 1000VA", "VGD", "pcs", 3, 6500, None, 6, "APL"),
    ("Whole House Voltage Protector 63A", "VGD", "pcs", 3, 4800, None, 6, "APL"),
    ("Electric Kettle 1.7L Stainless", "SAP", "pcs", 5, 2200, "Ramtons", 15, "APL"),
    ("Dry Iron Box", "SAP", "pcs", 5, 1500, None, 15, "APL"),
    ("Steam Iron", "SAP", "pcs", 3, 2800, "Philips", 8, "APL"),
    ("Instant Shower Heater 5.5kW", "SAP", "pcs", 5, 3800, "Lorenzetti", 15, "APL"),
    ("Single Hot Plate", "SAP", "pcs", 3, 1800, None, 8, "APL"),
    ("Ceiling Fan 56in", "SAP", "pcs", 2, 5500, None, 6, "APL"),
    ("Digital Multimeter", "TOL", "pcs", 3, 1800, None, 10, "ACC"),
    ("Clamp Meter", "TOL", "pcs", 2, 4500, None, 5, "ACC"),
    ("Tester Screwdriver", "TOL", "pcs", 20, 150, None, 60, "ACC"),
    ("Insulated Pliers 8in", "TOL", "pcs", 5, 650, None, 20, "ACC"),
    ("Wire Stripper", "TOL", "pcs", 5, 850, None, 15, "ACC"),
    ("Insulated Screwdriver Set", "TOL", "pcs", 5, 1200, None, 10, "ACC"),
    ("Crimping Tool", "TOL", "pcs", 2, 2200, None, 5, "ACC"),
    ("Copper Earth Rod 1.2m", "ERT", "pcs", 10, 1600, None, 30, "ACC"),
    ("Earth Rod Clamp", "ERT", "pcs", 20, 150, None, 60, "ACC"),
    ("Bare Copper Earth Wire 16mm", "ERT", "m", 20, 380, None, 100, "ACC"),
    ("Earth Bar 6-Way", "ERT", "pcs", 10, 300, None, 30, "ACC"),
]


# ---------------------------------------------------------------- Minimart
# Departments and codes as in MINIMART_ONBOARDING.md §3.
MM_CATS = [
    ("Staples & Dry Food", "DRY", None), ("Flour", "FLR", "DRY"), ("Rice", "RCE", "DRY"), ("Sugar", "SUG", "DRY"),
    ("Cooking Oil & Fats", "OIL", "DRY"), ("Pulses & Cereals", "PUL", "DRY"), ("Pasta & Noodles", "PST", "DRY"),
    ("Salt & Spices", "SPC", "DRY"),
    ("Bakery", "BKY", None), ("Bread", "BRD", "BKY"), ("Cakes & Buns", "CKE", "BKY"),
    ("Dairy & Eggs", "DAI", None), ("Milk", "MLK", "DAI"), ("Yoghurt & Mala", "YGT", "DAI"), ("Eggs", "EGG", "DAI"),
    ("Fresh Produce", "FRS", None), ("Vegetables", "VEG", "FRS"), ("Fruit", "FRT", "FRS"),
    ("Meat & Frozen", "MTF", None), ("Sausages & Processed", "SSG", "MTF"),
    ("Beverages", "BEV", None), ("Soft Drinks", "SFT", "BEV"), ("Water", "WTR", "BEV"), ("Juice", "JUC", "BEV"),
    ("Tea & Coffee", "TEA", "BEV"), ("Energy Drinks", "NRG", "BEV"),
    ("Snacks & Confectionery", "SNK", None), ("Biscuits", "BSC", "SNK"), ("Crisps", "CRS", "SNK"), ("Sweets & Chocolate", "SWT", "SNK"),
    ("Breakfast & Spreads", "BRK", None), ("Cereals & Porridge", "CER", "BRK"), ("Spreads & Jam", "SPD", "BRK"),
    ("Baby Care", "BBY", None), ("Diapers", "DPR", "BBY"), ("Baby Food", "BFD", "BBY"),
    ("Personal Care", "PER", None), ("Soap & Body", "SOP", "PER"), ("Oral Care", "ORL", "PER"), ("Hair", "HAR", "PER"),
    ("Sanitary", "SAN", "PER"), ("Lotions & Jelly", "DEO", "PER"),
    ("Household & Cleaning", "HSE", None), ("Laundry", "LND", "HSE"), ("Cleaning", "CLN", "HSE"), ("Tissue", "TIS", "HSE"),
    ("Insecticides", "INS", "HSE"),
    ("Gas & Energy", "GAS", None), ("LPG Refill", "LPR", "GAS"), ("Charcoal & Paraffin", "CHR", "GAS"), ("Batteries & Matches", "BAT", "GAS"),
    ("Stationery", "STN", None), ("School & Office", "SCH", "STN"),
    ("Bags & Packaging", "BAG", None), ("Carrier Bags", "CRB", "BAG"),
]
# Mark-up on cost: staples (unga, sugar, oil) 5-10% to bring people in; bread and milk 8-12% (the distributor sets the shelf
# price); drinks and toiletries 15-20%; snacks and sweets 20-30%; produce 40% to cover what spoils; carrier bags 50%.
MM_MARKUP = dict(FLR=0.06, RCE=0.15, SUG=0.06, OIL=0.10, PUL=0.20, PST=0.15, SPC=0.20, BRD=0.10, CKE=0.30, MLK=0.10, YGT=0.15,
                 EGG=0.15, VEG=0.40, FRT=0.40, SSG=0.15, SFT=0.18, WTR=0.25, JUC=0.18, TEA=0.15, NRG=0.20, BSC=0.20, CRS=0.25,
                 SWT=0.30, CER=0.15, SPD=0.12, DPR=0.15, BFD=0.10, SOP=0.15, ORL=0.15, HAR=0.20, SAN=0.15, DEO=0.18, LND=0.12,
                 CLN=0.18, TIS=0.15, INS=0.20, LPR=0.12, CHR=0.30, BAT=0.30, SCH=0.30, CRB=0.50)
# The usual VAT treatment, a starting point for the accountant (MINIMART_ONBOARDING.md §3, §12.2). The demo is not VAT-registered,
# so no VAT is charged whatever the class.
MM_TAX = dict(FLR="ZeroRated", BRD="ZeroRated", MLK="ZeroRated", EGG="Exempt", VEG="Exempt", FRT="Exempt")
MM_SUP = dict(WHL="Mwiki Wholesalers & Distributors", BRD="Mkate Fresh Bread Distributors", DAI="Maziwa Bora Dairy & Cold Chain",
              BEV="Kasarani Beverage Distributors", FRS="Githurai Market Produce (Mama Njeri)", EGG="Ruiru Poultry Farm",
              GAS="Mwiki Gas Point")
# Expiry is tracked only where it is managed: yoghurt, mala, UHT milk, sausages and baby food (MINIMART_ONBOARDING.md §6.4).
# Fresh milk and bread turn over daily and the van takes stale stock back, so they are not batch-tracked.
MM_DETAILS = dict(YGT=dict(batch=True, life=21), SSG=dict(batch=True, life=30), BFD=dict(batch=True, life=365))
BALE12 = ("bale", 12, 0.03)
CTN12 = ("ctn", 12, 0.05)
# (name, category, unit, reorder, retail, brand, opening qty, supplier, (pack unit, pack size, pack discount), details)
MM = [
    ("Soko Maize Meal 2kg", "FLR", "pcs", 24, 175, "Soko", 96, "WHL", BALE12),
    ("Jogoo Maize Meal 2kg", "FLR", "pcs", 24, 180, "Jogoo", 96, "WHL", BALE12),
    ("Pembe Maize Meal 2kg", "FLR", "pcs", 24, 175, "Pembe", 72, "WHL", BALE12),
    ("Jogoo Maize Meal 1kg", "FLR", "pcs", 12, 95, "Jogoo", 48, "WHL"),
    ("Ajab Home Baking Flour 2kg", "FLR", "pcs", 12, 195, "Ajab", 48, "WHL", BALE12),
    ("Exe All Purpose Flour 2kg", "FLR", "pcs", 12, 200, "Exe", 36, "WHL", BALE12),
    ("Pishori Rice (loose)", "RCE", "kg", 10, 220, None, 50, "WHL"),
    ("Daawat Basmati Rice 1kg", "RCE", "pcs", 6, 330, "Daawat", 24, "WHL"),
    ("Kabras Sugar 1kg", "SUG", "pcs", 24, 185, "Kabras", 72, "WHL", BALE12),
    ("Mumias Sugar 2kg", "SUG", "pcs", 12, 360, "Mumias", 30, "WHL"),
    ("Sugar (loose)", "SUG", "kg", 10, 170, None, 50, "WHL"),
    ("Rina Vegetable Oil 1L", "OIL", "pcs", 12, 290, "Rina", 36, "WHL", CTN12),
    ("Fresh Fri Cooking Oil 1L", "OIL", "pcs", 12, 300, "Fresh Fri", 36, "WHL", CTN12),
    ("Fresh Fri Cooking Oil 3L", "OIL", "pcs", 4, 880, "Fresh Fri", 12, "WHL"),
    ("Elianto Corn Oil 1L", "OIL", "pcs", 6, 380, "Elianto", 18, "WHL"),
    ("Kimbo Cooking Fat 500g", "OIL", "pcs", 12, 220, "Kimbo", 24, "WHL"),
    ("Kimbo Cooking Fat 1kg", "OIL", "pcs", 6, 420, "Kimbo", 12, "WHL"),
    ("Rosecoco Beans (loose)", "PUL", "kg", 10, 180, None, 25, "WHL"),
    ("Ndengu Green Grams (loose)", "PUL", "kg", 5, 200, None, 20, "WHL"),
    ("Spaghetti 400g", "PST", "pcs", 12, 110, None, 36, "WHL"),
    ("Indomie Chicken Noodles 70g", "PST", "pcs", 40, 35, "Indomie", 120, "WHL", ("ctn", 40, 0.05)),
    ("Kensalt Iodised Salt 1kg", "SPC", "pcs", 12, 50, "Kensalt", 48, "WHL"),
    ("Royco Mchuzi Mix 200g", "SPC", "pcs", 12, 120, "Royco", 24, "WHL"),
    ("Royco Beef Cube (single)", "SPC", "pcs", 50, 5, "Royco", 200, "WHL"),
    ("Supaloaf White Bread 400g", "BRD", "pcs", 10, 65, "Supaloaf", 30, "BRD"),
    ("Supaloaf Brown Bread 400g", "BRD", "pcs", 5, 70, "Supaloaf", 15, "BRD"),
    ("Festive White Bread 400g", "BRD", "pcs", 10, 65, "Festive", 30, "BRD"),
    ("Festive White Bread 800g", "BRD", "pcs", 4, 125, "Festive", 10, "BRD"),
    ("Mandazi (single)", "CKE", "pcs", 20, 10, None, 60, "BRD"),
    ("Queen Cakes (pack of 6)", "CKE", "pkt", 4, 100, None, 12, "BRD"),
    ("Brookside Whole Milk 500ml", "MLK", "pcs", 20, 65, "Brookside", 60, "DAI"),
    ("Fresha Milk 500ml", "MLK", "pcs", 12, 60, "Fresha", 40, "DAI"),
    ("KCC Gold Crown Milk 500ml", "MLK", "pcs", 12, 60, "KCC", 40, "DAI"),
    ("Brookside Long Life Milk 500ml", "MLK", "pcs", 12, 75, "Brookside", 48, "DAI", None, dict(batch=True, life=180)),
    ("Brookside Mala 500ml", "YGT", "pcs", 6, 85, "Brookside", 24, "DAI", None, dict(life=14)),
    ("Brookside Strawberry Yoghurt 500ml", "YGT", "pcs", 6, 150, "Brookside", 18, "DAI"),
    ("Daima Vanilla Yoghurt 250ml", "YGT", "pcs", 6, 80, "Daima", 24, "DAI"),
    ("Eggs (single)", "EGG", "pcs", 30, 20, None, 300, "EGG", ("tray", 30, 0.25)),
    ("Tomatoes", "VEG", "kg", 5, 120, None, 20, "FRS"),
    ("Red Onions", "VEG", "kg", 5, 140, None, 20, "FRS"),
    ("Potatoes", "VEG", "kg", 10, 90, None, 40, "FRS"),
    ("Sukuma Wiki (bunch)", "VEG", "pcs", 10, 20, None, 40, "FRS"),
    ("Cabbage (head)", "VEG", "pcs", 5, 60, None, 15, "FRS"),
    ("Dhania (bunch)", "VEG", "pcs", 10, 10, None, 30, "FRS"),
    ("Carrots", "VEG", "kg", 3, 100, None, 10, "FRS"),
    ("Bananas (single)", "FRT", "pcs", 20, 10, None, 100, "FRS"),
    ("Oranges (single)", "FRT", "pcs", 10, 20, None, 50, "FRS"),
    ("Avocado (single)", "FRT", "pcs", 10, 30, None, 40, "FRS"),
    ("Farmer's Choice Beef Sausages 500g", "SSG", "pcs", 4, 420, "Farmer's Choice", 12, "DAI"),
    ("Farmer's Choice Smokie (single)", "SSG", "pcs", 20, 30, "Farmer's Choice", 60, "DAI"),
    ("Coca-Cola 500ml", "SFT", "pcs", 24, 80, "Coca-Cola", 72, "BEV", CTN12),
    ("Coca-Cola 1.25L", "SFT", "pcs", 12, 140, "Coca-Cola", 36, "BEV"),
    ("Coca-Cola 2L", "SFT", "pcs", 6, 220, "Coca-Cola", 24, "BEV"),
    ("Fanta Orange 500ml", "SFT", "pcs", 12, 80, "Fanta", 48, "BEV", CTN12),
    ("Sprite 500ml", "SFT", "pcs", 12, 80, "Sprite", 36, "BEV", CTN12),
    ("Stoney Tangawizi 500ml", "SFT", "pcs", 12, 80, "Stoney", 36, "BEV", CTN12),
    ("Dasani Water 500ml", "WTR", "pcs", 24, 50, "Dasani", 48, "BEV", ("ctn", 24, 0.10)),
    ("Keringet Water 1L", "WTR", "pcs", 12, 90, "Keringet", 24, "BEV"),
    ("Minute Maid Mango 400ml", "JUC", "pcs", 12, 100, "Minute Maid", 24, "BEV"),
    ("Del Monte Orange Juice 1L", "JUC", "pcs", 4, 300, "Del Monte", 12, "BEV"),
    ("Ketepa Pride Tea Leaves 250g", "TEA", "pcs", 12, 150, "Ketepa", 24, "WHL"),
    ("Kericho Gold Tea Bags 50s", "TEA", "pcs", 4, 220, "Kericho Gold", 12, "WHL"),
    ("Nescafe Classic 50g", "TEA", "pcs", 4, 350, "Nescafe", 12, "WHL"),
    ("Milo 200g", "TEA", "pcs", 4, 450, "Milo", 12, "WHL"),
    ("Red Bull 250ml", "NRG", "pcs", 6, 250, "Red Bull", 24, "BEV"),
    ("Predator Energy 400ml", "NRG", "pcs", 12, 100, "Predator", 24, "BEV"),
    ("Nuvita Digestive Biscuits 100g", "BSC", "pcs", 12, 40, "Nuvita", 48, "WHL"),
    ("Manji Digestive Biscuits 200g", "BSC", "pcs", 6, 90, "Manji", 24, "WHL"),
    ("Tropical Heat Potato Crisps 50g", "CRS", "pcs", 12, 60, "Tropical Heat", 36, "WHL"),
    ("Krackles 30g", "CRS", "pcs", 24, 30, "Krackles", 60, "WHL"),
    ("Big G Chewing Gum (single)", "SWT", "pcs", 50, 5, "Big G", 200, "WHL", ("box", 100, 0.10)),
    ("Patco Lollipop (single)", "SWT", "pcs", 30, 10, "Patco", 100, "WHL"),
    ("Cadbury Dairy Milk 35g", "SWT", "pcs", 6, 120, "Cadbury", 24, "WHL"),
    ("Weetabix 450g", "CER", "pcs", 3, 420, "Weetabix", 12, "WHL"),
    ("Jungle Oats 500g", "CER", "pcs", 3, 270, "Jungle", 12, "WHL"),
    ("Millet Flour (Unga wa Wimbi) 1kg", "CER", "pcs", 4, 170, None, 12, "WHL"),
    ("Blue Band Original 250g", "SPD", "pcs", 6, 140, "Blue Band", 24, "WHL"),
    ("Blue Band Original 500g", "SPD", "pcs", 6, 260, "Blue Band", 24, "WHL"),
    ("Zesta Strawberry Jam 500g", "SPD", "pcs", 3, 260, "Zesta", 12, "WHL"),
    ("Zesta Peanut Butter 400g", "SPD", "pcs", 2, 350, "Zesta", 6, "WHL"),
    ("Softcare Diapers Medium (10)", "DPR", "pkt", 6, 320, "Softcare", 24, "WHL"),
    ("Softcare Diapers Large (10)", "DPR", "pkt", 6, 360, "Softcare", 24, "WHL"),
    ("Pampers Baby-Dry Size 4 (10)", "DPR", "pkt", 3, 550, "Pampers", 12, "WHL"),
    ("Cerelac Wheat 400g", "BFD", "pcs", 3, 680, "Nestle", 12, "WHL"),
    ("NAN 1 Infant Formula 400g", "BFD", "pcs", 2, 1950, "Nestle", 6, "WHL", None, dict(life=540)),
    ("Geisha Soap 90g", "SOP", "pcs", 12, 70, "Geisha", 48, "WHL"),
    ("Protex Herbal Soap 100g", "SOP", "pcs", 12, 110, "Protex", 36, "WHL"),
    ("Colgate Maximum Cavity 100ml", "ORL", "pcs", 6, 200, "Colgate", 24, "WHL"),
    ("Close Up Toothpaste 100ml", "ORL", "pcs", 4, 180, "Close Up", 12, "WHL"),
    ("Colgate Toothbrush Medium", "ORL", "pcs", 6, 80, "Colgate", 24, "WHL"),
    ("Dark and Lovely Hair Food 150g", "HAR", "pcs", 3, 250, "Dark and Lovely", 12, "WHL"),
    ("Always Maxi Thick Pads (8)", "SAN", "pkt", 12, 120, "Always", 36, "WHL"),
    ("Vaseline Petroleum Jelly 100ml", "DEO", "pcs", 6, 220, "Vaseline", 24, "WHL"),
    ("Nivea Body Lotion 400ml", "DEO", "pcs", 3, 700, "Nivea", 12, "WHL"),
    ("Nice & Lovely Lotion 400ml", "DEO", "pcs", 3, 400, "Nice & Lovely", 12, "WHL"),
    ("Omo Multi-Active 500g", "LND", "pcs", 12, 250, "Omo", 24, "WHL"),
    ("Omo Multi-Active 1kg", "LND", "pcs", 4, 470, "Omo", 12, "WHL"),
    ("Ariel Washing Powder 500g", "LND", "pcs", 6, 280, "Ariel", 12, "WHL"),
    ("Menengai Bar Soap 800g", "LND", "pcs", 12, 200, "Menengai", 24, "WHL"),
    ("Jik Bleach 750ml", "CLN", "pcs", 6, 160, "Jik", 24, "WHL"),
    ("Harpic Toilet Cleaner 500ml", "CLN", "pcs", 4, 300, "Harpic", 12, "WHL"),
    ("Sunlight Dishwashing Liquid 400ml", "CLN", "pcs", 4, 150, "Sunlight", 12, "WHL"),
    ("Steel Wool (single)", "CLN", "pcs", 12, 10, None, 50, "WHL"),
    ("Rosy Toilet Tissue (single roll)", "TIS", "pcs", 20, 35, "Rosy", 120, "WHL", ("pkt", 10, 0.08)),
    ("Doom Multi-Insect Spray 300ml", "INS", "pcs", 3, 450, "Doom", 12, "WHL"),
    ("Mosquito Coils (box of 10)", "INS", "pkt", 6, 120, None, 24, "WHL"),
    ("LPG Refill 6kg", "LPR", "pcs", 3, 1300, None, 10, "GAS"),
    ("LPG Refill 13kg", "LPR", "pcs", 2, 2800, None, 6, "GAS"),
    ("Charcoal (tin)", "CHR", "pcs", 10, 100, None, 40, "FRS"),
    ("Paraffin", "CHR", "l", 10, 180, None, 40, "GAS"),
    ("Eveready AA Battery (pair)", "BAT", "pcs", 6, 60, "Eveready", 48, "WHL"),
    ("Kasuku Matches (single box)", "BAT", "pcs", 20, 5, "Kasuku", 100, "WHL", ("pkt", 10, 0.10)),
    ("Kasuku Exercise Book 96 Pages", "SCH", "pcs", 24, 40, "Kasuku", 100, "WHL", ("doz", 12, 0.10)),
    ("Bic Pen Blue (single)", "SCH", "pcs", 20, 20, "Bic", 100, "WHL"),
    ("Carrier Bag Small (non-woven)", "CRB", "pcs", 50, 10, None, 200, "WHL"),
    ("Carrier Bag Large (non-woven)", "CRB", "pcs", 30, 20, None, 100, "WHL"),
]

# ---------------------------------------------------------------- Chemist
CH_CATS = [
    ("Pain, Cold & Allergy", "PCA", None), ("Pain Relief", "ANL", "PCA"), ("Cough, Cold & Flu", "CLD", "PCA"), ("Allergy", "ALG", "PCA"),
    ("Anti-infectives", "AIN", None), ("Antibiotics", "ABT", "AIN"), ("Antifungals", "AFG", "AIN"),
    ("Antimalarials & Dewormers", "AMW", "AIN"),
    ("Chronic Care", "CRC", None), ("Diabetes", "DIA", "CRC"), ("Blood Pressure & Heart", "HTN", "CRC"),
    ("Asthma & Steroids", "AST", "CRC"),
    ("Digestive Health", "DIG", None), ("Antacids & Ulcers", "ACD", "DIG"), ("Rehydration & Diarrhoea", "ORS", "DIG"),
    ("Skin", "SKN", None), ("Creams & Ointments", "TOP", "SKN"), ("Cosmetics & Skin Care", "CSM", "SKN"),
    ("Vitamins & Supplements", "VIT", None), ("Vitamins & Minerals", "VTM", "VIT"),
    ("Mother, Baby & Sexual Health", "MBS", None), ("Family Planning & Sexual Health", "FPL", "MBS"), ("Baby Care", "BAB", "MBS"),
    ("First Aid & Devices", "FAD", None), ("Wound Care", "WND", "FAD"), ("Medical Devices & Tests", "DEV", "FAD"),
    ("Eye & Ear", "EYE", None), ("Eye & Ear Drops", "EYD", "EYE"),
]
# Mark-up on cost by supplier: branded medicines 25-35%, generics 60-100% (the chemist's margin is in generics), surgical and
# devices about 40%, cosmetics about 25%.
CH_MARKUP = dict(DST=0.30, GEN=0.80, SRG=0.40, COS=0.25)
CH_SUP = dict(DST="Kilimani Pharmaceutical Distributors", GEN="Afya Generics Wholesalers", SRG="Mediplus Surgical Supplies Ltd",
              COS="Uzuri Cosmetics Distributors")
# Medicines are exempt from VAT (VAT Act, First Schedule); cosmetics and most devices are standard rated: confirm with the accountant.
MEDICINES = ["ANL", "CLD", "ALG", "ABT", "AFG", "AMW", "DIA", "HTN", "AST", "ACD", "ORS", "TOP", "VTM", "FPL", "BAB", "EYD"]
CH_TAX = {c: "Exempt" for c in MEDICINES}
# Every medicine is batch-tracked, with two years of shelf life unless the product says otherwise. Prescription-only (rx) follows
# usual Kenyan practice: antibiotics, oral antifungals, chronic medicines, steroids and inhalers. The superintendent confirms
# each against the PPB register before a real shop goes live.
CH_DETAILS = {c: dict(batch=True, life=730) for c in MEDICINES}
CH_DETAILS.update(ABT=dict(batch=True, life=730, rx=True), DIA=dict(batch=True, life=730, rx=True),
                  HTN=dict(batch=True, life=730, rx=True), AST=dict(batch=True, life=730, rx=True))
STRIP10 = ("strip", 10, 0.05)
BOX100 = ("box", 100, 0.10)


def med(generic, strength, form, **more):
    return dict(generic=generic, strength=strength, form=form, **more)


# (name, category, unit, reorder, retail, brand, opening qty, supplier, pack, details). near: the first batch received expires in
# that many days, so the expiry report and the near-expiry card have something to show.
CH = [
    ("Panadol Extra Tablets", "ANL", "tab", 100, 10, "Panadol", 600, "DST", ("strip", 10, 0.10), med("Paracetamol + Caffeine", "500mg/65mg", "Tablet")),
    ("Panadol Advance 500mg Tablets", "ANL", "tab", 50, 8, "Panadol", 400, "DST", ("strip", 10, 0.10), med("Paracetamol", "500mg", "Tablet")),
    ("Paracetamol 500mg Tablets", "ANL", "tab", 200, 2, None, 2000, "GEN", ("strip", 10, 0), med("Paracetamol", "500mg", "Tablet")),
    ("Mara Moja Tablets (2)", "ANL", "sachet", 40, 20, "Mara Moja", 200, "DST", None, med("Paracetamol + Aspirin + Caffeine", "2 tablets", "Tablet")),
    ("Ibuprofen 400mg Tablets", "ANL", "tab", 100, 3, None, 1000, "GEN", ("strip", 10, 0), med("Ibuprofen", "400mg", "Tablet")),
    ("Brufen 400mg Tablets", "ANL", "tab", 50, 15, "Brufen", 200, "DST", STRIP10, med("Ibuprofen", "400mg", "Tablet")),
    ("Diclofenac 50mg Tablets", "ANL", "tab", 100, 3, None, 600, "GEN", ("strip", 10, 0), med("Diclofenac sodium", "50mg", "Tablet")),
    ("Deep Heat Rub 35g", "ANL", "tube", 6, 450, "Deep Heat", 24, "DST", None, med("Methyl salicylate + Menthol", "35g", "Cream")),
    ("Coldcap Capsules", "CLD", "cap", 50, 10, "Coldcap", 300, "DST", STRIP10, med("Paracetamol + Phenylephrine + Chlorphenamine", "Combination", "Capsule")),
    ("Benylin 4 Flu Syrup 100ml", "CLD", "btl", 6, 550, "Benylin", 24, "DST", None, med("Paracetamol + Diphenhydramine + Pseudoephedrine", "100ml", "Syrup")),
    ("Expectorant Cough Syrup 100ml", "CLD", "btl", 10, 180, None, 40, "GEN", None, med("Guaifenesin", "100mg/5ml", "Syrup", near=50)),
    ("Strepsils Lozenges (8)", "CLD", "strip", 10, 200, "Strepsils", 48, "DST", None, med("Amylmetacresol + Dichlorobenzyl alcohol", "8 lozenges", "Lozenge")),
    ("Vicks VapoRub 25g", "CLD", "pcs", 6, 250, "Vicks", 36, "DST", None, med("Menthol + Camphor + Eucalyptus oil", "25g", "Ointment")),
    ("Saline Nasal Drops 10ml", "CLD", "btl", 6, 150, None, 24, "GEN", None, med("Sodium chloride", "0.9%", "Nasal drops")),
    ("Cetirizine 10mg Tablets", "ALG", "tab", 100, 5, None, 600, "GEN", ("strip", 10, 0.10), med("Cetirizine", "10mg", "Tablet")),
    ("Piriton 4mg Tablets", "ALG", "tab", 100, 3, "Piriton", 600, "DST", ("strip", 10, 0), med("Chlorphenamine", "4mg", "Tablet")),
    ("Loratadine 10mg Tablets", "ALG", "tab", 50, 5, None, 300, "GEN", ("strip", 10, 0.10), med("Loratadine", "10mg", "Tablet")),
    ("Amoxicillin 500mg Capsules", "ABT", "cap", 150, 10, None, 1000, "GEN", ("strip", 10, 0), med("Amoxicillin", "500mg", "Capsule")),
    ("Amoxil 500mg Capsules", "ABT", "cap", 50, 30, "Amoxil", 200, "DST", ("strip", 10, 0), med("Amoxicillin", "500mg", "Capsule")),
    ("Amoxicillin Suspension 125mg/5ml 100ml", "ABT", "btl", 10, 150, None, 30, "GEN", None, med("Amoxicillin", "125mg/5ml", "Suspension", life=540)),
    ("Augmentin 625mg Tablets", "ABT", "tab", 28, 150, "Augmentin", 140, "DST", ("strip", 14, 0.05), med("Amoxicillin + Clavulanic acid", "625mg", "Tablet")),
    ("Co-Amoxiclav 625mg Tablets", "ABT", "tab", 56, 60, None, 280, "GEN", ("strip", 14, 0.05), med("Amoxicillin + Clavulanic acid", "625mg", "Tablet")),
    ("Ciprofloxacin 500mg Tablets", "ABT", "tab", 50, 10, None, 500, "GEN", ("strip", 10, 0), med("Ciprofloxacin", "500mg", "Tablet")),
    ("Metronidazole 400mg Tablets", "ABT", "tab", 100, 2, None, 1000, "GEN", ("strip", 10, 0), med("Metronidazole", "400mg", "Tablet")),
    ("Flagyl 400mg Tablets", "ABT", "tab", 50, 12, "Flagyl", 200, "DST", ("strip", 10, 0), med("Metronidazole", "400mg", "Tablet")),
    ("Azithromycin 500mg Tablets (3)", "ABT", "pkt", 10, 300, None, 40, "GEN", None, med("Azithromycin", "500mg", "Tablet")),
    ("Doxycycline 100mg Capsules", "ABT", "cap", 50, 5, None, 500, "GEN", ("strip", 10, 0), med("Doxycycline", "100mg", "Capsule", near=40)),
    ("Cotrimoxazole 480mg Tablets", "ABT", "tab", 100, 2, None, 500, "GEN", ("strip", 10, 0), med("Sulfamethoxazole + Trimethoprim", "400mg/80mg", "Tablet")),
    ("Fluconazole 150mg Capsule", "AFG", "cap", 10, 60, None, 100, "GEN", None, med("Fluconazole", "150mg", "Capsule", rx=True)),
    ("Griseofulvin 500mg Tablets", "AFG", "tab", 30, 10, None, 200, "GEN", ("strip", 10, 0), med("Griseofulvin", "500mg", "Tablet", rx=True)),
    ("Clotrimazole Cream 1% 20g", "AFG", "tube", 10, 120, None, 40, "GEN", None, med("Clotrimazole", "1%", "Cream")),
    ("Canesten Cream 20g", "AFG", "tube", 4, 550, "Canesten", 12, "DST", None, med("Clotrimazole", "1%", "Cream")),
    ("Artemether/Lumefantrine 20/120mg Tablets (24)", "AMW", "pkt", 15, 250, None, 60, "GEN", None, med("Artemether + Lumefantrine", "20mg/120mg", "Tablet")),
    ("Coartem 20/120mg Tablets (24)", "AMW", "pkt", 6, 650, "Coartem", 24, "DST", None, med("Artemether + Lumefantrine", "20mg/120mg", "Tablet")),
    ("Albendazole 400mg Tablet", "AMW", "tab", 30, 30, None, 200, "GEN", None, med("Albendazole", "400mg", "Tablet")),
    ("Zentel 400mg Tablet", "AMW", "tab", 10, 100, "Zentel", 50, "DST", None, med("Albendazole", "400mg", "Tablet")),
    ("Metformin 500mg Tablets", "DIA", "tab", 300, 3, None, 2000, "GEN", BOX100, med("Metformin", "500mg", "Tablet")),
    ("Metformin 850mg Tablets", "DIA", "tab", 200, 4, None, 1000, "GEN", BOX100, med("Metformin", "850mg", "Tablet")),
    ("Glibenclamide 5mg Tablets", "DIA", "tab", 200, 2, None, 1000, "GEN", BOX100, med("Glibenclamide", "5mg", "Tablet")),
    ("Amlodipine 5mg Tablets", "HTN", "tab", 300, 3, None, 1500, "GEN", BOX100, med("Amlodipine", "5mg", "Tablet")),
    ("Amlodipine 10mg Tablets", "HTN", "tab", 200, 4, None, 1000, "GEN", BOX100, med("Amlodipine", "10mg", "Tablet")),
    ("Losartan 50mg Tablets", "HTN", "tab", 200, 10, None, 900, "GEN", ("strip", 10, 0), med("Losartan potassium", "50mg", "Tablet")),
    ("Hydrochlorothiazide 25mg Tablets", "HTN", "tab", 200, 2, None, 1000, "GEN", BOX100, med("Hydrochlorothiazide", "25mg", "Tablet")),
    ("Nifedipine 20mg Retard Tablets", "HTN", "tab", 200, 3, None, 1000, "GEN", BOX100, med("Nifedipine", "20mg", "Tablet (modified release)")),
    ("Atenolol 50mg Tablets", "HTN", "tab", 100, 2, None, 600, "GEN", BOX100, med("Atenolol", "50mg", "Tablet")),
    ("Aspirin 75mg Tablets", "HTN", "tab", 200, 1, None, 1000, "GEN", BOX100, med("Acetylsalicylic acid", "75mg", "Tablet", rx=False)),
    ("Salbutamol Inhaler 100mcg", "AST", "pcs", 5, 450, None, 20, "GEN", None, med("Salbutamol", "100mcg/dose", "Inhaler")),
    ("Ventolin Inhaler 100mcg", "AST", "pcs", 3, 750, "Ventolin", 3, "DST", None, med("Salbutamol", "100mcg/dose", "Inhaler")),
    ("Salbutamol 4mg Tablets", "AST", "tab", 50, 1, None, 500, "GEN", ("strip", 10, 0), med("Salbutamol", "4mg", "Tablet")),
    ("Prednisolone 5mg Tablets", "AST", "tab", 100, 2, None, 1000, "GEN", BOX100, med("Prednisolone", "5mg", "Tablet")),
    ("Gaviscon Liquid 150ml", "ACD", "btl", 4, 850, "Gaviscon", 12, "DST", None, med("Sodium alginate + Sodium bicarbonate + Calcium carbonate", "150ml", "Suspension")),
    ("Relcer Gel 180ml", "ACD", "btl", 4, 450, "Relcer", 12, "DST", None, med("Aluminium hydroxide + Magnesium hydroxide + Simethicone", "180ml", "Gel")),
    ("Omeprazole 20mg Capsules", "ACD", "cap", 100, 5, None, 1000, "GEN", ("strip", 10, 0), med("Omeprazole", "20mg", "Capsule")),
    ("Magnesium Trisilicate Tablets", "ACD", "tab", 100, 1, None, 1000, "GEN", BOX100, med("Magnesium trisilicate", "250mg", "Tablet")),
    ("Eno Fruit Salt Sachet", "ACD", "sachet", 20, 40, "Eno", 100, "DST", None, med("Sodium bicarbonate + Citric acid", "5g", "Powder")),
    ("ORS Sachet", "ORS", "sachet", 30, 20, None, 200, "GEN", None, med("Oral rehydration salts (low osmolarity)", "20.5g", "Powder")),
    ("Zinc 20mg Dispersible Tablets (10)", "ORS", "pkt", 10, 100, None, 40, "GEN", None, med("Zinc sulphate", "20mg", "Dispersible tablet")),
    ("Loperamide 2mg Capsules", "ORS", "cap", 50, 5, None, 300, "GEN", ("strip", 10, 0), med("Loperamide", "2mg", "Capsule")),
    ("Buscopan 10mg Tablets", "ORS", "tab", 50, 15, "Buscopan", 300, "DST", ("strip", 10, 0), med("Hyoscine butylbromide", "10mg", "Tablet")),
    ("Hydrocortisone Cream 1% 15g", "TOP", "tube", 10, 150, None, 30, "GEN", None, med("Hydrocortisone", "1%", "Cream")),
    ("Betamethasone Cream 0.1% 15g", "TOP", "tube", 10, 150, None, 30, "GEN", None, med("Betamethasone", "0.1%", "Cream", rx=True)),
    ("Silver Sulfadiazine Cream 1% 50g", "TOP", "tube", 3, 450, None, 10, "GEN", None, med("Silver sulfadiazine", "1%", "Cream", rx=True)),
    ("Calamine Lotion 100ml", "TOP", "btl", 6, 150, None, 24, "GEN", None, med("Calamine + Zinc oxide", "100ml", "Lotion", near=60)),
    ("Vaseline Petroleum Jelly 250ml", "CSM", "pcs", 6, 450, "Vaseline", 24, "COS"),
    ("Nivea Soft Cream 200ml", "CSM", "pcs", 4, 750, "Nivea", 12, "COS"),
    ("Cetaphil Gentle Skin Cleanser 250ml", "CSM", "pcs", 2, 2400, "Cetaphil", 6, "COS"),
    ("Nivea Sun SPF50 200ml", "CSM", "pcs", 2, 1800, "Nivea", 6, "COS"),
    ("Vaseline Lip Therapy 10g", "CSM", "pcs", 6, 250, "Vaseline", 24, "COS"),
    ("Aqueous Cream 500g", "CSM", "pcs", 4, 450, None, 12, "COS"),
    ("Vitamin C 1000mg Effervescent (10)", "VTM", "tube", 6, 450, None, 24, "GEN", None, med("Ascorbic acid", "1000mg", "Effervescent tablet")),
    ("Supradyn Daily Multivitamin (30)", "VTM", "pkt", 3, 1200, "Supradyn", 8, "DST", None, med("Multivitamin + Minerals", "30 tablets", "Tablet")),
    ("Ranferon-12 Syrup 200ml", "VTM", "btl", 4, 550, "Ranferon", 12, "DST", None, med("Ferrous fumarate + Folic acid + Vitamin B12", "200ml", "Syrup")),
    ("Folic Acid 5mg Tablets", "VTM", "tab", 100, 1, None, 1000, "GEN", BOX100, med("Folic acid", "5mg", "Tablet")),
    ("Seven Seas Cod Liver Oil Capsules (30)", "VTM", "pkt", 2, 1000, "Seven Seas", 6, "DST", None, med("Cod liver oil", "30 capsules", "Capsule")),
    ("Postinor-2 Tablets (2)", "FPL", "pkt", 10, 200, "Postinor", 30, "DST", None, med("Levonorgestrel", "0.75mg", "Tablet")),
    ("Combined Oral Contraceptive (28)", "FPL", "pkt", 10, 80, None, 30, "GEN", None, med("Levonorgestrel + Ethinylestradiol", "0.15mg/0.03mg", "Tablet")),
    ("Durex Condoms (3)", "FPL", "pkt", 6, 350, "Durex", 24, "COS", None, dict(batch=False)),
    ("Trust Condoms (3)", "FPL", "pkt", 20, 50, "Trust", 60, "COS", None, dict(batch=False)),
    ("Woodward's Gripe Water 150ml", "BAB", "btl", 4, 300, "Woodward's", 12, "DST", None, med("Sodium bicarbonate + Dill oil", "150ml", "Liquid")),
    ("Calpol Infant Suspension 60ml", "BAB", "btl", 6, 300, "Calpol", 24, "DST", None, med("Paracetamol", "120mg/5ml", "Suspension")),
    ("Paracetamol Syrup 120mg/5ml 60ml", "BAB", "btl", 10, 80, None, 40, "GEN", None, med("Paracetamol", "120mg/5ml", "Syrup")),
    ("Sudocrem 60g", "BAB", "pcs", 2, 950, "Sudocrem", 6, "DST", None, med("Zinc oxide", "15.25%", "Cream")),
    ("Johnson's Baby Oil 200ml", "BAB", "pcs", 4, 550, "Johnson's", 12, "COS", None, dict(batch=False)),
    ("Fabric Plaster (single)", "WND", "pcs", 100, 5, None, 500, "SRG", ("box", 100, 0.20)),
    ("Cotton Wool 100g", "WND", "pcs", 6, 120, None, 30, "SRG"),
    ("Crepe Bandage 10cm", "WND", "pcs", 6, 150, None, 30, "SRG"),
    ("Gauze Swabs (100)", "WND", "pkt", 3, 400, None, 10, "SRG"),
    ("Surgical Spirit 100ml", "WND", "btl", 6, 100, None, 24, "GEN"),
    ("Dettol Antiseptic Liquid 125ml", "WND", "btl", 6, 250, "Dettol", 24, "DST"),
    ("Hydrogen Peroxide 6% 100ml", "WND", "btl", 6, 100, None, 20, "GEN"),
    ("Digital Thermometer", "DEV", "pcs", 4, 400, None, 15, "SRG"),
    ("Omron Digital BP Monitor (upper arm)", "DEV", "pcs", 1, 4500, "Omron", 4, "SRG"),
    ("Glucometer with 10 Strips", "DEV", "pcs", 1, 3000, None, 4, "SRG"),
    ("Glucometer Test Strips (50)", "DEV", "pkt", 2, 2000, None, 6, "SRG", None, dict(batch=True, life=540)),
    ("Pregnancy Test Strip", "DEV", "pcs", 10, 80, None, 50, "SRG"),
    ("Surgical Face Mask (single)", "DEV", "pcs", 50, 10, None, 200, "SRG", ("box", 50, 0.40)),
    ("Chloramphenicol Eye Drops 0.5% 10ml", "EYD", "btl", 6, 100, None, 30, "GEN", None, med("Chloramphenicol", "0.5%", "Eye drops", rx=True, life=540)),
    ("Artificial Tears Eye Drops 10ml", "EYD", "btl", 4, 450, None, 12, "GEN", None, med("Hypromellose", "0.3%", "Eye drops")),
]

# ---------------------------------------------------------------- Liquor store (wines and spirits)
LQ_CATS = [
    ("Beer & Cider", "BER", None), ("Lager", "LAG", "BER"), ("Stout & Malt", "STO", "BER"), ("Cider & Ready-to-Drink", "CDR", "BER"),
    ("Spirits", "SPR", None), ("Whisky", "WHI", "SPR"), ("Vodka", "VOD", "SPR"), ("Gin", "GIN", "SPR"), ("Brandy & Cognac", "BRA", "SPR"),
    ("Rum & Cane Spirit", "RUM", "SPR"), ("Liqueurs & Creams", "LQR", "SPR"),
    ("Wine", "WIN", None), ("Red Wine", "RED", "WIN"), ("White & Rose Wine", "WHT", "WIN"), ("Sparkling & Champagne", "SPK", "WIN"),
    ("Mixers & Soft Drinks", "MIX", None), ("Sodas & Tonics", "SDA", "MIX"), ("Water", "WTR", "MIX"), ("Energy Drinks", "NRG", "MIX"),
    ("Ice", "ICE", "MIX"),
    ("Snacks", "SNK", None), ("Crisps & Nuts", "SNC", "SNK"),
    ("Tobacco", "TOB", None), ("Cigarettes", "CIG", "TOB"),
    ("Accessories & Bags", "ACC", None), ("Cups & Openers", "CUP", "ACC"), ("Carrier & Gift Bags", "BAG", "ACC"),
    ("Empties & Deposits", "EMP", None), ("Crates & Bottle Deposits", "DEP", "EMP"),
]
# Mark-up on cost: beer 10-12% (the brewer's distributor sets the price and every shop on the road sells the same crate); mainstream
# spirits 16-20%; premium spirits and liqueurs about 22%; wine 30% (slow, and the shop chooses what to stock); mixers 25%, snacks 30%,
# ice 60%; cigarettes 8%; deposits are passed on at cost.
LQ_MARKUP = dict(LAG=0.10, STO=0.10, CDR=0.12, WHI=0.20, VOD=0.18, GIN=0.18, BRA=0.20, RUM=0.16, LQR=0.22, RED=0.30, WHT=0.30,
                 SPK=0.30, SDA=0.25, WTR=0.30, NRG=0.20, ICE=0.60, SNC=0.30, CIG=0.08, CUP=0.50, BAG=0.50, DEP=0.0)
# Alcohol and soft drinks are standard rated (16%); excise duty is already in the distributor's price. A refundable deposit is not a
# sale of the crate: entered as exempt here, a starting point the accountant confirms.
LQ_TAX = dict(DEP="Exempt")
LQ_SUP = dict(KBL="Thika Road Beverages Ltd", WSW="Mlimani Wines & Spirits Wholesalers", SFT="Roysambu Soft Drinks Depot",
              SNK="Githurai Snacks & Ice Supplies", TOB="Kahawa Tobacco Distributors")
# Every drink with alcohol and every packet of cigarettes is age-restricted. Ciders and ready-to-drinks are slow and carry a
# best-before date, so they are batch-tracked (beer turns over in days and is not).
AGE = dict(age=True)
LQ_DETAILS = {c: AGE for c in ("LAG", "STO", "WHI", "VOD", "GIN", "BRA", "RUM", "LQR", "RED", "WHT", "SPK", "CIG")}
LQ_DETAILS["CDR"] = dict(age=True, batch=True, life=270)
CRATE_DEPOSIT = "Crate & 25 Empties (deposit)"
CRATE = ("crate", 25, 0.04, CRATE_DEPOSIT)   # a crate is cheaper per bottle; the empties and crate go with it, unless returned
CTN24 = ("ctn", 24, 0.04)
CASE12 = ("case", 12, 0.05)
# (name, category, unit, reorder, retail, brand, opening qty, supplier, (pack unit, size, discount, container), details)
LQ = [
    ("Tusker Lager 500ml Bottle", "LAG", "btl", 50, 250, "Tusker", 200, "KBL", CRATE),
    ("Tusker Lager 500ml Can", "LAG", "pcs", 24, 250, "Tusker", 96, "KBL", CTN24),
    ("Tusker Malt Lager 500ml Bottle", "LAG", "btl", 25, 270, "Tusker", 75, "KBL", CRATE),
    ("Tusker Lite 500ml Can", "LAG", "pcs", 24, 280, "Tusker", 48, "KBL", CTN24),
    ("Pilsner Lager 500ml Bottle", "LAG", "btl", 25, 230, "Pilsner", 75, "KBL", CRATE),
    ("Balozi Lager 500ml Bottle", "LAG", "btl", 25, 220, "Balozi", 75, "KBL", CRATE),
    ("White Cap Lager 500ml Bottle", "LAG", "btl", 25, 260, "White Cap", 75, "KBL", CRATE),
    ("Heineken 330ml Bottle", "LAG", "btl", 24, 250, "Heineken", 48, "WSW", CTN24),
    ("Corona Extra 355ml Bottle", "LAG", "btl", 12, 350, "Corona", 24, "WSW", CTN24),
    ("Guinness Foreign Extra Stout 500ml Bottle", "STO", "btl", 25, 270, "Guinness", 100, "KBL", CRATE),
    ("Guinness Smooth 500ml Can", "STO", "pcs", 24, 280, "Guinness", 48, "KBL", CTN24),
    ("Tusker Cider 500ml Can", "CDR", "pcs", 24, 260, "Tusker", 48, "KBL", CTN24),
    ("Smirnoff Ice Guarana 300ml Bottle", "CDR", "btl", 24, 220, "Smirnoff", 72, "KBL", CTN24),
    ("Snapp Apple 330ml Bottle", "CDR", "btl", 12, 230, "Snapp", 48, "KBL", CTN24, dict(near=45)),
    ("Savanna Dry Cider 330ml Bottle", "CDR", "btl", 12, 300, "Savanna", 24, "WSW", CTN24),
    ("Hunter's Gold Cider 330ml Bottle", "CDR", "btl", 12, 300, "Hunter's", 24, "WSW", CTN24),
    ("Chrome Vodka 250ml", "VOD", "btl", 24, 260, "Chrome", 96, "KBL", CTN24),
    ("Chrome Vodka 750ml", "VOD", "btl", 6, 700, "Chrome", 24, "KBL"),
    ("Smirnoff Red Vodka 250ml", "VOD", "btl", 12, 550, "Smirnoff", 36, "KBL"),
    ("Smirnoff Red Vodka 750ml", "VOD", "btl", 6, 1500, "Smirnoff", 18, "KBL", CASE12),
    ("Absolut Vodka 750ml", "VOD", "btl", 3, 2600, "Absolut", 6, "WSW"),
    ("Gilbey's Gin 250ml", "GIN", "btl", 12, 450, "Gilbey's", 48, "KBL", CTN24),
    ("Gilbey's Gin 750ml", "GIN", "btl", 6, 1250, "Gilbey's", 24, "KBL", CASE12),
    ("Chrome Gin 250ml", "GIN", "btl", 24, 260, "Chrome", 96, "KBL", CTN24),
    ("Chrome Gin 750ml", "GIN", "btl", 6, 700, "Chrome", 24, "KBL"),
    ("Best Gin 750ml", "GIN", "btl", 4, 1000, "Best", 12, "WSW"),
    ("Gordon's London Dry Gin 750ml", "GIN", "btl", 3, 2100, "Gordon's", 12, "KBL"),
    ("Tanqueray London Dry Gin 750ml", "GIN", "btl", 2, 3200, "Tanqueray", 6, "KBL"),
    ("Kenya Cane 250ml", "RUM", "btl", 24, 330, "Kenya Cane", 120, "KBL", CTN24),
    ("Kenya Cane 750ml", "RUM", "btl", 6, 950, "Kenya Cane", 36, "KBL", CASE12),
    ("Kenya Cane Pineapple 750ml", "RUM", "btl", 4, 950, "Kenya Cane", 12, "KBL"),
    ("Konyagi 750ml", "RUM", "btl", 4, 950, "Konyagi", 12, "WSW"),
    ("Captain Morgan Spiced Gold 750ml", "RUM", "btl", 4, 1500, "Captain Morgan", 12, "KBL"),
    ("Bacardi Carta Blanca 750ml", "RUM", "btl", 2, 2000, "Bacardi", 6, "WSW"),
    ("Hunter's Choice Whisky 250ml", "WHI", "btl", 12, 350, "Hunter's Choice", 48, "WSW", CTN24),
    ("Hunter's Choice Whisky 750ml", "WHI", "btl", 6, 1000, "Hunter's Choice", 18, "WSW"),
    ("Best Whisky 750ml", "WHI", "btl", 4, 1000, "Best", 12, "WSW"),
    ("Black & White Whisky 250ml", "WHI", "btl", 12, 550, "Black & White", 36, "KBL"),
    ("Black & White Whisky 750ml", "WHI", "btl", 4, 1500, "Black & White", 18, "KBL"),
    ("VAT 69 Whisky 750ml", "WHI", "btl", 3, 1900, "VAT 69", 12, "KBL"),
    ("Johnnie Walker Red Label 750ml", "WHI", "btl", 6, 2300, "Johnnie Walker", 24, "KBL", CASE12),
    ("Johnnie Walker Red Label 1L", "WHI", "btl", 3, 2900, "Johnnie Walker", 12, "KBL"),
    ("Johnnie Walker Black Label 750ml", "WHI", "btl", 3, 4500, "Johnnie Walker", 12, "KBL"),
    ("Jameson Irish Whiskey 750ml", "WHI", "btl", 4, 2900, "Jameson", 12, "WSW"),
    ("Jack Daniel's Old No. 7 700ml", "WHI", "btl", 2, 3900, "Jack Daniel's", 6, "WSW"),
    ("Grant's Triple Wood 750ml", "WHI", "btl", 3, 2200, "Grant's", 12, "WSW"),
    ("Famous Grouse 750ml", "WHI", "btl", 2, 2300, "Famous Grouse", 6, "WSW"),
    ("Singleton 12 Year Old 700ml", "WHI", "btl", 1, 6500, "Singleton", 3, "KBL"),
    ("Viceroy Brandy 250ml", "BRA", "btl", 12, 550, "Viceroy", 36, "WSW"),
    ("Viceroy Brandy 750ml", "BRA", "btl", 4, 1600, "Viceroy", 18, "WSW"),
    ("Richot Brandy 250ml", "BRA", "btl", 12, 450, "Richot", 36, "WSW"),
    ("Richot Brandy 750ml", "BRA", "btl", 4, 1300, "Richot", 12, "WSW"),
    ("Hennessy VS Cognac 700ml", "BRA", "btl", 1, 7000, "Hennessy", 4, "WSW"),
    ("Amarula Cream 750ml", "LQR", "btl", 3, 2600, "Amarula", 12, "WSW"),
    ("Baileys Original Irish Cream 750ml", "LQR", "btl", 3, 2900, "Baileys", 12, "KBL"),
    ("Jagermeister 700ml", "LQR", "btl", 2, 3000, "Jagermeister", 6, "WSW"),
    ("Four Cousins Sweet Red 750ml", "RED", "btl", 6, 1100, "Four Cousins", 36, "WSW", CASE12),
    ("Four Cousins Sweet Red 1.5L", "RED", "btl", 3, 1900, "Four Cousins", 12, "WSW"),
    ("4th Street Sweet Red 750ml", "RED", "btl", 6, 1100, "4th Street", 24, "WSW", CASE12),
    ("Robertson Sweet Red 750ml", "RED", "btl", 3, 1400, "Robertson", 12, "WSW"),
    ("Drostdy-Hof Claret Select 750ml", "RED", "btl", 3, 1250, "Drostdy-Hof", 12, "WSW"),
    ("Nederburg Cabernet Sauvignon 750ml", "RED", "btl", 2, 1700, "Nederburg", 6, "WSW"),
    ("Casillero del Diablo Cabernet Sauvignon 750ml", "RED", "btl", 2, 2100, "Casillero del Diablo", 6, "WSW"),
    ("Caprice Dry Red 750ml", "RED", "btl", 4, 850, "Caprice", 12, "WSW"),
    ("Four Cousins Sweet Rose 750ml", "WHT", "btl", 6, 1100, "Four Cousins", 24, "WSW", CASE12),
    ("4th Street Sweet Rose 750ml", "WHT", "btl", 4, 1100, "4th Street", 12, "WSW"),
    ("Drostdy-Hof Extra Light White 750ml", "WHT", "btl", 3, 1250, "Drostdy-Hof", 12, "WSW"),
    ("Nederburg Sauvignon Blanc 750ml", "WHT", "btl", 2, 1700, "Nederburg", 6, "WSW"),
    ("Caprice White 750ml", "WHT", "btl", 4, 850, "Caprice", 12, "WSW"),
    ("Martini Asti 750ml", "SPK", "btl", 2, 2800, "Martini", 6, "WSW"),
    ("JC Le Roux La Fleurette 750ml", "SPK", "btl", 2, 1900, "JC Le Roux", 6, "WSW"),
    ("Moet & Chandon Brut Imperial 750ml", "SPK", "btl", 1, 9500, "Moet & Chandon", 3, "WSW"),
    ("Coca-Cola 500ml", "SDA", "pcs", 24, 80, "Coca-Cola", 72, "SFT", ("ctn", 12, 0.05)),
    ("Coca-Cola 1.25L", "SDA", "pcs", 12, 140, "Coca-Cola", 36, "SFT"),
    ("Coca-Cola 2L", "SDA", "pcs", 6, 220, "Coca-Cola", 24, "SFT"),
    ("Sprite 500ml", "SDA", "pcs", 12, 80, "Sprite", 36, "SFT", ("ctn", 12, 0.05)),
    ("Stoney Tangawizi 500ml", "SDA", "pcs", 12, 80, "Stoney", 36, "SFT", ("ctn", 12, 0.05)),
    ("Schweppes Tonic Water 500ml", "SDA", "pcs", 12, 100, "Schweppes", 48, "SFT", ("ctn", 12, 0.05)),
    ("Schweppes Soda Water 500ml", "SDA", "pcs", 12, 100, "Schweppes", 36, "SFT"),
    ("Krest Bitter Lemon 500ml", "SDA", "pcs", 12, 100, "Krest", 24, "SFT"),
    ("Dasani Water 500ml", "WTR", "pcs", 24, 50, "Dasani", 72, "SFT", ("ctn", 24, 0.10)),
    ("Dasani Water 1L", "WTR", "pcs", 12, 90, "Dasani", 24, "SFT"),
    ("Keringet Sparkling Water 500ml", "WTR", "pcs", 6, 120, "Keringet", 24, "SFT"),
    ("Red Bull 250ml", "NRG", "pcs", 12, 250, "Red Bull", 48, "SFT", CTN24),
    ("Monster Energy 500ml", "NRG", "pcs", 6, 300, "Monster", 24, "SFT"),
    ("Predator Energy 400ml", "NRG", "pcs", 12, 100, "Predator", 48, "SFT"),
    ("Ice Cubes 2kg Bag", "ICE", "pcs", 10, 150, None, 30, "SNK"),
    ("Ice Cubes 5kg Bag", "ICE", "pcs", 5, 300, None, 15, "SNK"),
    ("Pringles Original 165g", "SNC", "pcs", 6, 500, "Pringles", 24, "SNK"),
    ("Tropical Heat Potato Crisps 50g", "SNC", "pcs", 12, 60, "Tropical Heat", 48, "SNK"),
    ("Roasted Groundnuts 100g", "SNC", "pcs", 20, 50, None, 60, "SNK"),
    ("Salted Cashew Nuts 100g", "SNC", "pcs", 6, 250, None, 24, "SNK"),
    ("Sportsman Cigarettes (20)", "CIG", "pkt", 10, 300, "Sportsman", 50, "TOB"),
    ("Embassy Kingsize Cigarettes (20)", "CIG", "pkt", 10, 350, "Embassy", 40, "TOB"),
    ("Dunhill Switch Cigarettes (20)", "CIG", "pkt", 6, 450, "Dunhill", 30, "TOB"),
    ("Disposable Cups (pack of 50)", "CUP", "pkt", 5, 150, None, 20, "SNK"),
    ("Bottle Opener", "CUP", "pcs", 5, 100, None, 20, "SNK"),
    ("Carrier Bag Small (non-woven)", "BAG", "pcs", 50, 10, None, 200, "SNK"),
    ("Bottle Gift Bag", "BAG", "pcs", 10, 100, None, 40, "SNK"),
    # The deposits: what the customer pays for the crate and empties, or for one empty, and gets back when they return them. Stock is
    # the crates and empties on hand (those inside full crates included).
    (CRATE_DEPOSIT, "DEP", "pcs", 4, 1200, None, 30, "KBL"),
    ("Empty Beer Bottle 500ml (deposit)", "DEP", "pcs", 50, 30, None, 300, "KBL"),
]


def run(prefix, cats, products, markups, suppliers, **options):
    if ONLY and prefix not in ONLY:
        return
    codes = {c[1] for c in cats}
    leafs = {c[1] for c in cats if c[2]}
    names = set()
    for p in products:
        assert p[1] in leafs, p
        assert p[0].upper() not in names, p[0]
        names.add(p[0].upper())
    assert len(codes) == len(cats)
    write_categories(OUT / f"{prefix}-categories.xlsx", cats)
    total, packs, per_supplier = write_products(OUT / f"{prefix}-products.xlsx", products, markups, suppliers, **options)
    vat = "without VAT" if options.get("vat_registered", True) else "as paid"
    print(f"== {prefix}: {len(cats)} categories, {len(products)} products, opening stock at cost {vat} KES {total:,.0f}")
    for s, (n, v) in per_supplier.items():
        print(f"   {s}: {n} lines, KES {v:,.0f}")
    print("   packs:")
    for name, base, unit, size, pr, pw in packs:
        print(f"   | {name} | {base} | {unit} | {size:g} | {pr:,} | {pw:,} |")


run("hardware", HW_CATS, HW, HW_MARKUP, HW_SUP)
run("electrical", EL_CATS, EL, EL_MARKUP, EL_SUP)
# Neither shop is VAT-registered (a small minimart, and a chemist whose sales are mostly exempt medicines): costs are as paid.
run("minimart", MM_CATS, MM, MM_MARKUP, MM_SUP, vat_registered=False, taxes=MM_TAX, defaults=MM_DETAILS)
run("chemist", CH_CATS, CH, CH_MARKUP, CH_SUP, vat_registered=False, taxes=CH_TAX, defaults=CH_DETAILS, markup_by_supplier=True)
# A wines and spirits shop on a busy road sells well over KES 5 million a year: VAT-registered, so costs go in without VAT.
run("liquor", LQ_CATS, LQ, LQ_MARKUP, LQ_SUP, taxes=LQ_TAX, defaults=LQ_DETAILS, containers=True)
