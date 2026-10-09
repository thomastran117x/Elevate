using System.Buffers.Binary;
using System.Text;

using NetVips;

using VipsImage = NetVips.Image;

namespace backend.tests.Unit.Support;

/// <summary>
/// Image fixtures generated in code, so no binary assets live in the repository. Pixels come from
/// libvips; metadata is spliced in byte by byte, so a fixture carries exactly the EXIF or IPTC a
/// test asks for whatever the library's own metadata writer would do.
/// </summary>
internal static class TestImages
{
    /// <summary>A single-colour sRGB image.</summary>
    public static VipsImage Solid(int width, int height, byte red, byte green, byte blue)
    {
        using var black = VipsImage.Black(width, height, bands: 3);
        using var filled = black + new double[] { red, green, blue };
        return AsSrgb(filled);
    }

    /// <summary>Red on the left half, blue on the right: tells which way an image was turned.</summary>
    public static VipsImage RedLeftBlueRight(int width, int height)
    {
        using var xyz = VipsImage.Xyz(width, height);
        using var x = xyz[0];
        using var left = x < width / 2;
        using var split = left.Ifthenelse(new double[] { 255, 0, 0 }, new double[] { 0, 0, 255 });
        return AsSrgb(split);
    }

    /// <summary>
    /// An image whose three bands are each computed from the pixel position, for fixtures that
    /// need varied pixel data rather than one flat colour.
    /// </summary>
    public static VipsImage Pattern(
        int width,
        int height,
        Func<VipsImage, VipsImage, VipsImage> red,
        Func<VipsImage, VipsImage, VipsImage> green,
        Func<VipsImage, VipsImage, VipsImage> blue)
    {
        using var xyz = VipsImage.Xyz(width, height);
        using var x = xyz[0];
        using var y = xyz[1];
        using var r = red(x, y);
        using var g = green(x, y);
        using var b = blue(x, y);
        using var joined = r.Bandjoin(g, b);
        return AsSrgb(joined);
    }

    /// <summary>Frames stacked vertically with their page height set: libvips' animation layout.</summary>
    public static VipsImage Animation(params VipsImage[] frames)
    {
        using var strip = VipsImage.Arrayjoin(frames, across: 1);
        return strip.Mutate(image => image.Set(GValue.GIntType, "page-height", frames[0].Height));
    }

    public static byte[] Jpeg(VipsImage image) => image.JpegsaveBuffer();

    public static byte[] Png(VipsImage image) => image.PngsaveBuffer();

    public static byte[] Gif(VipsImage image) => image.GifsaveBuffer();

    public static byte[] Webp(VipsImage image) => image.WebpsaveBuffer();

    /// <summary>Encodes in the format a content type names, PNG for anything else.</summary>
    public static byte[] Encode(VipsImage image, string contentType) => contentType switch
    {
        "image/jpeg" => Jpeg(image),
        "image/gif" => Gif(image),
        "image/webp" => Webp(image),
        _ => Png(image)
    };

    /// <summary>A solid image of the given type, animated when <paramref name="frames"/> is above one.</summary>
    public static byte[] Encoded(string contentType, int width, int height, int frames = 1)
    {
        using var first = Solid(width, height, 30, 90, 150);
        if (frames <= 1)
            return Encode(first, contentType);

        var all = new List<VipsImage> { first };
        try
        {
            for (var i = 1; i < frames; i++)
                all.Add(Solid(width, height, 200, 20, 20));

            using var animation = Animation([.. all]);
            return Encode(animation, contentType);
        }
        finally
        {
            foreach (var frame in all.Skip(1))
                frame.Dispose();
        }
    }

    /// <summary>
    /// A JPEG carrying the given EXIF tags in an APP1 segment straight after SOI: orientation,
    /// the <c>Software</c> string, and a GPS latitude of 43°39'12" N.
    /// </summary>
    public static byte[] WithExif(byte[] jpeg, ushort? orientation = null, string? software = null, bool gps = false)
    {
        var payload = new List<byte>("Exif\0\0"u8.ToArray());
        payload.AddRange(Tiff(orientation, software, gps));
        return InsertSegment(jpeg, 0xE1, [.. payload]);
    }

    /// <summary>A JPEG carrying an IPTC by-line in a Photoshop APP13 segment straight after SOI.</summary>
    public static byte[] WithIptcByline(byte[] jpeg, string byline)
    {
        var name = Encoding.ASCII.GetBytes(byline);

        var dataset = new List<byte> { 0x1C, 0x02, 0x50 }; // record 2, dataset 80: By-line
        dataset.AddRange(BigEndian16((ushort)name.Length));
        dataset.AddRange(name);
        if (dataset.Count % 2 != 0)
            dataset.Add(0);

        var segment = new List<byte>("Photoshop 3.0\0"u8.ToArray());
        segment.AddRange("8BIM"u8.ToArray());
        segment.AddRange(BigEndian16(0x0404)); // IPTC-NAA resource
        segment.AddRange([0, 0]);              // empty Pascal name, padded to even
        segment.AddRange(BigEndian32((uint)dataset.Count));
        segment.AddRange(dataset);

        return InsertSegment(jpeg, 0xED, [.. segment]);
    }

