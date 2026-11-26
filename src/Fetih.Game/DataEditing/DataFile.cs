namespace Fetih.Game.DataEditing
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Encodings.Web;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.Json.Serialization.Metadata;

    public sealed class DataFileConflictException : IOException
    {
        public DataFileConflictException(string message) : base(message) { }
    }

    // A Data/*.json file: a header comment and an object whose one array holds the entries.
    // Edits rewrite only what changed, so editing one entry is a one-line diff, and the
    // comments, key order and layout of everything else stay byte for byte.
    public abstract class DataFile
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };

        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            // Only what a file can set: computed properties like Npc.IsGoal aren't data
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    info =>
                    {
                        if (info.Kind != JsonTypeInfoKind.Object)
                            return;
                        var computed = info.Properties.Where(p => p.Set == null).ToList();
                        foreach (var property in computed)
                            info.Properties.Remove(property);
                    },
                },
            },
        };

        private bool HasBom;
        private JsoncValue Root;

        protected DataFile(string text, string arrayKey)
        {
            this.ArrayKey = arrayKey;
            this.Text = text;
            this.Original = text;
            this.Parse();
        }

        public string Path { get; private set; }
        public string ArrayKey { get; }
        public abstract Type EntryType { get; }

        public string Text { get; private set; }

        // The text as last read or saved
        public string Original { get; private set; }

        // SHA-256 of the file's bytes when it was last read or saved, to detect someone else's write
        public string Hash { get; private set; }

        public bool IsChanged => this.Text != this.Original;

        public int Count => this.Entries.Items.Count;

        protected JsoncValue Entries { get; private set; }

        public static string HashOf(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

        // The line (from 1) on which an entry starts, for error messages and editors
        public int LineOf(int index) => this.LineAt(this.Entries.Items[index].Start);

        public JsonNode GetJson(int index) => Normalize(this.EntryType, this.EntryText(index));

        public JsonArray GetAllJson() => new JsonArray(Enumerable.Range(0, this.Count).Select(this.GetJson).ToArray());

        public void SetJson(int index, JsonNode entry) => this.SetEntry(index, this.FromJson(entry));

        public void InsertJson(int index, JsonNode entry) => this.InsertEntry(index, this.FromJson(entry));

        public void RemoveAt(int index)
        {
            this.CheckIndex(index);
            var patcher = new JsoncPatcher(this.Text);
            patcher.RemoveEntry(this.Entries, index);
            this.Commit(patcher);
        }

        // Fails if the file was changed since this copy was read, then writes it atomically
        public void Save(string path = null)
        {
            path ??= this.Path ?? throw new InvalidOperationException("No path to save to");
            if (this.Path != null && path == this.Path)
            {
                string current = File.Exists(path) ? HashOf(File.ReadAllBytes(path)) : null;
                if (current != this.Hash)
                    throw new DataFileConflictException($"{System.IO.Path.GetFileName(path)} was changed by someone else since it was read");
            }

            var bytes = this.Bytes();
            string temp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)),
                $".{System.IO.Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllBytes(temp, bytes);
                File.Move(temp, path, true);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }

            this.Path = path;
            this.Hash = HashOf(bytes);
            this.Original = this.Text;
        }

        public byte[] Bytes()
        {
            var body = Utf8.GetBytes(this.Text);
            return this.HasBom ? Bom.Concat(body).ToArray() : body;
        }

        public void EnsureHash(string expected)
        {
            if (!string.Equals(expected, this.Hash, StringComparison.OrdinalIgnoreCase))
                throw new DataFileConflictException($"{System.IO.Path.GetFileName(this.Path)} was changed since that copy was read");
        }

        protected static DataFile Read(string path, Func<string, DataFile> create)
        {
            var bytes = File.ReadAllBytes(path);
            bool bom = bytes.AsSpan().StartsWith(Bom);
            var file = create(Utf8.GetString(bom ? bytes[3..] : bytes));
            file.HasBom = bom;
            file.Path = path;
            file.Hash = HashOf(bytes);
            return file;
        }

        protected string EntryText(int index)
        {
            this.CheckIndex(index);
            var value = this.Entries.Items[index].Value;
            return this.Text[value.Start..value.End];
        }

        protected object ReadEntry(int index) =>
            JsonSerializer.Deserialize(this.EntryText(index), this.EntryType, JsonOptions)
                ?? throw new InvalidDataException($"Entry {index} of {this.ArrayKey} is null");

        protected void SetEntry(int index, object entry)
        {
            var after = JsonSerializer.SerializeToNode(entry, this.EntryType, JsonOptions);
            // Removing members and adding others takes a pass each
            for (int pass = 0; pass < 3; pass++)
            {
                this.CheckIndex(index);
                var patcher = new JsoncPatcher(this.Text);
                patcher.Patch(this.Entries.Items[index].Value, Normalize(this.EntryType, this.EntryText(index)), after);
                if (!patcher.HasEdits)
                    return;
                this.Commit(patcher);
                if (!patcher.Again)
                    return;
            }
        }

        protected void InsertEntry(int index, object entry)
        {
            if (index < 0 || index > this.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            var patcher = new JsoncPatcher(this.Text);
            var defaults = JsonSerializer.SerializeToNode(JsonSerializer.Deserialize("{}", this.EntryType, JsonOptions), this.EntryType, JsonOptions);
            patcher.InsertEntry(this.Entries, index, JsonSerializer.SerializeToNode(entry, this.EntryType, JsonOptions), defaults as JsonObject);
            this.Commit(patcher);
        }

        private object FromJson(JsonNode entry) =>
            entry.Deserialize(this.EntryType, JsonOptions) ?? throw new ArgumentNullException(nameof(entry));

        // What the file's text means, written back the way this code writes it, so text that
        // differs only in spelling (1.0 for 1, a default left out) compares equal
        private static JsonNode Normalize(Type type, string text) =>
            JsonSerializer.SerializeToNode(JsonSerializer.Deserialize(text, type, JsonOptions), type, JsonOptions);

        private void Commit(JsoncPatcher patcher)
        {
            this.Text = patcher.Apply();
            this.Parse();
        }

        private void Parse()
        {
            this.Root = JsoncTree.Parse(this.Text);
            if (this.Root.Kind != JsoncKind.Object)
                throw new InvalidDataException("The file's top-level value isn't an object");
            var entries = this.Root.Member(this.ArrayKey)?.Value
                ?? throw new InvalidDataException($"The file has no \"{this.ArrayKey}\" list");
            if (entries.Kind != JsoncKind.Array)
                throw new InvalidDataException($"\"{this.ArrayKey}\" isn't a list");
            this.Entries = entries;
        }

        private void CheckIndex(int index)
        {
            if (index < 0 || index >= this.Count)
                throw new ArgumentOutOfRangeException(nameof(index), $"{this.ArrayKey} has {this.Count} entries");
        }

        private int LineAt(int position)
        {
            int line = 1;
            for (int i = 0; i < position; i++)
                if (this.Text[i] == '\n')
                    line++;
            return line;
        }
    }

    public sealed class DataFile<T> : DataFile where T : class
    {
        private DataFile(string text, string arrayKey) : base(text, arrayKey) { }

        public override Type EntryType => typeof(T);

        public static DataFile<T> Parse(string text, string arrayKey) => new DataFile<T>(text, arrayKey);

        public static DataFile<T> Load(string path, string arrayKey) =>
            (DataFile<T>)Read(path, text => new DataFile<T>(text, arrayKey));

        public T this[int index]
        {
            get => (T)this.ReadEntry(index);
            set => this.SetEntry(index, value);
        }

        public IReadOnlyList<T> All() => Enumerable.Range(0, this.Count).Select(i => this[i]).ToList();

        public int IndexOf(Func<T, bool> match)
        {
            for (int i = 0; i < this.Count; i++)
                if (match(this[i]))
                    return i;
            return -1;
        }

        public void Insert(int index, T entry) => this.InsertEntry(index, entry);

        public void Add(T entry) => this.InsertEntry(this.Count, entry);
    }
}
