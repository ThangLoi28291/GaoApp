using GaoApp.Application.DTOs.POS;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Orders;

public sealed partial class OrderRepository
{
    private const string OrderSearchCollation = "Latin1_General_100_CI_AI";
    private static string SearchTerm(string? value) => (value ?? "").Trim().Replace("Đ", "D").Replace("đ", "d");

    public async Task<List<OrderFilterOptionDto>> GetListFilterOptionsAsync(bool employees, string? term, CancellationToken ct = default)
    {
        if (_db.CurrentStoreId is not > 0) return [];
        var storeId = _db.CurrentStoreId.Value;
        var text = SearchTerm(term);
        if (employees)
        {
            var users = _db.UserInStores.AsNoTracking()
                .Where(m => m.StoreId == storeId && !m.IsDeleted && m.IsActive && !m.User.IsDeleted && m.User.IsActive)
                .Select(m => new { Id = m.UserId, Name = string.IsNullOrWhiteSpace(m.User.FullName) ? m.User.UserName : m.User.FullName!, Code = m.User.UserName })
                .Distinct();
            if (text.Length > 0) users = users.Where(u =>
                EF.Functions.Collate(u.Name.Replace("Đ", "D").Replace("đ", "d"), OrderSearchCollation).Contains(text)
                || EF.Functions.Collate(u.Code, OrderSearchCollation).Contains(text));
            return await users.OrderBy(u => u.Name).ThenBy(u => u.Id).Take(20)
                .Select(u => new OrderFilterOptionDto(u.Id, u.Name, u.Code)).ToListAsync(ct);
        }
        var terminals = _db.POSTerminals.AsNoTracking().Where(t => t.StoreId == storeId && !t.IsDeleted && t.IsActive && t.Status == POSTerminalStatus.Active);
        if (text.Length > 0) terminals = terminals.Where(t =>
            EF.Functions.Collate(t.Name.Replace("Đ", "D").Replace("đ", "d"), OrderSearchCollation).Contains(text)
            || EF.Functions.Collate(t.Code, OrderSearchCollation).Contains(text));
        return await terminals.OrderBy(t => t.Name).ThenBy(t => t.Id).Take(20)
            .Select(t => new OrderFilterOptionDto(t.Id, t.Name, t.Code)).ToListAsync(ct);
    }

    private IQueryable<Order> ApplyListFilters(IQueryable<Order> orders, int storeId, OrderListQueryDto? filters)
    {
        if (filters == null) return orders;
        // Keep unfiltered count/timeline queries free of joins. Match dimensions only when requested.
        if (filters.EmployeeId.HasValue || !string.IsNullOrWhiteSpace(filters.Employee))
        {
            var term = SearchTerm(filters.Employee);
            var members = _db.UserInStores.Where(m => m.StoreId == storeId && !m.IsDeleted && m.IsActive && !m.User.IsDeleted && m.User.IsActive);
            members = filters.EmployeeId.HasValue ? members.Where(m => m.UserId == filters.EmployeeId.Value) : members.Where(m =>
                EF.Functions.Collate(m.User.UserName, OrderSearchCollation).Contains(term) || (m.User.FullName != null &&
                EF.Functions.Collate(m.User.FullName.Replace("Đ", "D").Replace("đ", "d"), OrderSearchCollation).Contains(term)));
            var users = members.Select(m => m.UserId);
            var shifts = _db.POSShifts.Where(s => s.StoreId == storeId && !s.IsDeleted && users.Contains(s.OpenedByUserId)).Select(s => s.Id);
            orders = orders.Where(o => shifts.Contains(o.POSShiftId));
        }
        if (filters.TerminalId.HasValue || !string.IsNullOrWhiteSpace(filters.Terminal))
        {
            var term = SearchTerm(filters.Terminal);
            var active = _db.POSTerminals.Where(t => t.StoreId == storeId && !t.IsDeleted && t.IsActive && t.Status == POSTerminalStatus.Active);
            active = filters.TerminalId.HasValue ? active.Where(t => t.Id == filters.TerminalId.Value) : active.Where(t =>
                EF.Functions.Collate(t.Name.Replace("Đ", "D").Replace("đ", "d"), OrderSearchCollation).Contains(term)
                || EF.Functions.Collate(t.Code, OrderSearchCollation).Contains(term));
            var terminals = active.Select(t => t.Id);
            var shifts = _db.POSShifts.Where(s => s.StoreId == storeId && !s.IsDeleted && terminals.Contains(s.TerminalId)).Select(s => s.Id);
            orders = orders.Where(o => shifts.Contains(o.POSShiftId));
        }
        if (!string.IsNullOrWhiteSpace(filters.Customer))
        {
            var term = SearchTerm(filters.Customer);
            var customers = _db.Customers.Where(c => c.StoreId == storeId && !c.IsDeleted &&
                (EF.Functions.Collate(c.Name.Replace("Đ", "D").Replace("đ", "d"), OrderSearchCollation).Contains(term)
                || (c.Phone != null && EF.Functions.Collate(c.Phone, OrderSearchCollation).Contains(term))
                || (c.Code != null && EF.Functions.Collate(c.Code, OrderSearchCollation).Contains(term))))
                .Select(c => c.Id);
            orders = orders.Where(o => o.CustomerId.HasValue && customers.Contains(o.CustomerId.Value));
        }
        if (filters.Settlement.HasValue)
        {
            var payments = _db.OrderPayments.Where(p => p.StoreId == storeId && !p.IsDeleted && p.Amount > 0);
            orders = filters.Settlement.Value switch
            {
                OrderSettlementFilter.Credit => orders.Where(o => o.IsCreditSale),
                OrderSettlementFilter.OutstandingCredit => orders.Where(o => o.IsCreditSale && o.Status == OrderStatus.Completed && o.BalanceDue > 0),
                OrderSettlementFilter.Mixed => orders.Where(o => payments.Where(p => p.OrderId == o.Id).Select(p => p.Method).Distinct().Count() > 1),
                OrderSettlementFilter.Cash => orders.Where(o => payments.Any(p => p.OrderId == o.Id && p.Method == PaymentMethod.Cash)),
                OrderSettlementFilter.BankTransfer => orders.Where(o => payments.Any(p => p.OrderId == o.Id && p.Method == PaymentMethod.BankTransfer)),
                OrderSettlementFilter.Card => orders.Where(o => payments.Any(p => p.OrderId == o.Id && p.Method == PaymentMethod.Card)),
                OrderSettlementFilter.EWallet => orders.Where(o => payments.Any(p => p.OrderId == o.Id && p.Method == PaymentMethod.EWallet)),
                OrderSettlementFilter.Other => orders.Where(o => payments.Any(p => p.OrderId == o.Id && p.Method == PaymentMethod.Other)),
                _ => orders.Where(o => false)
            };
        }
        return orders;
    }

