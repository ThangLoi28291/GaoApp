using GaoApp.Application.Common.Results;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Common.Responses;

public static class ApiResultMapper
{
    public static IActionResult ToActionResult(this ControllerBase controller, Result result)
    {
        if (result.IsSuccess)
        {
            return controller.Ok(new
            {
                success = true
            });
        }

        if (result.HasValidationErrors)
        {
            return controller.BadRequest(new ErrorResponse
            {
                Success = false,
                Message = result.Error.Message,
                StatusCode = StatusCodes.Status400BadRequest,
                ErrorCode = result.Error.Code,
                Metadata = result.ValidationErrors
            });
        }

        return result.Error.Code switch
        {
            "NotFound" => controller.NotFound(new ErrorResponse
            {
                Success = false,
                Message = result.Error.Message,
                StatusCode = StatusCodes.Status404NotFound,
                ErrorCode = result.Error.Code
            }),

            "Conflict" => controller.Conflict(new ErrorResponse
            {
                Success = false,
                Message = result.Error.Message,
                StatusCode = StatusCodes.Status409Conflict,
                ErrorCode = result.Error.Code
            }),

            "Forbidden" => controller.StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Success = false,
                Message = result.Error.Message,
                StatusCode = StatusCodes.Status403Forbidden,
                ErrorCode = result.Error.Code
            }),

            _ => controller.BadRequest(new ErrorResponse
            {
                Success = false,
                Message = result.Error.Message,
                StatusCode = StatusCodes.Status400BadRequest,
                ErrorCode = result.Error.Code
            })
        };
    }

    public static IActionResult ToActionResult<T>(this ControllerBase controller, Result<T> result)
    {
        if (result.IsSuccess)
        {
            return controller.Ok(result.Value);
        }

        return controller.ToActionResult((Result)result);
    }
}