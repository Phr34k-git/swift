using System.Net;
using Client.Services;

namespace Client.ViewModels;

/// <summary>
/// Maps credential sign-in API failures to the existing inline login messages.
/// </summary>
internal static class CredentialLoginErrorMapper
{
    public static string Map(AuthApiException ex)
    {
        return ex.Reason switch
        {
            "invalid_credentials" => "Invalid username or password.",
            "unlicensed" => "Purchase required.",
            "revoked" => "Access revoked.",
            _ when ex.StatusCode == HttpStatusCode.TooManyRequests =>
                "Too many attempts. Try again shortly.",
            _ when ex.IsTransient => "Could not reach the server.",
            _ => ex.Message,
        };
    }
}
