using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GaoApp.Infrastructure.Data.Migrations;

public sealed record DatabaseSchemaManifest(
    IReadOnlyList<string> AppliedMigrationIds,
    IReadOnlyList<DatabaseTableSchema> Tables,
    IReadOnlyList<DatabaseSequenceSchema> Sequences,
    string Fingerprint);

public sealed record DatabaseTableSchema(
    DatabaseObjectIdentity Identity,
    IReadOnlyList<DatabaseColumnSchema> Columns,
    DatabasePrimaryKeySchema? PrimaryKey,
    IReadOnlyList<DatabaseForeignKeySchema> ForeignKeys,
    IReadOnlyList<DatabaseIndexSchema> Indexes,
    IReadOnlyList<DatabaseCheckConstraintSchema> CheckConstraints);

public sealed record DatabaseColumnSchema(
    string Name,
    string StoreType,
    int? MaxLength,
    int? Precision,
    int? Scale,
    bool IsNullable,
    bool IsIdentity,
    bool IsComputed,
    bool IsRowVersion,
    bool HasDefault,
    string? DefaultExpression,
    string? ComputedExpression);

public sealed record DatabasePrimaryKeySchema(
    IReadOnlyList<string> Columns,
    bool IsClustered,
    bool IsDisabled);

public sealed record DatabaseForeignKeySchema(
    IReadOnlyList<string> Columns,
    DatabaseObjectIdentity PrincipalTable,
    IReadOnlyList<string> PrincipalColumns,
    string DeleteAction,
    bool IsDisabled,
    bool IsNotTrusted);

public sealed record DatabaseIndexColumnSchema(
    string Name,
    bool IsDescending);

public sealed record DatabaseIndexSchema(
    string Name,
    IReadOnlyList<DatabaseIndexColumnSchema> KeyColumns,
    IReadOnlyList<string> IncludedColumns,
    bool IsUnique,
    bool IsUniqueConstraint,
    bool IsClustered,
    bool IsDisabled,
    string? Filter);

public sealed record DatabaseCheckConstraintSchema(
    string Name,
    string Expression,
    bool IsDisabled,
    bool IsNotTrusted);

public sealed record DatabaseSequenceSchema(
    DatabaseObjectIdentity Identity,
    string StoreType,
    long StartValue,
    int IncrementBy,
    long? MinimumValue,
    long? MaximumValue,
    bool IsCyclic);

public sealed record DatabaseSchemaMismatchCounts(
    int Tables,
    int Columns,
    int PrimaryKeys,
    int ForeignKeys,
    int Indexes,
    int CheckConstraints,
    int Sequences)
{
    public int Total =>
        Tables
        + Columns
        + PrimaryKeys
        + ForeignKeys
        + Indexes
        + CheckConstraints
        + Sequences;
}

public sealed record DatabaseSchemaComparisonResult(
    string ExpectedFingerprint,
    string ActualFingerprint,
    bool IsMatch,
    DatabaseSchemaMismatchCounts Mismatches);

public interface IDatabaseSchemaManifestCatalog
{
    DatabaseSchemaManifest GetCurrentManifest();

    bool TryGetManifestForAppliedMigrationPrefix(
        IReadOnlyList<string> appliedMigrationIds,
        out DatabaseSchemaManifest manifest);
}

public interface ISqlServerSchemaSnapshotReader
{
    Task<DatabaseSchemaManifest> ReadAsync(
        IReadOnlyList<string> appliedMigrationIds,
        CancellationToken ct = default);
}

public static class DatabaseSchemaComparer
{
    public static DatabaseSchemaComparisonResult Compare(
        DatabaseSchemaManifest expected,
        DatabaseSchemaManifest actual)
    {
        var expectedCategories =
            DatabaseSchemaCanonicalizer.CreateCategoryRecords(expected);
        var actualCategories =
            DatabaseSchemaCanonicalizer.CreateCategoryRecords(actual);

        var mismatchCounts = new DatabaseSchemaMismatchCounts(
            SymmetricDifferenceCount(
                expectedCategories.Tables,
                actualCategories.Tables),
            SymmetricDifferenceCount(
                expectedCategories.Columns,
                actualCategories.Columns),
            SymmetricDifferenceCount(
                expectedCategories.PrimaryKeys,
                actualCategories.PrimaryKeys),
            SymmetricDifferenceCount(
                expectedCategories.ForeignKeys,
                actualCategories.ForeignKeys),
            SymmetricDifferenceCount(
                expectedCategories.Indexes,
                actualCategories.Indexes),
            SymmetricDifferenceCount(
                expectedCategories.CheckConstraints,
                actualCategories.CheckConstraints),
            SymmetricDifferenceCount(
                expectedCategories.Sequences,
                actualCategories.Sequences));

        return new DatabaseSchemaComparisonResult(
            expected.Fingerprint,
            actual.Fingerprint,
            mismatchCounts.Total == 0
                && string.Equals(
                    expected.Fingerprint,
                    actual.Fingerprint,
                    StringComparison.Ordinal),
            mismatchCounts);
    }

