"""Builds the demo upload files for the hardware and electrical demos.

Sheet 1 of each products file is the API's bulk-upload layout (columns A-J). Columns K onward are a build sheet the
upload ignores (it reads A-J only): unit cost, opening quantity, supplier and pack, used to raise the opening purchase
orders and add the packs by hand.
"""
import math
import sys
from pathlib import Path
from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill

OUT = Path(sys.argv[1])
OUT.mkdir(parents=True, exist_ok=True)

UPLOAD_HEADERS = ["Name", "Category Code", "Item Type", "Tax Classification", "Unit Symbol", "Reorder Level",
                  "Wholesale Price", "Retail Price", "Description", "Brand"]
BUILD_HEADERS = ["Unit Cost ex VAT (build)", "Opening Qty (build)", "Supplier (build)", "Pack Unit (build)", "Pack Size (build)",
                 "Pack Retail (build)", "Pack Wholesale (build)"]


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


def write_products(path, products, markups, suppliers):
    wb = Workbook()
    ws = wb.active
    ws.title = "Products"
    ws.append(UPLOAD_HEADERS + BUILD_HEADERS)
    total_cost = 0
    packs = []
    per_supplier = {}
    for p in products:
        name, cat, unit, reorder, retail, brand, qty, sup = p[:8]
        pack = p[8] if len(p) > 8 else None
        markup = markups[cat]
        cost = cost_of(retail, markup)
        wholesale = wholesale_of(retail, cost)
        cost = cost_ex_vat(cost)
        row = [name, cat, "FinishedGood", "Standard", unit, reorder, wholesale, retail, None, brand or None,
               cost, qty, suppliers[sup]]
        if pack:
            pack_unit, size, discount = pack
            pack_retail = r5(retail * size * (1 - discount))
            pack_wholesale = r5(wholesale * size * (1 - discount))
            row += [pack_unit, size, pack_retail, pack_wholesale]
            packs.append((name, unit, pack_unit, size, pack_retail, pack_wholesale))
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


def run(prefix, cats, products, markups, suppliers):
    codes = {c[1] for c in cats}
    leafs = {c[1] for c in cats if c[2]}
    names = set()
    for p in products:
        assert p[1] in leafs, p
        assert p[0].upper() not in names, p[0]
        names.add(p[0].upper())
    assert len(codes) == len(cats)
    write_categories(OUT / f"{prefix}-categories.xlsx", cats)
    total, packs, per_supplier = write_products(OUT / f"{prefix}-products.xlsx", products, markups, suppliers)
    print(f"== {prefix}: {len(cats)} categories, {len(products)} products, opening stock at cost KES {total:,.0f}")
    for s, (n, v) in per_supplier.items():
        print(f"   {s}: {n} lines, KES {v:,.0f}")
    print("   packs:")
    for name, base, unit, size, pr, pw in packs:
        print(f"   | {name} | {base} | {unit} | {size:g} | {pr:,} | {pw:,} |")


run("hardware", HW_CATS, HW, HW_MARKUP, HW_SUP)
run("electrical", EL_CATS, EL, EL_MARKUP, EL_SUP)
