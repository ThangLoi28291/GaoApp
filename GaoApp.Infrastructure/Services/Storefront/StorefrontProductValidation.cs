using System.ComponentModel.DataAnnotations;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Infrastructure.Services.Storefront;

internal static class StorefrontProductValidation
{
    internal static void Require(bool valid, string message)
    { if (!valid) throw new ValidationAppException(message); }
    internal static void Validate(object value)
    {
        var errors = new List<ValidationResult>();
        Require(Validator.TryValidateObject(value, new ValidationContext(value), errors, true), string.Join(" ", errors.Select(x => x.ErrorMessage)));
    }
    internal static void Page(int page, int size) => Require(page is > 0 and <= 1000000 && size is > 0 and <= 100, "Trang không hợp lệ; mỗi trang tối đa 100 dòng.");
    internal static void Kg(decimal? weight) => Require(weight is null || weight > 0 && weight < 1_000_000_000_000m && decimal.Round(weight.Value, 6) == weight,
        "Kg phải dương, trong giới hạn lưu trữ và tối đa sáu số lẻ.");
    internal static string Token(byte[]? value) => value is null ? "" : Convert.ToBase64String(value);
    internal static bool WellFormedVersion(string? version, bool exists)
    {
        if (!exists) return string.IsNullOrEmpty(version);
        try { return version is { Length: > 0 and <= 20 } && Convert.FromBase64String(version).Length == 8; }
        catch (FormatException) { return false; }
    }
    internal static bool Matches(string? version, byte[]? current)
    {
        try { return (current ?? []).AsSpan().SequenceEqual(string.IsNullOrEmpty(version) ? [] : version.Length <= 20 ? Convert.FromBase64String(version) : throw new FormatException()); }
        catch (FormatException) { return false; }
    }
    internal static void Version(string? version, byte[]? current)
    { if (!Matches(version, current)) throw new ConflictAppException("Sản phẩm website đã thay đổi. Hãy tải lại trang trước khi lưu."); }
    internal static async Task Lock(AppDbContext db, int store, CancellationToken ct)
    {
        if (!db.CanWriteStoreData || db.CurrentStoreId != store || store <= 0) throw new ForbiddenAppException("Không có phạm vi ghi cửa hàng.");
        await ResourceLock(db, "GaoApp:Storefront:Configuration:" + store, ct);
    }
    // Acquire only after the store configuration lock. Cleanup uses this exact resource and never takes the store lock.
    internal static Task LockMedia(AppDbContext db, CancellationToken ct) => ResourceLock(db, "gao-media-cleanup", ct);
    private static async Task ResourceLock(AppDbContext db, string resource, CancellationToken ct)
    {
        var tx = db.Database.CurrentTransaction ?? throw new InvalidOperationException("Publication lock requires a transaction.");
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = tx.GetDbTransaction();
        command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; SELECT @result;";
        var parameter = command.CreateParameter(); parameter.ParameterName = "@resource"; parameter.Value = resource; command.Parameters.Add(parameter);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) < 0) throw new ConflictAppException("Sản phẩm hoặc cấu hình đang được cập nhật. Hãy thử lại.");
    }
}
