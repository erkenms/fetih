namespace Fetih.Shared.Models.Admin
{
    public class AdminResult
    {
        public bool Ok { get; set; }

        // One of AdminErrors when not Ok
        public string Error { get; set; }

        // Whether the change reached a character in game or only the database
        public bool Online { get; set; }

        public static AdminResult Done(bool online) => new AdminResult { Ok = true, Online = online };

        public static AdminResult Fail(string error, bool online = false) =>
            new AdminResult { Ok = false, Error = error, Online = online };
    }

    public static class AdminErrors
    {
        public const string NotFound = "not_found";
        public const string Offline = "offline";
        // The account is logging in or creating a character; try again
        public const string Busy = "busy";
        public const string Invalid = "invalid";
        public const string UnknownItemType = "unknown_item_type";
        public const string InventoryFull = "inventory_full";
        public const string OutOfRange = "out_of_range";
        public const string BadDestination = "bad_destination";
        public const string NotBanned = "not_banned";
        public const string Failed = "failed";
    }
}
