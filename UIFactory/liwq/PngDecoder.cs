//#define PNG_FAST    //不进行filter以及crc，可以各提高25%性能
//todo 1、添加ios png格式的支持 
//todo 2、优化filter，把filter跟生成pixel分开

using ICSharpCode.SharpZipLib.Checksums;
using System;
using System.IO;

namespace liwq
{
    /// <remarks>
    /// At the moment the following features are supported:
    /// <para>
    /// <b>Filters:</b> all filters are supported.
    /// </para>
    /// <para>
    /// <b>Pixel formats:</b>
    /// <list type="bullet">
    ///     <item>Greyscale without alpha (8 bit).（ct0）</item>
    ///     <item>RGB (Truecolor) without alpha (8 bit).（ct2）</item>
    ///     <item>Palette Index without alpha (8 bit).（ct3）</item>
    ///     <item>Palette Index with alpha (8 bit).（ct3）</item>
    ///     <item>Greyscale with alpha (8 bit).（ct4）</item>
    ///     <item>RGB (Truecolor) with alpha (8 bit).（ct6）</item>
    /// </list>
    /// </para> 
    /// </remarks>
    public class PngDecoder
    {
        public static bool IsPngFile(byte[] header)
        {
            bool isPng = false;
            if (header.Length >= 8)
            {
                isPng =
                    header[0] == 0x89 &&
                    header[1] == 0x50 && // P
                    header[2] == 0x4E && // N
                    header[3] == 0x47 && // G
                    header[4] == 0x0D && // CR
                    header[5] == 0x0A && // LF
                    header[6] == 0x1A && // EOF
                    header[7] == 0x0A;   // LF
            }
            return isPng;
        }

        /// <summary>Converts byte array to a new array where each value in the original array is represented by a the specified number of bits.</summary>
        /// <param name="bytes">The bytes to convert from. Cannot be null.</param>
        /// <param name="bits">The number of bits per value.</param>
        /// <returns>The resulting byte array. Is never null.</returns>
        public static byte[] BitsToBytes(byte[] bytes, int bits)
        {
            if (bits == 8)
                return bytes;
            else if (bits == 4)
            {
                byte[] result = new byte[bytes.Length * 2];
                for (int i = 0; i < bytes.Length; i++)
                {
                    int offset = i << 1;
                    int value = bytes[i];
                    result[offset + 0] = (byte)((value >> 0) & 15);
                    result[offset + 1] = (byte)((value >> 4) & 15);
                }
                return result;
            }
            else if (bits == 2)
            {
                byte[] result = new byte[bytes.Length * 4];
                for (int i = 0; i < bytes.Length; i++)
                {
                    int offset = i << 2;
                    int value = bytes[i];
                    result[offset + 0] = (byte)((value >> 0) & 3);
                    result[offset + 1] = (byte)((value >> 2) & 3);
                    result[offset + 2] = (byte)((value >> 4) & 3);
                    result[offset + 3] = (byte)((value >> 6) & 3);
                }
                return result;
            }
            if (bits == 1)
            {
                byte[] result = new byte[bytes.Length * 8];
                for (int i = 0; i < bytes.Length; i++)
                {
                    int offset = i << 3;
                    int value = bytes[i];
                    result[offset + 0] = (byte)((value >> 0) & 1);
                    result[offset + 1] = (byte)((value >> 1) & 1);
                    result[offset + 2] = (byte)((value >> 2) & 1);
                    result[offset + 3] = (byte)((value >> 3) & 1);
                    result[offset + 4] = (byte)((value >> 4) & 1);
                    result[offset + 5] = (byte)((value >> 5) & 1);
                    result[offset + 6] = (byte)((value >> 6) & 1);
                    result[offset + 7] = (byte)((value >> 7) & 1);
                }
                return result;
            }
            else
                throw new FormatException("Bit depth is not supported or not valid.");
        }

        #region ChunkTypes
        /// <summary>
        /// The first chunk in a png file.
        /// Can only exists once. Contains common information like the width and the height of the image or the used compression method.
        /// </summary>
        const string PngChunkTypesHeader = "IHDR";

