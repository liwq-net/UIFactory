using System;
using System.Diagnostics;

namespace liwq
{
    public class ZipDecoder
    {
        // fast-way is faster to check than jpeg huffman, but slow way is slower
        const int ZFAST_BITS = 9; // accelerate all cases in default tables
        const int ZFAST_MASK = ((1 << ZFAST_BITS) - 1);

        static byte[] realloc(ref byte[] p, int sz)
        {
            byte[] newbufer = new byte[sz];
            Array.Copy(p, newbufer, p.Length);
            p = newbufer;
            return p;
        }

        internal class zhuffman
        {
            public ushort[] fast = new ushort[1 << ZFAST_BITS];
            public ushort[] firstcode = new ushort[16];
            public int[] maxcode = new int[17];
            public ushort[] firstsymbol = new ushort[16];
            public byte[] size = new byte[288];
            public ushort[] value = new ushort[288];
        }

        internal class zbuf
        {
            public byte[] zbuffer;
            public int pzbuffer;
            public int pzbuffer_end;

            public int num_bits;
            public uint code_buffer;

            public byte[] zout_start;
            public int pzout;
            public int pzout_start;
            public int pzout_end;

            public int z_expandable;
            public zhuffman z_length = new zhuffman();
            public zhuffman z_distance = new zhuffman();
        }

        static byte zget8(zbuf z)
        {
            if (z.pzbuffer >= z.pzbuffer_end) return 0;
            return z.zbuffer[z.pzbuffer++];
        }

        static int parse_zlib_header(zbuf a)
        {
            int cmf = zget8(a);
            int cm = cmf & 15;
            /* int cinfo = cmf >> 4; */
            int flg = zget8(a);
            if ((cmf * 256 + flg) % 31 != 0) throw new Exception("bad zlib header,Corrupt PNG");// zlib spec
            if ((flg & 32) != 0) throw new Exception("no preset dict,Corrupt PNG");             // preset dictionary not allowed in png
            if (cm != 8) throw new Exception("bad compression,Corrupt PNG");                    // DEFLATE required for png
            // window = 1 << (8 + cinfo)... but who cares, we fully buffer output
            return 1;
        }

        static void fill_bits(zbuf z)
        {
            do
            {
                Debug.Assert(z.code_buffer < (1U << z.num_bits));
                z.code_buffer |= (uint)(zget8(z) << z.num_bits);
                z.num_bits += 8;
            } while (z.num_bits <= 24);
        }

        static uint zreceive(zbuf z, int n)
        {
            uint k;
            if (z.num_bits < n) fill_bits(z);
            k = z.code_buffer & (uint)((1 << n) - 1);
            z.code_buffer >>= n;
            z.num_bits -= n;
            return k;
        }

        static int zexpand(zbuf z, int zout, int n)  // need to make room for n bytes
        {
            byte[] q;
            int cur, limit;
            z.pzout = zout;
            if (z.z_expandable == 0) throw new Exception("output buffer limit,Corrupt PNG");
            cur = z.pzout - z.pzout_start;
            limit = z.pzout_end - z.pzout_start;
            while (cur + n > limit)
                limit *= 2;
            q = realloc(ref z.zout_start, limit);
            if (q == null) throw new Exception("Out of memory");
            z.pzout_start = 0;
            z.pzout = cur;
            z.pzout_end = limit;
            return 1;
        }

        static int parse_uncomperssed_block(zbuf a)
        {
            byte[] header = new byte[4];
            int len, nlen, k;
            if ((a.num_bits & 7) != 0)
                zreceive(a, a.num_bits & 7); // discard
            // drain the bit-packed data into header
            k = 0;
            while (a.num_bits > 0)
            {
                header[k++] = (byte)a.code_buffer; // suppress MSVC run-time check
                a.code_buffer >>= 8;
                a.num_bits -= 8;
            }
            Debug.Assert(a.num_bits == 0);
            // now fill header the normal way
            while (k < 4)
                header[k++] = zget8(a);
            len = header[1] * 256 + header[0];
            nlen = header[3] * 256 + header[2];
            if (nlen != (len ^ 0xffff)) throw new Exception("zlib corrupt,Corrupt PNG");
            if (a.pzbuffer + len > a.pzbuffer_end) throw new Exception("read past buffer,Corrupt PNG");
            if (a.pzout + len > a.pzout_end)
                if (zexpand(a, a.pzout, len) == 0) return 0;
            Array.Copy(a.zbuffer, a.pzbuffer, a.zout_start, a.pzout, len);
            a.pzbuffer += len;
            a.pzout += len;
            return 1;
        }

        // @TODO: should statically initialize these for optimal thread safety
        static byte[] zdefault_length = new byte[288];
        static byte[] zdefault_distance = new byte[32];
        static void init_zdefaults()
        {
            int i;   // use <= to match clearly with spec
            for (i = 0; i <= 143; ++i) zdefault_length[i] = 8;
            for (; i <= 255; ++i) zdefault_length[i] = 9;
            for (; i <= 279; ++i) zdefault_length[i] = 7;
            for (; i <= 287; ++i) zdefault_length[i] = 8;
            for (i = 0; i <= 31; ++i) zdefault_distance[i] = 5;
        }

