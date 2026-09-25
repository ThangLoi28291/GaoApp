using System.Diagnostics;

namespace GaoApp.Tests.Purchases;

public sealed class InputInvoiceReconciliationUiContractTests
{
    [Fact]
    public void Unsaved_commercial_preview_is_explicit_and_cannot_expose_authoritative_actions()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "GaoApp.Web",
            "wwwroot", "Admin", "js", "stock-document-management.js"));

        Assert.Contains("renderInputInvoiceReconciliationPreview", script,
            StringComparison.Ordinal);
        Assert.Contains("isCommercialPreview", script, StringComparison.Ordinal);
        Assert.Contains("Xem trước từ giá và thuế chưa lưu", script,
            StringComparison.Ordinal);
        Assert.Contains("const preview = model.isCommercialPreview === true", script,
            StringComparison.Ordinal);
        Assert.Contains("if (preview)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Receipt_screen_exposes_bounded_reconciliation_panel_and_existing_controller_routes()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin",
            "Views", "StockDocumentManagement", "Edit.cshtml"));
        var script = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin",
            "js", "stock-document-management.js"));
        var controller = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin",
            "Controllers", "StockDocumentsController.cs"));

        Assert.Contains("inputInvoiceReconciliationPanel", view, StringComparison.Ordinal);
        Assert.Contains("Đối chiếu XML — tùy chọn", view, StringComparison.Ordinal);
        Assert.Contains("aria-live", view, StringComparison.Ordinal);
        Assert.Contains("inputInvoiceReconciliationReasonModal", view,
            StringComparison.Ordinal);
        Assert.Contains("input-invoices/reconciliation", controller, StringComparison.Ordinal);
        Assert.Contains("/ignore", controller, StringComparison.Ordinal);
        Assert.Contains("/unignore", controller, StringComparison.Ordinal);
        Assert.Contains("/accept", controller, StringComparison.Ordinal);
        Assert.Contains("loadInputInvoiceReconciliation", script, StringComparison.Ordinal);
        Assert.Contains("renderInputInvoiceReconciliation", script, StringComparison.Ordinal);
        Assert.Contains("model.isConfirmedReadOnly === true", script, StringComparison.Ordinal);
        Assert.Contains("model.isLateAssociationException", script, StringComparison.Ordinal);
        Assert.Contains("Ngoại lệ XML đến sau xác nhận · chỉ đọc", script,
            StringComparison.Ordinal);
        Assert.Contains("reconciliationStateIsWarning", script, StringComparison.Ordinal);
        Assert.DoesNotContain("model.confirmReady ? 'text-success' : 'text-danger'", script,
            StringComparison.Ordinal);
        Assert.DoesNotContain("autoUnlink", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Product_summary_uses_compact_cards_and_whole_vnd_presentation()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "GaoApp.Web",
            "wwwroot", "Admin", "js", "stock-document-management.js"));

        Assert.Contains("function formatWholeVnd", script, StringComparison.Ordinal);
        Assert.Contains("minimumFractionDigits: 0", script, StringComparison.Ordinal);
        Assert.Contains("maximumFractionDigits: 0", script, StringComparison.Ordinal);
        Assert.Contains("product-reconciliation-card", script, StringComparison.Ordinal);
        Assert.DoesNotContain("<th>Sản phẩm</th><th>Phiếu nhập</th><th>XML</th>", script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Global_summary_omits_zero_noise_and_accounts_for_vat_review()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "GaoApp.Web",
            "wwwroot", "Admin", "js", "stock-document-management.js"));

        Assert.Contains("function inputInvoiceProductGlobalSummaryText", script,
            StringComparison.Ordinal);
        Assert.Contains("Tất cả khớp", script, StringComparison.Ordinal);
        Assert.Contains("cần xem xét", script, StringComparison.Ordinal);
        Assert.Contains("dòng XML chưa nhận diện", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Detail_evidence_is_collapsed_and_focus_opens_it()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "GaoApp.Web",
            "wwwroot", "Admin", "js", "stock-document-management.js"));

        Assert.Contains("<details id=\"inputInvoiceReconciliationDetailEvidence\"", script,
            StringComparison.Ordinal);
        Assert.Contains("<summary", script, StringComparison.Ordinal);
        Assert.Contains("Xem chi tiết</summary>", script, StringComparison.Ordinal);
        Assert.Contains("detailEvidence.open = true", script, StringComparison.Ordinal);
        Assert.Contains("table-responsive d-none d-md-block", script, StringComparison.Ordinal);
        Assert.Contains("d-md-none", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Detail_product_summary_membership_lookup_uses_exact_xml_detail_ids()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "GaoApp.Web",
            "wwwroot", "Admin", "js", "stock-document-management.js"));

        Assert.Contains("function buildInputInvoiceProductSummaryByDetailId", script,
            StringComparison.Ordinal);
        Assert.Contains("product.xmlDetails", script, StringComparison.Ordinal);
        Assert.Contains("xmlDetail.inputInvoiceDetailId", script, StringComparison.Ordinal);
        Assert.Contains("ambiguousDetailIds", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Recognized_detail_presentation_reuses_product_aggregate_evidence()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "GaoApp.Web",
            "wwwroot", "Admin", "js", "stock-document-management.js"));

        Assert.Contains("Đã tính vào đối chiếu tổng", script, StringComparison.Ordinal);
        Assert.Contains("SL tổng", script, StringComparison.Ordinal);
        Assert.Contains("Giá BQ", script, StringComparison.Ordinal);
        Assert.Contains("Đã ghép dòng", script, StringComparison.Ordinal);
        Assert.Contains("Chưa nhận diện", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Detail_wording_is_optional_and_manager_friendly()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "GaoApp.Web",
            "wwwroot", "Admin", "js", "stock-document-management.js"));

        Assert.Contains("Chưa nhận diện", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Unmatched: 'Chưa ghép dòng'", script,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Chưa ghép dòng phiếu", script, StringComparison.Ordinal);
        Assert.Contains("chưa ghép/bỏ qua", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Chưa đủ dữ liệu quy đổi để đối chiếu.", script,
            StringComparison.Ordinal);
        Assert.DoesNotContain("chưa gắn/bỏ qua", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reconciliation_renderer_executes_approved_evidence_and_keyboard_contract()
    {
        var root = FindRepositoryRoot();
        var scriptPath = Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin",
            "js", "stock-document-management.js");
        var harnessPath = Path.Combine(Path.GetTempPath(),
            $"gaoapp-recon-ui-{Guid.NewGuid():N}.js");
        File.WriteAllText(harnessPath, BuildNodeHarness(scriptPath));
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "node",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(harnessPath);
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to start Node.js.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await outputTask;
            var error = await errorTask;

            Assert.True(process.ExitCode == 0,
                $"Executed reconciliation UI contract failed. stdout={output} stderr={error}");
            Assert.Contains("RECON_UI_BEHAVIOR_PASS", output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(harnessPath);
        }
    }

    [Fact]
    public async Task Reconciliation_render_generation_keeps_newest_commercial_preview()
    {
        var root = FindRepositoryRoot();
        var scriptPath = Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin",
            "js", "stock-document-management.js");
        var harnessPath = Path.Combine(Path.GetTempPath(),
            $"gaoapp-recon-generation-{Guid.NewGuid():N}.js");
        File.WriteAllText(harnessPath, BuildFreshnessNodeHarness(scriptPath));
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "node",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(harnessPath);
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to start Node.js.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await outputTask;
            var error = await errorTask;

            Assert.True(process.ExitCode == 0,
                $"Reconciliation freshness contract failed. stdout={output} stderr={error}");
            Assert.Contains("RECON_GENERATION_PASS", output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(harnessPath);
        }
    }

    [Fact]
    public void Persisted_xml_structure_refresh_finishes_with_current_commercial_preview()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "GaoApp.Web",
            "wwwroot", "Admin", "js", "stock-document-management.js"));
        var refreshStart = script.IndexOf(
            "async function loadInputInvoicesForStockDocument()",
            StringComparison.Ordinal);
        var refreshEnd = script.IndexOf(
            "async function loadInputInvoiceAssociation()",
            refreshStart,
            StringComparison.Ordinal);
        Assert.True(refreshStart >= 0 && refreshEnd > refreshStart);
        var refresh = script[refreshStart..refreshEnd];

        var persisted = refresh.IndexOf(
            "await loadInputInvoiceReconciliation();",
            StringComparison.Ordinal);
        var commercial = refresh.IndexOf(
            "await refreshCommercialReconciliationPreviewAfterXmlMutation();",
            StringComparison.Ordinal);
        Assert.True(persisted >= 0 && commercial > persisted,
            "the persisted structural refresh must finish before the current-DOM preview");
        Assert.Contains(
            "window.GaoAppPurchaseReceiptApproval?.refreshReconciliationPreview",
            script,
            StringComparison.Ordinal);
    }

    private static string BuildNodeHarness(string scriptPath)
    {
        var escapedPath = scriptPath.Replace("\\", "\\\\").Replace("'", "\\'");
        return $$"""
            const fs = require('fs');
            const vm = require('vm');
            const assert = require('assert');
            let focused = [];
            class Element {
              constructor(id) {
                this.id = id;
                this.innerHTML = '';
                this.textContent = '';
                this.className = '';
                this.value = '';
                this.dataset = {};
                this.listeners = {};
                this.open = false;
                this.classList = { add() {}, remove() {}, contains() { return false; } };
              }
              addEventListener(type, callback) { this.listeners[type] = callback; }
              focus() { focused.push(this.id); }
            }
            const ids = [
              'inputInvoiceReconciliationPanel',
              'inputInvoiceReconciliationTitle',
              'inputInvoiceReconciliationState',
              'inputInvoiceReconciliationMessage',
              'inputInvoiceReconciliationTolerance',
              'inputInvoiceReconciliationHeader',
              'inputInvoiceReconciliationDetails',
              'inputInvoiceReconciliationAcceptance',
              'inputInvoiceReconciliationReasonSnapshot',
              'inputInvoiceReconciliationDetailEvidence',
              'IsConfirmedReceipt'
            ];
            const elements = Object.fromEntries(ids.map(id => [id, new Element(id)]));
            const mismatch = new Element('first-reconciliation-mismatch');
            const action = new Element('first-reconciliation-action');
            const document = {
              body: { dataset: {} },
              addEventListener() {},
              getElementById(id) { return elements[id] || null; },
              querySelector(selector) {
                if (selector === '[data-recon-focus="first-mismatch"]') return mismatch;
                if (selector === '[data-recon-focus="action"]') return action;
                return null;
              }
            };
            const context = {
              document,
              window: { stockDocumentPage: null },
              console,
              setTimeout(callback) { callback(); return 1; },
              clearTimeout() {},
              sessionStorage: { getItem() { return null; }, removeItem() {}, setItem() {} },
              location: { reload() {} },
              alert() {},
              URL: { revokeObjectURL() {}, createObjectURL() { return ''; } },
              Blob: function () {},
              fetch: async () => { throw new Error('fetch not expected'); }
            };
            vm.createContext(context);
            vm.runInContext(fs.readFileSync('{{escapedPath}}', 'utf8'), context);

            const model = {
              stateName: 'Mismatch', confirmReady: false,
              message: 'Đối chiếu có chênh lệch',
              quantityTolerance: 0.0001, moneyTolerance: 1,
              evidenceFingerprint: 'ABC123',
              header: {
                receiptSubtotalBeforeVat: 120, xmlTotalBeforeTax: 100,
                subtotalDifference: 20, receiptVatAmount: 12, xmlTaxAmount: 10,
                vatDifference: 2, receiptGoodsTotal: 132, xmlPaymentAmount: 110,
                paymentDifference: 22, needsReview: false
              },
              productCount: 3, matchedProductCount: 1, differingProductCount: 2,
              unresolvedXmlDetailCount: 3,
              productSummaries: [{
                productVariantId: 60, productDisplayName: 'Sữa A',
                receiptLineCount: 2, xmlDetailCount: 3,
                receiptBaseQuantity: 150, xmlBaseQuantity: 208,
                baseUnitDisplayName: 'Hộp', quantityDifference: 58,
                quantityStatus: 'XmlExcess', receiptBaseUnitPriceBeforeVat: 9000,
                xmlBaseUnitPriceBeforeVat: 7855, baseUnitPriceDifference: -1145,
                priceStatus: 'XmlLower', vatStatus: 'NeedsReview',
                receiptLines: [
                  { stockDocumentLineId: 50, productName: 'Sữa A',
                    unitName: 'Hộp', quantity: 6, factor: 1, baseQuantity: 6 },
                  { stockDocumentLineId: 51, productName: 'Sữa A',
                    unitName: 'Thùng', quantity: 3, factor: 48, baseQuantity: 144 }
                ],
                xmlDetails: [
                  { inputInvoiceDetailId: 30, itemName: 'Sữa A',
                    unitName: 'Thùng', quantity: 4, factor: 48, baseQuantity: 192 },
                  { inputInvoiceDetailId: 31, itemName: 'Sữa A',
                    unitName: 'Hộp', quantity: 2, factor: 1, baseQuantity: 2 },
                  { inputInvoiceDetailId: 34, itemName: 'Sữa A',
                    unitName: 'Hộp', quantity: 14, factor: 1, baseQuantity: 14 }
                ]
              }, {
                productVariantId: 61, productDisplayName: 'Sữa B',
                receiptLineCount: 1, xmlDetailCount: 1,
                receiptBaseQuantity: 108, xmlBaseQuantity: 96,
                baseUnitDisplayName: 'Hộp', quantityDifference: -12,
                quantityStatus: 'XmlShort', receiptBaseUnitPriceBeforeVat: 30000,
                xmlBaseUnitPriceBeforeVat: 31500, baseUnitPriceDifference: 1500,
                priceStatus: 'XmlHigher', vatStatus: 'Mismatch',
                receiptLines: [{ stockDocumentLineId: 52, productName: 'Sữa B',
                  unitName: 'Thùng', quantity: 2.25, factor: 48, baseQuantity: 108 }],
                xmlDetails: [{ inputInvoiceDetailId: 32, itemName: 'Sữa B',
                  unitName: 'Hộp', quantity: 96, factor: 1, baseQuantity: 96 }]
              }, {
                productVariantId: 62, productDisplayName: 'Sữa C',
                receiptLineCount: 1, xmlDetailCount: 1,
                receiptBaseQuantity: 108, xmlBaseQuantity: 120,
                baseUnitDisplayName: 'đơn vị gốc', quantityDifference: 12,
                quantityStatus: 'XmlExcess', receiptBaseUnitPriceBeforeVat: 30000,
                xmlBaseUnitPriceBeforeVat: 28500, baseUnitPriceDifference: -1500,
                priceStatus: 'XmlLower', vatStatus: 'NeedsReview',
                receiptLines: [{ stockDocumentLineId: 53, productName: 'Sữa C',
                  unitName: 'Lốc', quantity: 27, factor: 4, baseQuantity: 108 }],
                xmlDetails: [{ inputInvoiceDetailId: 33, itemName: 'Sữa C',
                  unitName: 'Hộp', quantity: 120, factor: 1, baseQuantity: 120 }]
              }],
              details: [{
                inputInvoiceDetailId: 30, itemName: 'Sữa A thùng', unitName: 'Thùng',
                stateName: 'CombinedMismatch', xmlQuantity: 4,
                confirmedFactor: 48, derivedBaseQuantity: 192,
                receiptBaseQuantity: 48, quantityDifference: 0,
                receiptBeforeVatAmount: 1440000, xmlBeforeVatAmount: 1440000,
                amountDifference: 0,
                receiptBaseUnitPriceBeforeVat: 30000,
                xmlBaseUnitPriceBeforeVat: 30000,
                baseUnitPriceDifference: 0,
                receiptVatRate: 10, xmlVatRate: 8,
                receiptVatAmount: 12, xmlVatAmount: 8,
                vatAmountDifference: 4, isIgnored: false,
                stockDocumentLineIds: [50],
                receiptLines: [{ stockDocumentLineId: 50, productName: 'Sữa A',
                  unitName: 'Thùng', quantity: 3, factor: 48, baseQuantity: 144 }]
              }, {
                inputInvoiceDetailId: 31, itemName: 'Sữa A hộp nhỏ', unitName: 'Hộp',
                stateName: 'Unmatched', xmlQuantity: 2,
                confirmedFactor: null, derivedBaseQuantity: null,
                receiptBaseQuantity: null, quantityDifference: 0,
                receiptBeforeVatAmount: 0, xmlBeforeVatAmount: 0, amountDifference: 0,
                receiptVatRate: 0, xmlVatRate: 0, receiptVatAmount: 0,
                xmlVatAmount: 0, vatAmountDifference: 0, isIgnored: false,
                stockDocumentLineIds: [],
                receiptLines: []
              }, {
                inputInvoiceDetailId: 34, itemName: 'Sữa A hộp lẻ', unitName: 'Hộp',
                stateName: 'Unmatched', xmlQuantity: 14,
                confirmedFactor: 1, derivedBaseQuantity: 14,
                receiptBaseQuantity: 0, quantityDifference: -14,
                receiptBeforeVatAmount: 0, xmlBeforeVatAmount: 109970,
                amountDifference: -109970, receiptVatRate: null, xmlVatRate: 10,
                receiptVatAmount: 0, xmlVatAmount: 10997,
                vatAmountDifference: -10997, isIgnored: false,
                stockDocumentLineIds: [], receiptLines: []
              }, {
                inputInvoiceDetailId: 35, itemName: 'XML chưa rõ', unitName: 'Gói',
                stateName: 'Unmatched', xmlQuantity: 5,
                confirmedFactor: null, derivedBaseQuantity: 0,
                receiptBaseQuantity: 0, quantityDifference: 0,
                receiptBeforeVatAmount: 0, xmlBeforeVatAmount: 50000,
                amountDifference: -50000, receiptVatRate: null, xmlVatRate: 10,
                receiptVatAmount: 0, xmlVatAmount: 5000,
                vatAmountDifference: -5000, isIgnored: false,
                stockDocumentLineIds: [], receiptLines: []
              }, {
                inputInvoiceDetailId: 36, itemName: 'XML bỏ qua', unitName: 'Hộp',
                stateName: 'Ignored', xmlQuantity: 1,
                confirmedFactor: null, derivedBaseQuantity: 0,
                receiptBaseQuantity: 0, quantityDifference: 0,
                receiptBeforeVatAmount: 0, xmlBeforeVatAmount: 10000,
                amountDifference: -10000, receiptVatRate: null, xmlVatRate: 10,
                receiptVatAmount: 0, xmlVatAmount: 1000,
                vatAmountDifference: -1000, isIgnored: true,
                ignoreReason: 'Không thuộc phiếu',
                stockDocumentLineIds: [], receiptLines: []
              }]
            };

            const serverDerivedPriceResult = context.inputInvoiceProductPriceResultHtml({
              baseUnitDisplayName: 'Hộp',
              receiptBaseUnitPriceBeforeVat: 1,
              xmlBaseUnitPriceBeforeVat: 99999,
              baseUnitPriceDifference: -577.885,
              priceStatus: 'XmlLower'
            });
            assert.match(serverDerivedPriceResult, /Giá XML thấp hơn 578 đ\/Hộp/);
            assert.doesNotMatch(serverDerivedPriceResult, /577,885|Giá XML cao hơn/);

            context.renderInputInvoiceReconciliation(model);
            const detailHtml = elements.inputInvoiceReconciliationDetails.innerHTML;
            const detailText = detailHtml.replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ');
            assert.match(detailHtml, /3 sản phẩm[^<]*2 có chênh lệch[^<]*3 dòng XML chưa nhận diện/i);
            assert.doesNotMatch(detailHtml, /1 khớp/);
            assert.match(detailHtml, /Sữa A/);
            assert.match(detailHtml, /6[^<]*Hộp[^<]*3[^<]*Thùng/);
            assert.match(detailHtml, /4[^<]*Thùng[^<]*2[^<]*Hộp[^<]*14[^<]*Hộp/);
            assert.match(detailHtml, /150[^<]*↔[^<]*208[^<]*Hộp/);
            assert.match(detailText, /SL 150 ↔ 208 Hộp XML dư 58 Hộp/);
            assert.match(detailHtml, /XML thiếu[^<]*12[^<]*Hộp/);
            assert.match(detailHtml, /XML dư[^<]*12[^<]*đơn vị gốc/);
            assert.match(detailText, /Giá 9[.]000 ↔ 7[.]855 đ\/Hộp Giá XML thấp hơn 1[.]145 đ\/Hộp/);
            assert.match(detailText, /Giá XML cao hơn 1[.]500 đ\/Hộp/);
            assert.match(detailText, /Giá XML thấp hơn 1[.]500 đ\/đơn vị gốc/);
            assert.match(detailText, /VAT VAT cần xem xét/);
            assert.match(detailHtml, /product-reconciliation-summary/);
            assert.match(detailHtml, /product-reconciliation-card/);
            assert.doesNotMatch(detailHtml, /<th>Sản phẩm<\/th>/);
            assert.strictEqual((detailHtml.match(/Đã tính vào đối chiếu tổng/g) || []).length, 6);
            assert.strictEqual((detailHtml.match(/SL tổng/g) || []).length, 6);
            assert.strictEqual((detailHtml.match(/Giá BQ/g) || []).length, 6);
            assert.strictEqual((detailHtml.match(/SL tổng:<\/span> 150 ↔ 208 Hộp/g) || []).length, 6);
            assert.strictEqual((detailHtml.match(/Giá BQ:<\/span> 9[.]000 ↔ 7[.]855 đ\/Hộp/g) || []).length, 6);
            assert.strictEqual((detailHtml.match(/Đã ghép dòng/g) || []).length, 2);
            assert.strictEqual((detailHtml.match(/Chưa nhận diện/g) || []).length, 2);
            assert.strictEqual((detailHtml.match(/Đã bỏ qua/g) || []).length, 2);
            assert.doesNotMatch(detailHtml, /Chưa ghép dòng|Chênh lệch số lượng:|Trước VAT phiếu\/XML\/lệch:|Tiền VAT phiếu\/XML\/lệch:|Quy đổi đối chiếu:/);
            assert.match(detailHtml, /recon-stacked-card/);
            assert.match(detailHtml, /d-md-none/);
            assert.match(detailHtml, /<details id="inputInvoiceReconciliationDetailEvidence"[^>]*>/);
            assert.doesNotMatch(detailHtml, /<details id="inputInvoiceReconciliationDetailEvidence"[^>]*\sopen(?:\s|>)/);
            assert.match(detailHtml, /<summary[^>]*>Xem chi tiết<\/summary>/);
            assert.doesNotMatch(detailHtml, /Chưa có dòng phiếu|Chưa map|Chưa đủ dữ liệu quy đổi để đối chiếu\./);
            assert.match(detailHtml, /js-recon-ignore/);
            assert.match(detailHtml, /js-recon-unignore/);
            assert.match(elements.inputInvoiceReconciliationTolerance.textContent,
              /0[,.]0001.*1/);

            const allMatched = JSON.parse(JSON.stringify(model));
            allMatched.productCount = 2;
            allMatched.matchedProductCount = 2;
            allMatched.differingProductCount = 0;
            allMatched.unresolvedXmlDetailCount = 0;
            allMatched.productSummaries = allMatched.productSummaries.slice(0, 1);
            allMatched.productSummaries[0].quantityStatus = 'Matched';
            allMatched.productSummaries[0].quantityDifference = 0;
            allMatched.productSummaries[0].priceStatus = 'Matched';
            allMatched.productSummaries[0].baseUnitPriceDifference = 0;
            allMatched.productSummaries[0].vatStatus = 'Matched';
            context.renderInputInvoiceReconciliation(allMatched);
            assert.match(elements.inputInvoiceReconciliationDetails.innerHTML,
              /2 sản phẩm[^<]*Tất cả khớp/);
            assert.doesNotMatch(elements.inputInvoiceReconciliationDetails.innerHTML,
              /0 có chênh lệch|0 dòng XML/);

            const vatOnly = JSON.parse(JSON.stringify(allMatched));
            vatOnly.productCount = 3;
            vatOnly.matchedProductCount = 3;
            vatOnly.productSummaries[0].vatStatus = 'NeedsReview';
            context.renderInputInvoiceReconciliation(vatOnly);
            assert.match(elements.inputInvoiceReconciliationDetails.innerHTML,
              /3 sản phẩm[^<]*1 cần xem xét/);
            assert.doesNotMatch(elements.inputInvoiceReconciliationDetails.innerHTML,
              /Tất cả khớp/);

            const unresolvedOnly = JSON.parse(JSON.stringify(model));
            unresolvedOnly.productCount = 0;
            unresolvedOnly.matchedProductCount = 0;
            unresolvedOnly.differingProductCount = 0;
            unresolvedOnly.unresolvedXmlDetailCount = 2;
            unresolvedOnly.productSummaries = [];
            context.renderInputInvoiceReconciliation(unresolvedOnly);
            assert.match(elements.inputInvoiceReconciliationDetails.innerHTML,
              /^<section[^>]*>\s*<div[^>]*>2 dòng XML chưa nhận diện<\/div>/);
            assert.doesNotMatch(elements.inputInvoiceReconciliationDetails.innerHTML,
              /0 sản phẩm|0 khớp|0 có chênh lệch/);

            context.renderInputInvoiceReconciliation(model);

            assert.strictEqual(typeof context.buildInputInvoiceReconciliationAcceptanceSnapshot,
              'function');
            const snapshot = context.buildInputInvoiceReconciliationAcceptanceSnapshot(model);
            assert.match(snapshot, /dòng lệch/i);
            assert.match(snapshot, /số lượng/i);
            assert.match(snapshot, /tiền\/VAT\/header/i);
            assert.match(snapshot, /chưa ghép\/bỏ qua/i);

            assert.strictEqual(typeof context.focusInputInvoiceReconciliationTarget, 'function');
            context.focusInputInvoiceReconciliationTarget('summary');
            assert.strictEqual(elements.inputInvoiceReconciliationDetailEvidence.open, false);
            context.focusInputInvoiceReconciliationTarget('first-mismatch');
            assert.strictEqual(elements.inputInvoiceReconciliationDetailEvidence.open, true);
            elements.inputInvoiceReconciliationDetailEvidence.open = false;
            context.focusInputInvoiceReconciliationTarget('action');
            assert.strictEqual(elements.inputInvoiceReconciliationDetailEvidence.open, true);
            assert.deepStrictEqual(focused, [
              'inputInvoiceReconciliationTitle',
              'first-reconciliation-mismatch',
              'first-reconciliation-action'
            ]);
            console.log('RECON_UI_BEHAVIOR_PASS');
            """;
    }

    private static string BuildFreshnessNodeHarness(string scriptPath)
    {
        var escapedPath = scriptPath.Replace("\\", "\\\\").Replace("'", "\\'");
        return $$"""
            const fs = require('fs');
            const vm = require('vm');
            const assert = require('assert');
            class Element {
              constructor(id) {
                this.id = id;
                this.innerHTML = '';
                this.textContent = '';
                this.className = '';
                this.value = '';
                this.dataset = {};
                this.classList = { add() {}, remove() {}, contains() { return false; } };
              }
              addEventListener() {}
              focus() {}
            }
            const ids = [
              'inputInvoiceReconciliationPanel', 'inputInvoiceReconciliationTitle',
              'inputInvoiceReconciliationState', 'inputInvoiceReconciliationMessage',
              'inputInvoiceReconciliationTolerance', 'inputInvoiceReconciliationHeader',
              'inputInvoiceReconciliationDetails', 'inputInvoiceReconciliationAcceptance',
              'IsConfirmedReceipt'
            ];
            const elements = Object.fromEntries(ids.map(id => [id, new Element(id)]));
            const document = {
              body: { dataset: {} },
              addEventListener() {},
              getElementById(id) { return elements[id] || null; },
              querySelector() { return null; }
            };
            const context = {
              document,
              window: { stockDocumentPage: null },
              console,
              setTimeout(callback) { callback(); return 1; },
              clearTimeout() {},
              sessionStorage: { getItem() { return null; }, removeItem() {}, setItem() {} },
              location: { reload() {} },
              alert() {},
              URL: { revokeObjectURL() {}, createObjectURL() { return ''; } },
              Blob: function () {},
              fetch: async () => { throw new Error('fetch not expected'); }
            };
            vm.createContext(context);
            vm.runInContext(fs.readFileSync('{{escapedPath}}', 'utf8'), context);

            function model(receiptSubtotal, receiptBasePrice) {
              return {
                stateName: 'Mismatch', message: 'preview',
                quantityTolerance: 0.0001, moneyTolerance: 1,
                header: {
                  receiptSubtotalBeforeVat: receiptSubtotal, xmlTotalBeforeTax: receiptSubtotal,
                  subtotalDifference: 0, receiptVatAmount: 0, xmlTaxAmount: 0,
                  vatDifference: 0, receiptGoodsTotal: receiptSubtotal,
                  xmlPaymentAmount: receiptSubtotal, paymentDifference: 0, needsReview: false
                },
                productCount: 1, matchedProductCount: 1, differingProductCount: 0,
                unresolvedXmlDetailCount: 0,
                productSummaries: [{
                  productVariantId: 12, productDisplayName: 'Sữa tươi',
                  receiptLineCount: 2, xmlDetailCount: 2,
                  receiptBaseQuantity: 116, xmlBaseQuantity: 116,
                  baseUnitDisplayName: 'Hộp', quantityDifference: 0,
                  quantityStatus: 'Matched',
                  receiptBaseUnitPriceBeforeVat: receiptBasePrice,
                  xmlBaseUnitPriceBeforeVat: receiptBasePrice,
                  baseUnitPriceDifference: 0, priceStatus: 'Matched', vatStatus: 'Matched',
                  receiptLines: [{ quantity: 20, unitName: 'Hộp' },
                    { quantity: 2, unitName: 'Thùng' }],
                  xmlDetails: [{ itemName: 'Sữa tươi', quantity: 116, unitName: 'Hộp' }]
                }],
                details: []
              };
            }

            assert.strictEqual(typeof context.beginInputInvoiceReconciliationRenderRequest,
              'function');
            assert.strictEqual(typeof context.renderInputInvoiceReconciliationIfCurrent,
              'function');

            const persistedGeneration = context.beginInputInvoiceReconciliationRenderRequest();
            const previewGeneration = context.beginInputInvoiceReconciliationRenderRequest();
            assert.strictEqual(context.renderInputInvoiceReconciliationIfCurrent(
              model(1044000, 9000), previewGeneration, true), true);
            assert.strictEqual(context.renderInputInvoiceReconciliationIfCurrent(
              model(0, 0), persistedGeneration, false), false);
            assert.match(elements.inputInvoiceReconciliationHeader.innerHTML,
              /1[.,]044[.,]000/);
            assert.match(elements.inputInvoiceReconciliationDetails.innerHTML, /9[.,]000/);

            const previewOne = context.beginInputInvoiceReconciliationRenderRequest();
            const previewTwo = context.beginInputInvoiceReconciliationRenderRequest();
            assert.strictEqual(context.renderInputInvoiceReconciliationIfCurrent(
              model(2222, 2222), previewTwo, true), true);
            assert.strictEqual(context.renderInputInvoiceReconciliationIfCurrent(
              model(1111, 1111), previewOne, true), false);
            assert.match(elements.inputInvoiceReconciliationHeader.innerHTML, /2[.,]222/);
            assert.match(elements.inputInvoiceReconciliationDetails.innerHTML, /2[.,]222/);

            const structuralGeneration = context.beginInputInvoiceReconciliationRenderRequest();
            assert.strictEqual(context.renderInputInvoiceReconciliationIfCurrent(
              model(0, 0), structuralGeneration, false), true);
            const lifecyclePreview = context.beginInputInvoiceReconciliationRenderRequest();
            assert.strictEqual(context.renderInputInvoiceReconciliationIfCurrent(
              model(1317600, 9200), lifecyclePreview, true), true);
            const latestEdit = context.beginInputInvoiceReconciliationRenderRequest();
            assert.strictEqual(context.renderInputInvoiceReconciliationIfCurrent(
              model(1320000, 9300), latestEdit, true), true);
            assert.strictEqual(context.renderInputInvoiceReconciliationIfCurrent(
              model(1317600, 9200), lifecyclePreview, true), false);
            assert.match(elements.inputInvoiceReconciliationHeader.innerHTML, /1[.,]320[.,]000/);
            assert.match(elements.inputInvoiceReconciliationDetails.innerHTML, /9[.,]300/);
            console.log('RECON_GENERATION_PASS');
            """;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException(
            "Repository root was not found.");
    }
}