        /// <summary>
        /// The PLTE chunk contains from 1 to 256 palette entries,
        /// each a three byte series in the RGB format.
        /// </summary>
        const string PngChunkTypesPalette = "PLTE";

        /// <summary>
        /// The IDAT chunk contains the actual image data.
        /// The image can contains more than one chunk of this type.
        /// All chunks together are the whole image.
        /// </summary>
        const string PngChunkTypesData = "IDAT";

        /// <summary>
        /// This chunk must appear last. It marks the end of the PNG datastream. 
        /// The chunk's data field is empty. 
        /// </summary>
        const string PngChunkTypesEnd = "IEND";

        /// <summary>
        /// This chunk specifies that the image uses simple transparency: 
        /// either alpha values associated with palette entries (for indexed-color images) 
        /// or a single transparent color (for grayscale and truecolor images). 
        /// </summary>
        const string PngChunkTypesPaletteAlpha = "tRNS";

        /// <summary>
        /// Textual information that the encoder wishes to record with the image can be stored in tEXt chunks.
        /// Each tEXt chunk contains a keyword and a text string.
        /// </summary>
        const string PngChunkTypesText = "tEXt";

        /// <summary>
        /// This chunk specifies the relationship between the image samples and the desired display output intensity.
        /// </summary>
        const string PngChunkTypesGamma = "gAMA";

        /// <summary>
        /// The pHYs chunk specifies the intended pixel size or aspect ratio for display of the image. 
        /// </summary>
        const string PngChunkTypesPhysical = "pHYs";
        #endregion //ChunkTypes

        public class PngChunk
        {
            /// <summary>
            /// An unsigned integer giving the number of bytes in the chunk's data field.
            /// The length counts only the data field, not itself, the chunk type code, or the CRC. Zero is a valid length
            /// </summary>
            public int Length;
            /// <summary>
            /// A chunk type as string with 4 chars.
            /// </summary>
            public string Type;
            /// <summary>
            /// The data bytes appropriate to the chunk type, if any. 
            /// This field can be of zero length. 
            /// </summary>
            public byte[] Data;
            /// <summary>
            /// A CRC (Cyclic Redundancy Check) calculated on the preceding bytes in the chunk, 
            /// including the chunk type code and chunk data fields, but not including the length field. 
            /// The CRC is always present, even for chunks containing no data
            /// </summary>
            public uint Crc;
        }

        #region ReadChunks
        private int readChunkLength(PngChunk chunk)
        {
            byte[] lengthBuffer = new byte[4];
            int count = this.pngStream.Read(lengthBuffer, 0, 4);
            if (count >= 1 && count <= 3) throw new FormatException("Image stream is not valid!");
            Array.Reverse(lengthBuffer);
            chunk.Length = BitConverter.ToInt32(lengthBuffer, 0);
            return count;
        }
        private byte[] readChunkType(PngChunk chunk)
        {
            byte[] typeBuffer = new byte[4];
            int count = this.pngStream.Read(typeBuffer, 0, 4);
            if (count >= 1 && count <= 3) throw new FormatException("Image stream is not valid!");
            char[] chars = new char[4];
            chars[0] = (char)typeBuffer[0];
            chars[1] = (char)typeBuffer[1];
            chars[2] = (char)typeBuffer[2];
            chars[3] = (char)typeBuffer[3];
            chunk.Type = new string(chars);
            return typeBuffer;
        }
        private void readChunkData(PngChunk chunk)
        {
            chunk.Data = new byte[chunk.Length];
            this.pngStream.Read(chunk.Data, 0, chunk.Length);
        }
        private void readChunkCrc(PngChunk chunk, byte[] typeBuffer)
        {
            byte[] crcBuffer = new byte[4];
            int count = this.pngStream.Read(crcBuffer, 0, 4);
            if (count >= 1 && count <= 3) throw new FormatException("Image stream is not valid!");
#if !PNG_FAST
            Array.Reverse(crcBuffer);
            chunk.Crc = BitConverter.ToUInt32(crcBuffer, 0);
            Crc32 crc = new Crc32();
            crc.Update(typeBuffer);
            crc.Update(chunk.Data);
            if (crc.Value != chunk.Crc) throw new FormatException("CRC Error. PNG Image chunk is corrupt!");
#endif
        }
        private PngChunk readChunk()
        {
            PngChunk chunk = new PngChunk();
            if (this.readChunkLength(chunk) == 0) return null;
            byte[] typeBuffer = this.readChunkType(chunk);
            this.readChunkData(chunk);
            this.readChunkCrc(chunk, typeBuffer);
            return chunk;
        }
        private void readHeaderChunk(byte[] data)
        {
            Array.Reverse(data, 0, 4);
            Array.Reverse(data, 4, 4);
            this.Width = BitConverter.ToInt32(data, 0);
            this.Height = BitConverter.ToInt32(data, 4);
            this.BitDepth = data[8];
            this.ColorType = data[9];
            this.FilterMethod = data[11];
            this.InterlaceMethod = data[12];
            this.CompressionMethod = data[10];
        }
        #endregion //ReadChunks

