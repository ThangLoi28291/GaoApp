using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class PosCockpitModernUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string IndexPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Views", "POS", "Index.cshtml");

    private static readonly string HeaderPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Views", "POS", "_CockpitHeader.cshtml");

    private static readonly string ToolbarPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Views", "POS", "_Toolbar.cshtml");
    private static readonly string CartPath = Path.Combine(
    RepositoryRoot,
    "GaoApp.Web", "Areas", "Admin", "Views", "POS", "_CartTable.cshtml");

    private static readonly string CustomerPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Views", "POS", "_CustomerBox.cshtml");

    private static readonly string SummaryPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Views", "POS", "_SummaryPanel.cshtml");

    private static readonly string ActionsPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Views", "POS", "_SummaryActionsPanel.cshtml");

    private static readonly string DraftMetaPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Views", "POS", "_DraftMetaPanel.cshtml");

    private static readonly string HeldPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Views", "POS", "_HeldOrdersPanel.cshtml");

    private static readonly string StylePath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "wwwroot", "Admin", "css", "pos", "pos-cockpit-v2.css");

    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "wwwroot", "Admin", "js", "pos", "pos.cockpit.js");

    [Fact]
    public void Index_should_use_cashier_cockpit_shell_and_preserve_existing_POS_runtime()
    {
        var index = File.ReadAllText(IndexPath);

        Assert.Contains("pos-cockpit-v2", index, StringComparison.Ordinal);
        Assert.Contains("_CockpitHeader", index, StringComparison.Ordinal);
        Assert.Contains("pos-cockpit-workspace", index, StringComparison.Ordinal);
        Assert.Contains("pos-cockpit-cart-pane", index, StringComparison.Ordinal);
        Assert.Contains("pos-cockpit-checkout", index, StringComparison.Ordinal);

        Assert.DoesNotContain("_POSNavTabs.cshtml", index, StringComparison.Ordinal);
        Assert.DoesNotContain("TÍNH TIỀN", index, StringComparison.Ordinal);
        Assert.DoesNotContain("pos-bottom-action-bar", index, StringComparison.Ordinal);

        var legacyStyleIndex =
            index.IndexOf(
                "~/Admin/css/pos/pos.css",
                StringComparison.Ordinal);

        var cockpitStyleIndex =
            index.IndexOf(
                "~/Admin/css/pos/pos-cockpit-v2.css",
                StringComparison.Ordinal);

        Assert.True(legacyStyleIndex >= 0);
        Assert.True(cockpitStyleIndex > legacyStyleIndex);

        var cockpitScriptIndex =
            index.IndexOf(
                "~/Admin/js/pos/pos.cockpit.js",
                StringComparison.Ordinal);

        var appInitIndex =
            index.IndexOf(
                "window.PosApp.create()",
                StringComparison.Ordinal);

        Assert.True(cockpitScriptIndex >= 0);
        Assert.True(appInitIndex > cockpitScriptIndex);
    }

    [Fact]
    public void Cockpit_header_should_reuse_existing_context_held_orders_customer_display_and_shortcuts()
    {
        var header = File.ReadAllText(HeaderPath);
        var held = File.ReadAllText(HeldPath);

        Assert.Contains("POSHeaderContextViewModel", header, StringComparison.Ordinal);
        Assert.Contains("Model.ShiftCode", header, StringComparison.Ordinal);
        Assert.Contains("Model.WarehouseName", header, StringComparison.Ordinal);
        Assert.Contains("Model.UserName", header, StringComparison.Ordinal);
        Assert.Contains("Model.HasOpenShift", header, StringComparison.Ordinal);

        Assert.Contains("btnOpenCustomerDisplay", header, StringComparison.Ordinal);
        Assert.Contains("/admin/pos/customer-display", header, StringComparison.Ordinal);

        Assert.Contains("/admin/pos-shift", header, StringComparison.Ordinal);
        Assert.Contains("/admin/pos-shift/history", header, StringComparison.Ordinal);
        Assert.Contains("/admin/pos/orders-page", header, StringComparison.Ordinal);
        Assert.Contains("/admin/pos/dashboard", header, StringComparison.Ordinal);

        Assert.Contains("data-pos-shortcuts-toggle", header, StringComparison.Ordinal);
        Assert.Contains("F8", header, StringComparison.Ordinal);
        Assert.Contains("Alt+F7", header, StringComparison.Ordinal);

        Assert.Contains("id=\"heldList\"", held, StringComparison.Ordinal);
        Assert.Contains("id=\"btnOpenHeldOrders\"", held, StringComparison.Ordinal);
        Assert.Contains("data-bs-target=\"#heldOrdersModal\"", held, StringComparison.Ordinal);
    }

    [Fact]
    public void Core_POS_DOM_ids_should_remain_exactly_once_across_P1_markup()
    {
        var markup = string.Join(
     Environment.NewLine,
     File.ReadAllText(IndexPath),
     File.ReadAllText(ToolbarPath),
     File.ReadAllText(CartPath),
     File.ReadAllText(CustomerPath),
     File.ReadAllText(SummaryPath),
     File.ReadAllText(ActionsPath),
     File.ReadAllText(DraftMetaPath),
     File.ReadAllText(HeldPath));
        markup = System.Text.RegularExpressions.Regex.Replace(
    markup,
    @"@\*.*?\*@",
    string.Empty,
    System.Text.RegularExpressions.RegexOptions.Singleline);

        var requiredIds = new[]
        {
            "txtBarcode",
            "barcodeAutocomplete",
            "currentDraftBody",
            "paymentListBox",
            "customerInfoBox",
            "sumSubtotal",
            "sumDiscount",
            "sumGrandTotal",
            "sumPaid",
            "sumBalance",
            "posSummaryHero",
            "sumPaymentStateText",
            "posSummaryActionGrid",
            "btnRefreshScreen",
            "btnOpenPayment",
            "btnFinalizeCart",
            "btnHoldCart",
            "btnCancelCart",
            "btnNewCart",
            "heldList",
            "btnOpenHeldOrders",
            "heldOrdersModal",
            "paymentModal"
        };

        foreach (var id in requiredIds)
        {
            var actualCount = CountOccurrences(
                markup,
                $"id=\"{id}\"");

            Assert.True(
                actualCount == 1,
                $"Expected DOM id '{id}' exactly once, but found {actualCount} occurrence(s).");
        }
    }

    [Fact]
    public void Cockpit_styles_should_be_scoped_and_support_1366_cashier_layout()
    {
        var css = LegacyPosCssContractR1.Parse(File.ReadAllText(StylePath));

        // Contract theo baseline Human duyệt: không dò literal nằm sai selector hoặc sai breakpoint.
        css.Require(".pos-cockpit-v2", "--pos-v2-checkout-width", "360px");
        css.Require(".pos-cockpit-v2", "--pos-v2-checkout-width", "350px", LegacyPosCssContractR1.Compact);
        css.Require(".pos-cockpit-v2", "--pos-v2-checkout-width", "310px", LegacyPosCssContractR1.SmallDesktop);
        css.Require(".pos-cockpit-v2 .pos-cockpit-workspace", "display", "grid");
        css.Require(".pos-cockpit-v2 .pos-cockpit-workspace", "grid-template-columns",
            "minmax(0, 1fr) var(--pos-v2-checkout-width)");
        css.Require(".pos-cockpit-v2 .pos-cockpit-cart-body .pos-cart-scroll", "overflow-y", "auto");
        css.Require(".pos-cockpit-v2", "height",
            "var(--pos-cockpit-available-height, calc(100dvh - 78px))", LegacyPosCssContractR1.Desktop);
    }

    [Fact]
    public void Cockpit_script_should_remain_presentation_only()
    {
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("syncAvailableHeight", script, StringComparison.Ordinal);
        Assert.Contains("MutationObserver", script, StringComparison.Ordinal);
        Assert.Contains("data-pos-command-prefix", script, StringComparison.Ordinal);
        Assert.Contains("btnOpenCustomerDisplay", script, StringComparison.Ordinal);

        Assert.DoesNotContain("fetch(", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("XMLHttpRequest", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("postJson", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deleteJson", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localStorage", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/admin/pos/cart", script, StringComparison.OrdinalIgnoreCase);
    }

    private static int CountOccurrences(
        string source,
        string value)
    {
        var count = 0;
        var index = 0;

        while (true)
        {
            index = source.IndexOf(
                value,
                index,
                StringComparison.Ordinal);

            if (index < 0)
                return count;

            count++;
            index += value.Length;
        }
    }

    private static string FindRepositoryRoot()
    {
        var configured =
            Environment.GetEnvironmentVariable(
                "GAOAPP_REPOSITORY_ROOT");

        if (!string.IsNullOrWhiteSpace(configured)
            && File.Exists(
                Path.Combine(
                    configured,
                    "GaoApp.sln")))
        {
            return configured;
        }

        foreach (var start in new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        })
        {
            var directory =
                new DirectoryInfo(start);

            while (directory is not null)
            {
                if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "GaoApp.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException(
            "Could not locate GaoApp repository root from test output directory.");
    }
}

/// <summary>
/// Bộ đọc cấu trúc CSS cho contract legacy POS (không phải CSS engine của trình duyệt).
/// Nằm trong file test đã được duyệt, dùng chung bởi bốn class; không thêm dependency/path.
/// Đọc rule/declaration trong đúng chuỗi at-rule; bỏ comment nhưng giữ nguyên quoted string.
/// Cú pháp ngoài tập flat rules + media/supports/layer/container/scope/keyframes sẽ FAIL rõ ràng.
/// </summary>
internal sealed class LegacyPosCssContractR1
{
    internal const string Compact = "@media (min-width: 1100px) and (max-width: 1499.98px)";
    internal const string SmallDesktop = "@media (min-width: 992px) and (max-width: 1099.98px)";
    internal const string Desktop = "@media (min-width: 992px)";
    internal const string ReducedMotion = "@media (prefers-reduced-motion: reduce)";

    private sealed record Declaration(string Name, string Value, bool Important);
    private sealed record Rule(string[] Selectors, string[] Context, List<Declaration> Declarations);
    private readonly List<Rule> rules = new();
    private readonly List<string[]> groups = new();

    internal static LegacyPosCssContractR1 Parse(string source)
    {
        var css = new LegacyPosCssContractR1();
        var text = StripComments(source.TrimStart('\uFEFF'));
        var position = 0;
        css.ReadRules(text, ref position, Array.Empty<string>(), false);
        Assert.NotEmpty(css.rules);
        foreach (var rule in css.rules)
        {
            foreach (var declaration in rule.Declarations)
            {
                Assert.False(declaration.Important,
                    $"Không cho phép !important: {string.Join(", ", rule.Selectors)} / {declaration.Name}.");
            }
        }
        return css;
    }

    internal void Require(string selector, string property, string expected, params string[] context)
    {
        var actual = Value(selector, property, context);
        Assert.True(Normalize(actual, true) == Normalize(expected, true),
            $"CSS contract: {selector} / {property} / {Describe(context)}. " +
            $"Expected '{expected}', actual '{actual}'.");
    }

    internal void RequireDeclaration(string selector, string property, params string[] context)
        => Assert.False(string.IsNullOrWhiteSpace(Value(selector, property, context)));

    internal void RequireToken(string selector, string property, string token, params string[] context)
    {
        var value = Value(selector, property, context);
        // Các token được kiểm ở đây là tên animation hoặc keyword border, không phải substring/comment.
        var tokens = System.Text.RegularExpressions.Regex.Split(value.Trim(), @"\s+");
        Assert.True(tokens.Contains(token, StringComparer.Ordinal),
            $"Thiếu token '{token}': {selector} / {property} / {Describe(context)}; actual '{value}'.");
    }

    internal void RequireKeyframes(string name)
    {
        var context = new[] { Normalize("@keyframes " + name, true) };
        Assert.Single(groups.Where(group => group.SequenceEqual(context, StringComparer.Ordinal)));
        var frames = rules.Where(rule => rule.Context.SequenceEqual(context, StringComparer.Ordinal)).ToArray();
        Assert.True(frames.SelectMany(rule => rule.Selectors).Distinct(StringComparer.Ordinal).Count() >= 2,
            $"@keyframes {name} phải có ít nhất hai mốc với declaration thật.");
        Assert.All(frames, frame => Assert.NotEmpty(frame.Declarations));
    }

    private string Value(string selector, string property, string[] context)
    {
        var normalizedSelector = Normalize(selector, false);
        var normalizedContext = context.Select(value => Normalize(value, true)).ToArray();
        var name = PropertyName(property);
        var matches = rules.Where(rule =>
            rule.Context.SequenceEqual(normalizedContext, StringComparer.Ordinal)
            && rule.Selectors.Contains(normalizedSelector, StringComparer.Ordinal)).ToArray();
        Assert.True(matches.Length > 0, $"Thiếu rule CSS: {selector} / {Describe(context)}.");
        // Với cùng selector/context, declaration sau cùng thắng; không chọn rule đầu để che override sai.
        var declaration = matches.SelectMany(rule => rule.Declarations).LastOrDefault(item => item.Name == name);
        Assert.True(declaration is not null,
            $"Thiếu declaration: {selector} / {property} / {Describe(context)}.");
        return declaration!.Value;
    }

    private static string Describe(string[] context)
        => context.Length == 0 ? "ROOT (ngoài media)" : string.Join(" > ", context);

    private void ReadRules(string text, ref int position, string[] context, bool nested)
    {
        Assert.True(context.Length <= 16, "CSS nesting vượt giới hạn contract.");
        while (true)
        {
            SkipWhitespace(text, ref position);
            if (position == text.Length)
            {
                Assert.False(nested, "CSS thiếu dấu } đóng block.");
                return;
            }
            if (text[position] == '}')
            {
                Assert.True(nested, "CSS có dấu } dư.");
                position++;
                return;
            }
            var header = ReadUntil(text, ref position, "{};").Trim();
            Assert.True(header.Length > 0 && position < text.Length && text[position] == '{',
                $"CSS rule/at-rule không được hỗ trợ hoặc thiếu block: '{header}'.");
            position++;
            if (header.StartsWith('@'))
            {
                var match = System.Text.RegularExpressions.Regex.Match(header, @"^@([a-zA-Z-]+)(?=[\s(])\s*.+$",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                var keyword = match.Success ? match.Groups[1].Value.ToLowerInvariant() : string.Empty;
                Assert.True(new[] { "media", "supports", "layer", "container", "scope", "keyframes" }.Contains(keyword),
                    $"At-rule chưa được hỗ trợ trong contract: '{header}'. Không được bỏ qua âm thầm.");
                var childContext = context.Append(Normalize(header, true)).ToArray();
                groups.Add(childContext);
                ReadRules(text, ref position, childContext, true);
            }
            else
            {
                var selectors = SplitSelectors(header);
                var declarations = ReadDeclarations(text, ref position);
                rules.Add(new Rule(selectors, context, declarations));
            }
        }
    }

    private static List<Declaration> ReadDeclarations(string text, ref int position)
    {
        var result = new List<Declaration>();
        while (true)
        {
            SkipWhitespace(text, ref position);
            Assert.True(position < text.Length, "CSS declaration block chưa đóng.");
            if (text[position] == '}') { position++; return result; }
            var entry = ReadUntil(text, ref position, ";{}").Trim();
            Assert.True(position < text.Length && text[position] != '{',
                "CSS declaration sai hoặc có nested style rule ngoài tập contract hỗ trợ.");
            if (entry.Length > 0)
            {
                var colon = entry.IndexOf(':');
                Assert.True(colon > 0, $"CSS declaration thiếu dấu ':' : '{entry}'.");
                var property = entry[..colon].Trim();
                Assert.Matches(@"^[a-zA-Z_-][a-zA-Z0-9_-]*$", property);
                var value = entry[(colon + 1)..].Trim();
                Assert.NotEmpty(value);
                var visibleValue = MaskStrings(value);
                // Bắt cả ! important/!IMPORTANT, nhưng không bắt chữ '!important' trong content string.
                var important = System.Text.RegularExpressions.Regex.IsMatch(visibleValue, @"!\s*important\s*$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                result.Add(new Declaration(PropertyName(property), value, important));
            }
            if (text[position] == '}') { position++; return result; }
            position++;
        }
    }

    private static string PropertyName(string property)
        => property.StartsWith("--", StringComparison.Ordinal) ? property : property.ToLowerInvariant();

    private static string[] SplitSelectors(string header)
    {
        var result = new List<string>();
        var position = 0;
        while (position < header.Length)
        {
            var selector = ReadUntil(header, ref position, ",").Trim();
            Assert.NotEmpty(selector);
            result.Add(Normalize(selector, false));
            if (position < header.Length)
            {
                position++;
                Assert.True(position < header.Length && !string.IsNullOrWhiteSpace(header[position..]),
                    "CSS selector list có dấu phẩy cuối không hợp lệ.");
            }
        }
        Assert.NotEmpty(result);
        return result.ToArray();
    }

    private static string ReadUntil(string text, ref int position, string delimiters)
    {
        var start = position;
        var stack = new Stack<char>();
        while (position < text.Length)
        {
            var c = text[position];
            if (c is '\'' or '"') { SkipString(text, ref position); continue; }
            if (c == '\\')
                throw new InvalidOperationException("CSS escape ngoài quoted string chưa được hỗ trợ trong contract.");
            if (stack.Count == 0 && delimiters.Contains(c)) break;
            if (c is '(' or '[') stack.Push(c);
            if (c is ')' or ']')
            {
                var expected = c == ')' ? '(' : '[';
                Assert.True(stack.Count > 0 && stack.Pop() == expected, "CSS ngoặc ()/[] không cân bằng.");
            }
            position++;
        }
        Assert.True(stack.Count == 0, "CSS ngoặc ()/[] chưa đóng.");
        return text[start..position];
    }

    private static void SkipString(string text, ref int position)
    {
        var quote = text[position++];
        while (position < text.Length)
        {
            var c = text[position++];
            if (c == quote) return;
            if (c == '\\')
            {
                Assert.True(position < text.Length, "CSS quoted string kết thúc bằng escape chưa đủ.");
                position++;
            }
        }
        throw new InvalidOperationException("CSS quoted string chưa đóng.");
    }

    private static string StripComments(string text)
    {
        var result = new System.Text.StringBuilder();
        var position = 0;
        while (position < text.Length)
        {
            if (text[position] is '\'' or '"')
            {
                var start = position;
                SkipString(text, ref position);
                result.Append(text, start, position - start);
            }
            else if (position + 1 < text.Length && text[position] == '/' && text[position + 1] == '*')
            {
                var end = text.IndexOf("*/", position + 2, StringComparison.Ordinal);
                Assert.True(end >= 0, "CSS comment chưa đóng.");
                result.Append(' ');
                position = end + 2;
            }
            else result.Append(text[position++]);
        }
        return result.ToString();
    }

    private static string MaskStrings(string text)
    {
        var result = new System.Text.StringBuilder(text);
        var position = 0;
        while (position < text.Length)
        {
            if (text[position] is '\'' or '"')
            {
                var start = position;
                SkipString(text, ref position);
                for (var i = start; i < position; i++) result[i] = ' ';
            }
            else position++;
        }
        return result.ToString();
    }

    private static string Normalize(string text, bool valueOrContext)
    {
        if (valueOrContext && text.TrimStart().StartsWith('@'))
        {
            text = System.Text.RegularExpressions.Regex.Replace(text,
                @"^(\s*@[a-zA-Z-]+)\s*(?=\()", "$1 ");
        }
        var result = new System.Text.StringBuilder();
        var position = 0;
        var pendingSpace = false;
        while (position < text.Length)
        {
            var c = text[position];
            if (char.IsWhiteSpace(c)) { pendingSpace = true; position++; continue; }
            // Chỉ bỏ whitespace tùy chọn quanh punctuation của value/media; không ghép selector hậu duệ.
            var before = valueOrContext ? ":,)" : ">+~";
            var after = valueOrContext ? ":,(" : ">+~";
            if (pendingSpace && result.Length > 0 && !before.Contains(c)
                && !after.Contains(result[result.Length - 1])) result.Append(' ');
            pendingSpace = false;
            if (c is '\'' or '"')
            {
                var start = position;
                SkipString(text, ref position);
                result.Append(text, start, position - start);
            }
            else { result.Append(c); position++; }
        }
        return result.ToString();
    }

    private static void SkipWhitespace(string text, ref int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
    }
}
