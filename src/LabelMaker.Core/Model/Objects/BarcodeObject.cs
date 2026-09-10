using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Variables;

namespace LabelMaker.Core.Model.Objects;

/// <summary>
/// A barcode on a label. Stays a live barcode object — the <see cref="Spec"/> is
/// re-encoded whenever the value or a bound field changes. Physical bar width is
/// governed by <see cref="BarcodeSpec.ModuleWidthMm"/>; <see cref="LabelObject.Bounds"/>
/// is the drawing envelope and, when <see cref="AutoSize"/> is on, is kept in sync
/// with the encoded symbol's natural size.
/// </summary>
public sealed class BarcodeObject : LabelObject
{
    public override ObjectKind Kind => ObjectKind.Barcode;

    public BarcodeSpec Spec { get; set; } = new();

    /// <summary>
    /// When true the object's <see cref="LabelObject.Bounds"/> width/height follow
    /// the encoded symbol at its module width; when false the symbol is scaled to
    /// fit <see cref="LabelObject.Bounds"/> (module width becomes a minimum hint).
    /// </summary>
    public bool AutoSize { get; set; } = true;

    public override IEnumerable<string> GetSearchableStrings()
    {
        yield return Spec.Data;
    }

    public override IEnumerable<string> GetReferencedFields() =>
        TokenScanner.ExtractFieldNames(Spec.Data);

    public override LabelObject Clone()
    {
        var copy = new BarcodeObject
        {
            Spec = Spec.Clone(),
            AutoSize = AutoSize,
        };
        CopyBaseTo(copy);
        return copy;
    }
}
