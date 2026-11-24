namespace Comet.Game.DataEditing
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    public enum JsoncKind { Object, Array, String, Number, Literal }

    // A value in the source text, by offsets, so edits can splice the text around it and
    // leave comments, spacing and line breaks as they were
    public sealed class JsoncValue
    {
        public JsoncKind Kind { get; init; }
        public int Start { get; init; }
        public int End { get; init; }
        public IReadOnlyList<JsoncItem> Items { get; init; } = Array.Empty<JsoncItem>();

        public bool IsContainer => this.Kind is JsoncKind.Object or JsoncKind.Array;

        public JsoncItem Member(string key)
        {
            foreach (var item in this.Items)
                if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                    return item;
            return null;
        }
    }

    // An object member or array element. Comma is the offset of the comma after it, or -1.
    public sealed class JsoncItem
    {
        public string Key { get; init; }
        public int Start { get; init; }
        public JsoncValue Value { get; init; }
        public int Comma { get; set; } = -1;

        public int End => this.Value.End;
    }

    // JSON with // and /* */ comments and trailing commas, as the Data files are written
    public sealed class JsoncTree
    {
        public static readonly JsonDocumentOptions DocumentOptions = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        private readonly string Text;
        private int Position;

        public JsoncValue Root { get; }

        private JsoncTree(string text)
        {
            this.Text = text;
            this.SkipTrivia();
            this.Root = this.ParseValue();
            this.SkipTrivia();
            if (this.Position != text.Length)
                throw this.Error("text after the top-level value");
        }

        public static JsoncValue Parse(string text) => new JsoncTree(text).Root;

        public static JsonNode ToNode(string text, JsoncValue value) =>
            JsonNode.Parse(text.AsSpan(value.Start, value.End - value.Start).ToString(), null, DocumentOptions);

        private JsoncValue ParseValue()
        {
            if (this.Position >= this.Text.Length)
                throw this.Error("a value expected");
            char c = this.Text[this.Position];
            int start = this.Position;
            switch (c)
            {
                case '{':
                case '[':
                    return this.ParseContainer(c == '{');
                case '"':
                    this.ParseString();
                    return new JsoncValue { Kind = JsoncKind.String, Start = start, End = this.Position };
                case 't':
                case 'f':
                case 'n':
                    while (this.Position < this.Text.Length && char.IsAsciiLetter(this.Text[this.Position]))
                        this.Position++;
                    string word = this.Text[start..this.Position];
                    if (word is not ("true" or "false" or "null"))
                        throw this.Error($"unknown word '{word}'");
                    return new JsoncValue { Kind = JsoncKind.Literal, Start = start, End = this.Position };
                default:
                    if (c != '-' && !char.IsAsciiDigit(c))
                        throw this.Error($"unexpected '{c}'");
                    while (this.Position < this.Text.Length && "+-.eE0123456789".Contains(this.Text[this.Position]))
                        this.Position++;
                    return new JsoncValue { Kind = JsoncKind.Number, Start = start, End = this.Position };
            }
        }

        private JsoncValue ParseContainer(bool isObject)
        {
            int start = this.Position++;
            char close = isObject ? '}' : ']';
            var items = new List<JsoncItem>();
            while (true)
            {
                this.SkipTrivia();
                if (this.Peek() == close)
                    break;
                int itemStart = this.Position;
                string key = null;
                if (isObject)
                {
                    if (this.Peek() != '"')
                        throw this.Error("a property name expected");
                    key = JsonSerializer.Deserialize<string>(this.ParseString());
                    this.SkipTrivia();
                    if (this.Peek() != ':')
                        throw this.Error("':' expected");
                    this.Position++;
                    this.SkipTrivia();
                }

                var item = new JsoncItem { Key = key, Start = itemStart, Value = this.ParseValue() };
                items.Add(item);
                this.SkipTrivia();
                if (this.Peek() == ',')
                {
                    item.Comma = this.Position++;
                    continue;
                }

                if (this.Peek() != close)
                    throw this.Error($"',' or '{close}' expected");
            }

            this.Position++;
            return new JsoncValue
            {
                Kind = isObject ? JsoncKind.Object : JsoncKind.Array,
                Start = start,
                End = this.Position,
                Items = items,
            };
        }

        private string ParseString()
        {
            int start = this.Position++;
            while (true)
            {
                if (this.Position >= this.Text.Length)
                    throw this.Error("unterminated string");
                char c = this.Text[this.Position++];
                if (c == '\\')
                    this.Position++;
                else if (c == '"')
                    return this.Text[start..this.Position];
            }
        }

        private char Peek() => this.Position < this.Text.Length ? this.Text[this.Position] : '\0';

        private void SkipTrivia()
        {
            while (this.Position < this.Text.Length)
            {
                char c = this.Text[this.Position];
                if (char.IsWhiteSpace(c))
                    this.Position++;
                else if (c == '/' && this.Position + 1 < this.Text.Length && this.Text[this.Position + 1] == '/')
                {
                    while (this.Position < this.Text.Length && this.Text[this.Position] != '\n')
                        this.Position++;
                }
                else if (c == '/' && this.Position + 1 < this.Text.Length && this.Text[this.Position + 1] == '*')
                {
                    int end = this.Text.IndexOf("*/", this.Position + 2, StringComparison.Ordinal);
                    if (end < 0)
                        throw this.Error("unterminated comment");
                    this.Position = end + 2;
                }
                else
                    break;
            }
        }

        private InvalidDataException Error(string message)
        {
            int line = 1;
            for (int i = 0; i < this.Position && i < this.Text.Length; i++)
                if (this.Text[i] == '\n')
                    line++;
            return new InvalidDataException($"Line {line}: {message}");
        }
    }
}
