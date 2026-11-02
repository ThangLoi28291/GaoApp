using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GaoApp.Web.Services.Assistant;

public sealed record CatalogSearchInfo(string Strategy, string MatchKind, bool RequiresConfirmation, int CandidateCount, string CatalogRevision);
public sealed record SearchDocument(int Id, int ProductId, string Name, string Sku, CatalogAttributes Attributes, string[] Aliases);
public sealed record CatalogSearchHit(SearchDocument Document, int Score, string Kind);
public sealed record CatalogRankedPage(CatalogSearchHit[] Hits, string? Cursor, CatalogSearchInfo Search);

// A derived name index. No business record, selling price or stock is changed by indexing.
public static class AssistantCatalogSearch
{
    public const string Strategy = "name_rank_v1";
    public const int MaximumDocuments = 50000;
    private static readonly Regex Measure = new(@"\b(?<n>\d+(?:[.,]\d+)?)\s*(?<u>mililit|ml|kilogam|kilogram|kg|lit|l|gam|gram|g)\b", RegexOptions.Compiled);
    private static readonly Regex SugarPattern = new(@"\b(?:co|it|khong)\s+duong\b", RegexOptions.Compiled);
    private static readonly HashSet<string> Stop = new("gia bao nhieu nhiu bn tien hang ton kho hien tai la may so luong kha dung san pham ma cua giup em toi minh cho anh chi nay do loai tim xem mua can muon nhe nha voi a si le loc thung".Split(' '));
    public static string Fold(string value) => new string(value.ToLowerInvariant().Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark))
        .Select(c => c == 'đ' ? 'd' : c).ToArray());
    public static string Canonical(string value)
    {
        var text = Measure.Replace(Fold(value), m => {
            if (!decimal.TryParse(m.Groups["n"].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) || n > 1000000000) return m.Value;
            var unit = m.Groups["u"].Value;
            if (unit is "l" or "lit" or "kg" or "kilogram" or "kilogam") n *= 1000;
            return n.ToString("0.####", CultureInfo.InvariantCulture) + (unit is "ml" or "mililit" or "l" or "lit" ? "ml" : "g");
        });
        // Keep a decimal measurement intact; other punctuation is a word boundary.
        text = Regex.Replace(text, @"(?<=\d)\.(?=\d+(?:ml|g)\b)", "p");
        return string.Join(' ', Regex.Matches(text, "[a-z0-9]+").Select(m => m.Value));
    }
    public static string Key(string value) => string.Join(' ', Canonical(value).Split(' ').Where(t => !Stop.Contains(t)));
    public static string? Sugar(string value) => SugarPattern.Match(Canonical(value)).Value switch {
        "co duong" => "sweetened", "it duong" => "less_sugar", "khong duong" => "unsweetened", _ => null
    };
    public static string? Capacity(string value) => Regex.Match(Canonical(value), @"\b\d+(?:p\d+)?(?:ml|g)\b") is { Success: true } m ? m.Value : null;
    public static CatalogAttributes Infer(string name, string? brand = null, string? family = null)
    {
        var text = Canonical(name);
        // Imported classification may disagree with the literal name. Do not add contradictory brand words to the index.
        if (!string.IsNullOrWhiteSpace(brand) && !(" " + text + " ").Contains(" " + Canonical(brand) + " ", StringComparison.Ordinal)) brand = null;
        if (string.IsNullOrWhiteSpace(brand)) {
            if (Regex.IsMatch(text, @"\bth\b")) brand = "TH";
            else if (Regex.IsMatch(text, @"\bvinamilk\b")) brand = "Vinamilk";
        }
        var sugar = SugarPattern.Matches(text).Select(m => m.Value).Distinct().Count() > 1 ? null : Sugar(name);
        // Existing store-confirmed naming convention, restricted to the plain TH milk family.
        if (sugar is null && Regex.IsMatch(text, @"^sua tuoi th nguyen chat(?: (?:hop|bich))? \d+(?:p\d+)?ml$")) sugar = "unsweetened";
        var measure = Measure.Match(Fold(name));
        var namedFamily = new[] { "sua tuoi", "sua chua", "nuoc rua chen", "nuoc giat", "nuoc ngot", "dau an", "nuoc mam", "gao" }
            .FirstOrDefault(f => Regex.IsMatch(text, @"\b" + Regex.Escape(f) + @"\b"));
        string? Short(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(s.Trim().Length, 120)];
        return new() { Brand = Short(brand), Family = namedFamily ?? Short(family), Sugar = sugar,
            Capacity = measure.Success && Regex.Matches(text, @"\b\d+(?:p\d+)?(?:ml|g)\b").Select(m => m.Value).Distinct().Count() == 1 ? measure.Value.Replace(" ", "") : null };
    }
    public static bool Compatible(CatalogAttributes actual, CatalogAttributes? filter, string? approvedName = null)
    {
        if (filter is null) return true;
        var approved = approvedName is null ? null : Infer(approvedName);
        bool Text(string? a, string? b) => b is null || a is not null && (" " + Canonical(a) + " ").Contains(" " + Canonical(b) + " ", StringComparison.Ordinal);
        return (Text(actual.Brand, filter.Brand) || actual.Brand is null && approvedName is not null && Text(approvedName, filter.Brand)) && Text(actual.Family, filter.Family) && Text(actual.Flavor, filter.Flavor)
            && (filter.Sugar is null || (actual.Sugar ?? approved?.Sugar) == filter.Sugar)
            && (filter.Capacity is null || Capacity(actual.Capacity ?? "") is { } capacity && capacity == Capacity(filter.Capacity)
                || filter.Capacity is not null && actual.Capacity is null && Capacity(approved?.Capacity ?? "") is { } learnedCapacity && learnedCapacity == Capacity(filter.Capacity));
    }
    public static bool IsSpecific(string query) {
        var key = Key(query);
        var descriptor = Regex.Replace(SugarPattern.Replace(key, " "), @"\b\d+(?:p\d+)?(?:ml|g)\b", " ");
        return key.Length >= 3 && key.Split(' ').Length <= 30 && Regex.IsMatch(descriptor, @"\b[a-z]{2,}\b");
    }
    public static bool IsOneEdit(string left, string right)
    {
        if (left == right) return true;
        if (left.Length < 4 || right.Length < 4 || Math.Abs(left.Length - right.Length) > 1
            || left.Any(char.IsDigit) || right.Any(char.IsDigit) || left[0] != right[0]) return false;
        if (left.Length == right.Length) {
            var differences = Enumerable.Range(0, left.Length).Where(i => left[i] != right[i]).ToArray();
            return differences.Length == 1 || differences.Length == 2 && differences[1] == differences[0] + 1
                && left[differences[0]] == right[differences[1]] && left[differences[1]] == right[differences[0]];
        }
        if (left.Length > right.Length) (left, right) = (right, left);
        var index = 0; while (index < left.Length && left[index] == right[index]) index++;
        return left.AsSpan(index).SequenceEqual(right.AsSpan(index + 1));
    }
    public static CatalogRankedPage Rank(IReadOnlyList<SearchDocument> documents, CatalogSearch input, string store)
    {
        if (documents.Count > MaximumDocuments) throw new AssistantException(503, "search_catalog_limit", "Danh mục vượt giới hạn chỉ mục hiện tại. Cần kiểm tra trước khi mở rộng.");
        var key = Key(input.Query); var raw = Canonical(input.Query);
        var filter = input.Attributes ?? new CatalogAttributes();
        var sugars = SugarPattern.Matches(raw).Select(m => m.Value).Distinct().ToArray();
        var capacities = Regex.Matches(raw, @"\b\d+(?:p\d+)?(?:ml|g)\b").Select(m => m.Value).Distinct().ToArray();
        if (sugars.Length > 1 || capacities.Length > 1) return Page([], null, documents, input, store);
        if (filter.Sugar is not null && Sugar(input.Query) is { } statedSugar && filter.Sugar != statedSugar
            || filter.Capacity is not null && capacities.Length == 1 && Capacity(filter.Capacity) != capacities[0])
            return Page([], null, documents, input, store);
        if (filter.Sugar is null && Sugar(input.Query) is { } sugar) filter = filter with { Sugar = sugar };
        if (filter.Capacity is null && capacities.Length == 1) filter = filter with { Capacity = capacities[0].Replace('p', '.') };
        if (filter.Brand is null) {
            var named = documents.Select(d => d.Attributes.Brand).Where(b => !string.IsNullOrWhiteSpace(b)).Distinct()
                .Where(b => (" " + raw + " ").Contains(" " + Canonical(b!) + " ", StringComparison.Ordinal)).OrderByDescending(b => b!.Length).FirstOrDefault();
            if (named is not null) filter = filter with { Brand = named };
        }
        var terms = Regex.Replace(SugarPattern.Replace(key, " "), @"\b\d+(?:p\d+)?(?:ml|g)\b", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
        if (terms.Length > 30) throw AssistantException.Invalid();
        var context = (input.ContextVariantIds ?? []).Select(AssistantAccess.Id).ToHashSet();
        var hits = new List<CatalogSearchHit>();
        foreach (var document in documents) {
            var approved = document.Aliases.FirstOrDefault(a => Key(a) == key);
            var evidence = document.Attributes;
            if (filter.Brand is not null && evidence.Brand is null && (" " + Canonical(document.Name) + " ").Contains(" " + Canonical(filter.Brand) + " ", StringComparison.Ordinal))
                evidence = evidence with { Brand = filter.Brand };
            if (!Compatible(evidence, filter, approved)) continue;
            var skuExact = !string.IsNullOrWhiteSpace(document.Sku) && string.Equals(input.Query.Trim(), document.Sku.Trim(), StringComparison.OrdinalIgnoreCase);
            var name = Key(document.Name); var allWords = Canonical(document.Name + " " + document.Sku).Split(' ').ToHashSet();
            var fuzzy = 0; var found = true;
            foreach (var term in terms) {
                if (allWords.Contains(term)) continue;
                if (allWords.Any(w => IsOneEdit(term, w))) fuzzy++;
                else { found = false; break; }
            }
            var constrained = filter.Sugar is not null || filter.Capacity is not null || filter.Brand is not null || filter.Family is not null || filter.Flavor is not null;
            if (terms.Length == 0 && !constrained && !context.Contains(document.Id)) found = false;
            if (fuzzy > 1) found = false;
            if (!skuExact && approved is null && !found) continue;
            var kind = skuExact ? "sku" : name == key ? "exact" : approved is not null ? "learned" : fuzzy > 0 ? "fuzzy" : "lexical";
            var score = kind switch { "sku" => 10000, "exact" => 9000, "learned" => 5000, "fuzzy" => 1000, _ => 3000 };
            score += terms.Length * 10 - Math.Min(200, Math.Max(0, name.Split(' ').Length - key.Split(' ').Length));
            hits.Add(new(document, score, kind));
        }
        return Page(hits.OrderByDescending(h => h.Score).ThenBy(h => h.Document.Id).ToArray(), key, documents, input, store);
    }
    private static CatalogRankedPage Page(CatalogSearchHit[] hits, string? key, IReadOnlyList<SearchDocument> documents, CatalogSearch input, string store)
    {
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(documents.OrderBy(d => d.Id)))));
        var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(store + "|" + Key(input.Query) + "|" + JsonSerializer.Serialize(input.Attributes) + "|" + string.Join(',', input.ContextVariantIds ?? []))));
        if (input.Cursor is { } cursor) {
            try {
                var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
                if (parts.Length != 4 || parts[0] != signature || parts[1] != revision
                    || !int.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var score)
                    || !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0) throw AssistantException.Invalid();
                hits = hits.Where(h => h.Score < score || h.Score == score && h.Document.Id > id).ToArray();
            } catch (FormatException) { throw AssistantException.Invalid(); }
        }
        var selected = hits.Take(input.Limit).ToArray();
        var next = hits.Length > input.Limit ? Convert.ToBase64String(Encoding.UTF8.GetBytes(signature + "|" + revision + "|" + selected[^1].Score + "|" + selected[^1].Document.Id)) : null;
        var descriptor = Regex.Replace(SugarPattern.Replace(Key(input.Query), " "), @"\b\d+(?:p\d+)?(?:ml|g)\b", " ").Trim();
        var confirmation = selected.Any(h => h.Kind is "fuzzy" or "learned") || selected.Length > 0 && descriptor.Length == 0;
        return new(selected, next, new(Strategy, selected.FirstOrDefault()?.Kind ?? "none", confirmation, hits.Length, revision));
    }
}