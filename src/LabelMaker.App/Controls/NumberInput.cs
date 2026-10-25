using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LabelMaker.Core.Localization;

namespace LabelMaker.App.Controls;

/// <summary>
/// For boxes that take a number: digits typed or pasted from a Persian, Kurdish, Arabic or Hindi
/// keyboard (۱۲, ١٢, १२) go in as 0–9, so the number is read. The NumberBox style turns it on.
/// </summary>
public static class NumberInput
{
    public static readonly DependencyProperty AsciiDigitsProperty = DependencyProperty.RegisterAttached(
        "AsciiDigits", typeof(bool), typeof(NumberInput), new PropertyMetadata(false, OnAsciiDigitsChanged));

    public static bool GetAsciiDigits(DependencyObject element) => (bool)element.GetValue(AsciiDigitsProperty);

    public static void SetAsciiDigits(DependencyObject element, bool value) => element.SetValue(AsciiDigitsProperty, value);

    private static void OnAsciiDigitsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box)
        {
            return;
        }

        box.PreviewTextInput -= OnPreviewTextInput;
        DataObject.RemovePastingHandler(box, OnPasting);
        if (e.NewValue is true)
        {
            box.PreviewTextInput += OnPreviewTextInput;
            DataObject.AddPastingHandler(box, OnPasting);
        }
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox box || !NativeDigits.HasNative(e.Text))
        {
            return;
        }

        e.Handled = true;
        string ascii = NativeDigits.ToAscii(e.Text)!;
        int at = box.SelectionStart;
        box.SelectedText = ascii;
        box.Select(at + ascii.Length, 0);
    }

    private static void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(DataFormats.UnicodeText) is string text && NativeDigits.HasNative(text))
        {
            e.DataObject = new DataObject(DataFormats.UnicodeText, NativeDigits.ToAscii(text)!);
        }
    }
}
