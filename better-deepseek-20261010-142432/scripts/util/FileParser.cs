using System;
using System.IO;
using System.Text;
using Godot;

namespace CustomRhythia.Util;

/// <summary>
/// Small little-endian binary reader used for the .sspm map format.
/// </summary>
public class FileParser
{
    public byte[] Buffer;
    public int Pointer;
    public long Length;

    public FileParser(string path)
    {
        using Godot.FileAccess f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (f == null) { Buffer = new byte[0]; Length = 0; Pointer = 0; return; }
        Length = (long)f.GetLength();
        Buffer = f.GetBuffer(Length);
        Pointer = 0;
    }

    public FileParser(byte[] buffer)
    {
        Buffer = buffer ?? new byte[0];
        Length = Buffer.Length;
        Pointer = 0;
    }

    private void CheckBounds(int amount)
    {
        if (Pointer + amount > Length)
            throw new EndOfStreamException($"Read {amount} bytes past end (len {Length}).");
    }

    public byte[] Get(int count)
    {
        CheckBounds(count);
        byte[] result = Buffer[Pointer..(Pointer + count)];
        Pointer += count;
        return result;
    }

    public void Skip(int count) => Pointer += count;
    public void Seek(int pointer) => Pointer = pointer;

    public string GetString(int count)
    {
        CheckBounds(count);
        string s = Encoding.UTF8.GetString(Buffer, Pointer, count);
        Pointer += count;
        return s;
    }

    public string GetLine()
    {
        StringBuilder sb = new();
        while (Pointer < Length)
        {
            char c = (char)Buffer[Pointer++];
            if (c == '\n') break;
            if (c != '\r') sb.Append(c);
        }
        return sb.ToString();
    }

    public bool GetBool() { CheckBounds(1); return Buffer[Pointer++] != 0; }
    public byte GetByte() { CheckBounds(1); return Buffer[Pointer++]; }
    public float GetFloat() { CheckBounds(4); float v = BitConverter.ToSingle(Buffer, Pointer); Pointer += 4; return v; }
    public double GetDouble() { CheckBounds(8); double v = BitConverter.ToDouble(Buffer, Pointer); Pointer += 8; return v; }
    public ushort GetUInt16() { CheckBounds(2); ushort v = BitConverter.ToUInt16(Buffer, Pointer); Pointer += 2; return v; }
    public uint GetUInt32() { CheckBounds(4); uint v = BitConverter.ToUInt32(Buffer, Pointer); Pointer += 4; return v; }
    public ulong GetUInt64() { CheckBounds(8); ulong v = BitConverter.ToUInt64(Buffer, Pointer); Pointer += 8; return v; }
}
