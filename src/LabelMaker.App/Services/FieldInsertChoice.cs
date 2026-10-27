namespace LabelMaker.App.Services;

/// <summary>
/// How a column of barcodes goes on the label: as a barcode of <paramref name="Barcode"/>, or as
/// text when that is null.
/// </summary>
public sealed record FieldInsertChoice(Core.Barcodes.BarcodeSymbology? Barcode);
