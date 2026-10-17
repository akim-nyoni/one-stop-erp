using System.Text.Json;
using System.Text.Json.Nodes;

namespace One4All.Application.Reporting;

/// <summary>How a value changed between two versions.</summary>
public enum JsonChangeKind
{
    /// <summary>It is new.</summary>
    Added = 1,

    /// <summary>It is gone.</summary>
    Removed = 2,

    /// <summary>Its value changed.</summary>
    Changed = 3,
}

/// <summary>One difference between two versions of a definition.</summary>
/// <param name="Path">Where, as a JSON path (e.g. <c>$.bands[2].elements[0].font.size</c>).</param>
/// <param name="Kind">What happened.</param>
/// <param name="Before">The old value (JSON), if any.</param>
/// <param name="After">The new value (JSON), if any.</param>
public sealed record JsonChange(string Path, JsonChangeKind Kind, string? Before, string? After);

/// <summary>
/// Structural comparison of two JSON documents - how report definition versions are diffed (brief §13.2 version history
/// with diff): property order and formatting are ignored; objects compare property by property, arrays element by element,
/// and every leaf that differs is reported with its path. Pure and unit-tested.
/// </summary>
public static class JsonDiff
{
    /// <summary>The differences from one document to another, in document order.</summary>
    /// <param name="before">The earlier JSON.</param>
    /// <param name="after">The later JSON.</param>
    /// <returns>The changes; empty when they are the same.</returns>
    public static IReadOnlyList<JsonChange> Compare(string before, string after)
    {
        List<JsonChange> changes = [];
        Walk("$", JsonNode.Parse(before), JsonNode.Parse(after), changes);
        return changes;
    }

    private static void Walk(string path, JsonNode? before, JsonNode? after, List<JsonChange> changes)
    {
        if (before is null && after is null)
        {
            return;
        }

        if (before is null)
        {
            changes.Add(new JsonChange(path, JsonChangeKind.Added, null, Text(after)));
            return;
        }

        if (after is null)
        {
            changes.Add(new JsonChange(path, JsonChangeKind.Removed, Text(before), null));
            return;
        }

        switch (before, after)
        {
            case (JsonObject a, JsonObject b):
                foreach (string key in a.Select(p => p.Key).Union(b.Select(p => p.Key), StringComparer.Ordinal))
                {
                    bool inA = a.TryGetPropertyValue(key, out JsonNode? va);
                    bool inB = b.TryGetPropertyValue(key, out JsonNode? vb);
                    string child = $"{path}.{key}";
                    if (inA && !inB)
                    {
                        changes.Add(new JsonChange(child, JsonChangeKind.Removed, Text(va), null));
                    }
                    else if (!inA && inB)
                    {
                        changes.Add(new JsonChange(child, JsonChangeKind.Added, null, Text(vb)));
                    }
                    else
                    {
                        Walk(child, va, vb, changes);
                    }
                }

                break;
            case (JsonArray a, JsonArray b):
                for (int i = 0; i < Math.Max(a.Count, b.Count); i++)
                {
                    string child = $"{path}[{i}]";
                    if (i >= a.Count)
                    {
                        changes.Add(new JsonChange(child, JsonChangeKind.Added, null, Text(b[i])));
                    }
                    else if (i >= b.Count)
                    {
                        changes.Add(new JsonChange(child, JsonChangeKind.Removed, Text(a[i]), null));
                    }
                    else
                    {
                        Walk(child, a[i], b[i], changes);
                    }
                }

                break;
            default:
                if (!JsonNode.DeepEquals(before, after))
                {
                    changes.Add(new JsonChange(path, JsonChangeKind.Changed, Text(before), Text(after)));
                }

                break;
        }
    }

    private static string? Text(JsonNode? node) => node?.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
}
