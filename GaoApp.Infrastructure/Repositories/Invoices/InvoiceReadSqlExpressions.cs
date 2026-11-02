using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Invoices;

internal static class InvoiceReadSqlExpressions
{
    // SQL's usual IsNullOrWhiteSpace translation only recognizes ordinary spaces.
    // Preserve the former in-memory classification for all .NET whitespace characters.
    public static Expression<Func<T, TResult>> ForProvider<T, TResult>(Expression<Func<T, TResult>> expression, bool sqlServer)
        => sqlServer ? (Expression<Func<T, TResult>>)new WhitespaceVisitor().Visit(expression)! : expression;

    // Dashboard markers are ASCII. A linguistic SQL collation can equate other
    // Unicode letters with them, unlike the former OrdinalIgnoreCase checks.
    public static string FoldAscii(string value)
    {
        foreach (var lower in "timeouhp") value = value.Replace(lower, char.ToUpperInvariant(lower));
        return value;
    }

    private sealed class WhitespaceVisitor : ExpressionVisitor
    {
        private const string Whitespace = "\t\n\v\f\r \u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000";
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(InvoiceReadSqlExpressions) && node.Method.Name == nameof(FoldAscii))
            {
                Expression folded = Binary(Visit(node.Arguments[0]));
                foreach (var lower in "timeouhp")
                    folded = Expression.Call(folded, nameof(string.Replace), Type.EmptyTypes,
                        Expression.Constant(lower.ToString()), Expression.Constant(char.ToUpperInvariant(lower).ToString()));
                return folded;
            }
            if (node.Method.DeclaringType != typeof(string) || node.Method.Name != nameof(string.IsNullOrWhiteSpace))
                return base.VisitMethodCall(node);
            var value = Visit(node.Arguments[0]);
            Expression stripped = Binary(value);
            foreach (var character in Whitespace)
                stripped = Expression.Call(stripped, nameof(string.Replace), Type.EmptyTypes,
                    Expression.Constant(character.ToString()), Expression.Constant(string.Empty));
            return Expression.OrElse(Expression.Equal(value, Expression.Constant(null, typeof(string))),
                Expression.Equal(stripped, Expression.Constant(string.Empty)));
        }
        private static Expression Binary(Expression value) => Expression.Call(typeof(RelationalDbFunctionsExtensions),
            nameof(RelationalDbFunctionsExtensions.Collate), [typeof(string)],
            Expression.Constant(EF.Functions), value, Expression.Constant("Latin1_General_100_BIN2"));
    }
}
