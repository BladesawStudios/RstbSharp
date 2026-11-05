using System.Text;
using static System.Buffers.Binary.BinaryPrimitives;

namespace RSTBSharp;

/// <summary>The values of a BYML hash map, whose hashed keys say nothing about its size.</summary>
internal sealed record BymlHashMap(List<object?> Values);

/// <summary>
/// Reads a BYML as far as size calculation needs: dictionaries, arrays, hash maps and strings. Other values come back as null.
/// </summary>
internal ref struct BymlReader
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly bool _bigEndian;
    private readonly string[] _keys;
    private readonly string[] _strings;

    private BymlReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _bigEndian = data.StartsWith("BY"u8);
        if (!_bigEndian && !data.StartsWith("YB"u8)) throw new InvalidDataException("Not a BYML: expected BY or YB magic.");
        _keys = StringTable(U32(4));
        _strings = StringTable(U32(8));
    }

    public static object? Read(ReadOnlySpan<byte> data)
    {
        BymlReader reader = new(data);
        int root = reader.U32(12);
        return root == 0 ? null : reader.Container(root);
    }

    private readonly int U32(int pos)
        => _bigEndian ? ReadInt32BigEndian(_data[pos..]) : ReadInt32LittleEndian(_data[pos..]);

    private readonly int U24(int pos)
        => _bigEndian ? _data[pos] << 16 | _data[pos + 1] << 8 | _data[pos + 2] : _data[pos + 2] << 16 | _data[pos + 1] << 8 | _data[pos];

    private readonly string[] StringTable(int pos)
    {
        if (pos == 0) return [];
        string[] table = new string[U24(pos + 1)];
        for (int i = 0; i < table.Length; i++)
        {
            ReadOnlySpan<byte> text = _data[(pos + U32(pos + 4 + 4 * i))..];
            table[i] = Encoding.UTF8.GetString(text[..text.IndexOf((byte)0)]);
        }

        return table;
    }

    private readonly object? Value(byte type, int slot) => type switch
    {
        0xa0 => _strings[U32(slot)],
        0xc0 or 0xc1 or 0x20 or 0x21 => Container(U32(slot)),
        _ => null,
    };

    private readonly object Container(int pos)
    {
        byte type = _data[pos];
        int count = U24(pos + 1);
        switch (type)
        {
            case 0xc0:
            {
                List<object?> array = new(count);
                int values = pos + 4 + ((count + 3) & ~3);
                for (int i = 0; i < count; i++) array.Add(Value(_data[pos + 4 + i], values + 4 * i));
                return array;
            }
            case 0xc1:
            {
                Dictionary<string, object?> dictionary = new(count);
                for (int i = 0; i < count; i++)
                {
                    int entry = pos + 4 + 8 * i;
                    dictionary[_keys[U24(entry)]] = Value(_data[entry + 3], entry + 4);
                }

                return dictionary;
            }
            case 0x20 or 0x21:
            {
                List<object?> values = new(count);
                int types = pos + 4 + 8 * count;
                for (int i = 0; i < count; i++) values.Add(Value(_data[types + i], pos + 8 + 8 * i));
                return new BymlHashMap(values);
            }
            default:
                throw new InvalidDataException($"Unexpected BYML container type 0x{type:x2} at 0x{pos:x}.");
        }
    }
}
