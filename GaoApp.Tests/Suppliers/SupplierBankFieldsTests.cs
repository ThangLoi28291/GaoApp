using System.ComponentModel.DataAnnotations;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Suppliers;
using GaoApp.Application.Mappings.Suppliers;
using GaoApp.Application.Services.Suppliers;
using GaoApp.Application.Validators.Suppliers;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Repositories.Suppliers;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using GaoApp.Web.Areas.Admin.ViewModels.Suppliers;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Suppliers;

public sealed class SupplierBankFieldsTests
{
    [Fact]
    public async Task Create_and_edit_roundtrip_trim_preserve_case_spaces_zeros_and_clear()
    {
        await using var f = new SupplierBankFixture();
        var result = await f.Service.CreateAsync(17, Request(), 9);
        Assert.True(result.IsSuccess);
        f.Db.ChangeTracker.Clear();
        var row = await f.Db.Suppliers.SingleAsync();
        Assert.Equal("0012 034567", row.BankAccountNumber);
        Assert.Equal("Nguyễn Văn Synthetic", row.BankAccountName);
        Assert.Equal("Ngân hàng Test", row.BankName);
        Assert.Equal("Separate note", row.Note);
        Assert.StartsWith("NCC", row.Code);
        Assert.Equal(17, row.StoreId);
        var edit = (await f.Service.GetForEditAsync(17, result.Value)).Value;
        Assert.Equal(row.BankAccountNumber, edit.BankAccountNumber);
        Assert.Equal(row.BankAccountName, edit.BankAccountName);
        Assert.Equal(row.BankName, edit.BankName);
        var update = Update(row);
        update.BankAccountNumber = "  0000 AB12  ";
        update.BankAccountName = "  Chủ mới  ";
        update.BankName = "  Test bank lowercase  ";
        Assert.True((await f.Service.UpdateAsync(17, update, 9)).IsSuccess);
        f.Db.ChangeTracker.Clear();
        row = await f.Db.Suppliers.SingleAsync();
        Assert.Equal("0000 AB12", row.BankAccountNumber);
        Assert.Equal("Chủ mới", row.BankAccountName);
        Assert.Equal("Test bank lowercase", row.BankName);
        update = Update(row);
        update.BankAccountNumber = " \t ";
        update.BankAccountName = "";
        update.BankName = null;
        Assert.True((await f.Service.UpdateAsync(17, update, 9)).IsSuccess);
        f.Db.ChangeTracker.Clear();
        row = await f.Db.Suppliers.SingleAsync();
        Assert.Null(row.BankAccountNumber);
        Assert.Null(row.BankAccountName);
        Assert.Null(row.BankName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    public async Task Create_optional_bank_fields_persist_null(string? blank)
    {
        await using var f = new SupplierBankFixture();
        var request = Request();
        request.BankAccountNumber = request.BankAccountName = request.BankName = blank;
        Assert.True((await f.Service.CreateAsync(17, request, 9)).IsSuccess);
        f.Db.ChangeTracker.Clear();
        var row = await f.Db.Suppliers.SingleAsync();
        Assert.Null(row.BankAccountNumber); Assert.Null(row.BankAccountName); Assert.Null(row.BankName);
    }

    [Theory]
    [InlineData("BankAccountNumber", 50, "Số tài khoản tối đa 50 ký tự")]
    [InlineData("BankAccountName", 250, "Tên chủ tài khoản tối đa 250 ký tự")]
    [InlineData("BankName", 250, "Tên ngân hàng tối đa 250 ký tự")]
    public async Task All_validation_surfaces_accept_max_and_reject_over_max(string field, int max, string message)
    {
        foreach (var type in new[] { typeof(CreateSupplierRequest), typeof(UpdateSupplierRequest), typeof(SupplierEditViewModel) })
        {
            var property = type.GetProperty(field)!;
            var attribute = Assert.IsType<StringLengthAttribute>(Attribute.GetCustomAttribute(property, typeof(StringLengthAttribute)));
            Assert.True(attribute.IsValid(new string('x', max)));
            Assert.False(attribute.IsValid(new string('x', max + 1)));
            Assert.Equal(message, attribute.ErrorMessage);
        }
        await using var f = new SupplierBankFixture();
        var request = Request();
        typeof(CreateSupplierRequest).GetProperty(field)!.SetValue(request, new string('x', max));
        Assert.True((await f.Service.CreateAsync(17, request, 9)).IsSuccess);
        var row = await f.Db.Suppliers.SingleAsync();
        var update = Update(row);
        typeof(UpdateSupplierRequest).GetProperty(field)!.SetValue(update, new string('x', max));
        Assert.True((await f.Service.UpdateAsync(17, update, 9)).IsSuccess);
        typeof(UpdateSupplierRequest).GetProperty(field)!.SetValue(update, new string('x', max + 1));
        Assert.True((await f.Service.UpdateAsync(17, update, 9)).IsFailure);
        request.Name = "Other synthetic supplier";
        typeof(CreateSupplierRequest).GetProperty(field)!.SetValue(request, new string('x', max + 1));
        Assert.True((await f.Service.CreateAsync(17, request, 9)).IsFailure);
        var edit = new SupplierEditDto { Name = "Synthetic" };
        typeof(SupplierEditDto).GetProperty(field)!.SetValue(edit, new string('x', max + 1));
        var errors = new SupplierEditDtoValidator().Validate(edit).Errors;
        Assert.Contains(errors, e => e.PropertyName == field && e.ErrorMessage == message);
        Assert.Equal(1, await f.Db.Suppliers.CountAsync());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData("1", "****")]
    [InlineData("1234", "****")]
    [InlineData("0012345", "**** 2345")]
    [InlineData("  0012 AB34  ", "**** AB34")]
    public void List_mapping_only_exposes_masked_number(string? number, string? expected)
    {
        var row = new Supplier { Code = "S", Name = "Synthetic", BankAccountNumber = number, BankName = "Test bank" };
        var list = row.ToListItemDto();
        Assert.Equal(expected, list.MaskedBankAccountNumber);
        Assert.Equal("Test bank", list.BankName);
        Assert.Null(typeof(SupplierListItemDto).GetProperty("BankAccountNumber"));
        Assert.Null(typeof(SupplierListItemDto).GetProperty("BankAccountName"));
    }

    [Fact]
    public async Task Bank_fields_preserve_status_delete_tax_resolution_and_store_boundaries()
    {
        await using var f = new SupplierBankFixture();
        var request = Request(); request.TaxCode = " 001-002 ";
        var id = (await f.Service.CreateAsync(17, request, 9)).Value;
        var row = await f.Db.Suppliers.SingleAsync();
        Assert.True((await f.Service.GetForEditAsync(18, id)).IsFailure);
        Assert.True((await f.Service.UpdateAsync(18, Update(row), 9)).IsFailure);
        Assert.True((await f.Service.ToggleStatusAsync(18, id, 9)).IsFailure);
        Assert.True((await f.Service.SoftDeleteAsync(18, id, 9)).IsFailure);
        Assert.Empty((await f.Service.GetPagedAsync(18, null, 1, 20)).Items);
        Assert.Single(await f.Repository.GetActiveByNormalizedTaxCodeAsync(17, "001002"));
        Assert.True((await f.Service.ToggleStatusAsync(17, id, 9)).IsSuccess);
        Assert.Empty(await f.Repository.GetActiveByNormalizedTaxCodeAsync(17, "001002"));
        Assert.True((await f.Service.ToggleStatusAsync(17, id, 9)).IsSuccess);
        Assert.Equal("0012 034567", row.BankAccountNumber);
        Assert.True((await f.Service.SoftDeleteAsync(17, id, 9)).IsSuccess);
        Assert.Empty((await f.Service.GetPagedAsync(17, null, 1, 20)).Items);
        Assert.Empty(await f.Repository.GetActiveByNormalizedTaxCodeAsync(17, "001002"));
        Assert.Equal("0012 034567", (await f.Db.Suppliers.IgnoreQueryFilters().SingleAsync()).BankAccountNumber);
    }

    [Theory]
    [InlineData("Code", "Supplier.DuplicateCode")]
    [InlineData("Name", "Supplier.DuplicateName")]
    [InlineData("TaxCode", "Supplier.DuplicateActiveTaxCode")]
    public async Task Create_and_update_keep_duplicate_guards_with_bank_fields(string field, string error)
    {
        await using var f = new SupplierBankFixture();
        var first = Request(); first.Code = "FIRST"; first.TaxCode = "001002";
        Assert.True((await f.Service.CreateAsync(17, first, 9)).IsSuccess);
        var other = Request(); other.Code = "SECOND"; other.Name = "Second synthetic"; other.TaxCode = "999";
        typeof(CreateSupplierRequest).GetProperty(field)!.SetValue(other, typeof(CreateSupplierRequest).GetProperty(field)!.GetValue(first));
        Assert.Equal(error, (await f.Service.CreateAsync(17, other, 9)).Error.Code);
        f.Db.ChangeTracker.Clear();
        var second = new Supplier { StoreId = 17, Code = "SECOND", Name = "Second synthetic", TaxCode = "999", IsActive = true };
        f.Db.Suppliers.Add(second); await f.Db.SaveChangesAsync();
        var update = Update(second);
        typeof(UpdateSupplierRequest).GetProperty(field)!.SetValue(update, typeof(CreateSupplierRequest).GetProperty(field)!.GetValue(first));
        Assert.Equal(error, (await f.Service.UpdateAsync(17, update, 9)).Error.Code);
    }

    internal static CreateSupplierRequest Request() => new()
    {
        Name = "Synthetic supplier", BankAccountNumber = " 0012 034567 ",
        BankAccountName = " Nguyễn Văn Synthetic ", BankName = " Ngân hàng Test ", Note = "Separate note"
    };

    internal static UpdateSupplierRequest Update(Supplier row) => new()
    {
        Id = row.Id, Code = row.Code, Name = row.Name, TaxCode = row.TaxCode, Status = row.IsActive,
        BankAccountNumber = row.BankAccountNumber, BankAccountName = row.BankAccountName, BankName = row.BankName,
        Note = row.Note, RowVersion = row.RowVersion
    };
}

internal sealed class SupplierBankFixture : IAsyncDisposable
{
    public TenantContext Tenant { get; } = new();
    public InMemoryAppDbContext Db { get; }
    public SupplierRepository Repository { get; }
    public SupplierService Service { get; }
    public SupplierBankFixture()
    {
        Tenant.SetStore(17, "supplier-bank-test");
        Db = new InMemoryAppDbContext(new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Tenant, new CurrentUser());
        Repository = new SupplierRepository(Db);
        Service = new SupplierService(Repository, new SupplierEditDtoValidator());
    }
    public ValueTask DisposeAsync() => Db.DisposeAsync();
    private sealed class CurrentUser : ICurrentUser
    {
        public int? UserId => 9;
        public string? UserName => "synthetic";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
