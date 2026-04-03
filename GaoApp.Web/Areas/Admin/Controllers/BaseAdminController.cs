using System.Security.Claims;
using GaoApp.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public abstract class BaseAdminController : Controller
    {
        protected void ToastSuccess(string msg) => TempData["Success"] = msg;
        protected void ToastError(string msg) => TempData["Error"] = msg;
        protected void ToastInfo(string msg) => TempData["Info"] = msg;
        protected void ToastWarning(string msg) => TempData["Warning"] = msg;

        /// <summary>
        /// Store hiện tại được resolve từ tenant context.
        /// </summary>
        protected int CurrentStoreId
        {
            get
            {
                var tenant = HttpContext.RequestServices.GetRequiredService<ITenantContext>();
                if (tenant.StoreId == null)
                    throw new InvalidOperationException("StoreId chưa được resolve. Hãy truy cập bằng subdomain store.");

                return tenant.StoreId.Value;
            }
        }

        /// <summary>
        /// User hiện tại từ claim NameIdentifier.
        /// </summary>
        protected int CurrentUserId
        {
            get
            {
                var value = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrWhiteSpace(value) || !int.TryParse(value, out var userId))
                    throw new InvalidOperationException("Không đọc được UserId từ claims hiện tại.");

                return userId;
            }
        }
    }
}