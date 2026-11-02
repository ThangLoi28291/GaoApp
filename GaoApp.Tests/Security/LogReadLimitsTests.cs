using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Audit;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Web.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Security;

public sealed class LogReadLimitsTests
{
    [Theory]
    [InlineData(0, 0, 1, 20, 0)]
    [InlineData(-5, -1, 1, 20, 0)]
    [InlineData(1, 1, 1, 1, 0)]
    [InlineData(2, 50, 2, 50, 50)]
    [InlineData(3, 100, 3, 100, 200)]
    [InlineData(3, int.MaxValue, 3, 100, 200)]
    [InlineData(int.MaxValue, 1, int.MaxValue, 1, 2147483646)]
    [InlineData(21474837, 100, 21474837, 100, 2147483600)]
    public void Both_query_types_normalize_paging_and_keep_repeated_validation_idempotent(
        int page, int size, int expectedPage, int expectedSize, int expectedOffset)
    {
        var audit = new AuditLogQueryDto { Page = page, PageSize = size };
        var invoice = new InvoiceIntegrationLogQueryDto { Page = page, PageSize = size };
        for (var repeat = 0; repeat < 3; repeat++)
        {
            Assert.Equal(expectedOffset, audit.NormalizeAndValidate());
            Assert.Equal(expectedOffset, invoice.NormalizeAndValidate());
            Assert.Equal((expectedPage, expectedSize), (audit.Page, audit.PageSize));
            Assert.Equal((expectedPage, expectedSize), (invoice.Page, invoice.PageSize));
        }
    }

    [Theory]
    [InlineData(int.MaxValue, 100)]
    [InlineData(int.MaxValue, int.MaxValue)]
    [InlineData(21474838, 100)]
    public void Overflowing_offsets_are_validation_errors_in_both_query_types(int page, int size)
    {
        Assert.Throws<ValidationAppException>(() => new AuditLogQueryDto { Page = page, PageSize = size }.NormalizeAndValidate());
        Assert.Throws<ValidationAppException>(() => new InvoiceIntegrationLogQueryDto { Page = page, PageSize = size }.NormalizeAndValidate());
    }

    [Fact]
    public void Length_limits_include_whitespace_and_exact_boundaries_remain_searchable()
    {
        new AuditLogQueryDto { Keyword = new('x', 200), EntityName = new('x', 150), EntityId = new('x', 100) }.NormalizeAndValidate();
        new InvoiceIntegrationLogQueryDto { Keyword = new('x', 200) }.NormalizeAndValidate();
        foreach (var keyword in new[] { new string('x', 201), new string(' ', 201) })
        {
            Assert.Throws<ValidationAppException>(() => new AuditLogQueryDto { Keyword = keyword }.NormalizeAndValidate());
            Assert.Throws<ValidationAppException>(() => new InvoiceIntegrationLogQueryDto { Keyword = keyword }.NormalizeAndValidate());
        }
        Assert.Throws<ValidationAppException>(() => new AuditLogQueryDto { EntityName = new('x', 151) }.NormalizeAndValidate());
        Assert.Throws<ValidationAppException>(() => new AuditLogQueryDto { EntityId = new('x', 101) }.NormalizeAndValidate());
    }

    [Fact]
    public void Audit_supports_full_UTC_range_and_invoice_dates_are_compared_as_business_dates()
    {
        new AuditLogQueryDto { FromUtc = DateTime.MinValue, ToUtc = DateTime.MaxValue }.NormalizeAndValidate();
        new InvoiceIntegrationLogQueryDto { FromDate = new(2026, 1, 1, 23, 0, 0), ToDate = new(2026, 1, 1) }.NormalizeAndValidate();
        Assert.Throws<ValidationAppException>(() => new AuditLogQueryDto { FromUtc = new(2026, 2, 1), ToUtc = new(2026, 1, 1) }.NormalizeAndValidate());
        Assert.Throws<ValidationAppException>(() => new InvoiceIntegrationLogQueryDto { FromDate = new(2026, 2, 1), ToDate = new(2026, 1, 1) }.NormalizeAndValidate());
    }

    [Theory]
    [InlineData("caller", 120)]
    [InlineData("store", 600)]
    [InlineData("process", 2400)]
    public void Default_heavy_read_budgets_bound_repeated_reads_at_each_scope(string scope, int limit)
    {
        var options = new ResourceProtectionOptions();
        Assert.Equal(60, options.WindowSeconds);
        Assert.Equal(4, options.Limits[ResourceGroup.HeavyRead].CallerConcurrency);
        using var admission = new ResourceAdmission(Options.Create(options), NullLogger<ResourceAdmission>.Instance);
        for (var i = 0; i < limit; i++)
        {
            var user = scope == "caller" ? 1 : i / 100 + 1;
            var store = scope == "process" ? i / 100 + 1 : 1;
            using var lease = admission.Enter(ResourceGroup.HeavyRead, ResourceSubject.User(user), store);
            Assert.True(lease.IsAcquired);
        }
        using var denied = admission.Enter(ResourceGroup.HeavyRead,
            ResourceSubject.User(scope == "caller" ? 1 : 999), scope == "process" ? 999 : 1);
        Assert.False(denied.IsAcquired);
        Assert.Equal("frequency", denied.Reason);
    }
}
