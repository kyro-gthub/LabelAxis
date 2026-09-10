using LabelMaker.Core.Barcodes;

namespace LabelMaker.Core.Barcodes.Code128;

/// <summary>
/// First-party Code 128 / GS1-128 encoder (spec §6, highest barcode priority).
/// Supports forced sets A/B/C, automatic cost-aware set selection, the four FNC
/// functions and GS1 FNC1. Output is a resolution-independent
/// <see cref="BarcodeSymbol"/>; correctness is guarded by a ZXing round-trip
/// corpus test.
///
/// FNC characters follow the widely-used convention (also used by ZXing):
/// <c>ñ</c>=FNC1, <c>ò</c>=FNC2, <c>ó</c>=FNC3, <c>ô</c>=FNC4.
/// </summary>
public static class Code128Encoder
{
    public const char Fnc1 = 'ñ';
    public const char Fnc2 = 'ò';
    public const char Fnc3 = 'ó';
    public const char Fnc4 = 'ô';

    private enum Set
    {
        A,
        B,
        C,
    }

    public static BarcodeSymbol Encode(
        string data,
        BarcodeSymbology symbology = BarcodeSymbology.Code128,
        int quietZoneModules = 10,
        bool gs1 = false)
    {
        ArgumentNullException.ThrowIfNull(data);

        bool forceGs1 = gs1 || symbology == BarcodeSymbology.GS1_128;
        string input = data;
        if (forceGs1 && (input.Length == 0 || input[0] != Fnc1))
        {
            input = Fnc1 + input;
        }

        List<int> codes = symbology switch
        {
            BarcodeSymbology.Code128A => EncodeFixed(input, Set.A),
            BarcodeSymbology.Code128B => EncodeFixed(input, Set.B),
            BarcodeSymbology.Code128C => EncodeFixed(input, Set.C),
            _ => EncodeAuto(input),
        };

        // Checksum: start value + sum(position * value), position from 1, mod 103.
        long sum = codes[0];
        for (int i = 1; i < codes.Count; i++)
        {
            sum += (long)i * codes[i];
        }

        codes.Add((int)(sum % 103));
        codes.Add(Code128Patterns.Stop);

        return BuildSymbol(symbology, codes, HumanReadable(data), Math.Max(quietZoneModules, 10));
    }

    // ---- fixed-set encoders ----

    private static List<int> EncodeFixed(string s, Set set)
    {
        var codes = new List<int>
        {
            set switch
            {
                Set.A => Code128Patterns.StartA,
                Set.B => Code128Patterns.StartB,
                _ => Code128Patterns.StartC,
            },
        };

        if (set == Set.C)
        {
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == Fnc1)
                {
                    codes.Add(102);
                    i++;
                    continue;
                }

                if (i + 1 >= s.Length || !IsDigit(s[i]) || !IsDigit(s[i + 1]))
                {
                    throw new FormatException(
                        "Code 128 Set C requires an even number of digits (FNC1 allowed). " +
                        $"Problem near position {i}.");
                }

                codes.Add(((s[i] - '0') * 10) + (s[i + 1] - '0'));
                i += 2;
            }

