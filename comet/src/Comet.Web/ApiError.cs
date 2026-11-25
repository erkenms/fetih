namespace Comet.Web;

using Comet.Shared.Models.Admin;
using Microsoft.AspNetCore.Http.HttpResults;

// Errors carry a code, not a sentence: the frontend turns it into text in the user's language
public record ApiError(string Error)
{
    public static JsonHttpResult<ApiError> Result(string code, int status) =>
        TypedResults.Json(new ApiError(code), statusCode: status);

    public static JsonHttpResult<ApiError> FromAdmin(string? code)
    {
        int status = code switch
        {
            AdminErrors.NotFound => StatusCodes.Status404NotFound,
            AdminErrors.Busy => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        return Result(code ?? AdminErrors.Failed, status);
    }
}
