using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using LabelMaker.Core.Localization;
using Xunit;

namespace LabelMaker.Tests.Localization;

/// <summary>The language files that ship with the app.</summary>
public sealed class LanguageFileTests
{
    // Each language with the script its translations are written in (null: Latin letters).
    private static readonly Dictionary<string, string?> Languages = new()
    {
        ["ar"] = "[؀-ۿ]",
        ["ckb"] = "[؀-ۿ]",
        ["fa"] = "[؀-ۿ]",
        ["fr"] = null,
        ["es"] = null,
        ["nl"] = null,
        ["ru"] = "[Ѐ-ӿ]",
        ["zh-Hans"] = "[一-鿿]",
        ["hi"] = "[ऀ-ॿ]",
    };

    public static TheoryData<string> Translated => new(Languages.Keys);

    // Names that stay in English in every language: the product, technical names, units and samples.
    private static readonly HashSet<string> StaysEnglish = new(StringComparer.Ordinal)
    {
        "LabelAxis", "QR Code", "ABC", "ABC001459", "HSB", "RGB", "mm", "px", "ZPL (Zebra)", "TSPL (TSC, Xprinter)",
        "Label", "Axis", "Rot",
    };

    private static string Repo
    {
        get
        {
            string? dir = AppContext.BaseDirectory;
            while (dir is not null && !File.Exists(Path.Combine(dir, "LabelMaker.slnx")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            return dir ?? throw new DirectoryNotFoundException("Repository root not found.");
        }
    }

    private static Dictionary<string, string> Entries(string language) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(Repo, "src", "LabelMaker.App", "Languages", language + ".json")))!;

    [Theory]
    [MemberData(nameof(Translated))]
    public void Every_language_translates_the_same_texts(string language)
    {
        Entries(language).Keys.Should().BeEquivalentTo(Entries("ar").Keys);
    }

    [Theory]
    [MemberData(nameof(Translated))]
    public void Every_translation_keeps_the_numbered_holes_of_its_english(string language)
    {
        foreach ((string english, string translated) in Entries(language))
        {
            Holes(translated).Should().Equal(Holes(english), $"\"{english}\" → \"{translated}\"");
            translated.Should().NotBeNullOrWhiteSpace(english);
        }

        static List<string> Holes(string s) => Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).Order().ToList();
    }

    [Theory]
    [MemberData(nameof(Translated))]
    public void Every_text_written_in_the_windows_has_a_translation(string language)
    {
        Translator translator = Translator.FromJson(File.ReadAllText(
            Path.Combine(Repo, "src", "LabelMaker.App", "Languages", language + ".json")));
        string[] attributes = { "Text", "Content", "Header", "ToolTip", "Title" };

        var missing = new List<string>();
        foreach (string file in Directory.EnumerateFiles(Path.Combine(Repo, "src", "LabelMaker.App"), "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            foreach (XElement element in XDocument.Load(file).Descendants())
            {
                foreach (XAttribute a in element.Attributes())
                {
                    bool shown = attributes.Contains(a.Name.LocalName) || (a.Name.LocalName == "Tag" && element.Name.LocalName == "TextBox");
                    string text = a.Value.Trim();
                    if (!shown || text.StartsWith('{') || !Regex.IsMatch(text, "[A-Za-z]{2,}") ||
                        StaysEnglish.Contains(text) || Regex.IsMatch(text, @"^(Ctrl|Alt|Shift|F\d)"))
                    {
                        continue;
                    }

                    if (translator.Translate(text) == text && !translator.Knows(text)) // "Port" is "Port" in French too
                    {
                        missing.Add($"{Path.GetFileName(file)}: {text}");
                    }
                }
            }
        }

        missing.Should().BeEmpty($"every window text needs a {language} translation (src/LabelMaker.App/Languages/{language}.json); missing: {string.Join(" | ", missing)}");
    }

    [Theory]
    [MemberData(nameof(Translated))]
    public void Technical_names_stay_in_english(string language)
    {
        Translator t = Translator.FromJson(File.ReadAllText(
            Path.Combine(Repo, "src", "LabelMaker.App", "Languages", language + ".json")));

        t.Translate("Save PDF…").Should().Contain("PDF");
        t.Translate("Add a QR code").Should().Contain("QR");
        t.Translate("Wi-Fi printers").Should().Contain("Wi-Fi");
        t.Translate("Import CSV…").Should().Contain("CSV");
        t.Translate("_Thermal print (ZPL / TSPL)…").Should().Contain("ZPL / TSPL");
        t.Translate("Bluetooth or serial port (COM)").Should().Contain("Bluetooth").And.Contain("COM");
        t.Translate("Code 128 (auto)").Should().StartWith("Code 128");
        t.Translate("Interleaved 2 of 5").Should().Be("Interleaved 2 of 5");
    }

    [Theory]
    [MemberData(nameof(Translated))]
    public void Messages_made_while_the_program_runs_are_translated(string language)
    {
        Translator t = Translator.FromJson(File.ReadAllText(
            Path.Combine(Repo, "src", "LabelMaker.App", "Languages", language + ".json")));

        foreach (string message in new[]
                 {
                     "Imported 120 records, 4 columns from stock.csv",
                     "Printing 3 sheet(s) on HP LaserJet",
                     "Remove page \"Back\"?",
                     "Sheet 2 of 9",
                     "Ready",
                     "Calibration: not set",
                 })
        {
            t.Translate(message).Should().NotBe(message);
            if (Languages[language] is { } script)
            {
                Regex.IsMatch(t.Translate(message), script).Should().BeTrue(message);
            }
        }
    }
}
