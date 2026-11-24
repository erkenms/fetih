namespace Comet.Web;

using Microsoft.AspNetCore.Http.HttpResults;

// Errors carry a code, not a sentence: the frontend turns it into text in the user's language
public record ApiError(string Error)
{
    public static JsonHttpResult<ApiError> Result(string code, int status) =>
        TypedResults.Json(new ApiError(code), statusCode: status);
}
