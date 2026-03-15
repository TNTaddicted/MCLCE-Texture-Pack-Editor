using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;

namespace LegacyConsolePackEditor;

internal enum SwfBitmapEncoding
{
    Jpeg2,
    Jpeg3,
    LosslessRgb,
    LosslessArgb
}

internal sealed class SwfBitmapEntry : IDisposable
{
    public int CharacterId { get; init; }
    public int TagCode { get; init; }
    public string Name { get; init; } = string.Empty;
    public SwfBitmapEncoding Encoding { get; init; }
    internal int TagIndex { get; init; }

    private Bitmap _bitmap;

    public SwfBitmapEntry(int characterId, int tagCode, string name, SwfBitmapEncoding encoding, int tagIndex, Bitmap bitmap)
    {
        CharacterId = characterId;
        TagCode = tagCode;
        Name = name;
        Encoding = encoding;
        TagIndex = tagIndex;
        _bitmap = bitmap;
    }

    public Bitmap Bitmap => _bitmap;

    public int Width => _bitmap.Width;

    public int Height => _bitmap.Height;

    internal void ReplaceBitmap(Bitmap bitmap)
    {
        _bitmap.Dispose();
        _bitmap = bitmap;
    }

    public override string ToString()
    {
        return $"{Name} ({Width}x{Height})";
    }

    public void Dispose()
    {
        _bitmap.Dispose();
    }
}

internal sealed class SwfBitmapDocument : IDisposable
{
    private sealed class SwfTagRecord
    {
        public required int Code { get; init; }
        public required byte[] Payload { get; set; }
    }

    private readonly byte _version;
    private readonly bool _compressed;
    private readonly byte[] _frameHeaderBytes;
    private readonly List<SwfTagRecord> _tags;
    private readonly List<SwfBitmapEntry> _bitmaps;

    private SwfBitmapDocument(byte version, bool compressed, byte[] frameHeaderBytes, List<SwfTagRecord> tags, List<SwfBitmapEntry> bitmaps)
    {
        _version = version;
        _compressed = compressed;
        _frameHeaderBytes = frameHeaderBytes;
        _tags = tags;
        _bitmaps = bitmaps;
    }

    public IReadOnlyList<SwfBitmapEntry> Bitmaps => _bitmaps;