        private void grayscaleReadScanline(byte[] scanline, byte[] pixels, ref int row, bool hasAlpha)
        {
            int offset = 0;
            byte[] newScanline = BitsToBytes(scanline, this.BitDepth);
            if (hasAlpha)
            {
                for (int x = 0; x < this.Width / 2; x++)
                {
                    offset = (row * this.Width + x) * 4;
                    pixels[offset + 0] = newScanline[x * 2];
                    pixels[offset + 1] = newScanline[x * 2];
                    pixels[offset + 2] = newScanline[x * 2];
                    pixels[offset + 3] = newScanline[x * 2 + 1];
                }
            }
            else
            {
                for (int x = 0; x < this.Width; x++)
                {
                    offset = (row * this.Width + x) * 4;
                    pixels[offset + 0] = newScanline[x];
                    pixels[offset + 1] = newScanline[x];
                    pixels[offset + 2] = newScanline[x];
                    pixels[offset + 3] = (byte)255;
                }
            }
            row++;
        }

        private void paletteIndexReadScanline(byte[] scanline, byte[] pixels, ref int row, byte[] palette, byte[] paletteAlpha)
        {
            byte[] newScanline = BitsToBytes(scanline, this.BitDepth);
            int offset = 0, index = 0;
            if (paletteAlpha != null && paletteAlpha.Length > 0)
            {
                // If the alpha palette is not null and does one or more entries,
                // this means, that the image contains and alpha channel and we should try to read it.
                for (int i = 0; i < this.Width; i++)
                {
                    index = newScanline[i];
                    offset = (row * this.Width + i) * 4;
                    pixels[offset + 0] = palette[index * 3];
                    pixels[offset + 1] = palette[index * 3 + 1];
                    pixels[offset + 2] = palette[index * 3 + 2];
                    pixels[offset + 3] = paletteAlpha.Length > index ? paletteAlpha[index] : (byte)255;
                }
            }
            else
            {
                offset = row * this.Width * 4;
                for (int i = 0; i < this.Width; i++)
                {
                    index = newScanline[i] * 3;
                    pixels[offset + 0] = palette[index];
                    pixels[offset + 1] = palette[index + 1];
                    pixels[offset + 2] = palette[index + 2];
                    pixels[offset + 3] = 255;
                    offset += 4;
                }
            }
            row++;
        }

        private void trueColorReadScanline(byte[] scanline, byte[] pixels, ref int row, bool hasAlpha)
        {
            byte[] newScanline = BitsToBytes(scanline, this.BitDepth);
            if (hasAlpha)
            {
                Array.Copy(newScanline, 0, pixels, row * this.Width * 4, newScanline.Length);
            }
            else
            {
                int offset = row * this.Width * 4;
                int lastOffset = offset + (newScanline.Length / 3) * 4;
                int index = 0;
                while (offset < lastOffset)
                {
                    pixels[offset + 0] = newScanline[index];
                    pixels[offset + 1] = newScanline[index + 1];
                    pixels[offset + 2] = newScanline[index + 2];
                    pixels[offset + 3] = 255;
                    offset += 4;
                    index += 3;
                }
            }
            row++;
        }

        #region Property