    private static int SymmetricDifferenceCount(
        IReadOnlySet<string> left,
        IReadOnlySet<string> right)
        => left.Except(right, StringComparer.Ordinal).Count()
            + right.Except(left, StringComparer.Ordinal).Count();
}

public static class DatabaseSchemaCanonicalizer
{
    public static DatabaseSchemaManifest WithFingerprint(
        IReadOnlyList<string> appliedMigrationIds,
        IReadOnlyList<DatabaseTableSchema> tables,
        IReadOnlyList<DatabaseSequenceSchema> sequences)
    {
        var withoutFingerprint = new DatabaseSchemaManifest(
            appliedMigrationIds.ToArray(),
            tables
                .OrderBy(
                    table => table.Identity.Schema,
                    StringComparer.Ordinal)
                .ThenBy(
                    table => table.Identity.Name,
                    StringComparer.Ordinal)
                .ToArray(),
            sequences
                .OrderBy(
                    sequence => sequence.Identity.Schema,
                    StringComparer.Ordinal)
                .ThenBy(
                    sequence => sequence.Identity.Name,
                    StringComparer.Ordinal)
                .ToArray(),
            string.Empty);
        var records = CreateCategoryRecords(withoutFingerprint);
        var canonical = string.Join(
            "\n",
            records.All.OrderBy(value => value, StringComparer.Ordinal));
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        return withoutFingerprint with { Fingerprint = fingerprint };
    }

    public static DatabaseSchemaCategoryRecords CreateCategoryRecords(
        DatabaseSchemaManifest manifest)
    {
        var tables = new HashSet<string>(StringComparer.Ordinal);
        var columns = new HashSet<string>(StringComparer.Ordinal);
        var primaryKeys = new HashSet<string>(StringComparer.Ordinal);
        var foreignKeys = new HashSet<string>(StringComparer.Ordinal);
        var indexes = new HashSet<string>(StringComparer.Ordinal);
        var checks = new HashSet<string>(StringComparer.Ordinal);
        var sequences = new HashSet<string>(StringComparer.Ordinal);

        foreach (var table in manifest.Tables)
        {
            var tableId = Identity(table.Identity);
            tables.Add($"table|{tableId}");

            foreach (var column in table.Columns)
            {
                columns.Add(string.Join(
                    "|",
                    "column",
                    tableId,
                    column.Name,
                    column.StoreType,
                    Facet(column.MaxLength),
                    Facet(column.Precision),
                    Facet(column.Scale),
                    Bool(column.IsNullable),
                    Bool(column.IsIdentity),
                    Bool(column.IsComputed),
                    Bool(column.IsRowVersion),
                    Bool(column.HasDefault),
                    column.DefaultExpression ?? "-",
                    column.ComputedExpression ?? "-"));
            }

            if (table.PrimaryKey is { } primaryKey)
            {
                primaryKeys.Add(string.Join(
                    "|",
                    "pk",
                    tableId,
                    string.Join(",", primaryKey.Columns),
                    Bool(primaryKey.IsClustered),
                    Bool(primaryKey.IsDisabled)));
            }

            foreach (var foreignKey in table.ForeignKeys)
            {
                foreignKeys.Add(string.Join(
                    "|",
                    "fk",
                    tableId,
                    string.Join(",", foreignKey.Columns),
                    Identity(foreignKey.PrincipalTable),
                    string.Join(",", foreignKey.PrincipalColumns),
                    foreignKey.DeleteAction,
                    Bool(foreignKey.IsDisabled),
                    Bool(foreignKey.IsNotTrusted)));
            }

            foreach (var index in table.Indexes)
            {
                indexes.Add(string.Join(
                    "|",
                    "index",
                    tableId,
                    index.Name,
                    string.Join(
                        ",",
                        index.KeyColumns.Select(
                            column =>
                                $"{column.Name}:{Bool(column.IsDescending)}")),
                    string.Join(",", index.IncludedColumns),
                    Bool(index.IsUnique),
                    Bool(index.IsUniqueConstraint),
                    Bool(index.IsClustered),
                    Bool(index.IsDisabled),
                    index.Filter ?? "-"));
            }

            foreach (var check in table.CheckConstraints)
            {
                checks.Add(string.Join(
                    "|",
                    "check",
                    tableId,
                    check.Name,
                    check.Expression,
                    Bool(check.IsDisabled),
                    Bool(check.IsNotTrusted)));
            }
        }

        foreach (var sequence in manifest.Sequences)
        {
            sequences.Add(string.Join(
                "|",
                "sequence",
                Identity(sequence.Identity),
                sequence.StoreType,
                sequence.StartValue.ToString(CultureInfo.InvariantCulture),
                sequence.IncrementBy.ToString(CultureInfo.InvariantCulture),
                Facet(sequence.MinimumValue),
                Facet(sequence.MaximumValue),
                Bool(sequence.IsCyclic)));
        }

        return new DatabaseSchemaCategoryRecords(
            tables,
            columns,
            primaryKeys,
            foreignKeys,
            indexes,
            checks,
            sequences);
    }