    public static SwfBitmapDocument? Load(byte[] swfBytes, out string error)
    {
        error = string.Empty;

        if (swfBytes.Length < 8)
        {
            error = "SWF file is too small.";
            return null;
        }

        string signature = System.Text.Encoding.ASCII.GetString(swfBytes, 0, 3);
        bool compressed = signature switch
        {
            "FWS" => false,
            "CWS" => true,
            _ => throw new InvalidDataException("Only FWS and CWS SWF files are supported.")
        };

        try
        {
            byte version = swfBytes[3];
            byte[] bodyBytes = ReadBodyBytes(swfBytes, compressed);
            int frameHeaderLength = GetFrameHeaderLength(bodyBytes);
            if (frameHeaderLength <= 0 || frameHeaderLength > bodyBytes.Length)
                throw new InvalidDataException("SWF frame header is invalid.");

            byte[] frameHeaderBytes = bodyBytes.Take(frameHeaderLength).ToArray();
            var tags = new List<SwfTagRecord>();
            var bitmaps = new List<SwfBitmapEntry>();

            byte[]? jpegTables = null;
            int position = frameHeaderLength;
            while (position + 2 <= bodyBytes.Length)
            {
                ushort tagHeader = BitConverter.ToUInt16(bodyBytes, position);
                position += 2;

                int code = tagHeader >> 6;
                int shortLength = tagHeader & 0x3F;
                int payloadLength = shortLength == 0x3F
                    ? BitConverter.ToInt32(bodyBytes, position)
                    : shortLength;

                if (shortLength == 0x3F)
                    position += 4;

                if (payloadLength < 0 || position + payloadLength > bodyBytes.Length)
                    throw new InvalidDataException($"SWF tag {code} has an invalid length.");

                byte[] payload = new byte[payloadLength];
                Buffer.BlockCopy(bodyBytes, position, payload, 0, payloadLength);
                position += payloadLength;

                tags.Add(new SwfTagRecord
                {
                    Code = code,
                    Payload = payload
                });

                if (code == 8) // JPEGTables — needed to reconstruct tag-6 DefineBits images
                    jpegTables = payload;

                try
                {
                    if (TryDecodeBitmapTag(code, payload, tags.Count - 1, jpegTables, out SwfBitmapEntry? entry) && entry != null)
                        bitmaps.Add(entry);
                }
                catch
                {
                    // Skip malformed tags so the rest of the file still loads.
                }

                if (code == 0)
                    break;
            }

            return new SwfBitmapDocument(version, compressed, frameHeaderBytes, tags, bitmaps);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    public void UpdateBitmap(int characterId, Bitmap bitmap)
    {
        SwfBitmapEntry? entry = _bitmaps.FirstOrDefault(item => item.CharacterId == characterId);
        if (entry == null)
            throw new InvalidOperationException($"SWF bitmap {characterId} was not found.");

        Bitmap replacement = CreateArgbBitmap(bitmap);
        byte[] payload = EncodePayload(entry, replacement);

        _tags[entry.TagIndex].Payload = payload;
        entry.ReplaceBitmap(replacement);
    }

    public byte[] ToByteArray()
    {
        using var bodyStream = new MemoryStream();
        bodyStream.Write(_frameHeaderBytes, 0, _frameHeaderBytes.Length);

        foreach (SwfTagRecord tag in _tags)
            WriteTag(bodyStream, tag.Code, tag.Payload);

        byte[] bodyBytes = bodyStream.ToArray();
        byte[] payloadBytes = _compressed ? CompressBody(bodyBytes) : bodyBytes;
        int fileLength = 8 + payloadBytes.Length;

        using var output = new MemoryStream();
        byte[] signature = System.Text.Encoding.ASCII.GetBytes(_compressed ? "CWS" : "FWS");
        output.Write(signature, 0, signature.Length);
        output.WriteByte(_version);
        output.Write(BitConverter.GetBytes(fileLength), 0, 4);
        output.Write(payloadBytes, 0, payloadBytes.Length);
        return output.ToArray();
    }

    public void Dispose()
    {
        foreach (SwfBitmapEntry bitmap in _bitmaps)
            bitmap.Dispose();
    }

    private static byte[] ReadBodyBytes(byte[] swfBytes, bool compressed)
    {
        byte[] rawBody = swfBytes.Skip(8).ToArray();
        if (!compressed)
            return rawBody;

        using var input = new MemoryStream(rawBody, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] CompressBody(byte[] bodyBytes)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(bodyBytes, 0, bodyBytes.Length);
        }

        return output.ToArray();
    }

    private static int GetFrameHeaderLength(byte[] bodyBytes)
    {
        if (bodyBytes.Length < 6)
            return -1;

        int rectNBits = bodyBytes[0] >> 3;
        int rectBits = 5 + (rectNBits * 4);
        int rectBytes = (rectBits + 7) / 8;
        return rectBytes + 4;
    }

    private static void WriteTag(Stream output, int code, byte[] payload)
    {
        if (payload.Length < 0x3F)
        {
            ushort header = (ushort)((code << 6) | payload.Length);
            output.Write(BitConverter.GetBytes(header), 0, 2);
        }
        else
        {
            ushort header = (ushort)((code << 6) | 0x3F);
            output.Write(BitConverter.GetBytes(header), 0, 2);
            output.Write(BitConverter.GetBytes(payload.Length), 0, 4);
        }

        output.Write(payload, 0, payload.Length);
    }

    private static bool TryDecodeBitmapTag(int code, byte[] payload, int tagIndex, byte[]? jpegTables, out SwfBitmapEntry? entry)
    {
        entry = null;

        if (payload.Length < 2)
            return false;

        return code switch
        {
            6  => TryDecodeDefineBits(payload, jpegTables, tagIndex, out entry), // DefineBits (legacy, needs JPEGTables)
            21 => TryDecodeJpeg2(payload, tagIndex, out entry),           // DefineBitsJPEG2
            35 => TryDecodeJpeg3(payload, tagIndex, out entry),           // DefineBitsJPEG3
            90 => TryDecodeJpeg3(payload, tagIndex, out entry),           // DefineBitsJPEG4
            20 => TryDecodeLossless(payload, tagIndex, hasAlpha: false, out entry),
            36 => TryDecodeLossless(payload, tagIndex, hasAlpha: true, out entry),
            _ => false
        };
    }

    private static bool TryDecodeDefineBits(byte[] payload, byte[]? jpegTables, int tagIndex, out SwfBitmapEntry? entry)
    {
        entry = null;
        if (payload.Length <= 2)
            return false;

        int characterId = BitConverter.ToUInt16(payload, 0);
        byte[] imageBytes;

        if (jpegTables != null && jpegTables.Length >= 2)
        {
            // Reconstruct full JPEG: tables (strip trailing FFD9 EOI) + image scan (strip leading FFD8 SOI)
            using var combined = new MemoryStream();
            int tablesLen = jpegTables.Length;
            if (tablesLen >= 2 && jpegTables[tablesLen - 2] == 0xFF && jpegTables[tablesLen - 1] == 0xD9)
                tablesLen -= 2;
            combined.Write(jpegTables, 0, tablesLen);

            int imgStart = 2; // skip CharacterID bytes
            if (payload.Length > imgStart + 1 && payload[imgStart] == 0xFF && payload[imgStart + 1] == 0xD8)
                imgStart += 2; // drop SOI — tables already supply it
            combined.Write(payload, imgStart, payload.Length - imgStart);
            imageBytes = combined.ToArray();
        }
        else
        {
            imageBytes = payload.Skip(2).ToArray();
        }

        Bitmap bitmap = DecodeJpegBitmap(imageBytes);
        entry = new SwfBitmapEntry(characterId, 21, $"JPEG {characterId}", SwfBitmapEncoding.Jpeg2, tagIndex, bitmap);
        return true;
    }

    private static bool TryDecodeJpeg2(byte[] payload, int tagIndex, out SwfBitmapEntry? entry)
    {
        entry = null;
        if (payload.Length <= 2)
            return false;

        int characterId = BitConverter.ToUInt16(payload, 0);
        byte[] imageBytes = payload.Skip(2).ToArray();
        Bitmap bitmap = DecodeJpegBitmap(imageBytes);

        entry = new SwfBitmapEntry(characterId, 21, $"JPEG {characterId}", SwfBitmapEncoding.Jpeg2, tagIndex, bitmap);
        return true;
    }

    private static bool TryDecodeJpeg3(byte[] payload, int tagIndex, out SwfBitmapEntry? entry)
    {
        entry = null;
        if (payload.Length <= 6)
            return false;

        int characterId = BitConverter.ToUInt16(payload, 0);
        int alphaOffset = BitConverter.ToInt32(payload, 2);
        if (alphaOffset < 0 || 6 + alphaOffset > payload.Length)
            return false;

        byte[] jpegBytes = payload.Skip(6).Take(alphaOffset).ToArray();
        byte[] alphaBytes = payload.Skip(6 + alphaOffset).ToArray();
        Bitmap bitmap = DecodeJpegBitmap(jpegBytes);
        Bitmap composed = ApplyAlpha(bitmap, alphaBytes);
        bitmap.Dispose();

        entry = new SwfBitmapEntry(characterId, 35, $"JPEG+Alpha {characterId}", SwfBitmapEncoding.Jpeg3, tagIndex, composed);
        return true;
    }

    private static bool TryDecodeLossless(byte[] payload, int tagIndex, bool hasAlpha, out SwfBitmapEntry? entry)
    {
        entry = null;
        if (payload.Length <= 7)
            return false;

        int characterId = BitConverter.ToUInt16(payload, 0);
        byte format = payload[2];
        int width = BitConverter.ToUInt16(payload, 3);
        int height = BitConverter.ToUInt16(payload, 5);

        if (width <= 0 || height <= 0)
            return false;

        int tagCode = hasAlpha ? 36 : 20;
        string name = $"Lossless {characterId}";

        switch (format)
        {
            case 3: // 8-bit indexed color with palette
            {
                if (payload.Length < 8)
                    return false;
                int colorTableSize = payload[7] + 1;
                byte[] rawData = DecompressZlib(payload.Skip(8).ToArray());
                Bitmap bitmap = DecodeLosslessIndexedBitmap(rawData, width, height, colorTableSize, hasAlpha);
                entry = new SwfBitmapEntry(characterId, tagCode, name,
                    hasAlpha ? SwfBitmapEncoding.LosslessArgb : SwfBitmapEncoding.LosslessRgb, tagIndex, bitmap);
                return true;
            }
            case 4: // 15-bit RGB (only valid for tag 20)
            {
                byte[] rawData = DecompressZlib(payload.Skip(7).ToArray());
                Bitmap bitmap = DecodeLosslessRgb15Bitmap(rawData, width, height);
                entry = new SwfBitmapEntry(characterId, tagCode, name,
                    SwfBitmapEncoding.LosslessRgb, tagIndex, bitmap);
                return true;
            }
            case 5: // 32-bit: pad+RGB for tag 20, premultiplied ARGB for tag 36
            {
                byte[] rawData = DecompressZlib(payload.Skip(7).ToArray());
                Bitmap bitmap = hasAlpha
                    ? DecodeLosslessArgbBitmap(rawData, width, height)
                    : DecodeLosslessRgbBitmap(rawData, width, height);
                entry = new SwfBitmapEntry(characterId, tagCode, name,
                    hasAlpha ? SwfBitmapEncoding.LosslessArgb : SwfBitmapEncoding.LosslessRgb, tagIndex, bitmap);
                return true;
            }
            default:
                return false;
        }
    }

    private static byte[] EncodePayload(SwfBitmapEntry entry, Bitmap bitmap)
    {
        return entry.Encoding switch
        {
            SwfBitmapEncoding.Jpeg2 => EncodeJpeg2Payload(entry.CharacterId, bitmap),
            SwfBitmapEncoding.Jpeg3 => EncodeJpeg3Payload(entry.CharacterId, bitmap),
            SwfBitmapEncoding.LosslessRgb => EncodeLosslessPayload(entry.CharacterId, bitmap, hasAlpha: false),
            SwfBitmapEncoding.LosslessArgb => EncodeLosslessPayload(entry.CharacterId, bitmap, hasAlpha: true),
            _ => throw new InvalidOperationException($"Unsupported SWF bitmap encoding: {entry.Encoding}")
        };
    }

    private static byte[] EncodeJpeg2Payload(int characterId, Bitmap bitmap)
    {
        byte[] jpegBytes = EncodeJpeg(bitmap, flattenAlpha: true);
        using var output = new MemoryStream();
        output.Write(BitConverter.GetBytes((ushort)characterId), 0, 2);
        output.Write(jpegBytes, 0, jpegBytes.Length);
        return output.ToArray();
    }

    private static byte[] EncodeJpeg3Payload(int characterId, Bitmap bitmap)
    {
        byte[] jpegBytes = EncodeJpeg(bitmap, flattenAlpha: true);
        byte[] alphaBytes = ExtractAlphaPlane(bitmap);
        byte[] compressedAlpha = CompressZlib(alphaBytes);

        using var output = new MemoryStream();
        output.Write(BitConverter.GetBytes((ushort)characterId), 0, 2);
        output.Write(BitConverter.GetBytes(jpegBytes.Length), 0, 4);
        output.Write(jpegBytes, 0, jpegBytes.Length);
        output.Write(compressedAlpha, 0, compressedAlpha.Length);
        return output.ToArray();
    }

    private static byte[] EncodeLosslessPayload(int characterId, Bitmap bitmap, bool hasAlpha)
    {
        Bitmap argbBitmap = CreateArgbBitmap(bitmap);
        byte[] rawBytes = hasAlpha
            ? BuildLosslessArgbBytes(argbBitmap)
            : BuildLosslessRgbBytes(argbBitmap);

        byte[] compressed = CompressZlib(rawBytes);
        using var output = new MemoryStream();
        output.Write(BitConverter.GetBytes((ushort)characterId), 0, 2);
        output.WriteByte(5);
        output.Write(BitConverter.GetBytes((ushort)argbBitmap.Width), 0, 2);
        output.Write(BitConverter.GetBytes((ushort)argbBitmap.Height), 0, 2);
        output.Write(compressed, 0, compressed.Length);
        return output.ToArray();
    }

    private static Bitmap DecodeJpegBitmap(byte[] jpegBytes)
    {
        byte[] normalized = NormalizeJpegBytes(jpegBytes);
        using var stream = new MemoryStream(normalized, writable: false);
        using var source = Image.FromStream(stream);
        return CreateArgbBitmap(new Bitmap(source));
    }

    private static Bitmap ApplyAlpha(Bitmap sourceBitmap, byte[] compressedAlpha)
    {
        byte[] alphaBytes = DecompressZlib(compressedAlpha);
        int expectedLength = sourceBitmap.Width * sourceBitmap.Height;
        if (alphaBytes.Length < expectedLength)
            throw new InvalidDataException("SWF JPEG alpha channel is truncated.");

        Bitmap result = CreateArgbBitmap(sourceBitmap);
        Rectangle rect = new Rectangle(0, 0, result.Width, result.Height);
        BitmapData data = result.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

        try
        {
            int stride = data.Stride;
            byte[] pixelBytes = new byte[stride * data.Height];
            Marshal.Copy(data.Scan0, pixelBytes, 0, pixelBytes.Length);

            int alphaIndex = 0;
            for (int y = 0; y < data.Height; y++)
            {
                int rowOffset = y * stride;
                for (int x = 0; x < data.Width; x++)
                {
                    pixelBytes[rowOffset + (x * 4) + 3] = alphaBytes[alphaIndex++];
                }
            }

            Marshal.Copy(pixelBytes, 0, data.Scan0, pixelBytes.Length);
        }
        finally
        {
            result.UnlockBits(data);
        }

        return result;
    }

    private static Bitmap DecodeLosslessArgbBitmap(byte[] rawData, int width, int height)
    {
        int expectedLength = width * height * 4;
        if (rawData.Length < expectedLength)
            throw new InvalidDataException("SWF lossless ARGB bitmap is truncated.");

        Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        Rectangle rect = new Rectangle(0, 0, width, height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            int stride = data.Stride;
            byte[] pixelBytes = new byte[stride * height];
            int sourceOffset = 0;

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * stride;
                for (int x = 0; x < width; x++)
                {
                    byte a = rawData[sourceOffset++];
                    byte r_pm = rawData[sourceOffset++];
                    byte g_pm = rawData[sourceOffset++];
                    byte b_pm = rawData[sourceOffset++];

                    // SWF stores premultiplied alpha — convert to straight alpha
                    byte r = a > 0 ? (byte)Math.Min(255, r_pm * 255 / a) : (byte)0;
                    byte g = a > 0 ? (byte)Math.Min(255, g_pm * 255 / a) : (byte)0;
                    byte b = a > 0 ? (byte)Math.Min(255, b_pm * 255 / a) : (byte)0;

                    pixelBytes[rowOffset + (x * 4)] = b;
                    pixelBytes[rowOffset + (x * 4) + 1] = g;
                    pixelBytes[rowOffset + (x * 4) + 2] = r;
                    pixelBytes[rowOffset + (x * 4) + 3] = a;
                }
            }

            Marshal.Copy(pixelBytes, 0, data.Scan0, pixelBytes.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    private static Bitmap DecodeLosslessRgbBitmap(byte[] rawData, int width, int height)
    {
        int expectedLength = width * height * 4;
        if (rawData.Length < expectedLength)
            throw new InvalidDataException("SWF lossless RGB bitmap is truncated.");

        Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        Rectangle rect = new Rectangle(0, 0, width, height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            int stride = data.Stride;
            byte[] pixelBytes = new byte[stride * height];
            int sourceOffset = 0;

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * stride;
                for (int x = 0; x < width; x++)
                {
                    sourceOffset++;
                    byte r = rawData[sourceOffset++];
                    byte g = rawData[sourceOffset++];
                    byte b = rawData[sourceOffset++];

                    pixelBytes[rowOffset + (x * 4)] = b;
                    pixelBytes[rowOffset + (x * 4) + 1] = g;
                    pixelBytes[rowOffset + (x * 4) + 2] = r;
                    pixelBytes[rowOffset + (x * 4) + 3] = 255;
                }
            }

            Marshal.Copy(pixelBytes, 0, data.Scan0, pixelBytes.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    private static Bitmap DecodeLosslessIndexedBitmap(byte[] rawData, int width, int height, int colorTableSize, bool hasAlpha)
    {
        int bytesPerEntry = hasAlpha ? 4 : 3;
        int paletteBytes  = colorTableSize * bytesPerEntry;
        int rowStride     = ((width + 3) / 4) * 4; // each row padded to 4-byte boundary

        if (rawData.Length < paletteBytes + rowStride * height)
            throw new InvalidDataException("SWF indexed bitmap data is truncated.");

        // Read palette
        Color[] palette = new Color[colorTableSize];
        int po = 0;
        for (int i = 0; i < colorTableSize; i++)
        {
            if (hasAlpha)
            {
                byte a    = rawData[po++];
                byte r_pm = rawData[po++];
                byte g_pm = rawData[po++];
                byte b_pm = rawData[po++];
                byte r = a > 0 ? (byte)Math.Min(255, r_pm * 255 / a) : (byte)0;
                byte g = a > 0 ? (byte)Math.Min(255, g_pm * 255 / a) : (byte)0;
                byte b = a > 0 ? (byte)Math.Min(255, b_pm * 255 / a) : (byte)0;
                palette[i] = Color.FromArgb(a, r, g, b);
            }
            else
            {
                byte r = rawData[po++];
                byte g = rawData[po++];
                byte b = rawData[po++];
                palette[i] = Color.FromArgb(255, r, g, b);
            }
        }

        Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        Rectangle rect    = new Rectangle(0, 0, width, height);
        BitmapData bmpData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] pixels = new byte[bmpData.Stride * height];
            for (int y = 0; y < height; y++)
            {
                int dstRow = y * bmpData.Stride;
                int srcRow = paletteBytes + y * rowStride;
                for (int x = 0; x < width; x++)
                {
                    int idx = rawData[srcRow + x];
                    if (idx >= colorTableSize) idx = 0;
                    Color c = palette[idx];
                    pixels[dstRow + x * 4]     = c.B;
                    pixels[dstRow + x * 4 + 1] = c.G;
                    pixels[dstRow + x * 4 + 2] = c.R;
                    pixels[dstRow + x * 4 + 3] = c.A;
                }
            }
            Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(bmpData);
        }
        return bitmap;
    }

    private static Bitmap DecodeLosslessRgb15Bitmap(byte[] rawData, int width, int height)
    {
        // Each pixel: 1 pad bit + 5R + 5G + 5B (little-endian ushort); rows padded to 4-byte boundary
        int rowStride = ((width * 2 + 3) / 4) * 4;
        if (rawData.Length < rowStride * height)
            throw new InvalidDataException("SWF RGB15 bitmap data is truncated.");

        Bitmap bitmap  = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        Rectangle rect = new Rectangle(0, 0, width, height);
        BitmapData bmpData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] pixels = new byte[bmpData.Stride * height];
            for (int y = 0; y < height; y++)
            {
                int dstRow = y * bmpData.Stride;
                int srcRow = y * rowStride;
                for (int x = 0; x < width; x++)
                {
                    ushort pixel = BitConverter.ToUInt16(rawData, srcRow + x * 2);
                    byte r = (byte)(((pixel >> 10) & 0x1F) << 3);
                    byte g = (byte)(((pixel >>  5) & 0x1F) << 3);
                    byte b = (byte)( (pixel        & 0x1F) << 3);
                    pixels[dstRow + x * 4]     = b;
                    pixels[dstRow + x * 4 + 1] = g;
                    pixels[dstRow + x * 4 + 2] = r;
                    pixels[dstRow + x * 4 + 3] = 255;
                }
            }
            Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(bmpData);
        }
        return bitmap;
    }

    private static byte[] BuildLosslessArgbBytes(Bitmap bitmap)
    {
        Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            byte[] pixelBytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, pixelBytes, 0, pixelBytes.Length);
            byte[] output = new byte[bitmap.Width * bitmap.Height * 4];
            int targetOffset = 0;

            for (int y = 0; y < bitmap.Height; y++)
            {
                int rowOffset = y * data.Stride;
                for (int x = 0; x < bitmap.Width; x++)
                {
                    byte b = pixelBytes[rowOffset + (x * 4)];
                    byte g = pixelBytes[rowOffset + (x * 4) + 1];
                    byte r = pixelBytes[rowOffset + (x * 4) + 2];
                    byte a = pixelBytes[rowOffset + (x * 4) + 3];

                    // SWF requires premultiplied alpha — re-premultiply straight-alpha bitmap
                    byte r_pm = (byte)((int)r * a / 255);
                    byte g_pm = (byte)((int)g * a / 255);
                    byte b_pm = (byte)((int)b * a / 255);

                    output[targetOffset++] = a;
                    output[targetOffset++] = r_pm;
                    output[targetOffset++] = g_pm;
                    output[targetOffset++] = b_pm;
                }
            }

            return output;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static byte[] BuildLosslessRgbBytes(Bitmap bitmap)
    {
        Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            byte[] pixelBytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, pixelBytes, 0, pixelBytes.Length);
            byte[] output = new byte[bitmap.Width * bitmap.Height * 4];
            int targetOffset = 0;

            for (int y = 0; y < bitmap.Height; y++)
            {
                int rowOffset = y * data.Stride;
                for (int x = 0; x < bitmap.Width; x++)
                {
                    byte b = pixelBytes[rowOffset + (x * 4)];
                    byte g = pixelBytes[rowOffset + (x * 4) + 1];
                    byte r = pixelBytes[rowOffset + (x * 4) + 2];

                    output[targetOffset++] = 0;
                    output[targetOffset++] = r;
                    output[targetOffset++] = g;
                    output[targetOffset++] = b;
                }
            }

            return output;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static byte[] ExtractAlphaPlane(Bitmap bitmap)
    {
        Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            byte[] pixelBytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, pixelBytes, 0, pixelBytes.Length);
            byte[] alpha = new byte[bitmap.Width * bitmap.Height];
            int alphaOffset = 0;

            for (int y = 0; y < bitmap.Height; y++)
            {
                int rowOffset = y * data.Stride;
                for (int x = 0; x < bitmap.Width; x++)
                    alpha[alphaOffset++] = pixelBytes[rowOffset + (x * 4) + 3];
            }

            return alpha;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static byte[] EncodeJpeg(Bitmap bitmap, bool flattenAlpha)
    {
        using Bitmap source = flattenAlpha ? FlattenBitmap(bitmap) : CreateArgbBitmap(bitmap);
        using var stream = new MemoryStream();
        source.Save(stream, ImageFormat.Jpeg);
        return stream.ToArray();
    }

    private static Bitmap FlattenBitmap(Bitmap bitmap)
    {
        Bitmap flattened = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(flattened);
        g.Clear(Color.Black);
        g.DrawImage(bitmap, 0, 0, bitmap.Width, bitmap.Height);
        return flattened;
    }

    private static Bitmap CreateArgbBitmap(Bitmap bitmap)
    {
        Bitmap argb = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(argb);
        g.DrawImage(bitmap, 0, 0, bitmap.Width, bitmap.Height);
        return argb;
    }

    private static byte[] NormalizeJpegBytes(byte[] jpegBytes)
    {
        if (jpegBytes.Length > 4 &&
            jpegBytes[0] == 0xFF && jpegBytes[1] == 0xD9 &&
            jpegBytes[2] == 0xFF && jpegBytes[3] == 0xD8)
        {
            return jpegBytes.Skip(4).ToArray();
        }

        return jpegBytes;
    }

    private static byte[] DecompressZlib(byte[] compressed)
    {
        using var input = new MemoryStream(compressed, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] CompressZlib(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(raw, 0, raw.Length);
        }

        return output.ToArray();
    }
}