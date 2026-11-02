# WEB00 independent contract validation

Run from the repository root with the installed .NET 10 SDK:

```powershell
dotnet run --project tools/validation/storefront-web00/Storefront.Web00.Validation.csproj -- --results .artifacts/storefront-web00
```

The standalone console harness compiles only its own test runner and the six
`GaoApp.Domain/Storefront/*.cs` executable contracts. It has no project reference,
package, database, network, ACB transport, or web startup dependency. Its local
NuGet configuration clears package sources. Build intermediates, binaries and
machine-readable results are under `.artifacts/storefront-web00/`.

The requirement oracle was prepared independently before reading the APIs.
Cases exercise allowed and denied state changes, guest access, permissions,
revision conflicts, receipt binding and retained mismatch money, late/duplicate
payments, unresolved receipt review across successive payments, shipping
boundaries and overflow, negative stock and stock budgets, shipping accounting
choices and carrier payable differences, COD remittance, and evidenced refund budgets.
The report records a hash of each tested source file.

These tests verify only pure decisions. They do not verify HTTP authorization,
server-side projection integrity, durable bank ingestion, signatures, database
locking/idempotency, receipt allocation uniqueness, stock/financial postings,
POS regression, price/publication revalidation, real shipment providers,
invoices, migrations, or the mobile UI. Those require the later implementation
and isolated integration/browser fixtures specified in the storefront plan.