    private static string Identity(DatabaseObjectIdentity identity)
        => $"{identity.Schema}.{identity.Name}";

    private static string Facet<T>(T? value)
        where T : struct
        => value?.ToString() ?? "-";

    private static string Bool(bool value) => value ? "1" : "0";
}

public sealed record DatabaseSchemaCategoryRecords(
    IReadOnlySet<string> Tables,
    IReadOnlySet<string> Columns,
    IReadOnlySet<string> PrimaryKeys,
    IReadOnlySet<string> ForeignKeys,
    IReadOnlySet<string> Indexes,
    IReadOnlySet<string> CheckConstraints,
    IReadOnlySet<string> Sequences)
{
    public IEnumerable<string> All =>
        Tables
            .Concat(Columns)
            .Concat(PrimaryKeys)
            .Concat(ForeignKeys)
            .Concat(Indexes)
            .Concat(CheckConstraints)
            .Concat(Sequences);
}

public static partial class DatabaseSchemaNormalization
{
    public static string NormalizeIdentifier(string value)
        => value.Trim().ToLowerInvariant();

    public static string NormalizeStoreType(string value)
    {
        var normalized = WhitespaceRegex()
            .Replace(value.Trim().ToLowerInvariant(), string.Empty);
        return normalized == "timestamp"
            ? "rowversion"
            : normalized;
    }

    public static string NormalizeDeleteAction(string value)
        => value
            .Trim()
            .Replace(' ', '_')
            .ToLowerInvariant() switch
        {
            "restrict" => "no_action",
            "noaction" => "no_action",
            "setnull" => "set_null",
            "setdefault" => "set_default",
            var normalized => normalized
        };

    public static string? NormalizeSqlExpression(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = WhitespaceRegex()
            .Replace(NormalizeNullGuardedInPredicate(value).ToLowerInvariant(), string.Empty)
            .Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal);
        normalized = NumericLiteralParenthesesRegex()
            .Replace(normalized, "$1");

