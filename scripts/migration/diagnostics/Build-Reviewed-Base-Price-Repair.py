"""Build an offline, reviewed SQL repair from the user's 38-column audit text.

No database connections. The SQL is standalone, outside the immutable migration
package. Candidate IDs, before/after prices and base conversion IDs are frozen.
"""
from argparse import ArgumentParser
from collections import Counter
from decimal import Decimal
from pathlib import Path


COLUMNS = """Report LegacyProductDetailId SourceCode SourceName ExpectedBaseUnit
SourceRetailPrice ProductBasePrice VariantRetailPrice BaseUnitRetailPrice
SourceWholesalePrice VariantWholesalePrice BaseUnitWholesalePrice
ProductRetailDelta VariantRetailDelta BaseRetailDelta VariantWholesaleDelta
BaseWholesaleDelta ProductRetailMismatch VariantRetailMismatch BaseRetailMismatch
VariantWholesaleMismatch BaseWholesaleMismatch TargetProductId TargetAlias
TargetVariantId VariantSku BaseConversionId BaseConversionCount ProductBaseUnit
ConversionBaseUnit BaseFactor MappingIssue UnitIssue SourceRetailMissing
SourceModifiedLocal ProductUpdatedLocal VariantUpdatedLocal BaseUnitUpdatedLocal""".split()


def money(value):
    return None if value == "NULL" else Decimal(value)


def discount(product_id, code):
    token = "".join(c for c in code.lower() if c.isalnum())
    return product_id == 384155 or token in {"chietkhauhoadon", "chietkhauthuongmai"}


def sql_money(value):
    return "NULL" if value is None else format(value, ".2f")


def sql_string(value):
    return "N'" + value.replace("'", "''") + "'"


def load_candidates(path):
    rows = []
    for line in Path(path).read_text(encoding="utf-8-sig").splitlines():
        if not line.startswith("PRICE_MISMATCHES\t"):
            continue
        values = line.split("\t")
        if len(values) != len(COLUMNS):
            raise ValueError(f"Expected {len(COLUMNS)} fields, got {len(values)}")
        row = dict(zip(COLUMNS, values))
        product_id = int(row["LegacyProductDetailId"])
        if discount(product_id, row["SourceCode"]):
            continue
        if (int(row["TargetProductId"]) != product_id
                or int(row["TargetVariantId"]) != product_id
                or row["VariantSku"] != row["SourceCode"]
                or int(row["BaseConversionCount"]) != 1
                or Decimal(row["BaseFactor"]) != 1
                or row["UnitIssue"] != "0"):
            raise ValueError(f"Unreviewed ID/unit mapping: {product_id}")
        for column in ("SourceRetailPrice", "SourceWholesalePrice"):
            price = money(row[column])
            if price is not None and price < 0:
                raise ValueError(f"Unreviewed negative source price: {product_id}")
        if money(row["SourceRetailPrice"]) is None:
            raise ValueError(f"Missing source retail price: {product_id}")
        if money(row["VariantRetailPrice"]) != money(row["SourceRetailPrice"]):
            raise ValueError("This reviewed repair does not change variant retail prices")
        rows.append(row)
    if len(rows) != 164 or len({r["LegacyProductDetailId"] for r in rows}) != 164:
        raise ValueError("Expected exactly 164 distinct reviewed, non-discount products")
    return rows


def build(rows):
    flags = ("ProductRetailMismatch", "VariantRetailMismatch", "VariantWholesaleMismatch",
             "BaseRetailMismatch", "BaseWholesaleMismatch")
    counts = [sum(int(r[f]) for r in rows) for f in flags]
    if counts != [149, 0, 26, 149, 133]:
        raise ValueError(f"Reviewed field counts changed: {counts}")
    values = []
    for row in rows:
        cells = [str(int(row["LegacyProductDetailId"])), sql_string(row["SourceCode"]),
                 sql_string(row["ExpectedBaseUnit"]), str(int(row["BaseConversionId"]))]
        cells += [sql_money(money(row[name])) for name in (
            "ProductBasePrice", "VariantRetailPrice", "VariantWholesalePrice",
            "BaseUnitRetailPrice", "BaseUnitWholesalePrice",
            "SourceRetailPrice", "SourceWholesalePrice")]
        values.append("    (" + ", ".join(cells) + ")")
    template = Path(__file__).with_name("Reviewed-Base-Price-Repair.template.sql").read_text(encoding="utf-8")
    if template.count("-- REVIEWED_VALUES_PLACEHOLDER") != 1:
        raise ValueError("SQL template placeholder missing or repeated")
    return template.replace("-- REVIEWED_VALUES_PLACEHOLDER", ",\n".join(values) + ";")


def main():
    parser = ArgumentParser()
    parser.add_argument("audit_text")
    parser.add_argument("output_sql")
    args = parser.parse_args()
    rows = load_candidates(args.audit_text)
    output = Path(args.output_sql)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(build(rows), encoding="utf-8-sig")
    counts = Counter()
    for r in rows:
        if r["ProductRetailMismatch"] == "1":
            counts["Products"] += 1
        if r["VariantWholesaleMismatch"] == "1":
            counts["ProductVariant"] += 1
        if r["BaseRetailMismatch"] == "1" or r["BaseWholesaleMismatch"] == "1":
            counts["ProductUnitConversion"] += 1
    print(f"164 candidates; excluded discount 384155; initial row updates: {dict(counts)}")
    print("Price field updates: 149 Product retail, 26 variant wholesale, 149 base retail, 133 base wholesale")
    print("No database connection made.")


if __name__ == "__main__":
    main()