        static int bitreverse16(int n)
        {
            n = ((n & 0xAAAA) >> 1) | ((n & 0x5555) << 1);
            n = ((n & 0xCCCC) >> 2) | ((n & 0x3333) << 2);
            n = ((n & 0xF0F0) >> 4) | ((n & 0x0F0F) << 4);
            n = ((n & 0xFF00) >> 8) | ((n & 0x00FF) << 8);
            return n;
        }

        static int bit_reverse(int v, int bits)
        {
            Debug.Assert(bits <= 16);
            // to bit reverse n bits, reverse 16 and shift
            // e.g. 11 bits, bit reverse and shift away 5
            return bitreverse16(v) >> (16 - bits);
        }

        static int zbuild_huffman(zhuffman z, byte[] sizelist, int num, int sizelistOffset = 0)
        {
            int i, k = 0;
            int code; int[] next_code = new int[16]; int[] sizes = new int[17];

            // DEFLATE spec for generating codes
            Array.Clear(z.fast, 0, z.fast.Length);

            for (i = 0; i < num; ++i)
                ++sizes[sizelist[i + sizelistOffset]];
            sizes[0] = 0;
            for (i = 1; i < 16; ++i)
                Debug.Assert(sizes[i] <= (1 << i));
            code = 0;
            for (i = 1; i < 16; ++i)
            {
                next_code[i] = code;
                z.firstcode[i] = (ushort)code;
                z.firstsymbol[i] = (ushort)k;
                code = (code + sizes[i]);
                if (sizes[i] != 0)
                    if (code - 1 >= (1 << i)) throw new Exception("bad codelengths,Corrupt JPEG");
                z.maxcode[i] = code << (16 - i); // preshift for inner loop
                code <<= 1;
                k += sizes[i];
            }
            z.maxcode[16] = 0x10000; // sentinel
            for (i = 0; i < num; ++i)
            {
                int s = sizelist[i + sizelistOffset];
                if (s != 0)
                {
                    int c = next_code[s] - z.firstcode[s] + z.firstsymbol[s];
                    ushort fastv = (ushort)((s << 9) | i);
                    z.size[c] = (byte)s;
                    z.value[c] = (ushort)i;
                    if (s <= ZFAST_BITS)
                    {
                        int kk = bit_reverse(next_code[s], s);
                        while (kk < (1 << ZFAST_BITS))
                        {
                            z.fast[kk] = fastv;
                            kk += (1 << s);
                        }
                    }
                    ++next_code[s];
                }
            }
            return 1;
        }

        static int zhuffman_decode_slowpath(zbuf a, zhuffman z)
        {
            int b, s, k;
            // not resolved by fast table, so compute it the slow way
            // use jpeg approach, which requires MSbits at top
            k = bit_reverse((int)a.code_buffer, 16);
            for (s = ZFAST_BITS + 1; ; ++s)
                if (k < z.maxcode[s])
                    break;
            if (s == 16) return -1; // invalid code!
            // code size is s, so:
            b = (k >> (16 - s)) - z.firstcode[s] + z.firstsymbol[s];
            Debug.Assert(z.size[b] == s);
            a.code_buffer >>= s;
            a.num_bits -= s;
            return z.value[b];
        }

        static int zhuffman_decode(zbuf a, zhuffman z)
        {
            int b, s;
            if (a.num_bits < 16) fill_bits(a);
            b = z.fast[a.code_buffer & ZFAST_MASK];
            if (b != 0)
            {
                s = b >> 9;
                a.code_buffer >>= s;
                a.num_bits -= s;
                return b & 511;
            }
            return zhuffman_decode_slowpath(a, z);
        }

        static byte[] length_dezigzag = new byte[19] { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 };
        static int compute_huffman_codes(zbuf a)
        {
            zhuffman z_codelength = new zhuffman();
            byte[] lencodes = new byte[286 + 32 + 137];//padding for maximum single op
            byte[] codelength_sizes = new byte[19];
            int i, n;
            int hlit = (int)zreceive(a, 5) + 257;
            int hdist = (int)zreceive(a, 5) + 1;
            int hclen = (int)zreceive(a, 4) + 4;
            for (i = 0; i < hclen; ++i)
            {
                int s = (int)zreceive(a, 3);
                codelength_sizes[length_dezigzag[i]] = (byte)s;
            }
            if (zbuild_huffman(z_codelength, codelength_sizes, 19) == 0) return 0;
            n = 0;
            while (n < hlit + hdist)
            {
                int c = zhuffman_decode(a, z_codelength);
                Debug.Assert(c >= 0 && c < 19);
                if (c < 16)
                    lencodes[n++] = (byte)c;
                else if (c == 16)
                {
                    c = (int)zreceive(a, 2) + 3;
                    {
                        byte value = lencodes[n - 1];
                        int last = n + c;
                        for (int idx = n; idx < last; idx++)
                            lencodes[idx] = value;
                    }
                    n += c;
                }
                else if (c == 17)
                {
                    c = (int)zreceive(a, 3) + 3;
                    Array.Clear(lencodes, n, c);
                    n += c;
                }
                else
                {
                    Debug.Assert(c == 18);
                    c = (int)zreceive(a, 7) + 11;
                    Array.Clear(lencodes, n, c);
                    n += c;
                }
            }
            if (n != hlit + hdist) throw new Exception("bad codelengths,Corrupt PNG");
            if (zbuild_huffman(a.z_length, lencodes, hlit) == 0) return 0;
            if (zbuild_huffman(a.z_distance, lencodes, hdist, hlit) == 0) return 0;
            return 1;
        }