        /// <summary>
        /// The dimension in x-direction of the image in pixels.
        /// </summary>
        public int Width { get; private set; }
        /// <summary>
        /// The dimension in y-direction of the image in pixels.
        /// </summary>
        public int Height { get; private set; }
        /// <summary>
        /// Bit depth is a single-byte integer giving the number of bits per sample or per palette index (not per pixel). 
        /// Valid values are 1, 2, 4, 8, and 16, although not all values are allowed for all color types. 
        /// </summary>
        public byte BitDepth { get; private set; }
        /// <summary>
        /// Color type is a integer that describes the interpretation of the image data.
        /// Color type codes represent sums of the following values: 
        /// 1 (palette used), 2 (color used), and 4 (alpha channel used).
        /// </summary>
        public byte ColorType { get; private set; }
        /// <summary>
        /// Indicates the method  used to compress the image data. At present, 
        /// only compression method 0 (deflate/inflate compression with a sliding 
        /// window of at most 32768 bytes) is defined.
        /// </summary>
        public byte CompressionMethod { get; private set; }
        /// <summary>
        /// Indicates the preprocessing method applied to the image data before compression.
        /// At present, only filter method 0 (adaptive filtering with five basic filter types) is defined.
        /// </summary>
        public byte FilterMethod { get; private set; }
        /// <summary>
        /// Indicates the transmission order of the image data. 
        /// Two values are currently defined: 0 (no interlace) or 1 (Adam7 interlace).
        /// </summary>
        public byte InterlaceMethod { get; private set; }

        #endregion //Property


        private Stream pngStream;
        public byte[] Decode(Stream stream)
        {
            this.pngStream = stream;
            this.pngStream.Seek(8, SeekOrigin.Current);
            bool isEndChunckReached = false;
            PngChunk currentChunk = null;
            byte[] palette = null;
            byte[] paletteAlpha = null;
            int readScanlineRow = 0;
            using (MemoryStream dataStream = new MemoryStream())
            {
                while ((currentChunk = this.readChunk()) != null)
                {
                    if (isEndChunckReached)
                        throw new FormatException("Image does not end with end chunk.");

                    if (currentChunk.Type == PngChunkTypesHeader)
                    {
                        this.readHeaderChunk(currentChunk.Data);
                        if (this.ColorType != 0 && this.ColorType != 2 && this.ColorType != 3 && this.ColorType != 4 && this.ColorType != 6) throw new FormatException("Color type is not supported or not valid.");
                        if (this.ColorType == 0) if (this.BitDepth != 1 && this.BitDepth != 2 && this.BitDepth != 4 && this.BitDepth != 8) throw new FormatException("Bit depth is not supported or not valid.");
                        if (this.ColorType == 2) if (this.BitDepth != 8) throw new FormatException("Bit depth is not supported or not valid.");
                        if (this.ColorType == 3) if (this.BitDepth != 1 && this.BitDepth != 2 && this.BitDepth != 4 && this.BitDepth != 8) throw new FormatException("Bit depth is not supported or not valid.");
                        if (this.ColorType == 4) if (this.BitDepth != 8) throw new FormatException("Bit depth is not supported or not valid.");
                        if (this.ColorType == 6) if (this.BitDepth != 8) throw new FormatException("Bit depth is not supported or not valid.");
                        if (this.FilterMethod != 0) throw new FormatException("The png specification only defines 0 as filter method.");
                        if (this.InterlaceMethod != 0) throw new FormatException("Interlacing is not supported.");
                    }
                    else if (currentChunk.Type == PngChunkTypesData)
                    {
                        dataStream.Write(currentChunk.Data, 0, currentChunk.Data.Length);   //这里的数据可以是多块
                    }
                    else if (currentChunk.Type == PngChunkTypesPalette)
                    {
                        palette = currentChunk.Data;
                    }
                    else if (currentChunk.Type == PngChunkTypesPaletteAlpha)
                    {
                        paletteAlpha = currentChunk.Data;
                    }
                    else if (currentChunk.Type == PngChunkTypesEnd)
                    {
                        isEndChunckReached = true;
                    }
                    else if (currentChunk.Type == PngChunkTypesPhysical) { }
                    else if (currentChunk.Type == PngChunkTypesText) { }
                }

                byte[] pixels = new byte[this.Width * this.Height * 4];
                this.readScanlines(dataStream, pixels, ref readScanlineRow, palette, paletteAlpha);
                return pixels;
            }
        }

