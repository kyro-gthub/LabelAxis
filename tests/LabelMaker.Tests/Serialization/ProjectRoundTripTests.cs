using System.Text;
using FluentAssertions;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Serialization;
using LabelMaker.Core.Units;
using Xunit;

namespace LabelMaker.Tests.Serialization;

public sealed class ProjectRoundTripTests
{
    private static ProjectDocument BuildSampleDocument()
    {
        LabelTemplate template = TemplateStore.CreateWithBuiltIns().Get("formtec-3200")!;
        ProjectModel project = ProjectModel.CreateNew(template, "Round trip");

        project.MasterLabel.Objects.Add(new TextObject
        {
            Name = "title",
            Bounds = new RectMm(2, 2, 34, 8),
            Content = RichTextContent.FromPlainText("{Product}"),
            FontSizePt = 9,
            Bold = true,
        });
        project.MasterLabel.Objects.Add(new BarcodeObject
        {
            Name = "code",
            Bounds = new RectMm(2, 11, 34, 8),
            Spec = new BarcodeSpec { Symbology = BarcodeSymbology.Code128, Data = "{Barcode}", ModuleWidthMm = 0.25 },
        });
        project.MasterLabel.Objects.Add(new ShapeObject
        {
            Name = "border",
            Bounds = new RectMm(0.5, 0.5, 37, 20),
            Shape = ShapeKind.RoundRectangle,
            Stroke = new ColorRgba(0, 0, 0),
            StrokeWidthMm = 0.2,
            CornerRadiusMm = 1.5,
        });

        project.Sequences.Add(new LabelMaker.Core.Sequences.SequenceDefinition { Name = "SN", Prefix = "SN", Start = 1, End = 100 });
        project.Variables.Add(new CustomVariable { Name = "Lot", Kind = VariableKind.Constant, Value = "LOT-9" });

        var doc = new ProjectDocument { Project = project };
        string assetId = doc.Assets.Add(Encoding.UTF8.GetBytes("PNGDATA-not-really"), ".png");
        project.MasterLabel.Objects.Add(new ImageObject
        {
            Name = "logo",
            Bounds = new RectMm(24, 2, 12, 8),
            AssetId = assetId,
            SourceFileName = "logo.png",
        });
        return doc;
    }

    [Fact]
    public void Save_then_load_preserves_the_document_model()
    {
        ProjectDocument original = BuildSampleDocument();
        string path = Path.Combine(Path.GetTempPath(), $"lm-{Guid.NewGuid():N}.labelproj");

        try
        {
            ProjectFile.Save(original, path);
            File.Exists(path).Should().BeTrue();

            ProjectDocument loaded = ProjectFile.Load(path);

            loaded.Project.Metadata.Title.Should().Be("Round trip");
            loaded.Project.Template!.Id.Should().Be("formtec-3200");
            loaded.Project.MasterLabel.Objects.Should().HaveCount(4);

            var text = loaded.Project.MasterLabel.Objects.OfType<TextObject>().Single();
            text.Content.PlainText.Should().Be("{Product}");
            text.Bold.Should().BeTrue();

            var barcode = loaded.Project.MasterLabel.Objects.OfType<BarcodeObject>().Single();
            barcode.Spec.Symbology.Should().Be(BarcodeSymbology.Code128);
            barcode.Spec.Data.Should().Be("{Barcode}");
            barcode.Spec.ModuleWidthMm.Should().BeApproximately(0.25, 1e-9);

            var shape = loaded.Project.MasterLabel.Objects.OfType<ShapeObject>().Single();
            shape.Shape.Should().Be(ShapeKind.RoundRectangle);
            shape.CornerRadiusMm.Should().BeApproximately(1.5, 1e-9);

            loaded.Project.Sequences.Single().Prefix.Should().Be("SN");
            loaded.Project.Variables.Single().Value.Should().Be("LOT-9");
            loaded.Assets.Count.Should().Be(1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Atomic_save_keeps_a_backup_and_never_loses_the_previous_file()
    {
        ProjectDocument doc = BuildSampleDocument();
        string path = Path.Combine(Path.GetTempPath(), $"lm-{Guid.NewGuid():N}.labelproj");

        try
        {
            ProjectFile.Save(doc, path);
            long firstSize = new FileInfo(path).Length;

            doc.Project.Metadata.Title = "Second version";
            ProjectFile.Save(doc, path);

            File.Exists(path + ".bak").Should().BeTrue();
            ProjectFile.Load(path + ".bak").Project.Metadata.Title.Should().Be("Round trip");
            ProjectFile.Load(path).Project.Metadata.Title.Should().Be("Second version");
            firstSize.Should().BeGreaterThan(0);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }

    [Fact]
    public void Loading_rejects_a_newer_schema_version()
    {
        ProjectDocument doc = BuildSampleDocument();
        using var ms = new MemoryStream();
        ProjectBundle source = doc.ToBundle();
        var bundle = new ProjectBundle
        {
            Manifest = new ProjectManifest { SchemaVersion = 999 },
            Project = source.Project,
            Assets = source.Assets,
            DataCaches = source.DataCaches,
            ThumbnailPng = source.ThumbnailPng,
        };
        ProjectArchive.Write(ms, bundle);
        ms.Position = 0;

        Action act = () => ProjectArchive.Read(ms);
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Project_json_has_no_dotnet_type_names()
    {
        ProjectDocument doc = BuildSampleDocument();
        using var ms = new MemoryStream();
        ProjectArchive.Write(ms, doc.ToBundle());
        ms.Position = 0;

        using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("project.json")!.Open());
        string json = reader.ReadToEnd();

        json.Should().NotContain("$type");
        json.Should().NotContain("LabelMaker.Core"); // no CLR type leakage
        json.Should().Contain("\"kind\": \"Barcode\"");
    }
}
