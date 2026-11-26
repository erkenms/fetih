namespace Fetih.Core.Logging
{
    using System;
    using System.IO;
    using System.IO.Compression;
    using System.Text;
    using System.Threading.Tasks;
    using Serilog.Sinks.File;

    // Compresses previous days' rolled audit files to .jsonl.gz whenever a new audit file opens.
    public sealed class AuditCompressHook : FileLifecycleHooks
    {
        private readonly string prefix;

        public AuditCompressHook(string prefix)
        {
            this.prefix = prefix;
        }

        public override Stream OnFileOpened(string path, Stream underlyingStream, Encoding encoding)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                _ = Task.Run(() => CompressRolledFiles(dir, path, this.prefix));

            return underlyingStream;
        }

        public static void CompressRolledFiles(string directory, string activePath, string prefix)
        {
            if (!Directory.Exists(directory))
                return;

            var activeFull = string.IsNullOrEmpty(activePath) ? null : Path.GetFullPath(activePath);
            foreach (var file in Directory.EnumerateFiles(directory, prefix + "*.jsonl"))
            {
                if (activeFull != null &&
                    string.Equals(Path.GetFullPath(file), activeFull, StringComparison.OrdinalIgnoreCase))
                    continue;

                var gzPath = file + ".gz";
                var tmpPath = gzPath + ".tmp";
                try
                {
                    using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var output = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
                    {
                        input.CopyTo(gzip);
                    }

                    File.Move(tmpPath, gzPath, overwrite: true);
                    File.Delete(file);
                }
                catch
                {
                    try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                }
            }
        }
    }
}
