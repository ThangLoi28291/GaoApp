using GaoApp.Application.Common.Results;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace GaoApp.Web.Extensions;

public static class ModelStateExtensions
{
    public static void AddResultErrors(this ModelStateDictionary modelState, Result result)
    {
        if (result.IsSuccess)
            return;

        if (result.HasValidationErrors)
        {
            foreach (var error in result.ValidationErrors)
            {
                var key = string.IsNullOrWhiteSpace(error.PropertyName)
                    ? string.Empty
                    : error.PropertyName;

                modelState.AddModelError(key, error.ErrorMessage);
            }

            return;
        }

        modelState.AddModelError(string.Empty, result.Error.Message);
    }

    public static void AddResultErrors<T>(this ModelStateDictionary modelState, Result<T> result)
    {
        if (result.IsSuccess)
            return;

        if (result.HasValidationErrors)
        {
            foreach (var error in result.ValidationErrors)
            {
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