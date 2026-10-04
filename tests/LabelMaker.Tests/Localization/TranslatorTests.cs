using FluentAssertions;
using LabelMaker.Core.Localization;
using Xunit;

namespace LabelMaker.Tests.Localization;

public sealed class TranslatorTests
{
    private static Translator Arabic() => new(new Dictionary<string, string>
    {
        ["Save"] = "حفظ",
        ["Current page"] = "الصفحة الحالية",
        ["Browse"] = "استعراض",
        ["Imported {0} records from {1}"] = "تم استيراد {0} سجلًا من {1}",
        ["Page {0}"] = "الصفحة {0}",
        ["Printing failed:"] = "فشلت الطباعة:",
        ["Check the printer is on."] = "تأكد من أن الطابعة قيد التشغيل.",
        ["No printer selected"] = "لم تُحدَّد طابعة",
        ["Ready"] = "جاهز",
        ["{1} of {0}"] = "{1} من {0}",
    });

    [Fact]
    public void Whole_texts_are_translated_and_unknown_ones_stay_in_english()
    {
        Translator t = Arabic();
        t.Translate("Save").Should().Be("حفظ");
        t.Translate("Something new").Should().Be("Something new");
        t.Translate("").Should().BeEmpty();
        t.Translate("حفظ").Should().Be("حفظ", "text with no English letters is left alone");
    }

    [Fact]
    public void Access_keys_capitals_and_trailing_dots_use_the_plain_entry()
    {
        Translator t = Arabic();
        t.Translate("_Save").Should().Be("حفظ");
        t.Translate("CURRENT PAGE").Should().Be("الصفحة الحالية");
        t.Translate("Browse…").Should().Be("استعراض…");
        t.Translate("Browse:").Should().Be("استعراض:");
        t.Translate("  Save ").Should().Be("  حفظ ");
        t.Translate("★  Current page").Should().Be("★  الصفحة الحالية");
        t.Translate("2.  Page 4").Should().Be("2.  الصفحة 4");
        t.Translate("Imported 3 records from x.csv.").Should().Be($"تم استيراد 3 سجلًا من {Lrm}x.csv{Lrm}.");
    }

    private const char Lrm = Translator.LeftToRightMark;

    [Fact]
    public void Text_put_together_at_run_time_is_matched_by_its_template()
    {
        Translator t = Arabic();

        // English inside the Arabic (a file name) is marked so it keeps its own order.
        t.Translate("Imported 12 records from stock.csv").Should().Be($"تم استيراد 12 سجلًا من {Lrm}stock.csv{Lrm}");

        // A hole that is itself a known text is translated too.
        t.Translate("Imported 3 records from Page 2").Should().Be("تم استيراد 3 سجلًا من الصفحة 2");
    }

    [Fact]
    public void Holes_can_change_order_in_the_translation()
    {
        new Translator(new Dictionary<string, string> { ["{0} sheets on {1}"] = "على {1}: {0} ورقة" })
            .Translate("4 sheets on HP LaserJet").Should().Be($"على {Lrm}HP LaserJet{Lrm}: 4 ورقة");
    }

    [Fact]
    public void Several_sentences_and_status_parts_are_translated_one_by_one()
    {
        Translator t = Arabic();
        t.Translate("Printing failed:\n\nCheck the printer is on.").Should().Be("فشلت الطباعة:\n\nتأكد من أن الطابعة قيد التشغيل.");
        t.Translate("Ready · No printer selected").Should().Be("جاهز · لم تُحدَّد طابعة");
        t.Translate("Printing failed: paper jam").Should().Be("فشلت الطباعة: paper jam");
    }

    [Fact]
    public void A_template_with_almost_no_words_is_not_used_to_recognise_text()
    {
        // "{1} of {0}" would otherwise swallow every text containing " of ".
        Arabic().Translate("Proof of concept").Should().Be("Proof of concept");
    }

    [Fact]
    public void Format_fills_in_the_translated_template()
    {
        Arabic().Format("Page {0}", 7).Should().Be("الصفحة 7");
        Translator.None.Format("Page {0}", 7).Should().Be("Page 7");
    }

    [Fact]
    public void Language_files_are_read_from_json()
    {
        Translator t = Translator.FromJson("""
            {
              // comments are allowed
              "Save": "پاشەکەوت",
            }
            """);
        t.Translate("Save").Should().Be("پاشەکەوت");
        t.Knows("Save").Should().BeTrue();
    }
}
