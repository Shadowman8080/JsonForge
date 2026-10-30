using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

namespace JsonForge.Core.Json;

public enum JsonTreeNodeKind
{
    Object,
    Array,
    String,
    Number,
    Boolean,
    Null,
    Property
}

public sealed class JsonTreeNode
{
    public string Name { get; set; } = string.Empty;
    public JsonTreeNodeKind Kind { get; set; }
    public string? Value { get; set; }
    public string Path { get; set; } = string.Empty;
    public ObservableCollection<JsonTreeNode> Children { get; } = [];
    public bool IsExpanded { get; set; }

    public static JsonTreeNode Build(string? json)
    {
        var root = new JsonTreeNode { Name = "root", Kind = JsonTreeNodeKind.Object, Path = "$" };
        if (string.IsNullOrWhiteSpace(json))
        {
            return root;
        }

        JsonNode? node = null;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch
        {
            return root;
        }

        if (node is null)
        {
            root.Kind = JsonTreeNodeKind.Null;
            root.Value = "null";
            return root;
        }

        return BuildNode(node, "root", "$");
    }

    private static JsonTreeNode BuildNode(JsonNode? node, string name, string path)
    {
        if (node is null)
        {
            return new JsonTreeNode { Name = name, Kind = JsonTreeNodeKind.Null, Value = "null", Path = path };
        }

        if (node is JsonObject obj)
        {
            var n = new JsonTreeNode { Name = name, Kind = JsonTreeNodeKind.Object, Path = path };
            foreach (var kv in obj)
            {
                var childPath = path == "$" ? $"$.{kv.Key}" : $"{path}.{kv.Key}";
                var child = BuildNode(kv.Value, kv.Key, childPath);
                child.Kind = JsonTreeNodeKind.Property;
                n.Children.Add(child);
            }
            return n;
        }

        if (node is JsonArray arr)
        {
            var n = new JsonTreeNode { Name = name, Kind = JsonTreeNodeKind.Array, Path = path };
            for (var i = 0; i < arr.Count; i++)
            {
                var childPath = $"{path}[{i}]";
                var child = BuildNode(arr[i], $"[{i}]", childPath);
                n.Children.Add(child);
            }
            return n;
        }

        if (node is JsonValue val)
        {
            var kind = val.GetValueKind();
            if (kind == System.Text.Json.JsonValueKind.String)
            {
                return new JsonTreeNode { Name = name, Kind = JsonTreeNodeKind.String, Value = val.ToJsonString(), Path = path };
            }
            if (kind == System.Text.Json.JsonValueKind.Number)
            {
                return new JsonTreeNode { Name = name, Kind = JsonTreeNodeKind.Number, Value = val.ToJsonString(), Path = path };
            }
            if (kind == System.Text.Json.JsonValueKind.True || kind == System.Text.Json.JsonValueKind.False)
            {
                return new JsonTreeNode { Name = name, Kind = JsonTreeNodeKind.Boolean, Value = val.ToJsonString(), Path = path };
            }
            if (kind == System.Text.Json.JsonValueKind.Null)
            {
                return new JsonTreeNode { Name = name, Kind = JsonTreeNodeKind.Null, Value = "null", Path = path };
            }
        }

        return new JsonTreeNode { Name = name, Kind = JsonTreeNodeKind.Null, Value = node.ToJsonString(), Path = path };
    }
}
