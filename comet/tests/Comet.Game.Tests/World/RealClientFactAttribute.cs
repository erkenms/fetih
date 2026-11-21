namespace Comet.Game.Tests.World
{
    using System;
    using System.IO;
    using Xunit;

    public sealed class RealClientFactAttribute : FactAttribute
    {
        public static readonly string Directory = Find();

        public RealClientFactAttribute()
        {
            if (Directory == null)
                this.Skip = "No _CLIENT/fetih folder above the test folder";
        }

        private static string Find()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var client = Path.Combine(dir.FullName, "_CLIENT", "fetih");
                if (System.IO.Directory.Exists(Path.Combine(client, "map")))
                    return client;
            }

            return null;
        }
    }
}
