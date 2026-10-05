using LabelMaker.App.Rendering;
using LabelMaker.Core.Model.Objects;

namespace LabelMaker.App.ViewModels;

/// <summary>Fonts added from font files ("Add font…" by the font lists, and Settings › Fonts).</summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// Asks for a .ttf or .otf file, adds it to LabelAxis and gives it to the selected text
    /// boxes (or table, or barcode digits). Returns the family added, or null.
    /// </summary>
    public string? AddFontFromFile()
    {
        string? path = _shell.PickOpenFile("Font files (*.ttf, *.otf)|*.ttf;*.otf", "Add a font");
        if (path is null)
        {
            return null;
        }

        string family;
        try
        {
            family = CustomFonts.Add(path);
        }
        catch (FontFileException e)
        {
            _shell.Warn(e.Message);
            return null;
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            _shell.Warn(Localization.Localizer.F("The font could not be copied into LabelAxis: {0}", e.Message));
            return null;
        }

        if (_session.Selection.Count > 0)
        {
            if (Properties.IsText)
            {
                Properties.FontFamily = family;
            }
            else if (Properties.IsTable)
            {
                Properties.TableFontFamily = family;
            }
        }

        RefreshCanvas();
        StatusText = Localization.Localizer.F("Font “{0}” added", family);
        return family;
    }
}
