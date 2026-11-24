namespace Comet.Game.DataEditing
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Encodings.Web;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    // Turns a value's old and new content into the smallest text edits: only values that
    // changed are rewritten, members and elements are added and removed line by line, and
    // new text copies the layout of the value it replaces or of its neighbours.
    internal sealed class JsoncPatcher
    {
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private readonly string Text;
        private readonly string Newline;
        private readonly List<(int Start, int End, string Text)> Edits = new List<(int, int, string)>();

        // Set when a pass removed members of an object; members it adds wait for the next pass
        public bool Again { get; private set; }

        public JsoncPatcher(string text)
        {
            this.Text = text;
            this.Newline = text.Contains("\r\n") ? "\r\n" : "\n";
        }

        public bool HasEdits => this.Edits.Count > 0;

        public string Apply()
        {
            var sorted = this.Edits.Select((e, i) => (Edit: e, Order: i))
                .OrderByDescending(e => e.Edit.Start).ThenByDescending(e => e.Order).ToList();
            var text = new StringBuilder(this.Text);
            int limit = int.MaxValue;
            foreach (var (edit, _) in sorted)
            {
                if (edit.End > limit)
                    throw new InvalidOperationException("Overlapping text edits");
                text.Remove(edit.Start, edit.End - edit.Start).Insert(edit.Start, edit.Text);
                limit = edit.Start;
            }

            return text.ToString();
        }

        public void Patch(JsoncValue node, JsonNode before, JsonNode after)
        {
            if (JsonNode.DeepEquals(before, after))
                return;

            if (node.Kind == JsoncKind.Object && before is JsonObject oldObject && after is JsonObject newObject)
                this.PatchObject(node, oldObject, newObject);
            else if (node.Kind == JsoncKind.Array && before is JsonArray oldArray && after is JsonArray newArray
                && oldArray.Count == node.Items.Count)
                this.PatchArray(node, oldArray, newArray);
            else
                this.Replace(node, this.Format(after, node));
        }

        // defaults: the entry type's members as a new instance has them; a new entry leaves out
        // those with that value when its neighbour leaves them out too
        public void InsertEntry(JsoncValue array, int index, JsonNode entry, JsonObject defaults)
        {
            if (index >= array.Items.Count)
            {
                entry = WithoutDefaults(entry, array.Items.Count > 0 ? array.Items[^1].Value : null, defaults);
                this.AppendEntries(array, new[] { entry });
                return;
            }

            var item = array.Items[index];
            entry = WithoutDefaults(entry, item.Value, defaults);
            if (this.AloneOnLines(item, item))
                this.Edits.Add((this.LineStart(item.Start), this.LineStart(item.Start),
                    this.IndentAt(item.Start) + this.Format(entry, item.Value) + "," + this.Newline));
            else
                this.Edits.Add((item.Start, item.Start, this.Format(entry, item.Value) + ", "));
        }

        private static JsonNode WithoutDefaults(JsonNode entry, JsoncValue neighbour, JsonObject defaults)
        {
            if (entry is not JsonObject json || defaults == null)
                return entry;
            var kept = json.Where(p => neighbour?.Member(p.Key) != null
                || !(TryGet(defaults, p.Key, out var value) && JsonNode.DeepEquals(value, p.Value)));
            return new JsonObject(kept.Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone())));
        }

        public void RemoveEntry(JsoncValue array, int index) => this.RemoveItems(array, new[] { index });

        public void AppendEntries(JsoncValue array, IEnumerable<JsonNode> entries)
        {
            if (array.Items.Count == 0)
                this.Replace(array, this.Format(new JsonArray(entries.Select(e => e?.DeepClone()).ToArray()), null));
            else
                this.Append(array, entries.Select(e => this.Format(e, array.Items[^1].Value)).ToList());
        }

        private void PatchObject(JsoncValue node, JsonObject before, JsonObject after)
        {
            var removed = new List<int>();
            for (int i = 0; i < node.Items.Count; i++)
            {
                var item = node.Items[i];
                bool wasRead = TryGet(before, item.Key, out var oldValue);
                bool isWritten = TryGet(after, item.Key, out var newValue);
                if (!wasRead && !isWritten)
                    continue;
                if (!isWritten)
                    removed.Add(i);
                else
                    this.Patch(item.Value, oldValue, newValue);
            }

            if (removed.Count > 0)
            {
                this.RemoveItems(node, removed);
                this.Again = true;
                return;
            }

            var added = after.Where(p => node.Member(p.Key) == null && p.Value != null
                    && !(TryGet(before, p.Key, out var old) && JsonNode.DeepEquals(old, p.Value)))
                .ToList();
            if (added.Count == 0)
                return;
            if (node.Items.Count == 0)
                this.Replace(node, this.Format(after, node));
            else
                this.Append(node, added.Select(p => Key(p.Key) + ": " + this.Format(p.Value, null)).ToList());
        }

        private void PatchArray(JsoncValue node, JsonArray before, JsonArray after)
        {
            int common = Math.Min(before.Count, after.Count);
            for (int i = 0; i < common; i++)
                this.Patch(node.Items[i].Value, before[i], after[i]);
            if (after.Count < before.Count)
                this.RemoveItems(node, Enumerable.Range(after.Count, before.Count - after.Count).ToList());
            else if (after.Count > before.Count)
                this.AppendEntries(node, after.Skip(before.Count));
        }

        private static bool TryGet(JsonObject json, string key, out JsonNode value)
        {
            foreach (var property in json)
            {
                if (string.Equals(property.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        private void Replace(JsoncValue node, string text) => this.Edits.Add((node.Start, node.End, text));

        private void Append(JsoncValue container, IReadOnlyList<string> texts)
        {
            var last = container.Items[^1];
            if (!this.IsMultiLine(container))
            {
                this.Edits.Add((last.End, last.End, string.Concat(texts.Select(t => ", " + t))));
                return;
            }

            bool trailing = last.Comma >= 0;
            int at = this.LineEndAfter(last);
            string indent = this.IndentAt(container.Items[0].Start);
            var text = new StringBuilder();
            if (!trailing)
            {
                if (at == last.End)
                    text.Append(',');
                else
                    this.Edits.Add((last.End, last.End, ","));
            }

            for (int i = 0; i < texts.Count; i++)
                text.Append(this.Newline).Append(indent).Append(texts[i]).Append(i < texts.Count - 1 || trailing ? "," : "");
            this.Edits.Add((at, at, text.ToString()));
        }

        // Removes runs of neighbouring items together, so their edits don't overlap
        private void RemoveItems(JsoncValue container, IReadOnlyList<int> indexes)
        {
            var items = container.Items;
            var set = indexes.OrderBy(i => i).ToList();
            int r = 0;
            while (r < set.Count)
            {
                int first = set[r], lastIndex = first;
                while (r + 1 < set.Count && set[r + 1] == lastIndex + 1)
                    lastIndex = set[++r];
                r++;

                bool isTail = lastIndex == items.Count - 1;
                if (this.AloneOnLines(items[first], items[lastIndex]))
                {
                    this.Edits.Add((this.LineStart(items[first].Start), this.LineTailEnd(items[lastIndex]), ""));
                    if (isTail && first > 0 && items[lastIndex].Comma < 0 && items[first - 1].Comma >= 0)
                        this.Edits.Add((items[first - 1].Comma, items[first - 1].Comma + 1, ""));
                }
                else if (!isTail)
                    this.Edits.Add((items[first].Start, items[lastIndex + 1].Start, ""));
                else if (first > 0)
                    this.Edits.Add((items[first - 1].End, AfterComma(items[lastIndex]), ""));
                else
                    this.Edits.Add((items[first].Start, AfterComma(items[lastIndex]), ""));
            }
        }

        private static int AfterComma(JsoncItem item) => item.Comma >= 0 ? item.Comma + 1 : item.End;

        private string Format(JsonNode value, JsoncValue template)
        {
            switch (value)
            {
                case JsonObject json:
                {
                    if (json.Count == 0)
                        return "{}";
                    var like = template is { Kind: JsoncKind.Object } ? template : null;
                    var members = json.Select(p => (Head: Key(p.Key) + ": ", Value: p.Value, Like: like?.Member(p.Key))).ToList();
                    if (like != null && like.Items.Count > 0 && this.IsMultiLine(like))
                        return this.Lines('{', '}', like, members);
                    bool space = like == null || (like.End - like.Start > 2 && this.Text[like.Start + 1] == ' ');
                    return this.Inline('{', '}', space, members);
                }
                case JsonArray json:
                {
                    if (json.Count == 0)
                        return "[]";
                    var like = template is { Kind: JsoncKind.Array, Items.Count: > 0 } ? template : null;
                    var elements = json.Select((v, i) => (Head: "", Value: v,
                        Like: i < like?.Items.Count ? like.Items[i] : like?.Items[^1])).ToList();
                    if (like != null && this.IsMultiLine(like))
                        return this.Lines('[', ']', like, elements);
                    bool space = like != null && this.Text[like.Start + 1] == ' ';
                    return this.Inline('[', ']', space, elements);
                }
                case null:
                    return "null";
                default:
                    return value.ToJsonString(WriteOptions);
            }
        }

        private string Inline(char open, char close, bool space, List<(string Head, JsonNode Value, JsoncItem Like)> items)
        {
            string pad = space ? " " : "";
            var inner = items.Select(i => i.Head + this.Format(i.Value, i.Like?.Value is { } like && this.IsMultiLine(like) ? null : i.Like?.Value));
            return open + pad + string.Join(", ", inner) + pad + close;
        }

        // Members that share a line in the template share one here too
        private string Lines(char open, char close, JsoncValue like, List<(string Head, JsonNode Value, JsoncItem Like)> items)
        {
            string indent = this.IndentAt(like.Items[0].Start);
            bool trailing = like.Items[^1].Comma >= 0;
            var text = new StringBuilder().Append(open);
            for (int i = 0; i < items.Count; i++)
            {
                bool sameLine = i > 0 && items[i].Like != null && items[i - 1].Like is { } previous
                    && items[i].Like != previous && previous.Start < items[i].Like.Start
                    && !this.Text.AsSpan(previous.End, items[i].Like.Start - previous.End).ContainsAny('\r', '\n');
                if (sameLine)
                    text.Append(' ');
                else
                    text.Append(this.Newline).Append(indent);
                text.Append(items[i].Head).Append(this.Format(items[i].Value, items[i].Like?.Value));
                if (i < items.Count - 1 || trailing)
                    text.Append(',');
            }

            return text.Append(this.Newline).Append(this.IndentAt(like.End - 1)).Append(close).ToString();
        }

        private static string Key(string key) => JsonSerializer.Serialize(key, WriteOptions);

        private bool IsMultiLine(JsoncValue container) =>
            container.Items.Count > 0 && this.Text.AsSpan(container.Start, container.Items[0].Start - container.Start).Contains('\n');

        private int LineStart(int position) => this.Text.LastIndexOf('\n', Math.Max(position - 1, 0)) + 1;

        private string IndentAt(int position)
        {
            int start = this.LineStart(position), end = start;
            while (end < this.Text.Length && this.Text[end] is ' ' or '\t')
                end++;
            return this.Text[start..end];
        }

        // Where the line of an item ends after its comma and comment, before the line break;
        // the item's end if something else follows it on the line
        private int LineEndAfter(JsoncItem item)
        {
            int position = this.SkipLineTrivia(AfterComma(item));
            return position < this.Text.Length && this.Text[position] is '\r' or '\n' ? position : item.End;
        }

        // Just past the line break that ends the item's line, or -1 if more follows on the line
        private int LineTailEnd(JsoncItem item)
        {
            int position = this.SkipLineTrivia(AfterComma(item));
            if (position < this.Text.Length && this.Text[position] == '\r')
                position++;
            if (position < this.Text.Length && this.Text[position] == '\n')
                return position + 1;
            return -1;
        }

        private int SkipLineTrivia(int position)
        {
            while (position < this.Text.Length && this.Text[position] is ' ' or '\t')
                position++;
            if (position + 1 < this.Text.Length && this.Text[position] == '/' && this.Text[position + 1] == '/')
                while (position < this.Text.Length && this.Text[position] is not ('\r' or '\n'))
                    position++;
            return position;
        }

        private bool AloneOnLines(JsoncItem first, JsoncItem last) =>
            string.IsNullOrWhiteSpace(this.Text[this.LineStart(first.Start)..first.Start]) && this.LineTailEnd(last) >= 0;
    }
}
