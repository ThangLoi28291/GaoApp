using System.ComponentModel.DataAnnotations;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Common.POS;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services;

public sealed record GuestWifiSettings(string Name, string Password, string RowVersion);
public sealed record SaveGuestWifiRequest
{
    [StringLength(128)] public string? Name { get; init; }
    [StringLength(128)] public string? Password { get; init; }
    [Required] public string RowVersion { get; init; } = "";
}
public sealed record CustomerDisplayInfo(int StoreId, int? TerminalId, string TerminalName, string CashierName,
    string WifiName, string WifiPassword);

public sealed class CustomerDisplayService(AppDbContext db, IPOSRuntimeContextAccessor runtime)
{
    private int StoreId => db.CurrentStoreId is > 0 ? db.CurrentStoreId.Value
        : throw new ConflictAppException("Chưa xác định cửa hàng.");

    public async Task<GuestWifiSettings> GetWifiAsync(CancellationToken ct)
    {
        var store = await db.Stores.AsNoTracking().SingleAsync(x => x.Id == StoreId && !x.IsDeleted, ct);
        return new(store.GuestWifiName ?? "", store.GuestWifiPassword ?? "", Convert.ToBase64String(store.RowVersion));
    }

    public async Task<CustomerDisplayInfo> GetInfoAsync(CancellationToken ct)
    {
        var wifi = await GetWifiAsync(ct);
        // The operator belongs to this authenticated device session, not to the order or shift creator.
        var employee = await db.UserInStores.AsNoTracking()
            .Where(x => x.StoreId == StoreId && x.UserId == runtime.UserId && x.IsActive && !x.IsDeleted
                && x.User.IsActive && !x.User.IsDeleted)
            .Select(x => new { x.User.FullName, x.User.UserName }).SingleOrDefaultAsync(ct);
        var name = !string.IsNullOrWhiteSpace(employee?.FullName) ? employee.FullName : employee?.UserName;
        return new(StoreId, runtime.TerminalId, runtime.TerminalName ?? "Quầy thanh toán",
            name ?? "Chưa xác định nhân viên", wifi.Name, string.IsNullOrEmpty(wifi.Name) ? "" : wifi.Password);
    }

    public async Task<GuestWifiSettings> SaveWifiAsync(SaveGuestWifiRequest request, CancellationToken ct)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), errors, true))
            throw new ValidationAppException("Tên và mật khẩu Wi-Fi tối đa 128 ký tự. Tải lại trang nếu thông tin đã thay đổi.");
        var name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name;
        if (name is null && !string.IsNullOrEmpty(request.Password))
            throw new ValidationAppException("Nhập tên Wi-Fi hoặc xóa cả tên và mật khẩu để ẩn thông tin Wi-Fi.");
        var store = await db.Stores.SingleAsync(x => x.Id == StoreId && !x.IsDeleted, ct);
        if (request.RowVersion != Convert.ToBase64String(store.RowVersion))
            throw new ConflictAppException("Thông tin cửa hàng đã thay đổi ở máy khác. Tải lại trang trước khi lưu.");
        store.GuestWifiName = name;
        // Spaces and case are part of Wi-Fi credentials; do not trim them.
        store.GuestWifiPassword = name is null || string.IsNullOrEmpty(request.Password) ? null : request.Password;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        { throw new ConflictAppException("Thông tin cửa hàng đã thay đổi ở máy khác. Tải lại trang trước khi lưu."); }
        return new(store.GuestWifiName ?? "", store.GuestWifiPassword ?? "", Convert.ToBase64String(store.RowVersion));
    }
}
