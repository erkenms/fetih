namespace Comet.Game
{
    using System;
    using System.IO;
    using System.Text.Json;

    // Static game data in Data/*.json next to the server: comments and trailing commas allowed
    public static class DataFiles
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
        };

        public static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "Data", name);

        public static T Read<T>(string path) where T : class =>
            JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
                ?? throw new InvalidDataException($"{path} is empty");
    }
}
