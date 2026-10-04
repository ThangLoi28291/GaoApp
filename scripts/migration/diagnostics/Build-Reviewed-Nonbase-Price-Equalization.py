"""Build a bounded SQL repair from the user's target-only pasted price listing."""
from decimal import Decimal
from pathlib import Path
import argparse


def sql_text(value: str) -> str:
    return "N'" + value.replace("'", "''") + "'"


def sql_price(value: Decimal | None) -> str:
    return "NULL" if value is None else format(value, ".2f")


def target_select(locked: bool) -> str:
    conversion_hint = " WITH (UPDLOCK,HOLDLOCK,ROWLOCK)" if locked else ""
    mapping_hint = " WITH (HOLDLOCK,ROWLOCK)" if locked else ""
    return f"""        SELECT c.Id,p.Id,v.Id,u.Id,v.Sku,p.Name,u.Name,c.Factor,c.Price,c.WholesalePrice,c.IsActive
        FROM #ReviewedUnits r
        JOIN dbo.ProductUnitConversion c{conversion_hint} ON c.Id=r.ConversionId
        JOIN dbo.ProductVariant v{mapping_hint} ON v.Id=c.ProductVariantId AND v.StoreId=@StoreId AND v.IsDeleted=0
        JOIN dbo.Products p{mapping_hint} ON p.Id=v.ProductId AND p.StoreId=@StoreId AND p.IsDeleted=0
        JOIN dbo.Unit u{mapping_hint} ON u.Id=c.UnitId AND u.StoreId=@StoreId AND u.IsDeleted=0
        WHERE c.StoreId=@StoreId AND c.IsBaseUnit=0 AND c.IsDeleted=0
            AND p.Id=r.ProductId AND v.Id=r.ProductVariantId
            AND v.Sku COLLATE DATABASE_DEFAULT=r.Sku COLLATE DATABASE_DEFAULT
            AND u.Name COLLATE DATABASE_DEFAULT=r.UnitName COLLATE DATABASE_DEFAULT
            AND c.Factor=r.Factor
            AND p.Id<>384155
            AND LTRIM(RTRIM(v.Sku)) COLLATE Latin1_General_100_CI_AI
                NOT IN (N'chietkhauhoadon',N'chietkhauthuongmai');"""


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    rows = []
    seen = set()
    retail_changes = wholesale_changes = 0
    for number, line in enumerate(args.input.read_text(encoding="utf-8-sig").splitlines(), 1):
        if not line.strip():
            continue
        cells = line.split("\t")
        if len(cells) != 12:
            raise ValueError(f"Line {number}: expected 12 fields, got {len(cells)}")
        product_id, variant_id, conversion_id = map(int, cells[:3])
        sku, unit_name = cells[3], cells[6]
        if conversion_id in seen:
            raise ValueError(f"Duplicate ConversionId {conversion_id}")
        seen.add(conversion_id)
        if product_id == 384155 or sku.strip().lower() in {"chietkhauhoadon", "chietkhauthuongmai"}:
            raise ValueError(f"Discount exception present: {conversion_id}")
        factor = Decimal(cells[7])
        retail = None if not cells[8].strip() or cells[8].strip().upper() == "NULL" else Decimal(cells[8])
        wholesale = None if not cells[9].strip() or cells[9].strip().upper() == "NULL" else Decimal(cells[9])
        values = [price for price in (retail, wholesale) if price is not None]
        if not values:
            raise ValueError(f"Both prices absent: {conversion_id}")
        new_price = max(values)
        if retail == new_price and wholesale == new_price:
            raise ValueError(f"Nothing to equalize in reviewed row: {conversion_id}")
        if factor <= 0 or not sku or not unit_name:
            raise ValueError(f"Invalid unit mapping: {conversion_id}")
        retail_changes += retail != new_price
        wholesale_changes += wholesale != new_price
        rows.append((conversion_id, product_id, variant_id, sku, unit_name, factor, retail, wholesale, new_price))
    if len(rows) != 1424 or (retail_changes, wholesale_changes) != (210, 1214):
        raise ValueError(f"Unexpected reviewed scope/counts: {len(rows)}, {retail_changes}, {wholesale_changes}")
    rows.sort(key=lambda row: row[0])
    inserts = []
    for offset in range(0, len(rows), 800):
        literals = []
        for conversion_id, product_id, variant_id, sku, unit_name, factor, retail, wholesale, new_price in rows[offset:offset+800]:
            literals.append(f"    ({conversion_id},{product_id},{variant_id},{sql_text(sku)},{sql_text(unit_name)},"
                            f"{factor:.4f},{sql_price(retail)},{sql_price(wholesale)},{sql_price(new_price)})")
        inserts.append("    INSERT #ReviewedUnits VALUES\n" + ",\n".join(literals) + ";")
    template_path = Path(__file__).with_name("Equalize-Reviewed-Nonbase-Prices.template.sql")
    sql = template_path.read_text(encoding="utf-8-sig")
    replacements = {
        "-- REVIEWED_UNIT_VALUES_PLACEHOLDER": "\n".join(inserts),
        "-- LOCKED_TARGET_SELECT_PLACEHOLDER": target_select(True),
        "-- PREVIEW_TARGET_SELECT_PLACEHOLDER": target_select(False),
    }
    for marker, replacement in replacements.items():
        if sql.count(marker) != 1:
            raise ValueError(f"Expected one {marker}")
        sql = sql.replace(marker, replacement)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(sql, encoding="utf-8-sig", newline="\r\n")
    print(f"BUILT: {len(rows)} units; retail changes={retail_changes}; wholesale changes={wholesale_changes}")
    print(args.output.resolve())


if __name__ == "__main__":
    main()
