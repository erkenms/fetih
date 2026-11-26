namespace Fetih.Game
{
    using System;
    using System.IO;
    using System.Text.Json;

    public static class DataFiles
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
        };

        // The source Data/ folder on a dev server, so the web's edits and the server read the
        // same files; the build's copy when not set
        public static string Directory = Path.Combine(AppContext.BaseDirectory, "Data");

        public static string PathOf(string name) => Path.Combine(Directory, name);

        public static T Read<T>(string path) where T : class =>
            JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
                ?? throw new InvalidDataException($"{path} is empty");
    }
}