        return TrimOuterParentheses(normalized);
    }

    /// <summary>
    /// Canonicalizes CHECK-only numeric equality disjunctions. Only a complete
    /// group of column = number OR column = number on the same simple column
    /// is reordered. Logical grouping, other operators and constraint flags
    /// are not changed. Index/default normalization keeps its existing path.
    /// </summary>
    public static string? NormalizeCheckConstraintExpression(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var rewritten = TryTokenizeCheckExpression(value, out var tokens, out var closing)
            ? RewriteCheckGroup(value, tokens, closing, 0, tokens.Count)
            : value;

        return NormalizeSqlExpression(rewritten);
    }

    private readonly record struct CheckExpressionToken(
        string Text,
        int Offset,
        bool IsIdentifier,
        bool IsNumber)
    {
        public int End => Offset + Text.Length;
    }

    private static bool TryTokenizeCheckExpression(
        string value,
        out List<CheckExpressionToken> tokens,
        out Dictionary<int, int> closing)
    {
        tokens = [];
        closing = [];
        var opens = new Stack<int>();

        // Conservative limits: unsupported/oversized expressions keep the
        // prior normalizer. Do not partially rewrite strings, comments,
        // quoted/complex identifiers or unsupported SQL syntax.
        if (value.Length > 32768)
        {
            return false;
        }

        for (var offset = 0; offset < value.Length;)
        {
            var match = CheckExpressionTokenRegex().Match(value, offset);
            if (!match.Success || match.Index != offset)
            {
                return false;
            }

            offset += match.Length;
            if (match.Groups["space"].Success)
            {
                continue;
            }

            var token = new CheckExpressionToken(
                match.Value,
                match.Index,
                match.Groups["identifier"].Success,
                match.Groups["number"].Success);
            var index = tokens.Count;
            tokens.Add(token);

            if (token.Text == "(")
            {
                opens.Push(index);
                if (opens.Count > 64)
                {
                    return false;
                }
            }
            else if (token.Text == ")")
            {
                if (opens.Count == 0)
                {
                    return false;
                }

                closing.Add(opens.Pop(), index);
            }
        }

        return tokens.Count > 0 && opens.Count == 0;
    }

    private static string RewriteCheckGroup(
        string value,
        IReadOnlyList<CheckExpressionToken> tokens,
        IReadOnlyDictionary<int, int> closing,
        int first,
        int end)
    {
        if (first == end)
        {
            return string.Empty;
        }

        if (TryNormalizeCheckDisjunction(tokens, closing, first, end, out var normalized))
        {
            return normalized;
        }

        // Rewrite child groups independently, retaining every enclosing pair
        // of parentheses. Never flatten or sort a mixed AND/OR expression.
        var result = new StringBuilder();
        var cursor = tokens[first].Offset;
        for (var index = first; index < end; index++)
        {
            if (tokens[index].Text != "(")
            {
                continue;
            }

            var close = closing[index];
            result.Append(value, cursor, tokens[index].End - cursor);
            result.Append(RewriteCheckGroup(value, tokens, closing, index + 1, close));
            result.Append(')');
            cursor = tokens[close].End;
            index = close;
        }

        result.Append(value, cursor, tokens[end - 1].End - cursor);
        return result.ToString();
    }

    private static bool TryNormalizeCheckDisjunction(
        IReadOnlyList<CheckExpressionToken> tokens,
        IReadOnlyDictionary<int, int> closing,
        int first,
        int end,
        out string normalized)
    {
        normalized = string.Empty;
        var terms = new List<(int First, int End)>();
        var start = first;

        for (var index = first; index < end; index++)
        {
            if (tokens[index].Text == "(")
            {
                index = closing[index];
            }
            else if (string.Equals(tokens[index].Text, "OR", StringComparison.OrdinalIgnoreCase))
            {
                terms.Add((start, index));
                start = index + 1;
            }
        }

        if (terms.Count == 0)
        {
            return false;
        }

        terms.Add((start, end));
        string? column = null;
        var numbers = new List<string>();

        foreach (var term in terms)
        {
            if (!TryReadCheckEquality(tokens, closing, term.First, term.End,
                    out var termColumn, out var number))
            {
                return false;
            }

            if (column is not null && !string.Equals(column, termColumn, StringComparison.Ordinal))
            {
                return false;
            }

            column = termColumn;
            numbers.Add(number);
        }

        // Sort literal spellings, without parsing/rounding/retyping numbers.
        // Retain duplicate terms: do not broaden the normalization contract.
        numbers.Sort(StringComparer.Ordinal);
        normalized = string.Join(" OR ", numbers.Select(number => $"[{column}] = {number}"));
        return true;
    }

    private static bool TryReadCheckEquality(
        IReadOnlyList<CheckExpressionToken> tokens,
        IReadOnlyDictionary<int, int> closing,
        int first,
        int end,
        out string column,
        out string number)
    {
        column = string.Empty;
        number = string.Empty;
        UnwrapCheckTerm(tokens, closing, ref first, ref end);

        if (end - first < 3 || !tokens[first].IsIdentifier || tokens[first + 1].Text != "=")
        {
            return false;
        }

        var numberFirst = first + 2;
        var numberEnd = end;
        UnwrapCheckTerm(tokens, closing, ref numberFirst, ref numberEnd);
        if (numberEnd - numberFirst != 1 || !tokens[numberFirst].IsNumber)
        {
            return false;
        }

        var identifier = tokens[first].Text;
        column = (identifier[0] == '[' ? identifier[1..^1] : identifier).ToLowerInvariant();
        number = tokens[numberFirst].Text;
        return true;
    }

    private static void UnwrapCheckTerm(
        IReadOnlyList<CheckExpressionToken> tokens,
        IReadOnlyDictionary<int, int> closing,
        ref int first,
        ref int end)
    {
        while (end - first >= 2 && tokens[first].Text == "(" && closing[first] == end - 1)
        {
            first++;
            end--;
        }
    }

    [GeneratedRegex(
        @"\G(?:(?<space>\s+)|(?<identifier>\[[A-Za-z_][A-Za-z0-9_]*\]|[A-Za-z_][A-Za-z0-9_]*)|(?<number>-?[0-9]+(?:\.[0-9]+)?)|(?<symbol><>|>=|<=|!=|[=><()]))",
        RegexOptions.CultureInvariant)]
    private static partial Regex CheckExpressionTokenRegex();

    private static string NormalizeNullGuardedInPredicate(string value)
    {
        // SQL Server wraps the IN predicate of this filtered-index shape in
        // parentheses. Recognize the complete expression before whitespace is
        // removed, so identifiers/literals cannot be mistaken for SQL tokens.
        // Other expressions retain their grouping; this is not a SQL parser.
        var expression = TrimOuterParentheses(value.Trim());
        var match = NullGuardedPredicateRegex().Match(expression);
        if (!match.Success)
        {
            return value.Trim();
        }

        var predicate = TrimOuterParentheses(match.Groups["predicate"].Value.Trim());
        return LiteralInPredicateRegex().IsMatch(predicate)
            ? match.Groups["guard"].Value + predicate
            : value.Trim();
    }

    public static string? NormalizeDefaultExpression(string? value)
    {
        var normalized = NormalizeSqlExpression(value);
        if (normalized is null)
        {
            return null;
        }

        normalized = normalized.Replace("n'", "'", StringComparison.Ordinal);

        while (TryUnwrapConvert(normalized, out var converted))
        {
            normalized = converted;
        }

        normalized = TrimOuterParentheses(normalized);
        return decimal.TryParse(
            normalized,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var numeric)
                ? numeric.ToString(
                    "G29",
                    CultureInfo.InvariantCulture)
                : normalized;
    }

    public static string NormalizeDefaultValue(object value)
        => value switch
        {
            bool boolean => boolean ? "1" : "0",
            string text =>
                $"'{text.Replace("'", "''", StringComparison.Ordinal).ToLowerInvariant()}'",
            byte[] bytes => Convert.ToHexString(bytes).ToLowerInvariant(),
            IFormattable formattable =>
                formattable.ToString(null, CultureInfo.InvariantCulture)
                    ?.ToLowerInvariant()
                ?? string.Empty,
            _ => value.ToString()?.ToLowerInvariant() ?? string.Empty
        };

    private static bool TryUnwrapConvert(
        string value,
        out string converted)
    {
        converted = value;
        if (!value.StartsWith("convert(", StringComparison.Ordinal)
            || !value.EndsWith(')'))
        {
            return false;
        }

        var inner = value["convert(".Length..^1];
        var depth = 0;

        for (var index = 0; index < inner.Length; index++)
        {
            switch (inner[index])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    converted = TrimOuterParentheses(inner[(index + 1)..]);
                    return true;
            }
        }

        return false;
    }

    private static string TrimOuterParentheses(string value)
    {
        var result = value.Trim();
        while (result.Length >= 2
               && result[0] == '('
               && result[^1] == ')'
               && OuterParenthesesWrapWholeExpression(result))
        {
            result = result[1..^1].Trim();
        }

        return result;
    }

    private static bool OuterParenthesesWrapWholeExpression(string value)
    {
        var depth = 0;
        var inString = false;

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '\'')
            {
                if (inString
                    && index + 1 < value.Length
                    && value[index + 1] == '\'')
                {
                    index++;
                    continue;
                }

                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (character == '(')
            {
                depth++;
            }
            else if (character == ')')
            {
                depth--;
                if (depth == 0 && index != value.Length - 1)
                {
                    return false;
                }
            }
        }

        return depth == 0;
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(
        @"\((-?\d+(?:\.\d+)?)\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex NumericLiteralParenthesesRegex();

    [GeneratedRegex(
        @"\A(?<guard>(?:\[[^\]]+\]|[a-z_][a-z0-9_]*)\s+IS\s+NOT\s+NULL\s+AND\s+)(?<predicate>.+)\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex NullGuardedPredicateRegex();

    [GeneratedRegex(
        @"\A(?:\[[^\]]+\]|[a-z_][a-z0-9_]*)\s+IN\s*\(\s*N?'(?:[^']|'')*'(?:\s*,\s*N?'(?:[^']|'')*')*\s*\)\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LiteralInPredicateRegex();
}