        private static byte paethPredicator(byte a, byte b, byte c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a);
            int pb = Math.Abs(p - b);
            int pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc) return a;
            else if (pb <= pc) return b;
            else return c;
        }

        private void readScanlines(MemoryStream dataStream, byte[] pixels, ref int readScanlineRow, byte[] palette, byte[] paletteAlpha)
        {
            //ColorType 0: grayscale - alpha
            //ColorType 2: trueColor - alpha
            //ColorType 3: paletteIndex
            //ColorType 4: grayscale + alpha
            //ColorType 6: trueColor + alpha

            int channelsPerColor = 0;
            switch (this.ColorType)
            {
                case 0: channelsPerColor = 1; break;
                case 2: channelsPerColor = 3; break;
                case 3: channelsPerColor = 1; break;
                case 4: channelsPerColor = 2; break;
                case 6: channelsPerColor = 4; break;
            }

            //calculateScanlineLength
            int scanlineLength = (this.Width * this.BitDepth * channelsPerColor);
            int amount = scanlineLength % 8;
            if (amount != 0) scanlineLength += 8 - amount;
            scanlineLength = scanlineLength / 8;

            //calculateScanlineStep
            int scanlineStep = 1;
            if (this.BitDepth >= 8)
                scanlineStep = (channelsPerColor * this.BitDepth) / 8;

            dataStream.Position = 0;
            byte[] lastScanline = new byte[scanlineLength];
            byte[] currScanline = new byte[scanlineLength];
            byte a = 0;
            byte b = 0;
            byte c = 0;
            int row = 0, filter = 0, column = -1;


            int bpl = (this.Width * this.BitDepth + 7) / 8;     // bytes per line, per component
            int unzipSize = bpl * this.Height * channelsPerColor /* pixels */ + this.Height /* filter mode per row */;
            byte[] zipBuffer = dataStream.GetBuffer();
            byte[] unzipBuffer = ZipDecoder.zlib_decode(zipBuffer, zipBuffer.Length, unzipSize, ref unzipSize);
            int unzipBufferIndex = 0;
            //using (InflaterInputStream compressedStream = new InflaterInputStream(dataStream))    //sharpziplib 6x slow!!
            {
                int readByte = 0;
                while (unzipBufferIndex < unzipBuffer.Length)
                {
                    readByte = unzipBuffer[unzipBufferIndex++];
                    if (column == -1)
                    {
                        filter = readByte;
                        column++;
                    }
                    else
                    {
                        currScanline[column] = (byte)readByte;
                        if (column >= scanlineStep)
                        {
                            a = currScanline[column - scanlineStep];
                            c = lastScanline[column - scanlineStep];
                        }
                        else
                        {
                            a = 0;
                            c = 0;
                        }
                        b = lastScanline[column];

#if !PNG_FAST
                        if (filter == 1) { currScanline[column] = (byte)(currScanline[column] + a); }
                        else if (filter == 2) { currScanline[column] = (byte)(currScanline[column] + b); }
                        else if (filter == 3) { currScanline[column] = (byte)(currScanline[column] + (byte)Math.Floor((double)(a + b) / 2)); }
                        else if (filter == 4) { currScanline[column] = (byte)(currScanline[column] + paethPredicator(a, b, c)); }
#endif

                        column++;
                        if (column == scanlineLength)
                        {
                            switch (this.ColorType)
                            {
                                case 0: grayscaleReadScanline(currScanline, pixels, ref readScanlineRow, false); break;
                                case 2: trueColorReadScanline(currScanline, pixels, ref readScanlineRow, false); break;
                                case 3: paletteIndexReadScanline(currScanline, pixels, ref readScanlineRow, palette, paletteAlpha); break;
                                case 4: grayscaleReadScanline(currScanline, pixels, ref readScanlineRow, true); break;
                                case 6: trueColorReadScanline(currScanline, pixels, ref readScanlineRow, true); break;
                            }

                            column = -1;
                            row++;

                            var temp = currScanline;
                            currScanline = lastScanline;
                            lastScanline = temp;
                        }
                    }
                }
            }
        }

    }
}