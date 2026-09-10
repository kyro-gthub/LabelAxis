using LabelMaker.Core.Formtec;

namespace LabelMaker.Cli.Commands;

internal static class TemplatesCommand
{
    public static int Run(ArgMap o)
    {
        var store = TemplateStore.CreateWithBuiltIns();
        string sub = o.Positional.Count > 0 ? o.Positional[0].ToLowerInvariant() : "list";

        switch (sub)
        {
            case "list":
            {
                Console.WriteLine($"{"ID",-24} {"BRAND",-10} {"GRID",-8} {"LABEL (mm)",-16} NAME");
                foreach (LabelTemplate t in store.Search(new TemplateQuery { Text = o.Get("filter") }))
                {
                    Console.WriteLine($"{t.Id,-24} {t.Brand,-10} {t.Grid.Columns}x{t.Grid.Rows,-5} " +
                                      $"{t.Grid.LabelWidthMm:0.#}x{t.Grid.LabelHeightMm:0.#}".PadRight(41) + t.Name);
                }

                return 0;
            }

            case "show":
            {
                string id = o.Positional.Count > 1 ? o.Positional[1] : o.GetRequired("id");
                LabelTemplate t = store.Get(id) ?? throw new CliException($"No template '{id}'.");
                Console.WriteLine(TemplateJson.ToJson(t));
                TemplateValidationResult v = TemplateValidator.Validate(t);
                foreach (TemplateIssue issue in v.Issues)
                {
                    Console.Error.WriteLine($"{issue.Severity.ToString().ToLowerInvariant()}: {issue.Message}");
                }

                return 0;
            }

            case "export-csv":
            {
                string path = o.GetRequired("out");
                File.WriteAllText(path, store.ExportCsv());
                Console.WriteLine($"Wrote {store.All.Count} templates to {path}");
                return 0;
            }

            default:
                throw new CliException($"Unknown subcommand '{sub}'. Use: list | show <id> | export-csv --out file.csv");
        }
    }
}