            return codes;
        }

        foreach (char c in s)
        {
            switch (c)
            {
                case Fnc1: codes.Add(102); break;
                case Fnc2 when set == Set.A: codes.Add(Code128Patterns.Fnc2); break;
                case Fnc3 when set == Set.A: codes.Add(Code128Patterns.Fnc3); break;
                case Fnc4 when set == Set.A: codes.Add(Code128Patterns.Fnc4A); break;
                case Fnc2 when set == Set.B: codes.Add(Code128Patterns.Fnc2); break;
                case Fnc3 when set == Set.B: codes.Add(Code128Patterns.Fnc3); break;
                case Fnc4 when set == Set.B: codes.Add(Code128Patterns.Fnc4B); break;
                default:
                    int v = ValueInSet(c, set);
                    if (v < 0)
                    {
                        throw new FormatException(
                            $"Character U+{(int)c:X4} cannot be encoded in Code 128 Set {set}.");
                    }

                    codes.Add(v);
                    break;
            }
        }

        return codes;
    }

    // ---- automatic set selection ----

    private static List<int> EncodeAuto(string s)
    {
        var codes = new List<int>();
        int n = s.Length;
        Set set = ChooseStartSet(s);
        codes.Add(set switch
        {
            Set.A => Code128Patterns.StartA,
            Set.B => Code128Patterns.StartB,
            _ => Code128Patterns.StartC,
        });

        int i = 0;
        while (i < n)
        {
            if (set == Set.C)
            {
                if (s[i] == Fnc1)
                {
                    codes.Add(102);
                    i++;
                    continue;
                }

                if (i + 1 < n && IsDigit(s[i]) && IsDigit(s[i + 1]))
                {
                    codes.Add(((s[i] - '0') * 10) + (s[i + 1] - '0'));
                    i += 2;
                    continue;
                }

                // Leave Set C.
                if (NeedsSetA(s[i]))
                {
                    codes.Add(Code128Patterns.CodeA);
                    set = Set.A;
                }
                else
                {
                    codes.Add(Code128Patterns.CodeB);
                    set = Set.B;
                }

                continue;
            }

            // Set A or B: consider a run of digits worth switching to C for.
            int dr = DigitRun(s, i);
            bool atEnd = i + dr == n;
            if (dr >= 4 || (atEnd && dr >= 2 && dr % 2 == 0))
            {
                if (dr % 2 == 1)
                {
                    codes.Add(ValueInSet(s[i], set));
                    i++;
                    dr--;
                }

                if (dr >= 2)
                {
                    codes.Add(Code128Patterns.CodeC);
                    set = Set.C;
                    continue;
                }
            }

            char c = s[i];
            switch (c)
            {
                case Fnc1: codes.Add(102); i++; continue;
                case Fnc2: codes.Add(Code128Patterns.Fnc2); i++; continue;
                case Fnc3: codes.Add(Code128Patterns.Fnc3); i++; continue;
                case Fnc4: codes.Add(set == Set.A ? Code128Patterns.Fnc4A : Code128Patterns.Fnc4B); i++; continue;
            }

            int value = ValueInSet(c, set);
            if (value >= 0)
            {
                codes.Add(value);
                i++;
                continue;
            }

            Set other = set == Set.A ? Set.B : Set.A;
            int otherValue = ValueInSet(c, other);
            if (otherValue < 0)
            {
                throw new FormatException($"Character U+{(int)c:X4} is not representable in Code 128.");
            }

            bool nextAlsoNeedsOther = i + 1 < n && IsPlainChar(s[i + 1]) && ValueInSet(s[i + 1], set) < 0;
            if (nextAlsoNeedsOther || i + 1 == n)
            {
                codes.Add(other == Set.A ? Code128Patterns.CodeA : Code128Patterns.CodeB);
                set = other;
                codes.Add(otherValue);
            }
            else
            {
                codes.Add(Code128Patterns.ShiftFromA); // 98, same value from either set
                codes.Add(otherValue);
            }

            i++;
        }

        return codes;
    }

    private static Set ChooseStartSet(string s)
    {
        int scan = 0;
        while (scan < s.Length && s[scan] == Fnc1)
        {
            scan++;
        }

        if (scan >= s.Length)
        {
            return Set.B;
        }

        bool allDigits = true;
        int digitCount = 0;
        for (int k = scan; k < s.Length; k++)
        {
            if (IsDigit(s[k]))
            {
                digitCount++;
            }
            else
            {
                allDigits = false;
                break;
            }
        }

        if (allDigits && digitCount >= 2 && digitCount % 2 == 0)
        {
            return Set.C;
        }

        if (DigitRun(s, scan) >= 4)
        {
            return Set.C;
        }

        for (int k = scan; k < s.Length; k++)
        {
            char c = s[k];
            if (!IsPlainChar(c))
            {
                continue;
            }

            if (NeedsSetA(c))
            {
                return Set.A;
            }

            if (c is >= (char)96 and <= (char)127)
            {
                return Set.B;
            }
        }

        return Set.B;
    }

    // ---- helpers ----

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    private static bool IsPlainChar(char c) => c is not (Fnc1 or Fnc2 or Fnc3 or Fnc4);

    /// <summary>A-only: ASCII control characters (0..31).</summary>
    private static bool NeedsSetA(char c) => c < 32;

    private static int DigitRun(string s, int start)
    {
        int k = start;
        while (k < s.Length && IsDigit(s[k]))
        {
            k++;
        }

        return k - start;
    }

    private static int ValueInSet(char c, Set set) => set switch
    {
        Set.A => c switch
        {
            >= (char)32 and <= (char)95 => c - 32,
            >= (char)0 and <= (char)31 => c + 64,
            _ => -1,
        },
        Set.B => c is >= (char)32 and <= (char)127 ? c - 32 : -1,
        Set.C => -1,
        _ => -1,
    };

    private static string HumanReadable(string data)
    {
        Span<char> buffer = stackalloc char[data.Length];
        int n = 0;
        foreach (char c in data)
        {
            if (c is Fnc1 or Fnc2 or Fnc3 or Fnc4)
            {
                continue;
            }

            buffer[n++] = c;
        }

        return new string(buffer[..n]);
    }

    private static BarcodeSymbol BuildSymbol(
        BarcodeSymbology symbology, List<int> codes, string hri, int quietZone)
    {
        var bars = new List<BarModule>();
        int module = 0;

        void AddRun(int width, bool isBar)
        {
            bars.Add(new BarModule(module, width, isBar));
            module += width;
        }

        AddRun(quietZone, isBar: false);

        foreach (int code in codes)
        {
            string pattern = Code128Patterns.Elements[code];
            bool isBar = true; // every element pattern starts with a bar
            foreach (char widthChar in pattern)
            {
                AddRun(widthChar - '0', isBar);
                isBar = !isBar;
            }
        }

        AddRun(quietZone, isBar: false);

        return new BarcodeSymbol
        {
            Symbology = symbology,
            Dimensionality = BarcodeDimensionality.Linear,
            EncodedValue = hri,
            HumanReadableText = hri,
            QuietZoneModules = quietZone,
            Bars = bars,
            TotalWidthModules = module,
        };
    }
}
