namespace Comet.Web.Features.Packets;

using System.Collections;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;

public enum PacketFieldKind
{
    Number,
    Text,
    Enum,
    Bool,
    Bytes,
    Null,
    List,
    Object,
}

// One decoded value: a leaf has Value, a list or object has Children
public record PacketField(string Name, PacketFieldKind Kind, string? Value, string TypeName, PacketField[]? Children);

public static class PacketFields
{
    private const int MaxDepth = 6;
    private const int MaxItems = 256;

    public static PacketField[] Describe(object packet, IReadOnlySet<string> skip) =>
        Members(packet, 0, new HashSet<object>(ReferenceEqualityComparer.Instance), skip);

    private static PacketField[] Members(object obj, int depth, HashSet<object> seen, IReadOnlySet<string>? skip)
    {
        var type = obj.GetType();
        var fields = new List<PacketField>();

        foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (prop.GetIndexParameters().Length > 0 || !prop.CanRead || skip?.Contains(prop.Name) == true)
                continue;
            object? value;
            try { value = prop.GetValue(obj); }
            catch (TargetInvocationException) { continue; }
            fields.Add(Describe(prop.Name, value, prop.PropertyType, depth, seen));
        }

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            if (skip?.Contains(field.Name) == true)
                continue;
            fields.Add(Describe(field.Name, field.GetValue(obj), field.FieldType, depth, seen));
        }

        return fields.ToArray();
    }

    private static PacketField Describe(string name, object? value, Type declared, int depth, HashSet<object> seen)
    {
        if (value == null)
            return new PacketField(name, PacketFieldKind.Null, null, TypeName(declared), null);

        var type = value.GetType();
        string typeName = TypeName(type);

        switch (value)
        {
            case Enum e:
                object raw = Convert.ChangeType(e, Enum.GetUnderlyingType(type));
                string text = e.ToString();
                string number = PacketDecoder.Invariant(raw);
                return new PacketField(name, PacketFieldKind.Enum, text == number ? number : $"{text} ({number})", typeName, null);
            case string s:
                return new PacketField(name, PacketFieldKind.Text, s, typeName, null);
            case bool b:
                return new PacketField(name, PacketFieldKind.Bool, b ? "true" : "false", typeName, null);
            case byte[] bytes:
                return new PacketField(name, PacketFieldKind.Bytes, Convert.ToHexString(bytes), typeName, null);
            case Color color:
                return new PacketField(name, PacketFieldKind.Number, $"#{(uint)color.ToArgb():X8}", typeName, null);
            case DateTime time:
                return new PacketField(name, PacketFieldKind.Text, time.ToString("O"), typeName, null);
            case char c:
                return new PacketField(name, PacketFieldKind.Text, c.ToString(), typeName, null);
        }

        if (type.IsPrimitive || value is decimal)
            return new PacketField(name, PacketFieldKind.Number, PacketDecoder.Invariant(value), typeName, null);

        if (depth >= MaxDepth || (!type.IsValueType && !seen.Add(value)))
            return new PacketField(name, PacketFieldKind.Object, "…", typeName, []);

        if (value is IDictionary dictionary)
        {
            var children = new List<PacketField>();
            foreach (DictionaryEntry entry in dictionary)
            {
                if (children.Count >= MaxItems)
                    break;
                string key = entry.Key is Enum or string or IFormattable
                    ? PacketDecoder.Invariant(entry.Key) : entry.Key.ToString() ?? "";
                children.Add(Describe(key, entry.Value, typeof(object), depth + 1, seen));
            }
            return new PacketField(name, PacketFieldKind.List, PacketDecoder.Invariant(dictionary.Count), typeName, children.ToArray());
        }

        if (value is IEnumerable list)
        {
            var children = new List<PacketField>();
            int count = 0;
            foreach (object? item in list)
            {
                if (count < MaxItems)
                    children.Add(Describe($"[{count}]", item, typeof(object), depth + 1, seen));
                count++;
            }
            return new PacketField(name, PacketFieldKind.List, PacketDecoder.Invariant(count), typeName, children.ToArray());
        }

        if (value is ITuple tuple)
        {
            var children = new PacketField[tuple.Length];
            for (int i = 0; i < tuple.Length; i++)
                children[i] = Describe($"Item{i + 1}", tuple[i], typeof(object), depth + 1, seen);
            return new PacketField(name, PacketFieldKind.Object, null, typeName, children);
        }

        return new PacketField(name, PacketFieldKind.Object, null, typeName, Members(value, depth + 1, seen, null));
    }

    private static string TypeName(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } inner)
            return TypeName(inner) + "?";
        if (type.IsArray)
            return TypeName(type.GetElementType()!) + "[]";
        if (!type.IsGenericType)
            return type.Name;
        string baseName = type.Name[..type.Name.IndexOf('`')];
        return $"{baseName}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>";
    }
}
