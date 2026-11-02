using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using GaoApp.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace GaoApp.Infrastructure.Data;

public sealed class TenantDataScopeException() : InvalidOperationException(
    "Chưa xác định phạm vi cửa hàng hợp lệ. Không thực hiện truy cập dữ liệu.");

/// <summary>
/// Classify query shape once; check the actual request scope on every SQL execution,
/// including cached queries and IgnoreQueryFilters. Unclassified raw SQL is guarded too.
/// </summary>
internal sealed class TenantQueryScopeInterceptor : IQueryExpressionInterceptor
{
    internal const string StoreTag = "GaoApp:store-data";
    internal const string MetadataTag = "GaoApp:global-metadata";
    internal const string RawSqlTag = "GaoApp:raw-sql";
    internal static readonly TenantQueryScopeInterceptor Instance = new();
    private static readonly MethodInfo TagMethod = typeof(EntityFrameworkQueryableExtensions).GetMethods()
        .Single(method => method.Name == nameof(EntityFrameworkQueryableExtensions.TagWith) && method.GetParameters().Length == 2);

    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        if (eventData.Context is not AppDbContext db) return queryExpression;
        var scan = new ScopeScanner(db.Model); scan.Visit(queryExpression);
        if (!scan.HasRoot) return queryExpression;
        if ((scan.RequiresStore && !db.CanReadStoreData) || (scan.HasRawSql && !db.CanWriteStoreData))
            throw new TenantDataScopeException();
        return new TagRoots(scan.RequiresStore ? StoreTag : MetadataTag, scan.HasRawSql).Visit(queryExpression);
    }

    internal static bool IsStoreEntity(IEntityType entity, HashSet<IEntityType>? visited = null)
    {
        if (entity.FindProperty("StoreId")?.ClrType is { } type && (type == typeof(int) || type == typeof(int?))) return true;
        visited ??= [];
        if (!visited.Add(entity)) return false;
        return entity.GetForeignKeys().Any(fk => IsStoreEntity(fk.PrincipalEntityType, visited));
    }

    private sealed class ScopeScanner(IModel model) : ExpressionVisitor
    {
        public bool HasRoot { get; private set; }
        public bool RequiresStore { get; private set; }
        public bool HasRawSql { get; private set; }
        protected override Expression VisitExtension(Expression node)
        {
            if (node is EntityQueryRootExpression root)
            {
                HasRoot = true;
                HasRawSql |= root.GetType() != typeof(EntityQueryRootExpression);
                RequiresStore |= root.GetType() != typeof(EntityQueryRootExpression) || IsStoreEntity(root.EntityType);
                RequiresStore |= root.EntityType.GetNavigations().Any(n => n.IsEagerLoaded && IsStoreEntity(n.TargetEntityType));
            }
            else if (node is QueryRootExpression)
            {
                // Includes Database.SqlQuery<T> scalar/unmapped SQL roots. Never let a
                // raw SQL comment impersonate the classification of a global lookup.
                HasRoot = true;
                RequiresStore = true;
                HasRawSql = true;
            }
            return base.VisitExtension(node);
        }
        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression is not null && model.FindEntityType(node.Expression.Type) is { } owner)
            {
                var target = owner.FindNavigation(node.Member.Name)?.TargetEntityType
                    ?? owner.FindSkipNavigation(node.Member.Name)?.TargetEntityType;
                if (target is not null) RequiresStore |= IsStoreEntity(target);
            }
            return base.VisitMember(node);
        }
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(EF) && node.Method.Name == nameof(EF.Property) &&
                node.Arguments[1] is ConstantExpression { Value: string property } &&
                model.FindEntityType(node.Arguments[0].Type)?.FindNavigation(property) is { } navigation)
                RequiresStore |= IsStoreEntity(navigation.TargetEntityType);
            if (node.Method.Name == nameof(EntityFrameworkQueryableExtensions.Include) &&
                node.Arguments.Count == 2 && node.Arguments[1] is ConstantExpression { Value: string path })
            {
                var owner = model.FindEntityType(node.Arguments[0].Type.GetGenericArguments().FirstOrDefault() ?? typeof(object));
                foreach (var segment in path.Split('.'))
                {
                    owner = owner?.FindNavigation(segment)?.TargetEntityType ?? owner?.FindSkipNavigation(segment)?.TargetEntityType;
                    if (owner is not null) RequiresStore |= IsStoreEntity(owner);
                }
            }
            return base.VisitMethodCall(node);
        }
    }

    private sealed class TagRoots(string tag, bool rawSql) : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node)
            => node is QueryRootExpression root
                ? Tag(root, node)
                : base.VisitExtension(node);

        private Expression Tag(QueryRootExpression root, Expression node)
        {
            var method = TagMethod.MakeGenericMethod(root.ElementType);
            var tagged = Expression.Call(method, node, Expression.Constant(tag));
            return rawSql ? Expression.Call(method, tagged, Expression.Constant(RawSqlTag)) : tagged;
        }
    }
}

internal sealed class TenantCommandScopeInterceptor : DbCommandInterceptor
{
    internal static readonly TenantCommandScopeInterceptor Instance = new();
    private static void Check(DbCommand command, CommandEventData data, bool write)
    {
        if (data.Context is not AppDbContext db) return;
        if (write || data.CommandSource == CommandSource.SaveChanges)
        {
            if (!db.CanWriteStoreData) throw new TenantDataScopeException();
            return;
        }
        if (db.CanReadStoreData)
        {
            // Read-only discovery uses classified LINQ queries. Raw SQL may contain writes
            // even when executed through a reader (for example UPDATE ... OUTPUT).
            if (!db.CanWriteStoreData &&
                (command.CommandText.Contains("-- " + TenantQueryScopeInterceptor.RawSqlTag, StringComparison.Ordinal) ||
                 (!command.CommandText.Contains("-- " + TenantQueryScopeInterceptor.StoreTag, StringComparison.Ordinal) &&
                  !command.CommandText.Contains("-- " + TenantQueryScopeInterceptor.MetadataTag, StringComparison.Ordinal))))
                throw new TenantDataScopeException();
            return;
        }
        // A global lookup may resolve the store or authenticate a user before a store scope exists.
        // The classifier always marks store-dependent roots/navigations, even when filters are ignored.
        if (command.CommandText.Contains("-- " + TenantQueryScopeInterceptor.StoreTag, StringComparison.Ordinal) ||
            !command.CommandText.Contains("-- " + TenantQueryScopeInterceptor.MetadataTag, StringComparison.Ordinal))
            throw new TenantDataScopeException();
    }
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    { Check(command, eventData, false); return result; }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    { Check(command, eventData, false); return ValueTask.FromResult(result); }
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    { Check(command, eventData, false); return result; }
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    { Check(command, eventData, false); return ValueTask.FromResult(result); }
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    { Check(command, eventData, true); return result; }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { Check(command, eventData, true); return ValueTask.FromResult(result); }
}
