namespace LabelMaker.Core.Barcodes.Code128;

/// <summary>
/// The 107 Code 128 element patterns (ISO/IEC 15417). Each entry is the
/// bar/space widths in modules, starting with a bar. Entries 0-102 are 11 modules
/// wide (3 bars + 3 spaces); 103-105 are the start codes; 106 is the 13-module
/// stop pattern (4 bars + 3 spaces).
///
/// This table is transcription-risk: it is guarded by
/// <c>Code128RoundTripTests</c>, which encodes a large corpus with this table and
/// decodes every symbol back with ZXing.
/// </summary>
internal static class Code128Patterns
{
    public const int StartA = 103;
    public const int StartB = 104;
    public const int StartC = 105;
    public const int Stop = 106;

    // Shared special code values
    public const int CodeC = 99;   // from A or B
    public const int CodeB = 100;  // from A or C
    public const int CodeA = 101;  // from B or C
    public const int Fnc1 = 102;   // in A/B; value 102 in C
    public const int Fnc2 = 97;    // A/B only
    public const int Fnc3 = 96;    // A/B only
    public const int Fnc4A = 101;  // in set A
    public const int Fnc4B = 100;  // in set B
    public const int ShiftFromA = 98; // shift to B for one char
    public const int ShiftFromB = 98; // shift to A for one char

    public static readonly string[] Elements =
    {
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
    };
}
