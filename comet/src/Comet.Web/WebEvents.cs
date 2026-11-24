namespace Comet.Web;

public static class WebEvents
{
    public const string Start = "server.start";
    public const string Login = "web.login";
    public const string LoginFailed = "web.login_failed";
    public const string Logout = "web.logout";
    public const string GameRpcConnected = "web.rpc_connected";
    public const string GameRpcLost = "web.rpc_lost";
    public const string Error = "web.error";

    // Audited actions log as web.<action>
    public static string Audit(string action) => "web." + action;
}
