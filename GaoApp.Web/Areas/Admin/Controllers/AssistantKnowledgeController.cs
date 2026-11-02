using System.Globalization;
using GaoApp.Application.Common.Security;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Security;
using GaoApp.Web.Services.Assistant;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Areas.Admin.Controllers;

public sealed record PolicyAdminPage(string StoreName, PolicyDocument Document, PolicyDraft Draft, DateTimeOffset Now, string? Error = null);
[Area("Admin"), Authorize(Policy = PermissionCodes.System.Setting.Update), AutoValidateAntiforgeryToken, Route("admin/assistant/knowledge")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AssistantKnowledgeController(AssistantAccess access, AssistantPolicyKnowledge knowledge, AppDbContext db, TimeProvider clock, ResourceAdmission admission) : Controller
{
    private async Task Recheck(AssistantIdentity who, CancellationToken ct) {
        var current = await access.MonitorAsync(ct); if (current.UserId != who.UserId || current.Scope != who.Scope) throw AssistantException.Denied();
    }
    private async Task<PolicyAdminPage> Page(AssistantIdentity who, string? topic, Guid? edit, PolicyDraft? draft, string? error, CancellationToken ct) {
        var store = AssistantAccess.Id(who.Scope.StoreId);
        var info = await db.Stores.IgnoreQueryFilters().AsNoTracking().Where(s => s.Id == store && !s.IsDeleted && s.IsActive)
            .Select(s => new { s.Name, s.ReceiptAddress, s.ReceiptPhone }).SingleOrDefaultAsync(ct) ?? throw AssistantException.Denied();
        var doc = await knowledge.ReadAsync(store, ct);
        var old = edit is null ? null : doc.Revisions.SingleOrDefault(r => r.Id == edit);
        if (edit is not null && old is null) throw new AssistantException(404, "policy_not_found", "Không tìm thấy phiên bản trong cửa hàng.");
        topic = old?.Topic ?? (AssistantPolicyKnowledge.Topics.Contains(topic) ? topic : "store");
        var body = topic == "store" ? $"Tên cửa hàng: {info.Name}\nĐịa chỉ: {info.ReceiptAddress}\nLiên hệ: {info.ReceiptPhone}\n[CẦN XÁC NHẬN] Kiểm tra địa chỉ, số liên hệ và bổ sung giờ mở cửa."
            : topic switch {
                "delivery" => "[CẦN XÁC NHẬN] Điền khu vực phục vụ, phí giao, điều kiện miễn phí, thời gian dự kiến và cách liên hệ khi giao trễ.",
                "returns" => "[CẦN XÁC NHẬN] Điền điều kiện đổi/trả, thời hạn, chứng từ cần có, ngoại lệ theo nhóm hàng và cách tiếp nhận.",
                "payment" => "[CẦN XÁC NHẬN] Điền phương thức thanh toán đang nhận và cách xác minh. Không đưa khóa truy cập, mật khẩu hoặc thông tin khách hàng vào đây.",
                "membership" => "[CẦN XÁC NHẬN] Điền điều kiện thành viên, cách tích/đổi điểm, hạn dùng và ngoại lệ. Điểm riêng của khách phải tra từ nghiệp vụ.",
                _ => "[CẦN XÁC NHẬN] Điền tên chương trình, đối tượng, điều kiện, thời gian bắt đầu/kết thúc và giới hạn. Giá từng mã hàng vẫn tra từ nguồn giá hiện hành." };
        var result = new PolicyAdminPage(info.Name, doc, draft ?? new(topic!, old?.Title ?? AssistantPolicyKnowledge.Label(topic!), old?.Body ?? body,
            clock.GetUtcNow(), null, old is null ? "Bản soạn từ trường cấu hình/mẫu GaoApp; chủ cửa hàng cần kiểm tra." : "Biên soạn từ phiên bản " + old.Id), clock.GetUtcNow(), error);
        await Recheck(who, ct); return result;
    }
    [HttpGet("")]
    public async Task<IActionResult> Index(string? topic, Guid? edit, CancellationToken ct) {
        try {
            var who = await access.MonitorAsync(ct);
            using var lease = admission.Enter(ResourceGroup.AssistantRead, ResourceSubject.User(who.UserId), AssistantAccess.Id(who.Scope.StoreId));
            if (!lease.IsAcquired) return ResourceProtectionResponse.Result(HttpContext, lease);
            return View("~/Areas/Admin/Views/AssistantKnowledge/Index.cshtml", await Page(who, topic, edit, null, null, ct));
        } catch (AssistantException ex) { return StatusCode(ex.Status, ex.Body(Guid.Empty)); }
    }
    private static DateTimeOffset? Date(string? input) => DateTimeOffset.TryParseExact(input, ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
        ? new DateTimeOffset(parsed.DateTime, TimeSpan.FromHours(7)) : null;
    [HttpPost("save")]
    public async Task<IActionResult> Save(string topic, string title, string body, string from, string? until, string sourceNote, string version, Guid requestId, CancellationToken ct) {
        AssistantIdentity? who = null; PolicyDraft? draft = null;
        try {
            who = await access.MonitorAsync(ct);
            using var lease = admission.Enter(ResourceGroup.AssistantWork, ResourceSubject.User(who.UserId), AssistantAccess.Id(who.Scope.StoreId));
            if (!lease.IsAcquired) return ResourceProtectionResponse.Result(HttpContext, lease);
            var start = Date(from); var end = string.IsNullOrWhiteSpace(until) ? null : Date(until);
            if (start is null || !string.IsNullOrWhiteSpace(until) && end is null || !ModelState.IsValid) throw AssistantException.Invalid();
            draft = new(topic, title, body, start.Value, end, sourceNote); await Recheck(who, ct);
            await knowledge.SaveAsync(AssistantAccess.Id(who.Scope.StoreId), who.UserId, draft, version, requestId, ct);
            TempData["PolicyResult"] = "Đã lưu bản nháp. Kiểm tra nội dung và duyệt ở lịch sử phiên bản bên dưới.";
            return RedirectToAction(nameof(Index), new { topic });
        } catch (AssistantException ex) {
            if (who is null || ex.Status is 401 or 403) return StatusCode(ex.Status, ex.Body(requestId));
            Response.StatusCode = ex.Status;
            return View("~/Areas/Admin/Views/AssistantKnowledge/Index.cshtml", await Page(who, topic, null, draft, ex.Message, ct));
        }
    }
    [HttpPost("review")]
    public async Task<IActionResult> Review(Guid id, string decision, string version, Guid requestId, bool confirmed, CancellationToken ct) {
        try {
            var who = await access.MonitorAsync(ct);
            using var lease = admission.Enter(ResourceGroup.AssistantWork, ResourceSubject.User(who.UserId), AssistantAccess.Id(who.Scope.StoreId));
            if (!lease.IsAcquired) return ResourceProtectionResponse.Result(HttpContext, lease);
            await Recheck(who, ct);
            await knowledge.ReviewAsync(AssistantAccess.Id(who.Scope.StoreId), who.UserId, id, decision, version, requestId, confirmed, ct);
            TempData["PolicyResult"] = decision == "approve" ? "Đã duyệt. Bản này thay bản cũ từ ngày áp dụng; lịch sử chat cũ vẫn là ảnh chụp tại thời điểm trả lời." : "Đã thu hồi/hủy lịch. Hãy hỏi lại chatbot để kiểm tra bản đang áp dụng.";
        } catch (AssistantException ex) { if (ex.Status is 401 or 403) return StatusCode(ex.Status, ex.Body(requestId)); TempData["PolicyResult"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }
}