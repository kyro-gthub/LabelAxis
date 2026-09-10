using System.IO;
using System.Windows;
using System.Windows.Controls;
using LabelMaker.App.Services;
using LabelMaker.Core.Formtec;
using Microsoft.Win32;

namespace LabelMaker.App.Views;

public partial class TemplateManagerWindow : Window
{
    private readonly AppEnvironment _env;

    public TemplateManagerWindow(AppEnvironment env)
    {
        InitializeComponent();
        _env = env;
        SearchBox.TextChanged += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        List.ItemsSource = _env.Templates.Search(new TemplateQuery { Text = SearchBox.Text })
            .Select(t => new Row(t)).ToList();
    }

    private void OnSel(object sender, SelectionChangedEventArgs e)
    {
        if (List.SelectedItem is Row row)
        {
            Json.Text = TemplateJson.ToJson(row.Template);
        }
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Template JSON|*.json", Multiselect = true };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        int ok = 0;
        foreach (string file in dlg.FileNames)
        {
            try
            {
                LabelTemplate t = _env.Templates.ImportJson(file);
                _env.Templates.SaveUserTemplate(t, _env.Paths.UserTemplatesDirectory);
                ok++;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{Path.GetFileName(file)}: {ex.Message}", "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        Refresh();
        MessageBox.Show(this, $"Imported {ok} template(s).", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnImportCsv(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "CSV|*.csv" };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var added = _env.Templates.ImportCsv(File.ReadAllText(dlg.FileName));
            foreach (LabelTemplate t in added)
            {
                _env.Templates.SaveUserTemplate(t, _env.Paths.UserTemplatesDirectory);
            }

            Refresh();
            MessageBox.Show(this, $"Imported {added.Count} template(s) from CSV.", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Import", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not Row row)
        {
            return;
        }

        var dlg = new SaveFileDialog { Filter = "Template JSON|*.json", FileName = row.Template.Id };
        if (dlg.ShowDialog(this) == true)
        {
            _env.Templates.ExportJson(row.Template.Id, dlg.FileName);
        }
    }

    private void OnExportCsv(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "label-templates" };
        if (dlg.ShowDialog(this) == true)
        {
            File.WriteAllText(dlg.FileName, _env.Templates.ExportCsv());
        }
    }

    private sealed record Row(LabelTemplate Template)
    {
        public string Id => Template.Id;
        public string Brand => Template.Brand;
        public string GridText => $"{Template.Grid.Columns}×{Template.Grid.Rows}";
        public string LabelText => $"{Template.Grid.LabelWidthMm:0.#}×{Template.Grid.LabelHeightMm:0.#}";
        public string UserMark => Template.IsUserDefined ? "✓" : string.Empty;
    }
}
