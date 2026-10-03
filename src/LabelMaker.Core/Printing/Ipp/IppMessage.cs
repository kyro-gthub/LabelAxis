using System.Buffers.Binary;
using System.Text;

namespace LabelMaker.Core.Printing.Ipp;

/// <summary>The value tags and group delimiters of the Internet Printing Protocol (RFC 8010) this app uses.</summary>
public static class IppTag
{
    public const byte OperationGroup = 0x01;
    public const byte JobGroup = 0x02;
    public const byte EndOfAttributes = 0x03;
    public const byte PrinterGroup = 0x04;
    public const byte UnsupportedGroup = 0x05;

    public const byte Integer = 0x21;
    public const byte Boolean = 0x22;
    public const byte Enum = 0x23;
    public const byte Resolution = 0x32;
    public const byte RangeOfInteger = 0x33;
    public const byte TextWithoutLanguage = 0x41;
    public const byte NameWithoutLanguage = 0x42;
    public const byte Keyword = 0x44;
    public const byte Uri = 0x45;
    public const byte Charset = 0x47;
    public const byte NaturalLanguage = 0x48;
    public const byte MimeMediaType = 0x49;

    /// <summary>Tags below this mark the start of an attribute group, not a value.</summary>
    public const byte FirstValueTag = 0x10;
}

public static class IppOperation
{
    public const short PrintJob = 0x0002;
    public const short GetPrinterAttributes = 0x000B;
}

/// <summary>One attribute: its value tag, its name and one or more raw values.</summary>
public sealed record IppAttribute(byte Tag, string Name, IReadOnlyList<byte[]> Values)
{
    public string Text => Values.Count == 0 ? string.Empty : Encoding.UTF8.GetString(Values[0]);

    public IEnumerable<string> Texts => Values.Select(v => Encoding.UTF8.GetString(v));

    public int Integer => Values.Count > 0 && Values[0].Length >= 4 ? BinaryPrimitives.ReadInt32BigEndian(Values[0]) : 0;

    public bool Boolean => Values.Count > 0 && Values[0].Length >= 1 && Values[0][0] != 0;

    /// <summary>Resolutions as dots per inch (dots per cm are converted).</summary>
    public IEnumerable<int> ResolutionsDpi => Values.Where(v => v.Length >= 9).Select(v =>
    {
        int x = BinaryPrimitives.ReadInt32BigEndian(v);
        return v[8] == 4 ? (int)Math.Round(x * 2.54) : x;
    });
}

/// <summary>A request or response: version 2.0, an operation (or status) code, a request id and attribute groups.</summary>
public sealed class IppMessage
{
    public short Code { get; init; }

    public int RequestId { get; init; } = 1;

    public List<(byte Group, List<IppAttribute> Attributes)> Groups { get; } = new();

    /// <summary>IPP status codes below 0x0100 are successes ("successful-ok" and its variants).</summary>
    public bool IsSuccess => Code is >= 0 and < 0x0100;

    /// <summary>A new request carrying the attributes every request needs, aimed at <paramref name="printerUri"/>.</summary>
    public static IppMessage Request(short operation, string printerUri, int requestId = 1)
    {
        var message = new IppMessage { Code = operation, RequestId = requestId };
        message.Add(IppTag.OperationGroup, IppTag.Charset, "attributes-charset", "utf-8");
        message.Add(IppTag.OperationGroup, IppTag.NaturalLanguage, "attributes-natural-language", "en");
        message.Add(IppTag.OperationGroup, IppTag.Uri, "printer-uri", printerUri);
        return message;
    }

    public IppMessage Add(byte group, byte tag, string name, params string[] values) =>
        Add(group, new IppAttribute(tag, name, values.Select(v => Encoding.UTF8.GetBytes(v)).ToList()));

    public IppMessage AddInteger(byte group, byte tag, string name, int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return Add(group, new IppAttribute(tag, name, new[] { bytes }));
    }