        static int[] zlength_base = new int[31] { 3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258, 0, 0 };
        static int[] zlength_extra = new int[31] { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0, 0, 0 };
        static int[] zdist_base = new int[32] { 1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577, 0, 0 };
        static int[] zdist_extra = new int[32] { 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 0, 0 };

        static int parse_huffman_block(zbuf a)
        {
            int zout = a.pzout;
            for (; ; )
            {
                int z = (int)zhuffman_decode(a, a.z_length);
                if (z < 256)
                {
                    if (z < 0) throw new Exception("bad huffman code,Corrupt PNG"); // error in huffman codes
                    if (zout >= a.pzout_end)
                    {
                        if (zexpand(a, zout, 1) == 0) return 0;
                        zout = a.pzout;
                    }
                    a.zout_start[zout++] = (byte)z;
                }
                else
                {
                    int p;
                    int len, dist;
                    if (z == 256)
                    {
                        a.pzout = zout;
                        return 1;
                    }
                    z -= 257;
                    len = zlength_base[z];
                    if (zlength_extra[z] != 0) len += (int)zreceive(a, zlength_extra[z]);
                    z = zhuffman_decode(a, a.z_distance);
                    if (z < 0) throw new Exception("bad huffman code,Corrupt PNG");
                    dist = zdist_base[z];
                    if (zdist_extra[z] != 0) dist += (int)zreceive(a, zdist_extra[z]);
                    if (zout - a.pzout_start < dist) throw new Exception("bad dist,Corrupt PNG");
                    if (zout + len > a.pzout_end)
                    {
                        if (zexpand(a, zout, len) == 0) return 0;
                        zout = a.pzout;
                    }
                    p = zout - dist;
                    if (dist == 1)
                    { // run of one byte; common in images.
                        do a.zout_start[zout++] = a.zout_start[p]; while ((--len) != 0);    //todo fix
                    }
                    else
                    {
                        do a.zout_start[zout++] = a.zout_start[p++]; while ((--len) != 0);   //todo fix
                    }
                }
            }
        }

        static int parse_zlib(zbuf a, int parse_header)
        {
            int final, type;
            if (parse_header != 0)
                if (parse_zlib_header(a) == 0) return 0;
            a.num_bits = 0;
            a.code_buffer = 0;
            do
            {
                final = (int)zreceive(a, 1);
                type = (int)zreceive(a, 2);
                if (type == 0)
                {
                    if (parse_uncomperssed_block(a) == 0) return 0;
                }
                else if (type == 3)
                {
                    return 0;
                }
                else
                {
                    if (type == 1)
                    {
                        // use fixed code lengths
                        if (zdefault_distance[31] == 0) init_zdefaults();
                        if (zbuild_huffman(a.z_length, zdefault_length, 288) == 0) return 0;
                        if (zbuild_huffman(a.z_distance, zdefault_distance, 32) == 0) return 0;
                    }
                    else
                    {
                        if (compute_huffman_codes(a) == 0) return 0;
                    }
                    if (parse_huffman_block(a) == 0) return 0;
                }
            } while (final == 0);
            return 1;
        }

        static int do_zlib(zbuf a, byte[] obuf, int olen, int exp, int parse_header)
        {
            a.zout_start = obuf;
            a.pzout_start = 0;
            a.pzout = 0;
            a.pzout_end = olen;
            a.z_expandable = exp;
            return parse_zlib(a, parse_header);
        }


        public static int zlib_decode_buffer(byte[] obuffer, int olen, byte[] ibuffer, int ilen)
        {
            zbuf a = new zbuf();
            a.zbuffer = ibuffer;
            a.pzbuffer_end = ilen;
            if (do_zlib(a, obuffer, olen, 0, 1) != 0)
                return (int)(a.pzout - a.pzout_start);
            else
                return -1;
        }

        public static byte[] zlib_decode(byte[] buffer, int len, int initial_size, ref int outlen, int parse_header = 1)
        {
            zbuf a = new zbuf();
            byte[] p = new byte[initial_size];
            if (p == null) return null;
            a.zbuffer = buffer;
            a.pzbuffer = 0;
            a.pzbuffer_end = len;
            if (do_zlib(a, p, initial_size, 1, parse_header) != 0)
            {
                outlen = (int)(a.pzout - a.pzout_start);
                return a.zout_start;
            }
            else
            {
                return null;
            }
        }

    }
}