    /// <summary>
    /// The chunk types of a RIFF WebP file. Metadata lives in its own <c>EXIF</c>, <c>XMP </c>
    /// and <c>ICCP</c> chunks, so their absence is what "stripped" means at the byte level.
    /// </summary>
    public static IReadOnlyList<string> WebpChunks(byte[] webp)
    {
        var chunks = new List<string>();
        var offset = 12;
        while (offset + 8 <= webp.Length)
        {
            chunks.Add(Encoding.ASCII.GetString(webp, offset, 4));
            var size = BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(offset + 4));
            offset += 8 + (int)size + (int)(size % 2);
        }

        return chunks;
    }

    /// <summary>The metadata fields libvips reports for an encoded image, if any survived.</summary>
    public static IReadOnlyList<string> MetadataFields(byte[] encoded)
    {
        using var image = VipsImage.NewFromBuffer(encoded);
        return new[] { "exif-data", "xmp-data", "icc-profile-data", "iptc-data" }
            .Where(image.Contains)
            .ToList();
    }

    /// <summary>A minimal 1x1, 24-bit BMP: a real format, just not one that is allowed.</summary>
    public static byte[] Bmp()
    {
        var bmp = new byte[58];
        "BM"u8.CopyTo(bmp);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(2), bmp.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), 54); // pixel data offset
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(14), 40); // BITMAPINFOHEADER
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), 1);  // width
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), 1);  // height
        BinaryPrimitives.WriteInt16LittleEndian(bmp.AsSpan(26), 1);  // planes
        BinaryPrimitives.WriteInt16LittleEndian(bmp.AsSpan(28), 24); // bits per pixel
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(34), 4);  // image size, row padded to 4
        return bmp;
    }

    private static VipsImage AsSrgb(VipsImage image)
    {
        using var bytes = image.Cast(Enums.BandFormat.Uchar);
        return bytes.Copy(interpretation: Enums.Interpretation.Srgb);
    }

    /// <summary>A little-endian TIFF structure: IFD0, then the GPS IFD and values it points at.</summary>
    private static byte[] Tiff(ushort? orientation, string? software, bool gps)
    {
        const ushort Short = 3;
        const ushort Ascii = 2;
        const ushort Long = 4;
        const ushort Rational = 5;

        var softwareBytes = software is null ? null : Encoding.ASCII.GetBytes(software + "\0");
        var ifd0Count = (orientation is null ? 0 : 1) + (softwareBytes is null ? 0 : 1) + (gps ? 1 : 0);

        // Layout: header (8) | IFD0 | software string | GPS IFD (2 entries) | latitude rationals.
        var ifd0Size = 2 + (12 * ifd0Count) + 4;
        var softwareOffset = 8 + ifd0Size;
        var gpsIfdOffset = softwareOffset + (softwareBytes?.Length ?? 0);
        const int GpsIfdSize = 2 + (12 * 2) + 4;
        var latitudeOffset = gpsIfdOffset + GpsIfdSize;

        var tiff = new byte[latitudeOffset + (gps ? 24 : 0)];
        "II"u8.CopyTo(tiff);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(4), 8);

        var entry = 8;
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(entry), (ushort)ifd0Count);
        entry += 2;

        void WriteEntry(ushort tag, ushort type, uint count, uint value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(entry), tag);
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(entry + 2), type);
            BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(entry + 4), count);
            BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(entry + 8), value);
            entry += 12;
        }

        // Entries in ascending tag order, as TIFF requires.
        if (orientation is { } value)
            WriteEntry(0x0112, Short, 1, value);
        if (softwareBytes is not null)
        {
            WriteEntry(0x0131, Ascii, (uint)softwareBytes.Length, (uint)softwareOffset);
            softwareBytes.CopyTo(tiff, softwareOffset);
        }

        if (gps)
            WriteEntry(0x8825, Long, 1, (uint)gpsIfdOffset);

        // Next-IFD offset of 0 ends the chain; the array is already zeroed.
        if (!gps)
            return tiff;

        entry = gpsIfdOffset;
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(entry), 2);
        entry += 2;
        WriteEntry(0x0001, Ascii, 2, 'N');                   // GPSLatitudeRef "N\0", inline
        WriteEntry(0x0002, Rational, 3, (uint)latitudeOffset); // GPSLatitude

        uint[] latitude = [43, 1, 39, 1, 12, 1];
        for (var i = 0; i < latitude.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(latitudeOffset + (i * 4)), latitude[i]);

        return tiff;
    }

    private static byte[] InsertSegment(byte[] jpeg, byte marker, byte[] payload)
    {
        if (jpeg.Length < 2 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
            throw new ArgumentException("Not a JPEG.", nameof(jpeg));

        var segment = new List<byte>(payload.Length + 4) { 0xFF, marker };
        segment.AddRange(BigEndian16(checked((ushort)(payload.Length + 2))));
        segment.AddRange(payload);

        return [.. jpeg[..2], .. segment, .. jpeg[2..]];
    }

    private static byte[] BigEndian16(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] BigEndian32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }
}