    public IppMessage Add(byte group, IppAttribute attribute)
    {
        int index = Groups.FindIndex(g => g.Group == group);
        if (index < 0)
        {
            Groups.Add((group, new List<IppAttribute>()));
            index = Groups.Count - 1;
        }

        Groups[index].Attributes.Add(attribute);
        return this;
    }

    /// <summary>The first attribute with this name in any group, or null.</summary>
    public IppAttribute? Find(string name) =>
        Groups.SelectMany(g => g.Attributes).FirstOrDefault(a => a.Name == name);

    public byte[] Encode()
    {
        using var ms = new MemoryStream();
        Span<byte> head = stackalloc byte[8];
        head[0] = 2;
        head[1] = 0;
        BinaryPrimitives.WriteInt16BigEndian(head[2..], Code);
        BinaryPrimitives.WriteInt32BigEndian(head[4..], RequestId);
        ms.Write(head);

        foreach ((byte group, List<IppAttribute> attributes) in Groups)
        {
            ms.WriteByte(group);
            foreach (IppAttribute a in attributes)
            {
                for (int i = 0; i < a.Values.Count; i++)
                {
                    // Second and later values of an attribute have an empty name.
                    WriteField(ms, a.Tag, i == 0 ? Encoding.UTF8.GetBytes(a.Name) : Array.Empty<byte>(), a.Values[i]);
                }
            }
        }

        ms.WriteByte(IppTag.EndOfAttributes);
        return ms.ToArray();
    }

    /// <summary>Reads a response (or request); anything after the end-of-attributes tag is ignored.</summary>
    public static IppMessage Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 9)
        {
            throw new InvalidDataException("The printer's answer is too short to be an IPP message.");
        }

        var message = new IppMessage
        {
            Code = BinaryPrimitives.ReadInt16BigEndian(data[2..]),
            RequestId = BinaryPrimitives.ReadInt32BigEndian(data[4..]),
        };

        int pos = 8;
        List<IppAttribute>? current = null;
        string? lastName = null;
        byte lastTag = 0;
        List<byte[]>? lastValues = null;

        void Flush()
        {
            if (current is not null && lastName is not null && lastValues is not null)
            {
                current.Add(new IppAttribute(lastTag, lastName, lastValues));
            }

            lastName = null;
            lastValues = null;
        }

        while (pos < data.Length)
        {
            byte tag = data[pos++];
            if (tag == IppTag.EndOfAttributes)
            {
                break;
            }

            if (tag < IppTag.FirstValueTag)
            {
                Flush();
                current = new List<IppAttribute>();
                message.Groups.Add((tag, current));
                continue;
            }

            if (pos + 2 > data.Length)
            {
                throw new InvalidDataException("The printer's answer ends in the middle of an attribute.");
            }

            int nameLength = BinaryPrimitives.ReadInt16BigEndian(data[pos..]);
            pos += 2;
            string name = Encoding.UTF8.GetString(data.Slice(pos, nameLength));
            pos += nameLength;
            int valueLength = BinaryPrimitives.ReadInt16BigEndian(data[pos..]);
            pos += 2;
            if (pos + valueLength > data.Length)
            {
                throw new InvalidDataException("The printer's answer ends in the middle of a value.");
            }

            byte[] value = data.Slice(pos, valueLength).ToArray();
            pos += valueLength;

            if (nameLength == 0 && lastValues is not null)
            {
                lastValues.Add(value);    // another value of the same attribute
            }
            else
            {
                Flush();
                lastName = name;
                lastTag = tag;
                lastValues = new List<byte[]> { value };
            }
        }

        Flush();
        return message;
    }

    private static void WriteField(Stream s, byte tag, byte[] name, byte[] value)
    {
        Span<byte> len = stackalloc byte[2];
        s.WriteByte(tag);
        BinaryPrimitives.WriteInt16BigEndian(len, (short)name.Length);
        s.Write(len);
        s.Write(name);
        BinaryPrimitives.WriteInt16BigEndian(len, (short)value.Length);
        s.Write(len);
        s.Write(value);
    }
}
