using GaoApp.Application.Common.Results;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace GaoApp.Web.Common.Extensions;

/// <summary>
/// Helper đưa Result vào ModelState.
/// Dùng cho MVC form post.
/// </summary>
public static class ModelStateDictionaryExtensions
{
    /// <summary>
    /// Đẩy lỗi từ Result vào ModelState.
    /// - Nếu có ValidationErrors: add theo field
    /// - Nếu không: add lỗi tổng quát
    /// </summary>
    public static void AddResultErrors(this ModelStateDictionary modelState, Result result)
    {
        if (result.IsSuccess)
        {
            return;
        }

        if (result.HasValidationErrors)
        {
            foreach (var error in result.ValidationErrors)
            {
                // Nếu không có field name thì add lỗi global
                var key = string.IsNullOrWhiteSpace(error.PropertyName)
                    ? string.Empty
                    : error.PropertyName;

                modelState.AddModelError(key, error.ErrorMessage);
            }

            return;
        }

        modelState.AddModelError(string.Empty, result.Error.Message);
    }
}