using System.Text.Json;
using System.Text.Json.Nodes;
using LabelMaker.Core.Model;

namespace LabelMaker.Core.Serialization;

/// <summary>
/// Runs ordered migrations on a project's raw JSON, oldest → current, so old
/// files keep opening after the schema evolves (spec §18, §28). Each step is a
/// pure JSON transform; unknown newer versions are rejected before we get here.
/// </summary>
public static class ProjectUpgrader
{
    private static readonly IReadOnlyList<Func<JsonObject, JsonObject>> Migrations = new List<Func<JsonObject, JsonObject>>
    {
        // index 0: schema 0 -> 1 (no-op placeholder; kept so the table stays 1:1 with versions)
        root => root,
    };

    public static string Upgrade(string projectJson, int fromVersion)
    {
        if (fromVersion >= ProjectModel.CurrentSchemaVersion)
        {
            return projectJson;
        }

        JsonNode? node = JsonNode.Parse(projectJson);
        if (node is not JsonObject root)
        {
            return projectJson;
        }

        for (int v = fromVersion; v < ProjectModel.CurrentSchemaVersion; v++)
        {
            if (v >= 0 && v < Migrations.Count)
            {
                root = Migrations[v](root);
            }
        }

        root["schemaVersion"] = ProjectModel.CurrentSchemaVersion;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