    private async Task EnrichListPageAsync(List<OrderListItemDto> items, int storeId, CancellationToken ct)
    {
        if (items.Count == 0) return;
        var ids = items.Select(o => o.OrderId).ToArray();
        // All related information is loaded only for the already paged IDs. Missing/deleted
        // dimensions must not drop orders or change their count/pagination.
        var details = await (from o in _db.Orders.AsNoTracking().Where(o => o.StoreId == storeId && !o.IsDeleted && ids.Contains(o.Id))
            join s in _db.POSShifts.Where(s => s.StoreId == storeId && !s.IsDeleted) on o.POSShiftId equals s.Id into shifts
            from s in shifts.DefaultIfEmpty()
            join t in _db.POSTerminals.Where(t => t.StoreId == storeId && !t.IsDeleted) on s.TerminalId equals t.Id into terminals
            from t in terminals.DefaultIfEmpty()
            join c in _db.Customers.Where(c => c.StoreId == storeId && !c.IsDeleted) on o.CustomerId equals c.Id into customers
            from c in customers.DefaultIfEmpty()
            select new
            {
                o.Id, o.CustomerId, CustomerName = c.Name, CustomerPhone = c.Phone,
                TerminalName = t.Name, TerminalCode = t.Code,
                CashierName = _db.UserInStores.Where(m => m.StoreId == storeId && !m.IsDeleted && !m.User.IsDeleted && m.UserId == s.OpenedByUserId)
                    .OrderBy(m => m.Id).Select(m => string.IsNullOrWhiteSpace(m.User.FullName) ? m.User.UserName : m.User.FullName).FirstOrDefault()
            }).TagWith("POS orders: page identities").ToDictionaryAsync(x => x.Id, ct);
        var methods = await _db.OrderPayments.AsNoTracking()
            .Where(p => p.StoreId == storeId && !p.IsDeleted && p.Amount > 0 && ids.Contains(p.OrderId))
            .Select(p => new { p.OrderId, p.Method }).Distinct()
            .TagWith("POS orders: page payment methods").ToListAsync(ct);
        var byOrder = methods.ToLookup(p => p.OrderId, p => p.Method);
        foreach (var item in items)
        {
            item.PaymentMethods = byOrder[item.OrderId].OrderBy(m => m).Select(m => m.ToString()).ToList();
            if (!details.TryGetValue(item.OrderId, out var detail)) continue;
            item.CustomerName = detail.CustomerName ?? (detail.CustomerId.HasValue ? "Khách đã xóa / không còn thông tin" : "Khách lẻ");
            item.CustomerPhone = detail.CustomerPhone;
            item.CashierName = detail.CashierName;
            item.TerminalName = detail.TerminalName;
            item.TerminalCode = detail.TerminalCode;
        }
    }
}
