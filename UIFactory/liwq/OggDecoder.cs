using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

#region OggContainerReader
namespace NVorbis.Ogg
{
    /// <summary>
    /// Provides an <see cref="IContainerReader"/> implementation for basic Ogg files.
    /// </summary>
    public class ContainerReader : IContainerReader
    {
        Crc _crc = new Crc();
        BufferedReadStream _stream;
        Dictionary<int, PacketReader> _packetReaders;
        List<int> _disposedStreamSerials;
        long _nextPageOffset;
        int _pageCount;

        byte[] _readBuffer = new byte[65025];   // up to a full page of data (but no more!)

        long _containerBits, _wasteBits;

        /// <summary>
        /// Gets the list of stream serials found in the container so far.
        /// </summary>
        public int[] StreamSerials
        {
            get { return System.Linq.Enumerable.ToArray<int>(_packetReaders.Keys); }
        }

        /// <summary>
        /// Event raised when a new logical stream is found in the container.
        /// </summary>
        public event EventHandler<NewStreamEventArgs> NewStream;

        /// <summary>
        /// Creates a new instance with the specified file.
        /// </summary>
        /// <param name="path">The full path to the file.</param>
        public ContainerReader(string path)
            : this(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read), true)
        {
        }

        /// <summary>
        /// Creates a new instance with the specified stream.  Optionally sets to close the stream when disposed.
        /// </summary>
        /// <param name="stream">The stream to read.</param>
        /// <param name="closeOnDispose"><c>True</c> to close the stream when <see cref="Dispose"/> is called, otherwise <c>False</c>.</param>
        public ContainerReader(Stream stream, bool closeOnDispose)
        {
            _packetReaders = new Dictionary<int, PacketReader>();
            _disposedStreamSerials = new List<int>();

            _stream = (stream as BufferedReadStream) ?? new BufferedReadStream(stream) { CloseBaseStream = closeOnDispose };
        }

        /// <summary>
        /// Initializes the container and finds the first stream.
        /// </summary>
        /// <returns><c>True</c> if a valid logical stream is found, otherwise <c>False</c>.</returns>
        public bool Init()
        {
            _stream.TakeLock();
            try
            {
                return GatherNextPage() != -1;
            }
            finally
            {
                _stream.ReleaseLock();
            }
        }

        /// <summary>
        /// Disposes this instance.
        /// </summary>
        public void Dispose()
        {
            // don't use _packetReaders directly since that'll change the enumeration...
            foreach (var streamSerial in StreamSerials)
            {
                _packetReaders[streamSerial].Dispose();
            }

            _nextPageOffset = 0L;
            _containerBits = 0L;
            _wasteBits = 0L;

            _stream.Dispose();
        }

        /// <summary>
        /// Gets the <see cref="IPacketProvider"/> instance for the specified stream serial.
        /// </summary>
        /// <param name="streamSerial">The stream serial to look for.</param>
        /// <returns>An <see cref="IPacketProvider"/> instance.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The specified stream serial was not found.</exception>
        public IPacketProvider GetStream(int streamSerial)
        {
            PacketReader provider;
            if (!_packetReaders.TryGetValue(streamSerial, out provider))
            {
                throw new ArgumentOutOfRangeException("streamSerial");
            }
            return provider;
        }

        /// <summary>
        /// Finds the next new stream in the container.
        /// </summary>
        /// <returns><c>True</c> if a new stream was found, otherwise <c>False</c>.</returns>
        /// <exception cref="InvalidOperationException"><see cref="CanSeek"/> is <c>False</c>.</exception>
        public bool FindNextStream()
        {
            if (!CanSeek) throw new InvalidOperationException();

            // goes through all the pages until the serial count increases
            var cnt = this._packetReaders.Count;
            while (this._packetReaders.Count == cnt)
            {
                _stream.TakeLock();
                try
                {
                    // acquire & release the lock every pass so we don't block any longer than necessary
                    if (GatherNextPage() == -1)
                    {
                        break;
                    }
                }
                finally
                {
                    _stream.ReleaseLock();
                }
            }
            return cnt > this._packetReaders.Count;
        }

        /// <summary>
        /// Gets the number of pages that have been read in the container.
        /// </summary>
        public int PagesRead
        {
            get { return _pageCount; }
        }

        /// <summary>
        /// Retrieves the total number of pages in the container.
        /// </summary>
        /// <returns>The total number of pages.</returns>
        /// <exception cref="InvalidOperationException"><see cref="CanSeek"/> is <c>False</c>.</exception>
        public int GetTotalPageCount()
        {
            if (!CanSeek) throw new InvalidOperationException();

            // just read pages until we can't any more...
            while (true)
            {
                _stream.TakeLock();
                try
                {
                    // acquire & release the lock every pass so we don't block any longer than necessary
                    if (GatherNextPage() == -1)
                    {
                        break;
                    }
                }
                finally
                {
                    _stream.ReleaseLock();
                }
            }

            return _pageCount;
        }

        /// <summary>
        /// Gets whether the container supports seeking.
        /// </summary>
        public bool CanSeek
        {
            get { return _stream.CanSeek; }
        }

        /// <summary>
        /// Gets the number of bits in the container that are not associated with a logical stream.
        /// </summary>
        public long WasteBits
        {
            get { return _wasteBits; }
        }


        // private implmentation bits
        class PageHeader
        {
            public int StreamSerial { get; set; }
            public PageFlags Flags { get; set; }
            public long GranulePosition { get; set; }
            public int SequenceNumber { get; set; }
            public long DataOffset { get; set; }
            public int[] PacketSizes { get; set; }
            public bool LastPacketContinues { get; set; }
            public bool IsResync { get; set; }
        }

        PageHeader ReadPageHeader(long position)
        {
            // set the stream's position
            _stream.Seek(position, SeekOrigin.Begin);

            // header
            // NB: if the stream didn't have an EOS flag, this is the most likely spot for the EOF to be found...
            if (_stream.Read(_readBuffer, 0, 27) != 27) return null;

            // capture signature
            if (_readBuffer[0] != 0x4f || _readBuffer[1] != 0x67 || _readBuffer[2] != 0x67 || _readBuffer[3] != 0x53) return null;

            // check the stream version
            if (_readBuffer[4] != 0) return null;

            // start populating the header
            var hdr = new PageHeader();

            // bit flags
            hdr.Flags = (PageFlags)_readBuffer[5];

            // granulePosition
            hdr.GranulePosition = BitConverter.ToInt64(_readBuffer, 6);

            // stream serial
            hdr.StreamSerial = BitConverter.ToInt32(_readBuffer, 14);

            // sequence number
            hdr.SequenceNumber = BitConverter.ToInt32(_readBuffer, 18);

            // save off the CRC
            var crc = BitConverter.ToUInt32(_readBuffer, 22);

            // start calculating the CRC value for this page
            _crc.Reset();
            for (int i = 0; i < 22; i++)
            {
                _crc.Update(_readBuffer[i]);
            }
            _crc.Update(0);
            _crc.Update(0);
            _crc.Update(0);
            _crc.Update(0);
            _crc.Update(_readBuffer[26]);

            // figure out the length of the page
            var segCnt = (int)_readBuffer[26];
            if (_stream.Read(_readBuffer, 0, segCnt) != segCnt) return null;

            var packetSizes = new List<int>(segCnt);

            int size = 0, idx = 0;
            for (int i = 0; i < segCnt; i++)
            {
                var temp = _readBuffer[i];
                _crc.Update(temp);

                if (idx == packetSizes.Count) packetSizes.Add(0);
                packetSizes[idx] += temp;
                if (temp < 255)
                {
                    ++idx;
                    hdr.LastPacketContinues = false;
                }
                else
                {
                    hdr.LastPacketContinues = true;
                }

                size += temp;
            }
            hdr.PacketSizes = packetSizes.ToArray();
            hdr.DataOffset = position + 27 + segCnt;

            // now we have to go through every byte in the page
            if (_stream.Read(_readBuffer, 0, size) != size) return null;
            for (int i = 0; i < size; i++)
            {
                _crc.Update(_readBuffer[i]);
            }

            if (_crc.Test(crc))
            {
                _containerBits += 8 * (27 + segCnt);
                ++_pageCount;
                return hdr;
            }
            return null;
        }

        PageHeader FindNextPageHeader()
        {
            var startPos = _nextPageOffset;

            var isResync = false;
            PageHeader hdr;
            while ((hdr = ReadPageHeader(startPos)) == null)
            {
                isResync = true;
                _wasteBits += 8;
                _stream.Position = ++startPos;

                var cnt = 0;
                do
                {
                    var b = _stream.ReadByte();
                    if (b == 0x4f)
                    {
                        if (_stream.ReadByte() == 0x67 && _stream.ReadByte() == 0x67 && _stream.ReadByte() == 0x53)
                        {
                            // found it!
                            startPos += cnt;
                            break;
                        }
                        else
                        {
                            _stream.Seek(-3, SeekOrigin.Current);
                        }
                    }
                    else if (b == -1)
                    {
                        return null;
                    }
                    _wasteBits += 8;
                } while (++cnt < 65536);    // we will only search through 64KB of data to find the next sync marker.  if it can't be found, we have a badly corrupted stream.
                if (cnt == 65536) return null;
            }
            hdr.IsResync = isResync;

            _nextPageOffset = hdr.DataOffset;
            for (int i = 0; i < hdr.PacketSizes.Length; i++)
            {
                _nextPageOffset += hdr.PacketSizes[i];
            }

            return hdr;
        }

        bool AddPage(PageHeader hdr)
        {
            // get our packet reader (create one if we have to)
            PacketReader packetReader;
            if (!_packetReaders.TryGetValue(hdr.StreamSerial, out packetReader))
            {
                packetReader = new PacketReader(this, hdr.StreamSerial);
            }

            // save off the container bits
            packetReader.ContainerBits += _containerBits;
            _containerBits = 0;

            // get our flags prepped
            var isContinued = false;
            var isContinuation = (hdr.Flags & PageFlags.ContinuesPacket) == PageFlags.ContinuesPacket;
            var isEOS = false;
            var isResync = hdr.IsResync;

            // add all the packets, making sure to update flags as needed
            var dataOffset = hdr.DataOffset;
            var cnt = hdr.PacketSizes.Length;
            foreach (var size in hdr.PacketSizes)
            {
                var packet = new Packet(this, dataOffset, size)
                {
                    PageGranulePosition = hdr.GranulePosition,
                    IsEndOfStream = isEOS,
                    PageSequenceNumber = hdr.SequenceNumber,
                    IsContinued = isContinued,
                    IsContinuation = isContinuation,
                    IsResync = isResync,
                };
                packetReader.AddPacket(packet);

                // update the offset into the stream for each packet
                dataOffset += size;

                // only the first packet in a page can be a continuation or resync
                isContinuation = false;
                isResync = false;

                // only the last packet in a page can be continued or flagged end of stream
                if (--cnt == 1)
                {
                    isContinued = hdr.LastPacketContinues;
                    isEOS = (hdr.Flags & PageFlags.EndOfStream) == PageFlags.EndOfStream;
                }
            }

            // if the packet reader list doesn't include the serial in question, add it to the list and indicate a new stream to the caller
            if (!_packetReaders.ContainsKey(hdr.StreamSerial))
            {
                _packetReaders.Add(hdr.StreamSerial, packetReader);
                return true;
            }
            else
            {
                // otherwise, indicate an existing stream to the caller
                return false;
            }
        }

        int GatherNextPage()
        {
            while (true)
            {
                // get our next header
                var hdr = FindNextPageHeader();
                if (hdr == null)
                {
                    return -1;
                }

                // if it's in a disposed stream, grab the next page instead
                if (_disposedStreamSerials.Contains(hdr.StreamSerial)) continue;

                // otherwise, add it
                if (AddPage(hdr))
                {
                    var callback = NewStream;
                    if (callback != null)
                    {
                        var ea = new NewStreamEventArgs(_packetReaders[hdr.StreamSerial]);
                        callback(this, ea);
                        if (ea.IgnoreStream)
                        {
                            _packetReaders[hdr.StreamSerial].Dispose();
                            continue;
                        }
                    }
                }
                return hdr.StreamSerial;
            }
        }

        // packet reader bits...
        internal void DisposePacketReader(PacketReader packetReader)
        {
            _disposedStreamSerials.Add(packetReader.StreamSerial);
            _packetReaders.Remove(packetReader.StreamSerial);
        }

        internal int PacketReadByte(long offset)
        {
            _stream.TakeLock();
            try
            {
                _stream.Position = offset;
                return _stream.ReadByte();
            }
            finally
            {
                _stream.ReleaseLock();
            }
        }

        internal void PacketDiscardThrough(long offset)
        {
            _stream.TakeLock();
            try
            {
                _stream.DiscardThrough(offset);
            }
            finally
            {
                _stream.ReleaseLock();
            }
        }

        internal void GatherNextPage(int streamSerial)
        {
            if (!_packetReaders.ContainsKey(streamSerial)) throw new ArgumentOutOfRangeException("streamSerial");

            int nextSerial;
            do
            {
                _stream.TakeLock();
                try
                {
                    if (_packetReaders[streamSerial].HasEndOfStream) break;

                    nextSerial = GatherNextPage();
                    if (nextSerial == -1)
                    {
                        foreach (var reader in _packetReaders)
                        {
                            if (!reader.Value.HasEndOfStream)
                            {
                                reader.Value.SetEndOfStream();
                            }
                        }
                        break;
                    }
                }
                finally
                {
                    _stream.ReleaseLock();
                }
            } while (nextSerial != streamSerial);
        }
    }
}
#endregion //OggContainerReader

#region OggCrc
namespace NVorbis.Ogg
{
    class Crc
    {
        const uint CRC32_POLY = 0x04c11db7;
        static uint[] crcTable = new uint[256];

        static Crc()
        {
            for (uint i = 0; i < 256; i++)
            {
                uint s = i << 24;
                for (int j = 0; j < 8; ++j)
                {
                    s = (s << 1) ^ (s >= (1U << 31) ? CRC32_POLY : 0);
                }
                crcTable[i] = s;
            }
        }

        uint _crc;

        public Crc()
        {
            Reset();
        }

        public void Reset()
        {
            _crc = 0U;
        }

        public void Update(int nextVal)
        {
            _crc = (_crc << 8) ^ crcTable[nextVal ^ (_crc >> 24)];
        }

        public bool Test(uint checkCrc)
        {
            return _crc == checkCrc;
        }
    }
}
#endregion //OggCrc

#region OggPacket
namespace NVorbis.Ogg
{
    class Packet : DataPacket
    {
        long _offset;                       // 8
        int _length;                        // 4
        int _curOfs;                        // 4
        Packet _mergedPacket;               // IntPtr.Size
        Packet _next;                       // IntPtr.Size
        Packet _prev;                       // IntPtr.Size
        ContainerReader _containerReader;   // IntPtr.Size

        internal Packet Next
        {
            get { return _next; }
            set { _next = value; }
        }
        internal Packet Prev
        {
            get { return _prev; }
            set { _prev = value; }
        }
        internal bool IsContinued
        {
            get { return GetFlag(PacketFlags.User1); }
            set { SetFlag(PacketFlags.User1, value); }
        }
        internal bool IsContinuation
        {
            get { return GetFlag(PacketFlags.User2); }
            set { SetFlag(PacketFlags.User2, value); }
        }

        internal Packet(ContainerReader containerReader, long streamOffset, int length)
            : base(length)
        {
            _containerReader = containerReader;

            _offset = streamOffset;
            _length = length;
            _curOfs = 0;
        }

        internal void MergeWith(NVorbis.DataPacket continuation)
        {
            var op = continuation as Packet;

            if (op == null) throw new ArgumentException("Incorrect packet type!");

            Length += continuation.Length;

            if (_mergedPacket == null)
            {
                _mergedPacket = op;
            }
            else
            {
                _mergedPacket.MergeWith(continuation);
            }

            // per the spec, a partial packet goes with the next page's granulepos.  we'll go ahead and assign it to the next page as well
            PageGranulePosition = continuation.PageGranulePosition;
            PageSequenceNumber = continuation.PageSequenceNumber;
        }

        internal void Reset()
        {
            _curOfs = 0;
            ResetBitReader();

            if (_mergedPacket != null)
            {
                _mergedPacket.Reset();
            }
        }

        protected override int ReadNextByte()
        {
            if (_curOfs == _length)
            {
                if (_mergedPacket == null) return -1;

                return _mergedPacket.ReadNextByte();
            }

            var b = _containerReader.PacketReadByte(_offset + _curOfs);
            if (b != -1)
            {
                ++_curOfs;
            }
            return b;
        }

        public override void Done()
        {
            if (_mergedPacket != null)
            {
                _mergedPacket.Done();
            }
            else
            {
                _containerReader.PacketDiscardThrough(_offset + _length);
            }
        }
    }
}
#endregion //OggPacket

#region OggPacketReader
namespace NVorbis.Ogg
{
    [System.Diagnostics.DebuggerTypeProxy(typeof(PacketReader.DebugView))]
    class PacketReader : IPacketProvider
    {
        class DebugView
        {
            PacketReader _reader;

            public DebugView(PacketReader reader)
            {
                if (reader == null) throw new ArgumentNullException("reader");
                _reader = reader;
            }

            public ContainerReader Container { get { return _reader._container; } }
            public int StreamSerial { get { return _reader._streamSerial; } }
            public bool EndOfStreamFound { get { return _reader._eosFound; } }

            public int CurrentPacketIndex
            {
                get
                {
                    if (_reader._current == null) return -1;
                    return Array.IndexOf(Packets, _reader._current);
                }
            }

            Packet _last, _first;
            Packet[] _packetList = new Packet[0];
            public Packet[] Packets
            {
                get
                {
                    if (_reader._last == _last && _reader._first == _first)
                    {
                        return _packetList;
                    }

                    _last = _reader._last;
                    _first = _reader._first;

                    var packets = new List<Packet>();
                    var node = _first;
                    while (node != null)
                    {
                        packets.Add(node);
                        node = node.Next;
                    }
                    _packetList = packets.ToArray();
                    return _packetList;
                }
            }
        }

        // IPacketProvider requires this, but we aren't using it
#pragma warning disable 67  // disable the "unused" warning
        public event EventHandler<ParameterChangeEventArgs> ParameterChange;
#pragma warning restore 67

        ContainerReader _container;
        int _streamSerial;
        bool _eosFound;

        Packet _first, _current, _last;

        object _packetLock = new object();

        internal PacketReader(ContainerReader container, int streamSerial)
        {
            _container = container;
            _streamSerial = streamSerial;
        }

        public void Dispose()
        {
            _eosFound = true;

            _container.DisposePacketReader(this);
            _container = null;

            _current = null;

            if (_first != null)
            {
                var node = _first;
                _first = null;
                while (node.Next != null)
                {
                    var temp = node.Next;
                    node.Next = null;
                    node = temp;
                    node.Prev = null;
                }
                node = null;
            }

            _last = null;
        }

        internal void AddPacket(Packet packet)
        {
            lock (_packetLock)
            {
                // if we've already found the end of the stream, don't accept any more packets
                if (_eosFound) return;

                // if the packet is a resync, it cannot be a continuation...
                if (packet.IsResync)
                {
                    packet.IsContinuation = false;
                    if (_last != null) _last.IsContinued = false;
                }

                if (packet.IsContinuation)
                {
                    // if we get here, the stream is invalid if there isn't a previous packet
                    if (_last == null) throw new InvalidDataException();

                    // if the last packet isn't continued, something is wrong
                    if (!_last.IsContinued) throw new InvalidDataException();

                    _last.MergeWith(packet);
                    _last.IsContinued = packet.IsContinued;
                }
                else
                {
                    var p = packet as Packet;
                    if (p == null) throw new ArgumentException("Wrong packet datatype", "packet");

                    if (_first == null)
                    {
                        // this is the first packet to add, so just set first & last to point at it
                        _first = p;
                        _last = p;
                    }
                    else
                    {
                        // swap the new packet in to the last position (remember, we're doubly-linked)
                        _last = ((p.Prev = _last).Next = p);
                    }
                }

                if (packet.IsEndOfStream)
                {
                    SetEndOfStream();
                }
            }
        }

        internal bool HasEndOfStream
        {
            get { return _eosFound; }
        }

        internal void SetEndOfStream()
        {
            lock (_packetLock)
            {
                // set the flag...
                _eosFound = true;

                // make sure we're handling the last packet correctly
                if (_last.IsContinued)
                {
                    // last packet was a partial... spec says dump it
                    _last = _last.Prev;
                    _last.Next.Prev = null;
                    _last.Next = null;
                }
            }
        }

        public int StreamSerial
        {
            get { return _streamSerial; }
        }

        public long ContainerBits
        {
            get;
            set;
        }

        public bool CanSeek
        {
            get { return _container.CanSeek; }
        }

        // This is fast path... don't make the caller wait if we can help it...
        public DataPacket GetNextPacket()
        {
            return (_current = PeekNextPacketInternal());
        }

        public DataPacket PeekNextPacket()
        {
            return PeekNextPacketInternal();
        }

        Packet PeekNextPacketInternal()
        {
            // try to get the next packet in the sequence
            Packet curPacket;
            if (_current == null)
            {
                curPacket = _first;
            }
            else
            {
                while (true)
                {
                    lock (_packetLock)
                    {
                        curPacket = _current.Next;

                        // if we have a valid packet or we can't get any more, bail out of the loop
                        if ((curPacket != null && !curPacket.IsContinued) || _eosFound) break;
                    }

                    // we need another packet and we've not found the end of the stream...
                    _container.GatherNextPage(_streamSerial);
                }
            }

            // if we're returning a packet, prep is for use
            if (curPacket != null)
            {
                if (curPacket.IsContinued) throw new InvalidDataException("Packet is incomplete!");
                curPacket.Reset();
            }

            return curPacket;
        }

        internal void ReadAllPages()
        {
            if (!CanSeek) throw new InvalidOperationException();

            // don't hold the lock any longer than we have to
            while (!_eosFound)
            {
                _container.GatherNextPage(_streamSerial);
            }
        }

        internal DataPacket GetLastPacket()
        {
            ReadAllPages();

            return _last;
        }

        public int GetTotalPageCount()
        {
            ReadAllPages();

            // here we just count the number of times the page sequence number changes
            var cnt = 0;
            var lastPageSeqNo = 0;
            var packet = _first;
            while (packet != null)
            {
                if (packet.PageSequenceNumber != lastPageSeqNo)
                {
                    ++cnt;
                    lastPageSeqNo = packet.PageSequenceNumber;
                }
                packet = packet.Next;
            }
            return cnt;
        }

        public DataPacket GetPacket(int packetIndex)
        {
            if (!CanSeek) throw new InvalidOperationException();
            if (packetIndex < 0) throw new ArgumentOutOfRangeException("index");

            // if _first is null, something is borked
            if (_first == null) throw new InvalidOperationException("Packet reader has no packets!");

            // starting from the beginning, count packets until we have the one we want...
            var packet = _first;
            while (--packetIndex >= 0)
            {
                while (packet.Next == null)
                {
                    if (_eosFound)
                    {
                        throw new ArgumentOutOfRangeException("index");
                    }
                    _container.GatherNextPage(_streamSerial);
                }

                packet = packet.Next;
            }

            packet.Reset();
            return packet;
        }

        Packet GetLastPacketInPage(Packet packet)
        {
            if (packet != null)
            {
                var pageSeqNumber = packet.PageSequenceNumber;
                while (packet.Next != null && packet.Next.PageSequenceNumber == pageSeqNumber)
                {
                    packet = packet.Next;
                }

                if (packet != null && packet.IsContinued)
                {
                    // move to the *actual* last packet of the page... If .Prev is null, something is wrong and we can't seek anyway
                    packet = packet.Prev;
                }
            }
            return packet;
        }

        Packet FindPacketInPage(Packet pagePacket, long targetGranulePos, Func<DataPacket, DataPacket, int> packetGranuleCountCallback)
        {
            var lastPacketInPage = GetLastPacketInPage(pagePacket);
            if (lastPacketInPage == null)
            {
                return null;
            }

            // return the packet the granule position is in
            var packet = lastPacketInPage;
            do
            {
                if (!packet.GranuleCount.HasValue)
                {
                    // we don't know its length or position...

                    // if it's the last packet in the page, it gets the page's granule position. Otherwise, calc it.
                    if (packet == lastPacketInPage)
                    {
                        packet.GranulePosition = packet.PageGranulePosition;
                    }
                    else
                    {
                        packet.GranulePosition = packet.Next.GranulePosition - packet.Next.GranuleCount.Value;
                    }

                    // if it's the last packet in the stream, it might be a partial.  The spec says the last packet has to be on its own page, so if it is not assume the stream was truncated.
                    if (packet == _last && _eosFound && packet.Prev.PageSequenceNumber < packet.PageSequenceNumber)
                    {
                        packet.GranuleCount = (int)(packet.GranulePosition - packet.Prev.PageGranulePosition);
                    }
                    else if (packet.Prev != null)
                    {
                        packet.Prev.Reset();
                        packet.Reset();

                        packet.GranuleCount = packetGranuleCountCallback(packet, packet.Prev);
                    }
                    else
                    {
                        // probably the first data packet...
                        if (packet.GranulePosition > packet.Next.GranulePosition - packet.Next.GranuleCount)
                        {
                            throw new InvalidOperationException("First data packet size mismatch");
                        }
                        packet.GranuleCount = (int)packet.GranulePosition;
                    }
                }

                // we now know the granule position and count of the packet... is the target within that range?
                if (targetGranulePos <= packet.GranulePosition && targetGranulePos > packet.GranulePosition - packet.GranuleCount)
                {
                    // make sure the previous packet has a position too
                    if (packet.Prev != null && !packet.Prev.GranuleCount.HasValue)
                    {
                        packet.Prev.GranulePosition = packet.GranulePosition - packet.GranuleCount.Value;
                    }
                    return packet;
                }

                packet = packet.Prev;
            } while (packet != null && packet.PageSequenceNumber == lastPacketInPage.PageSequenceNumber);

            // couldn't find it, but maybe that's because something glitched in the file...
            // we're doing this in case there's a dicontinuity in the file...  It's not perfect, but it'll work
            if (packet != null && packet.PageGranulePosition < targetGranulePos)
            {
                packet.GranulePosition = packet.PageGranulePosition;
                return packet.Next;
            }
            return null;
        }

        public DataPacket FindPacket(long granulePos, Func<DataPacket, DataPacket, int> packetGranuleCountCallback)
        {
            // This will find which packet contains the granule position being requested.  It is basically a linear search.
            // Please note, the spec actually calls for a bisection search, but the result here should be the same.

            // don't look for any position before 0!
            if (granulePos < 0) throw new ArgumentOutOfRangeException("granulePos");

            Packet foundPacket = null;

            // determine which direction to search from...
            var packet = _current ?? _first;
            if (granulePos > packet.PageGranulePosition)
            {
                // forward search

                // find the first packet in the page the requested granule is on
                while (granulePos > packet.PageGranulePosition)
                {
                    if ((packet.Next == null || packet.IsContinued) && !_eosFound)
                    {
                        _container.GatherNextPage(_streamSerial);
                        if (_eosFound)
                        {
                            packet = null;
                            break;
                        }
                    }
                    packet = packet.Next;
                }

                foundPacket = FindPacketInPage(packet, granulePos, packetGranuleCountCallback);
            }
            else
            {
                // reverse search (or we're looking at the same page)
                while (packet.Prev != null && (granulePos < packet.Prev.PageGranulePosition || packet.Prev.PageGranulePosition == -1))
                {
                    packet = packet.Prev;
                }

                foundPacket = FindPacketInPage(packet, granulePos, packetGranuleCountCallback);
            }

            return foundPacket;
        }

        public void SeekToPacket(DataPacket packet, int preRoll)
        {
            if (preRoll < 0) throw new ArgumentOutOfRangeException("preRoll");
            if (packet == null) throw new ArgumentNullException("granulePos");

            var op = packet as Packet;
            if (op == null) throw new ArgumentException("Incorrect packet type!", "packet");

            while (--preRoll >= 0)
            {
                op = op.Prev;
                if (op == null) throw new ArgumentOutOfRangeException("preRoll");
            }

            // _current always points to the last packet returned by PeekNextPacketInternal
            _current = op.Prev;
        }

        public long GetGranuleCount()
        {
            return GetLastPacket().PageGranulePosition;
        }
    }
}
#endregion //OggPacketReader

#region OggPageFlags
namespace NVorbis.Ogg
{
    [Flags]
    enum PageFlags
    {
        None = 0,
        ContinuesPacket = 1,
        BeginningOfStream = 2,
        EndOfStream = 4,
    }
}
#endregion //OggPageFlags

#region BufferedReadStream
namespace NVorbis
{
    /// <summary>
    /// A thread-safe, read-only, buffering stream wrapper.
    /// </summary>
    partial class BufferedReadStream : Stream
    {
        const int DEFAULT_INITIAL_SIZE = 32768; // 32KB  (1/2 full page)
        const int DEFAULT_MAX_SIZE = 262144;    // 256KB (4 full pages)

        Stream _baseStream;
        StreamReadBuffer _buffer;
        long _readPosition;
        object _localLock = new object();
        System.Threading.Thread _owningThread;
        int _lockCount;

        public BufferedReadStream(Stream baseStream)
            : this(baseStream, DEFAULT_INITIAL_SIZE, DEFAULT_MAX_SIZE, false)
        {
        }

        public BufferedReadStream(Stream baseStream, bool minimalRead)
            : this(baseStream, DEFAULT_INITIAL_SIZE, DEFAULT_MAX_SIZE, minimalRead)
        {
        }

        public BufferedReadStream(Stream baseStream, int initialSize, int maxSize)
            : this(baseStream, initialSize, maxSize, false)
        {
        }

        public BufferedReadStream(Stream baseStream, int initialSize, int maxBufferSize, bool minimalRead)
        {
            if (baseStream == null) throw new ArgumentNullException("baseStream");
            if (!baseStream.CanRead) throw new ArgumentException("baseStream");

            if (maxBufferSize < 1) maxBufferSize = 1;
            if (initialSize < 1) initialSize = 1;
            if (initialSize > maxBufferSize) initialSize = maxBufferSize;

            _baseStream = baseStream;
            _buffer = new StreamReadBuffer(baseStream, initialSize, maxBufferSize, minimalRead);
            _buffer.MaxSize = maxBufferSize;
            _buffer.MinimalRead = minimalRead;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                if (_buffer != null)
                {
                    _buffer.Dispose();
                    _buffer = null;
                }

                if (CloseBaseStream)
                {
                    _baseStream.Close();
                }
            }
        }

        // route all the container locking through here so we can track whether the caller actually took the lock...
        public void TakeLock()
        {
            System.Threading.Monitor.Enter(_localLock);
            if (++_lockCount == 1)
            {
                _owningThread = System.Threading.Thread.CurrentThread;
            }
        }

        void CheckLock()
        {
            if (_owningThread != System.Threading.Thread.CurrentThread)
            {
                throw new System.Threading.SynchronizationLockException();
            }
        }

        public void ReleaseLock()
        {
            CheckLock();
            if (--_lockCount == 0)
            {
                _owningThread = null;
            }
            System.Threading.Monitor.Exit(_localLock);
        }

        public bool CloseBaseStream
        {
            get;
            set;
        }

        public bool MinimalRead
        {
            get { return _buffer.MinimalRead; }
            set { _buffer.MinimalRead = value; }
        }

        public int MaxBufferSize
        {
            get { return _buffer.MaxSize; }
            set
            {
                CheckLock();
                _buffer.MaxSize = value;
            }
        }

        public long BufferBaseOffset
        {
            get { return _buffer.BaseOffset; }
        }

        public int BufferBytesFilled
        {
            get { return _buffer.BytesFilled; }
        }

        public void Discard(int bytes)
        {
            CheckLock();
            _buffer.DiscardThrough(_buffer.BaseOffset + bytes);
        }

        public void DiscardThrough(long offset)
        {
            CheckLock();
            _buffer.DiscardThrough(offset);
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return true; }
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override void Flush()
        {
            // no-op
        }

        public override long Length
        {
            get { return _baseStream.Length; }
        }

        public override long Position
        {
            get { return _readPosition; }
            set { Seek(value, SeekOrigin.Begin); }
        }

        public override int ReadByte()
        {
            CheckLock();
            var val = _buffer.ReadByte(Position);
            if (val > -1)
            {
                Seek(1, SeekOrigin.Current);
            }
            return val;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            CheckLock();
            var cnt = _buffer.Read(Position, buffer, offset, count);
            Seek(cnt, SeekOrigin.Current);
            return cnt;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            CheckLock();
            switch (origin)
            {
                case SeekOrigin.Begin:
                    // no-op
                    break;
                case SeekOrigin.Current:
                    offset += Position;
                    break;
                case SeekOrigin.End:
                    offset += _baseStream.Length;
                    break;
            }

            if (!_baseStream.CanSeek)
            {
                if (offset < _buffer.BaseOffset) throw new InvalidOperationException("Cannot seek to before the start of the buffer!");
                if (offset >= _buffer.BufferEndOffset) throw new InvalidOperationException("Cannot seek to beyond the end of the buffer!  Discard some bytes.");
            }

            return (_readPosition = offset);
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
#endregion //BufferedReadStream

#region DataPacket
namespace NVorbis
{
    /// <summary>
    /// A single data packet from a logical Vorbis stream.
    /// </summary>
    public abstract class DataPacket
    {
        ulong _bitBucket;           // 8
        int _bitCount;              // 4
        int _readBits;              // 4
        byte _overflowBits;         // 1
        PacketFlags _packetFlags;   // 1
        long _granulePosition;      // 8
        long _pageGranulePosition;  // 8
        int _length;                // 4
        int _granuleCount;          // 4
        int _pageSequenceNumber;    // 4

        /// <summary>
        /// Defines flags to apply to the current packet
        /// </summary>
        [Flags]
        // for now, let's use a byte... if we find we need more space, we can always expand it...
        protected enum PacketFlags : byte
        {
            /// <summary>
            /// Packet is first since reader had to resync with stream.
            /// </summary>
            IsResync = 0x01,
            /// <summary>
            /// Packet is the last in the logical stream.
            /// </summary>
            IsEndOfStream = 0x02,
            /// <summary>
            /// Packet does not have all its data available.
            /// </summary>
            IsShort = 0x04,
            /// <summary>
            /// Packet has a granule count defined.
            /// </summary>
            HasGranuleCount = 0x08,

            /// <summary>
            /// Flag for use by inheritors.
            /// </summary>
            User1 = 0x10,
            /// <summary>
            /// Flag for use by inheritors.
            /// </summary>
            User2 = 0x20,
            /// <summary>
            /// Flag for use by inheritors.
            /// </summary>
            User3 = 0x40,
            /// <summary>
            /// Flag for use by inheritors.
            /// </summary>
            User4 = 0x80,
        }

        /// <summary>
        /// Gets the value of the specified flag.
        /// </summary>
        protected bool GetFlag(PacketFlags flag)
        {
            return (_packetFlags & flag) == flag;
        }

        /// <summary>
        /// Sets the value of the specified flag.
        /// </summary>
        protected void SetFlag(PacketFlags flag, bool value)
        {
            if (value)
            {
                _packetFlags |= flag;
            }
            else
            {
                _packetFlags &= ~flag;
            }
        }

        /// <summary>
        /// Creates a new instance with the specified length.
        /// </summary>
        /// <param name="length">The length of the packet.</param>
        protected DataPacket(int length)
        {
            Length = length;
        }

        /// <summary>
        /// Reads the next byte of the packet.
        /// </summary>
        /// <returns>The next byte if available, otherwise -1.</returns>
        abstract protected int ReadNextByte();

        /// <summary>
        /// Indicates that the packet has been read and its data is no longer needed.
        /// </summary>
        virtual public void Done()
        {
        }

        /// <summary>
        /// Attempts to read the specified number of bits from the packet, but may return fewer.  Does not advance the position counter.
        /// </summary>
        /// <param name="count">The number of bits to attempt to read.</param>
        /// <param name="bitsRead">The number of bits actually read.</param>
        /// <returns>The value of the bits read.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is not between 0 and 64.</exception>
        public ulong TryPeekBits(int count, out int bitsRead)
        {
            ulong value = 0;

            if (count < 0 || count > 64) throw new ArgumentOutOfRangeException("count");
            if (count == 0)
            {
                bitsRead = 0;
                return 0UL;
            }

            while (_bitCount < count)
            {
                var val = ReadNextByte();
                if (val == -1)
                {
                    bitsRead = _bitCount;
                    value = _bitBucket;
                    _bitBucket = 0;
                    _bitCount = 0;

                    IsShort = true;

                    return value;
                }
                _bitBucket = (ulong)(val & 0xFF) << _bitCount | _bitBucket;
                _bitCount += 8;

                if (_bitCount > 64)
                {
                    _overflowBits = (byte)(val >> (72 - _bitCount));
                }
            }

            value = _bitBucket;

            if (count < 64)
            {
                value &= (1UL << count) - 1;
            }

            bitsRead = count;
            return value;
        }

        /// <summary>
        /// Advances the position counter by the specified number of bits.
        /// </summary>
        /// <param name="count">The number of bits to advance.</param>
        public void SkipBits(int count)
        {
            if (count == 0)
            {
                // no-op
            }
            else if (_bitCount > count)
            {
                // we still have bits left over...
                if (count > 63)
                {
                    _bitBucket = 0;
                }
                else
                {
                    _bitBucket >>= count;
                }
                if (_bitCount > 64)
                {
                    var overflowCount = _bitCount - 64;
                    _bitBucket |= (ulong)_overflowBits << (_bitCount - count - overflowCount);

                    if (overflowCount > count)
                    {
                        // ugh, we have to keep bits in overflow
                        _overflowBits >>= count;
                    }
                }

                _bitCount -= count;
                _readBits += count;
            }
            else if (_bitCount == count)
            {
                _bitBucket = 0UL;
                _bitCount = 0;
                _readBits += count;
            }
            else //  _bitCount < count
            {
                // we have to move more bits than we have available...
                count -= _bitCount;
                _readBits += _bitCount;
                _bitCount = 0;
                _bitBucket = 0;

                while (count > 8)
                {
                    if (ReadNextByte() == -1)
                    {
                        count = 0;
                        IsShort = true;
                        break;
                    }
                    count -= 8;
                    _readBits += 8;
                }

                if (count > 0)
                {
                    var temp = ReadNextByte();
                    if (temp == -1)
                    {
                        IsShort = true;
                    }
                    else
                    {
                        _bitBucket = (ulong)(temp >> count);
                        _bitCount = 8 - count;
                        _readBits += count;
                    }
                }
            }
        }

        /// <summary>
        /// Resets the bit reader.
        /// </summary>
        protected void ResetBitReader()
        {
            _bitBucket = 0;
            _bitCount = 0;
            _readBits = 0;

            IsShort = false;
        }

        /// <summary>
        /// Gets whether the packet was found after a stream resync.
        /// </summary>
        public bool IsResync
        {
            get { return GetFlag(PacketFlags.IsResync); }
            internal set { SetFlag(PacketFlags.IsResync, value); }
        }

        /// <summary>
        /// Gets the position of the last granule in the packet.
        /// </summary>
        public long GranulePosition
        {
            get { return _granulePosition; }
            set { _granulePosition = value; }
        }

        /// <summary>
        /// Gets the position of the last granule in the page the packet is in.
        /// </summary>
        public long PageGranulePosition
        {
            get { return _pageGranulePosition; }
            internal set { _pageGranulePosition = value; }
        }

        /// <summary>
        /// Gets the length of the packet.
        /// </summary>
        public int Length
        {
            get { return _length; }
            protected set { _length = value; }
        }

        /// <summary>
        /// Gets whether the packet is the last one in the logical stream.
        /// </summary>
        public bool IsEndOfStream
        {
            get { return GetFlag(PacketFlags.IsEndOfStream); }
            internal set { SetFlag(PacketFlags.IsEndOfStream, value); }
        }

        /// <summary>
        /// Gets the number of bits read from the packet.
        /// </summary>
        public long BitsRead
        {
            get { return _readBits; }
        }

        /// <summary>
        /// Gets the number of granules in the packet.  If <c>null</c>, the packet has not been decoded yet.
        /// </summary>
        public int? GranuleCount
        {
            get
            {
                if (GetFlag(PacketFlags.HasGranuleCount))
                {
                    return _granuleCount;
                }
                return null;
            }
            set
            {
                if (value.HasValue)
                {
                    _granuleCount = value.Value;
                    SetFlag(PacketFlags.HasGranuleCount, true);
                }
                else
                {
                    SetFlag(PacketFlags.HasGranuleCount, false);
                }
            }
        }

        internal int PageSequenceNumber
        {
            get { return _pageSequenceNumber; }
            set { _pageSequenceNumber = value; }
        }

        internal bool IsShort
        {
            get { return GetFlag(PacketFlags.IsShort); }
            private set { SetFlag(PacketFlags.IsShort, value); }
        }

        /// <summary>
        /// Reads the specified number of bits from the packet and advances the position counter.
        /// </summary>
        /// <param name="count">The number of bits to read.</param>
        /// <returns>The value of the bits read.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The number of bits specified is not between 0 and 64.</exception>
        public ulong ReadBits(int count)
        {
            // short-circuit 0
            if (count == 0) return 0UL;

            int temp;
            var value = TryPeekBits(count, out temp);

            SkipBits(count);

            return value;
        }

        /// <summary>
        /// Reads the next byte from the packet.  Does not advance the position counter.
        /// </summary>
        /// <returns>The byte read from the packet.</returns>
        public byte PeekByte()
        {
            int temp;
            return (byte)TryPeekBits(8, out temp);
        }

        /// <summary>
        /// Reads the next byte from the packet and advances the position counter.
        /// </summary>
        /// <returns>The byte read from the packet.</returns>
        public byte ReadByte()
        {
            return (byte)ReadBits(8);
        }

        /// <summary>
        /// Reads the specified number of bytes from the packet and advances the position counter.
        /// </summary>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>A byte array holding the data read.</returns>
        public byte[] ReadBytes(int count)
        {
            var buf = new List<byte>(count);

            while (buf.Count < count)
            {
                buf.Add(ReadByte());
            }

            return buf.ToArray();
        }

        /// <summary>
        /// Reads the specified number of bytes from the packet into the buffer specified and advances the position counter.
        /// </summary>
        /// <param name="buffer">The buffer to read into.</param>
        /// <param name="index">The index into the buffer to start placing the read data.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>The number of bytes read.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is less than 0 or <paramref name="index"/> + <paramref name="count"/> is past the end of <paramref name="buffer"/>.</exception>
        public int Read(byte[] buffer, int index, int count)
        {
            if (index < 0 || index + count > buffer.Length) throw new ArgumentOutOfRangeException("index");
            for (int i = 0; i < count; i++)
            {
                int cnt;
                byte val = (byte)TryPeekBits(8, out cnt);
                if (cnt == 0)
                {
                    return i;
                }
                buffer[index++] = val;
                SkipBits(8);
            }
            return count;
        }

        /// <summary>
        /// Reads the next bit from the packet and advances the position counter.
        /// </summary>
        /// <returns>The value of the bit read.</returns>
        public bool ReadBit()
        {
            return ReadBits(1) == 1;
        }

        /// <summary>
        /// Retrieves the next 16 bits from the packet as a <see cref="short"/> and advances the position counter.
        /// </summary>
        /// <returns>The value of the next 16 bits.</returns>
        public short ReadInt16()
        {
            return (short)ReadBits(16);
        }

        /// <summary>
        /// Retrieves the next 32 bits from the packet as a <see cref="int"/> and advances the position counter.
        /// </summary>
        /// <returns>The value of the next 32 bits.</returns>
        public int ReadInt32()
        {
            return (int)ReadBits(32);
        }

        /// <summary>
        /// Retrieves the next 64 bits from the packet as a <see cref="long"/> and advances the position counter.
        /// </summary>
        /// <returns>The value of the next 64 bits.</returns>
        public long ReadInt64()
        {
            return (long)ReadBits(64);
        }

        /// <summary>
        /// Retrieves the next 16 bits from the packet as a <see cref="ushort"/> and advances the position counter.
        /// </summary>
        /// <returns>The value of the next 16 bits.</returns>
        public ushort ReadUInt16()
        {
            return (ushort)ReadBits(16);
        }

        /// <summary>
        /// Retrieves the next 32 bits from the packet as a <see cref="uint"/> and advances the position counter.
        /// </summary>
        /// <returns>The value of the next 32 bits.</returns>
        public uint ReadUInt32()
        {
            return (uint)ReadBits(32);
        }

        /// <summary>
        /// Retrieves the next 64 bits from the packet as a <see cref="ulong"/> and advances the position counter.
        /// </summary>
        /// <returns>The value of the next 64 bits.</returns>
        public ulong ReadUInt64()
        {
            return (ulong)ReadBits(64);
        }

        /// <summary>
        /// Advances the position counter by the specified number of bytes.
        /// </summary>
        /// <param name="count">The number of bytes to advance.</param>
        public void SkipBytes(int count)
        {
            SkipBits(count * 8);
        }
    }
}
#endregion //DataPacket

#region Huffman
namespace NVorbis
{
    static class Huffman
    {
        const int MAX_TABLE_BITS = 10;

        static internal System.Collections.Generic.List<HuffmanListNode> BuildPrefixedLinkedList(int[] values, int[] lengthList, int[] codeList, out int tableBits, out HuffmanListNode firstOverflowNode)
        {
            HuffmanListNode[] list = new HuffmanListNode[lengthList.Length];

            var maxLen = 0;
            for (int i = 0; i < list.Length; i++)
            {
                list[i] = new HuffmanListNode
                {
                    Value = values[i],
                    Length = lengthList[i] <= 0 ? 99999 : lengthList[i],
                    Bits = codeList[i],
                    Mask = (1 << lengthList[i]) - 1,
                };
                if (lengthList[i] > 0 && maxLen < lengthList[i])
                {
                    maxLen = lengthList[i];
                }
            }

            Array.Sort(list, SortCallback);

            tableBits = maxLen > MAX_TABLE_BITS ? MAX_TABLE_BITS : maxLen;

            var prefixList = new System.Collections.Generic.List<HuffmanListNode>(1 << tableBits);
            firstOverflowNode = null;
            for (int i = 0; i < list.Length && list[i].Length < 99999; i++)
            {
                if (firstOverflowNode == null)
                {
                    var itemBits = list[i].Length;
                    if (itemBits > tableBits)
                    {
                        firstOverflowNode = list[i];
                    }
                    else
                    {
                        var maxVal = 1 << (tableBits - itemBits);
                        var item = list[i];
                        for (int j = 0; j < maxVal; j++)
                        {
                            var idx = (j << itemBits) | item.Bits;
                            while (prefixList.Count <= idx)
                            {
                                prefixList.Add(null);
                            }
                            prefixList[idx] = item;
                        }
                    }
                }
                else
                {
                    list[i - 1].Next = list[i];
                }
            }

            while (prefixList.Count < 1 << tableBits)
            {
                prefixList.Add(null);
            }

            return prefixList;
        }

        static int SortCallback(HuffmanListNode i1, HuffmanListNode i2)
        {
            var len = i1.Length - i2.Length;
            if (len == 0)
            {
                return i1.Bits - i2.Bits;
            }
            return len;
        }
    }

    class HuffmanListNode
    {
        internal int Value;

        internal int Length;
        internal int Bits;
        internal int Mask;

        internal HuffmanListNode Next;
    }
}
#endregion //Huffman

#region IContainerReader
namespace NVorbis
{
    /// <summary>
    /// Provides a interface for a Vorbis logical stream container.
    /// </summary>
    public interface IContainerReader : IDisposable
    {
        /// <summary>
        /// Gets the list of stream serials found in the container so far.
        /// </summary>
        int[] StreamSerials { get; }

        /// <summary>
        /// Gets whether the container supports seeking.
        /// </summary>
        bool CanSeek { get; }

        /// <summary>
        /// Gets the number of bits in the container that are not associated with a logical stream.
        /// </summary>
        long WasteBits { get; }

        /// <summary>
        /// Gets the number of pages that have been read in the container.
        /// </summary>
        int PagesRead { get; }

        /// <summary>
        /// Event raised when a new logical stream is found in the container.
        /// </summary>
        event EventHandler<NewStreamEventArgs> NewStream;

        /// <summary>
        /// Initializes the container and finds the first stream.
        /// </summary>
        /// <returns><c>True</c> if a valid logical stream is found, otherwise <c>False</c>.</returns>
        bool Init();

        /// <summary>
        /// Finds the next new stream in the container.
        /// </summary>
        /// <returns><c>True</c> if a new stream was found, otherwise <c>False</c>.</returns>
        /// <exception cref="InvalidOperationException"><see cref="CanSeek"/> is <c>False</c>.</exception>
        bool FindNextStream();

        /// <summary>
        /// Retrieves the total number of pages in the container.
        /// </summary>
        /// <returns>The total number of pages.</returns>
        /// <exception cref="InvalidOperationException"><see cref="CanSeek"/> is <c>False</c>.</exception>
        int GetTotalPageCount();
    }
}
#endregion //IContainerReader

#region IPacketProvider
namespace NVorbis
{
    /// <summary>
    /// Provides packets on-demand for the Vorbis stream decoder.
    /// </summary>
    public interface IPacketProvider : IDisposable
    {
        /// <summary>
        /// Gets the serial number associated with this stream.
        /// </summary>
        int StreamSerial { get; }

        /// <summary>
        /// Gets whether seeking is supported on this stream.
        /// </summary>
        bool CanSeek { get; }

        /// <summary>
        /// Gets the number of bits of overhead in this stream's container.
        /// </summary>
        long ContainerBits { get; }

        /// <summary>
        /// Retrieves the total number of pages (or frames) this stream uses.
        /// </summary>
        /// <returns>The page count.</returns>
        /// <exception cref="InvalidOperationException"><see cref="CanSeek"/> is <c>False</c>.</exception>
        int GetTotalPageCount();

        /// <summary>
        /// Retrieves the next packet in the stream.
        /// </summary>
        /// <returns>The next packet in the stream or <c>null</c> if no more packets.</returns>
        DataPacket GetNextPacket();

        /// <summary>
        /// Retrieves the next packet in the stream but does not advance to the following packet.
        /// </summary>
        /// <returns>The next packet in the stream or <c>null</c> if no more packets.</returns>
        DataPacket PeekNextPacket();

        /// <summary>
        /// Retrieves the packet specified from the stream.
        /// </summary>
        /// <param name="packetIndex">The index of the packet to retrieve.</param>
        /// <returns>The specified packet.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="packetIndex"/> is less than 0 or past the end of the stream.</exception>
        /// <exception cref="InvalidOperationException"><see cref="CanSeek"/> is <c>False</c>.</exception>
        DataPacket GetPacket(int packetIndex);

        /// <summary>
        /// Retrieves the total number of granules in this Vorbis stream.
        /// </summary>
        /// <returns>The number of samples</returns>
        /// <exception cref="InvalidOperationException"><see cref="CanSeek"/> is <c>False</c>.</exception>
        long GetGranuleCount();

        /// <summary>
        /// Finds the packet index to the granule position specified in the current stream.
        /// </summary>
        /// <param name="granulePos">The granule position to seek to.</param>
        /// <param name="packetGranuleCountCallback">A callback method that takes the current and previous packets and returns the number of granules in the current packet.</param>
        /// <returns>The index of the packet that includes the specified granule position or -1 if none found.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="granulePos"/> is less than 0 or is after the last granule.</exception>
        DataPacket FindPacket(long granulePos, Func<DataPacket, DataPacket, int> packetGranuleCountCallback);

        /// <summary>
        /// Sets the next packet to be returned, applying a pre-roll as necessary.
        /// </summary>
        /// <param name="packet">The packet to key from.</param>
        /// <param name="preRoll">The number of packets to return before the indicated packet.</param>
        void SeekToPacket(DataPacket packet, int preRoll);

        /// <summary>
        /// Occurs when the stream is about to change parameters.
        /// </summary>
        event EventHandler<ParameterChangeEventArgs> ParameterChange;
    }
}
#endregion //IPacketProvider

#region IVorbisStreamStatus
namespace NVorbis
{
    public interface IVorbisStreamStatus
    {
        /// <summary>
        /// Gets the counters for latency and bitrate calculations, as well as overall bit counts
        /// </summary>
        void ResetStats();

        /// <summary>
        /// Gets the calculated bit rate of audio stream data for the everything decoded so far
        /// </summary>
        int EffectiveBitRate { get; }

        /// <summary>
        /// Gets the calculated bit rate for the last ~1 second of audio
        /// </summary>
        int InstantBitRate { get; }

        /// <summary>
        /// Gets the calculated latency per page
        /// </summary>
        TimeSpan PageLatency { get; }

        /// <summary>
        /// Gets the calculated latency per packet
        /// </summary>
        TimeSpan PacketLatency { get; }

        /// <summary>
        /// Gets the calculated latency per second of output
        /// </summary>
        TimeSpan SecondLatency { get; }

        /// <summary>
        /// Gets the number of bits read that do not contribute to the output audio
        /// </summary>
        long OverheadBits { get; }

        /// <summary>
        /// Gets the number of bits read that contribute to the output audio
        /// </summary>
        long AudioBits { get; }

        /// <summary>
        /// Gets the number of pages read so far in the current stream
        /// </summary>
        int PagesRead { get; }

        /// <summary>
        /// Gets the total number of pages in the current stream
        /// </summary>
        int TotalPages { get; }

        /// <summary>
        /// Gets whether the stream has been clipped since the last reset
        /// </summary>
        bool Clipped { get; }
    }
}
#endregion //IVorbisStreamStatus

#region Mdct
namespace NVorbis
{
    class Mdct
    {
        const float M_PI = 3.14159265358979323846264f;

        static Dictionary<int, Mdct> _setupCache = new Dictionary<int, Mdct>(2);

        public static void Reverse(float[] samples, int sampleCount)
        {
            GetSetup(sampleCount).CalcReverse(samples);
        }

        static Mdct GetSetup(int n)
        {
            lock (_setupCache)
            {
                if (!_setupCache.ContainsKey(n))
                {
                    _setupCache[n] = new Mdct(n);
                }

                return _setupCache[n];
            }
        }

        int _n, _n2, _n4, _n8, _ld;

        float[] _A, _B, _C;
        ushort[] _bitrev;

        private Mdct(int n)
        {
            this._n = n;
            _n2 = n >> 1;
            _n4 = _n2 >> 1;
            _n8 = _n4 >> 1;

            _ld = Utils.ilog(n) - 1;

            // first, calc the "twiddle factors"
            _A = new float[_n2];
            _B = new float[_n2];
            _C = new float[_n4];
            int k, k2;
            for (k = k2 = 0; k < _n4; ++k, k2 += 2)
            {
                _A[k2] = (float)Math.Cos(4 * k * M_PI / n);
                _A[k2 + 1] = (float)-Math.Sin(4 * k * M_PI / n);
                _B[k2] = (float)Math.Cos((k2 + 1) * M_PI / n / 2) * .5f;
                _B[k2 + 1] = (float)Math.Sin((k2 + 1) * M_PI / n / 2) * .5f;
            }
            for (k = k2 = 0; k < _n8; ++k, k2 += 2)
            {
                _C[k2] = (float)Math.Cos(2 * (k2 + 1) * M_PI / n);
                _C[k2 + 1] = (float)-Math.Sin(2 * (k2 + 1) * M_PI / n);
            }

            // now, calc the bit reverse table
            _bitrev = new ushort[_n8];
            for (int i = 0; i < _n8; ++i)
            {
                _bitrev[i] = (ushort)(Utils.BitReverse((uint)i, _ld - 3) << 2);
            }
        }

        #region Buffer Handling

        // This addresses the two constraints we have to deal with:
        //  1) Each Mdct instance must maintain a buffer of n / 2 size without allocating each pass
        //  2) Mdct must be thread-safe
        // To handle these constraints, we use a "thread-local" dictionary

        Dictionary<int, float[]> _threadLocalBuffers = new Dictionary<int, float[]>(1);
        float[] GetBuffer()
        {
            lock (_threadLocalBuffers)
            {
                float[] buf;
                if (!_threadLocalBuffers.TryGetValue(System.Threading.Thread.CurrentThread.ManagedThreadId, out buf))
                {
                    _threadLocalBuffers[System.Threading.Thread.CurrentThread.ManagedThreadId] = (buf = new float[_n2]);
                }
                return buf;
            }
        }

        #endregion

        void CalcReverse(float[] buffer)
        {
            float[] u, v, buf2;

            buf2 = GetBuffer();

            // copy and reflect spectral data
            // step 0

            {
                var d = _n2 - 2; // buf2
                var AA = 0;     // A
                var e = 0;      // buffer
                var e_stop = _n2;// buffer
                while (e != e_stop)
                {
                    buf2[d + 1] = (buffer[e] * _A[AA] - buffer[e + 2] * _A[AA + 1]);
                    buf2[d] = (buffer[e] * _A[AA + 1] + buffer[e + 2] * _A[AA]);
                    d -= 2;
                    AA += 2;
                    e += 4;
                }

                e = _n2 - 3;
                while (d >= 0)
                {
                    buf2[d + 1] = (-buffer[e + 2] * _A[AA] - -buffer[e] * _A[AA + 1]);
                    buf2[d] = (-buffer[e + 2] * _A[AA + 1] + -buffer[e] * _A[AA]);
                    d -= 2;
                    AA += 2;
                    e -= 4;
                }
            }

            // apply "symbolic" names
            u = buffer;
            v = buf2;

            // step 2

            {
                var AA = _n2 - 8;    // A

                var e0 = _n4;        // v
                var e1 = 0;         // v

                var d0 = _n4;        // u
                var d1 = 0;         // u

                while (AA >= 0)
                {
                    float v40_20, v41_21;

                    v41_21 = v[e0 + 1] - v[e1 + 1];
                    v40_20 = v[e0] - v[e1];
                    u[d0 + 1] = v[e0 + 1] + v[e1 + 1];
                    u[d0] = v[e0] + v[e1];
                    u[d1 + 1] = v41_21 * _A[AA + 4] - v40_20 * _A[AA + 5];
                    u[d1] = v40_20 * _A[AA + 4] + v41_21 * _A[AA + 5];

                    v41_21 = v[e0 + 3] - v[e1 + 3];
                    v40_20 = v[e0 + 2] - v[e1 + 2];
                    u[d0 + 3] = v[e0 + 3] + v[e1 + 3];
                    u[d0 + 2] = v[e0 + 2] + v[e1 + 2];
                    u[d1 + 3] = v41_21 * _A[AA] - v40_20 * _A[AA + 1];
                    u[d1 + 2] = v40_20 * _A[AA] + v41_21 * _A[AA + 1];

                    AA -= 8;

                    d0 += 4;
                    d1 += 4;
                    e0 += 4;
                    e1 += 4;
                }
            }

            // step 3

            // iteration 0
            step3_iter0_loop(_n >> 4, u, _n2 - 1 - _n4 * 0, -_n8);
            step3_iter0_loop(_n >> 4, u, _n2 - 1 - _n4 * 1, -_n8);

            // iteration 1
            step3_inner_r_loop(_n >> 5, u, _n2 - 1 - _n8 * 0, -(_n >> 4), 16);
            step3_inner_r_loop(_n >> 5, u, _n2 - 1 - _n8 * 1, -(_n >> 4), 16);
            step3_inner_r_loop(_n >> 5, u, _n2 - 1 - _n8 * 2, -(_n >> 4), 16);
            step3_inner_r_loop(_n >> 5, u, _n2 - 1 - _n8 * 3, -(_n >> 4), 16);

            // iterations 2 ... x
            var l = 2;
            for (; l < (_ld - 3) >> 1; ++l)
            {
                var k0 = _n >> (l + 2);
                var k0_2 = k0 >> 1;
                var lim = 1 << (l + 1);
                for (int i = 0; i < lim; ++i)
                {
                    step3_inner_r_loop(_n >> (l + 4), u, _n2 - 1 - k0 * i, -k0_2, 1 << (l + 3));
                }
            }

            // iterations x ... end
            for (; l < _ld - 6; ++l)
            {
                var k0 = _n >> (l + 2);
                var k1 = 1 << (l + 3);
                var k0_2 = k0 >> 1;
                var rlim = _n >> (l + 6);
                var lim = 1 << l + 1;
                var i_off = _n2 - 1;
                var A0 = 0;

                for (int r = rlim; r > 0; --r)
                {
                    step3_inner_s_loop(lim, u, i_off, -k0_2, A0, k1, k0);
                    A0 += k1 * 4;
                    i_off -= 8;
                }
            }

            // combine some iteration steps...
            step3_inner_s_loop_ld654(_n >> 5, u, _n2 - 1, _n);

            // steps 4, 5, and 6
            {
                var bit = 0;

                var d0 = _n4 - 4;    // v
                var d1 = _n2 - 4;    // v
                while (d0 >= 0)
                {
                    int k4;

                    k4 = _bitrev[bit];
                    v[d1 + 3] = u[k4];
                    v[d1 + 2] = u[k4 + 1];
                    v[d0 + 3] = u[k4 + 2];
                    v[d0 + 2] = u[k4 + 3];

                    k4 = _bitrev[bit + 1];
                    v[d1 + 1] = u[k4];
                    v[d1] = u[k4 + 1];
                    v[d0 + 1] = u[k4 + 2];
                    v[d0] = u[k4 + 3];

                    d0 -= 4;
                    d1 -= 4;
                    bit += 2;
                }
            }

            // step 7
            {
                var c = 0;      // C
                var d = 0;      // v
                var e = _n2 - 4; // v

                while (d < e)
                {
                    float a02, a11, b0, b1, b2, b3;

                    a02 = v[d] - v[e + 2];
                    a11 = v[d + 1] + v[e + 3];

                    b0 = _C[c + 1] * a02 + _C[c] * a11;
                    b1 = _C[c + 1] * a11 - _C[c] * a02;

                    b2 = v[d] + v[e + 2];
                    b3 = v[d + 1] - v[e + 3];

                    v[d] = b2 + b0;
                    v[d + 1] = b3 + b1;
                    v[e + 2] = b2 - b0;
                    v[e + 3] = b1 - b3;

                    a02 = v[d + 2] - v[e];
                    a11 = v[d + 3] + v[e + 1];

                    b0 = _C[c + 3] * a02 + _C[c + 2] * a11;
                    b1 = _C[c + 3] * a11 - _C[c + 2] * a02;

                    b2 = v[d + 2] + v[e];
                    b3 = v[d + 3] - v[e + 1];

                    v[d + 2] = b2 + b0;
                    v[d + 3] = b3 + b1;
                    v[e] = b2 - b0;
                    v[e + 1] = b1 - b3;

                    c += 4;
                    d += 4;
                    e -= 4;
                }
            }

            // step 8 + decode
            {
                var b = _n2 - 8; // B
                var e = _n2 - 8; // buf2
                var d0 = 0;     // buffer
                var d1 = _n2 - 4;// buffer
                var d2 = _n2;    // buffer
                var d3 = _n - 4; // buffer
                while (e >= 0)
                {
                    float p0, p1, p2, p3;

                    p3 = buf2[e + 6] * _B[b + 7] - buf2[e + 7] * _B[b + 6];
                    p2 = -buf2[e + 6] * _B[b + 6] - buf2[e + 7] * _B[b + 7];

                    buffer[d0] = p3;
                    buffer[d1 + 3] = -p3;
                    buffer[d2] = p2;
                    buffer[d3 + 3] = p2;

                    p1 = buf2[e + 4] * _B[b + 5] - buf2[e + 5] * _B[b + 4];
                    p0 = -buf2[e + 4] * _B[b + 4] - buf2[e + 5] * _B[b + 5];

                    buffer[d0 + 1] = p1;
                    buffer[d1 + 2] = -p1;
                    buffer[d2 + 1] = p0;
                    buffer[d3 + 2] = p0;


                    p3 = buf2[e + 2] * _B[b + 3] - buf2[e + 3] * _B[b + 2];
                    p2 = -buf2[e + 2] * _B[b + 2] - buf2[e + 3] * _B[b + 3];

                    buffer[d0 + 2] = p3;
                    buffer[d1 + 1] = -p3;
                    buffer[d2 + 2] = p2;
                    buffer[d3 + 1] = p2;

                    p1 = buf2[e] * _B[b + 1] - buf2[e + 1] * _B[b];
                    p0 = -buf2[e] * _B[b] - buf2[e + 1] * _B[b + 1];

                    buffer[d0 + 3] = p1;
                    buffer[d1] = -p1;
                    buffer[d2 + 3] = p0;
                    buffer[d3] = p0;

                    b -= 8;
                    e -= 8;
                    d0 += 4;
                    d2 += 4;
                    d1 -= 4;
                    d3 -= 4;
                }
            }
        }

        void step3_iter0_loop(int n, float[] e, int i_off, int k_off)
        {
            var ee0 = i_off;        // e
            var ee2 = ee0 + k_off;  // e
            var a = 0;
            for (int i = n >> 2; i > 0; --i)
            {
                float k00_20, k01_21;

                k00_20 = e[ee0] - e[ee2];
                k01_21 = e[ee0 - 1] - e[ee2 - 1];
                e[ee0] += e[ee2];
                e[ee0 - 1] += e[ee2 - 1];
                e[ee2] = k00_20 * _A[a] - k01_21 * _A[a + 1];
                e[ee2 - 1] = k01_21 * _A[a] + k00_20 * _A[a + 1];
                a += 8;

                k00_20 = e[ee0 - 2] - e[ee2 - 2];
                k01_21 = e[ee0 - 3] - e[ee2 - 3];
                e[ee0 - 2] += e[ee2 - 2];
                e[ee0 - 3] += e[ee2 - 3];
                e[ee2 - 2] = k00_20 * _A[a] - k01_21 * _A[a + 1];
                e[ee2 - 3] = k01_21 * _A[a] + k00_20 * _A[a + 1];
                a += 8;

                k00_20 = e[ee0 - 4] - e[ee2 - 4];
                k01_21 = e[ee0 - 5] - e[ee2 - 5];
                e[ee0 - 4] += e[ee2 - 4];
                e[ee0 - 5] += e[ee2 - 5];
                e[ee2 - 4] = k00_20 * _A[a] - k01_21 * _A[a + 1];
                e[ee2 - 5] = k01_21 * _A[a] + k00_20 * _A[a + 1];
                a += 8;

                k00_20 = e[ee0 - 6] - e[ee2 - 6];
                k01_21 = e[ee0 - 7] - e[ee2 - 7];
                e[ee0 - 6] += e[ee2 - 6];
                e[ee0 - 7] += e[ee2 - 7];
                e[ee2 - 6] = k00_20 * _A[a] - k01_21 * _A[a + 1];
                e[ee2 - 7] = k01_21 * _A[a] + k00_20 * _A[a + 1];
                a += 8;

                ee0 -= 8;
                ee2 -= 8;
            }
        }

        void step3_inner_r_loop(int lim, float[] e, int d0, int k_off, int k1)
        {
            float k00_20, k01_21;

            var e0 = d0;            // e
            var e2 = e0 + k_off;    // e
            int a = 0;

            for (int i = lim >> 2; i > 0; --i)
            {
                k00_20 = e[e0] - e[e2];
                k01_21 = e[e0 - 1] - e[e2 - 1];
                e[e0] += e[e2];
                e[e0 - 1] += e[e2 - 1];
                e[e2] = k00_20 * _A[a] - k01_21 * _A[a + 1];
                e[e2 - 1] = k01_21 * _A[a] + k00_20 * _A[a + 1];

                a += k1;

                k00_20 = e[e0 - 2] - e[e2 - 2];
                k01_21 = e[e0 - 3] - e[e2 - 3];
                e[e0 - 2] += e[e2 - 2];
                e[e0 - 3] += e[e2 - 3];
                e[e2 - 2] = k00_20 * _A[a] - k01_21 * _A[a + 1];
                e[e2 - 3] = k01_21 * _A[a] + k00_20 * _A[a + 1];

                a += k1;

                k00_20 = e[e0 - 4] - e[e2 - 4];
                k01_21 = e[e0 - 5] - e[e2 - 5];
                e[e0 - 4] += e[e2 - 4];
                e[e0 - 5] += e[e2 - 5];
                e[e2 - 4] = k00_20 * _A[a] - k01_21 * _A[a + 1];
                e[e2 - 5] = k01_21 * _A[a] + k00_20 * _A[a + 1];

                a += k1;

                k00_20 = e[e0 - 6] - e[e2 - 6];
                k01_21 = e[e0 - 7] - e[e2 - 7];
                e[e0 - 6] += e[e2 - 6];
                e[e0 - 7] += e[e2 - 7];
                e[e2 - 6] = k00_20 * _A[a] - k01_21 * _A[a + 1];
                e[e2 - 7] = k01_21 * _A[a] + k00_20 * _A[a + 1];

                a += k1;

                e0 -= 8;
                e2 -= 8;
            }
        }

        void step3_inner_s_loop(int n, float[] e, int i_off, int k_off, int a, int a_off, int k0)
        {
            var A0 = _A[a];
            var A1 = _A[a + 1];
            var A2 = _A[a + a_off];
            var A3 = _A[a + a_off + 1];
            var A4 = _A[a + a_off * 2];
            var A5 = _A[a + a_off * 2 + 1];
            var A6 = _A[a + a_off * 3];
            var A7 = _A[a + a_off * 3 + 1];

            float k00, k11;

            var ee0 = i_off;        // e
            var ee2 = ee0 + k_off;  // e

            for (int i = n; i > 0; --i)
            {
                k00 = e[ee0] - e[ee2];
                k11 = e[ee0 - 1] - e[ee2 - 1];
                e[ee0] += e[ee2];
                e[ee0 - 1] += e[ee2 - 1];
                e[ee2] = k00 * A0 - k11 * A1;
                e[ee2 - 1] = k11 * A0 + k00 * A1;

                k00 = e[ee0 - 2] - e[ee2 - 2];
                k11 = e[ee0 - 3] - e[ee2 - 3];
                e[ee0 - 2] += e[ee2 - 2];
                e[ee0 - 3] += e[ee2 - 3];
                e[ee2 - 2] = k00 * A2 - k11 * A3;
                e[ee2 - 3] = k11 * A2 + k00 * A3;

                k00 = e[ee0 - 4] - e[ee2 - 4];
                k11 = e[ee0 - 5] - e[ee2 - 5];
                e[ee0 - 4] += e[ee2 - 4];
                e[ee0 - 5] += e[ee2 - 5];
                e[ee2 - 4] = k00 * A4 - k11 * A5;
                e[ee2 - 5] = k11 * A4 + k00 * A5;

                k00 = e[ee0 - 6] - e[ee2 - 6];
                k11 = e[ee0 - 7] - e[ee2 - 7];
                e[ee0 - 6] += e[ee2 - 6];
                e[ee0 - 7] += e[ee2 - 7];
                e[ee2 - 6] = k00 * A6 - k11 * A7;
                e[ee2 - 7] = k11 * A6 + k00 * A7;

                ee0 -= k0;
                ee2 -= k0;
            }
        }

        void step3_inner_s_loop_ld654(int n, float[] e, int i_off, int base_n)
        {
            var a_off = base_n >> 3;
            var A2 = _A[a_off];
            var z = i_off;          // e
            var @base = z - 16 * n; // e

            while (z > @base)
            {
                float k00, k11;

                k00 = e[z] - e[z - 8];
                k11 = e[z - 1] - e[z - 9];
                e[z] += e[z - 8];
                e[z - 1] += e[z - 9];
                e[z - 8] = k00;
                e[z - 9] = k11;

                k00 = e[z - 2] - e[z - 10];
                k11 = e[z - 3] - e[z - 11];
                e[z - 2] += e[z - 10];
                e[z - 3] += e[z - 11];
                e[z - 10] = (k00 + k11) * A2;
                e[z - 11] = (k11 - k00) * A2;

                k00 = e[z - 12] - e[z - 4];
                k11 = e[z - 5] - e[z - 13];
                e[z - 4] += e[z - 12];
                e[z - 5] += e[z - 13];
                e[z - 12] = k11;
                e[z - 13] = k00;

                k00 = e[z - 14] - e[z - 6];
                k11 = e[z - 7] - e[z - 15];
                e[z - 6] += e[z - 14];
                e[z - 7] += e[z - 15];
                e[z - 14] = (k00 + k11) * A2;
                e[z - 15] = (k00 - k11) * A2;

                iter_54(e, z);
                iter_54(e, z - 8);

                z -= 16;
            }
        }

        private void iter_54(float[] e, int z)
        {
            float k00, k11, k22, k33;
            float y0, y1, y2, y3;

            k00 = e[z] - e[z - 4];
            y0 = e[z] + e[z - 4];
            y2 = e[z - 2] + e[z - 6];
            k22 = e[z - 2] - e[z - 6];

            e[z] = y0 + y2;
            e[z - 2] = y0 - y2;

            k33 = e[z - 3] - e[z - 7];

            e[z - 4] = k00 + k33;
            e[z - 6] = k00 - k33;

            k11 = e[z - 1] - e[z - 5];
            y1 = e[z - 1] + e[z - 5];
            y3 = e[z - 3] + e[z - 7];

            e[z - 1] = y1 + y3;
            e[z - 3] = y1 - y3;
            e[z - 5] = k11 - k22;
            e[z - 7] = k11 + k22;
        }
    }
}
#endregion //Mdct

#region NewStreamEventArgs
namespace NVorbis
{
    /// <summary>
    /// Event data for when a new logical stream is found in a container.
    /// </summary>
    public class NewStreamEventArgs : EventArgs
    {
        /// <summary>
        /// Creates a new instance of <see cref="NewStreamEventArgs"/> with the specified <see cref="IPacketProvider"/>.
        /// </summary>
        /// <param name="packetProvider">An <see cref="IPacketProvider"/> instance.</param>
        public NewStreamEventArgs(IPacketProvider packetProvider)
        {
            if (packetProvider == null) throw new ArgumentNullException("packetProvider");

            PacketProvider = packetProvider;
        }

        /// <summary>
        /// Gets new the <see cref="IPacketProvider"/> instance.
        /// </summary>
        public IPacketProvider PacketProvider { get; private set; }

        /// <summary>
        /// Gets or sets whether to ignore the logical stream associated with the packet provider.
        /// </summary>
        public bool IgnoreStream { get; set; }
    }
}
#endregion //NewStreamEventArgs

#region ParameterChangeEventArgs
namespace NVorbis
{
    /// <summary>
    /// Event data for when a logical stream has a parameter change.
    /// </summary>
    public class ParameterChangeEventArgs : EventArgs
    {
        /// <summary>
        /// Creates a new instance of <see cref="ParameterChangeEventArgs"/>.
        /// </summary>
        /// <param name="firstPacket">The first packet after the parameter change.</param>
        public ParameterChangeEventArgs(DataPacket firstPacket)
        {
            FirstPacket = firstPacket;
        }

        /// <summary>
        /// Gets the first packet after the parameter change.  This would typically be the parameters packet.
        /// </summary>
        public DataPacket FirstPacket { get; private set; }
    }
}
#endregion //ParameterChangeEventArgs

#region RingBuffer
namespace NVorbis
{
    class RingBuffer
    {
        float[] _buffer;
        int _start;
        int _end;
        int _bufLen;

        internal RingBuffer(int size)
        {
            _buffer = new float[size];
            _start = _end = 0;
            _bufLen = size;
        }

        internal void EnsureSize(int size)
        {
            // because _end == _start signifies no data, and _end is always 1 more than the data we have, we must make the buffer {channels} entries bigger than requested
            size += Channels;

            if (_bufLen < size)
            {
                var temp = new float[size];
                Array.Copy(_buffer, _start, temp, 0, _bufLen - _start);
                if (_end < _start)
                {
                    Array.Copy(_buffer, 0, temp, _bufLen - _start, _end);
                }
                var end = Length;
                _start = 0;
                _end = end;
                _buffer = temp;

                _bufLen = size;
            }
        }

        internal int Channels;

        internal void CopyTo(float[] buffer, int index, int count)
        {
            if (index < 0 || index + count > buffer.Length) throw new ArgumentOutOfRangeException("index");

            var start = _start;
            RemoveItems(count);

            // this is used to pull data out of the buffer, so we'll update the start position too...
            var len = (_end - start + _bufLen) % _bufLen;
            if (count > len) throw new ArgumentOutOfRangeException("count");

            var cnt = Math.Min(count, _bufLen - start);
            Array.Copy(_buffer, start, buffer, index, cnt);

            if (cnt < count)
            {
                Array.Copy(_buffer, 0, buffer, index + cnt, count - cnt);
            }
        }

        internal void RemoveItems(int count)
        {
            var cnt = (count + _start) % _bufLen;
            if (_end > _start)
            {
                if (cnt > _end || cnt < _start) throw new ArgumentOutOfRangeException();
            }
            else
            {
                // wrap-around
                if (cnt < _start && cnt > _end) throw new ArgumentOutOfRangeException();
            }

            _start = cnt;
        }

        internal void Clear()
        {
            _start = _end = 0;
        }

        internal int Length
        {
            get
            {
                var temp = _end - _start;
                if (temp < 0) temp += _bufLen;
                return temp;
            }
        }

        internal void Write(int channel, int index, int start, int switchPoint, int end, float[] pcm, float[] window)
        {
            // this is the index of the first sample to merge
            var idx = (index + start) * Channels + channel + _start;
            while (idx >= _bufLen)
            {
                idx -= _bufLen;
            }

            // blech...  gotta fix the first packet's pointers
            if (idx < 0)
            {
                start -= index;
                idx = channel;
            }

            // go through and do the overlap
            for (; idx < _bufLen && start < switchPoint; idx += Channels, ++start)
            {
                _buffer[idx] += pcm[start] * window[start];
            }
            if (idx >= _bufLen)
            {
                idx -= _bufLen;
                for (; start < switchPoint; idx += Channels, ++start)
                {
                    _buffer[idx] += pcm[start] * window[start];
                }
            }

            // go through and write the rest
            for (; idx < _bufLen && start < end; idx += Channels, ++start)
            {
                _buffer[idx] = pcm[start] * window[start];
            }
            if (idx >= _bufLen)
            {
                idx -= _bufLen;
                for (; start < end; idx += Channels, ++start)
                {
                    _buffer[idx] = pcm[start] * window[start];
                }
            }

            // finally, make sure the buffer end is set correctly
            _end = idx;
        }
    }
}
#endregion //RingBuffer

#region StreamReadBuffer
namespace NVorbis
{
    partial class StreamReadBuffer : IDisposable
    {
        class StreamWrapper
        {
            internal Stream Source;
            internal object LockObject = new object();
            internal long EofOffset = long.MaxValue;
            internal int RefCount = 1;
        }

        static Dictionary<Stream, StreamWrapper> _lockObjects = new Dictionary<Stream, StreamWrapper>();

        internal StreamReadBuffer(Stream source, int initialSize, int maxSize, bool minimalRead)
        {
            StreamWrapper wrapper;
            if (!_lockObjects.TryGetValue(source, out wrapper))
            {
                _lockObjects.Add(source, new StreamWrapper { Source = source });
                wrapper = _lockObjects[source];

                if (source.CanSeek)
                {
                    // assume that this is a quick operation
                    wrapper.EofOffset = source.Length;
                }
            }
            else
            {
                wrapper.RefCount++;
            }

            // make sure our initial size is a power of 2 (this makes resizing simpler to understand)
            initialSize = 2 << (int)Math.Log(initialSize - 1, 2);

            // make sure our max size is a power of 2 (in this case, just so we report a "real" number)
            maxSize = 1 << (int)Math.Log(maxSize, 2);

            _wrapper = wrapper;
            _data = new byte[initialSize];
            _maxSize = maxSize;
            _minimalRead = minimalRead;

            _savedBuffers = new List<SavedBuffer>();
        }

        public void Dispose()
        {
            if (--_wrapper.RefCount == 0)
            {
                _lockObjects.Remove(_wrapper.Source);
            }
        }

        StreamWrapper _wrapper;
        int _maxSize;

        byte[] _data;
        long _baseOffset;
        int _end;
        int _discardCount;

        bool _minimalRead;

        // we're locked already when we enter, so we can do whatever we need to do without worrying about it...
        class SavedBuffer
        {
            public byte[] Buffer;
            public long BaseOffset;
            public int End;
            public int DiscardCount;
            public long VersionSaved;
        }
        long _versionCounter;
        List<SavedBuffer> _savedBuffers;

        /// <summary>
        /// Gets or Sets whether to limit reads to the smallest size possible.
        /// </summary>
        public bool MinimalRead
        {
            get { return _minimalRead; }
            set { _minimalRead = value; }
        }

        /// <summary>
        /// Gets or Sets the maximum size of the buffer.  This is not a hard limit.
        /// </summary>
        public int MaxSize
        {
            get { return _maxSize; }
            set
            {
                if (value < 1) throw new ArgumentOutOfRangeException("Must be greater than zero.");

                var newMaxSize = 1 << (int)Math.Ceiling(Math.Log(value, 2));

                if (newMaxSize < _end)
                {
                    if (newMaxSize < _end - _discardCount)
                    {
                        // we can't discard enough bytes to satisfy the buffer request...
                        throw new ArgumentOutOfRangeException("Must be greater than or equal to the number of bytes currently buffered.");
                    }

                    CommitDiscard();
                    var newBuf = new byte[newMaxSize];
                    Buffer.BlockCopy(_data, 0, newBuf, 0, _end);
                    _data = newBuf;
                }
                _maxSize = newMaxSize;
            }
        }

        /// <summary>
        /// Gets the offset of the start of the buffered data.  Reads to offsets before this are likely to require a seek.
        /// </summary>
        public long BaseOffset
        {
            get { return _baseOffset + _discardCount; }
        }

        /// <summary>
        /// Gets the number of bytes currently buffered.
        /// </summary>
        public int BytesFilled
        {
            get { return _end - _discardCount; }
        }

        /// <summary>
        /// Gets the number of bytes the buffer can hold.
        /// </summary>
        public int Length
        {
            get { return _data.Length; }
        }

        internal long BufferEndOffset
        {
            get
            {
                if (_end - _discardCount > 0)
                {
                    // this is the base offset + discard bytes + buffer max length (though technically we could go a little further...)
                    return _baseOffset + _discardCount + _maxSize;
                }
                // if there aren't any bytes in the buffer, we can seek wherever we want
                return _wrapper.Source.Length;
            }
        }

        /// <summary>
        /// Reads the number of bytes specified into the buffer given, starting with the offset indicated.
        /// </summary>
        /// <param name="offset">The offset into the stream to start reading.</param>
        /// <param name="buffer">The buffer to read to.</param>
        /// <param name="index">The index into the buffer to start writing to.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>The number of bytes read.</returns>
        public int Read(long offset, byte[] buffer, int index, int count)
        {
            if (offset < 0L) throw new ArgumentOutOfRangeException("offset");
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (index < 0 || index + count > buffer.Length) throw new ArgumentOutOfRangeException("index");
            if (count < 0) throw new ArgumentOutOfRangeException("count");
            if (offset >= _wrapper.EofOffset) return 0;

            var startIdx = EnsureAvailable(offset, ref count, false);

            Buffer.BlockCopy(_data, startIdx, buffer, index, count);

            return count;
        }

        internal int ReadByte(long offset)
        {
            if (offset < 0L) throw new ArgumentOutOfRangeException("offset");
            if (offset >= _wrapper.EofOffset) return -1;

            int count = 1;
            var startIdx = EnsureAvailable(offset, ref count, false);
            if (count == 1)
            {
                return _data[startIdx];
            }
            return -1;
        }


        int EnsureAvailable(long offset, ref int count, bool isRecursion)
        {
            // simple... if we're inside the buffer, just return the offset (FAST PATH)
            if (offset >= _baseOffset && offset + count < _baseOffset + _end)
            {
                return (int)(offset - _baseOffset);
            }

            // not so simple... we're outside the buffer somehow...

            // let's make sure the request makes sense
            if (count > _maxSize)
            {
                throw new InvalidOperationException("Not enough room in the buffer!  Increase the maximum size and try again.");
            }

            // make sure we always bump the version counter when a change is made to the data in the "live" buffer
            ++_versionCounter;

            // can we satisfy the request with a saved buffer?
            if (!isRecursion)
            {
                for (int i = 0; i < _savedBuffers.Count; i++)
                {
                    var tempS = _savedBuffers[i].BaseOffset - offset;
                    if ((tempS < 0 && _savedBuffers[i].End + tempS > 0) || (tempS > 0 && count - tempS > 0))
                    {
                        SwapBuffers(_savedBuffers[i]);
                        return EnsureAvailable(offset, ref count, true);
                    }
                }
            }

            // look for buffers we need to drop due to age...
            while (_savedBuffers.Count > 0 && _savedBuffers[0].VersionSaved + 25 < _versionCounter)
            {
                _savedBuffers[0].Buffer = null;
                _savedBuffers.RemoveAt(0);
            }

            // if we have to seek back, we're doomed...
            if (offset < _baseOffset && !_wrapper.Source.CanSeek)
            {
                throw new InvalidOperationException("Cannot seek before buffer on forward-only streams!");
            }

            // figure up the new buffer parameters...
            int readStart;
            int readEnd;
            CalcBuffer(offset, count, out readStart, out readEnd);

            // fill the buffer...
            // if we did a reverse seek, there will be data still in end of the buffer...  Make sure to fill everything between
            count = FillBuffer(offset, count, readStart, readEnd);

            return (int)(offset - _baseOffset);
        }

        void SaveBuffer()
        {
            _savedBuffers.Add(
                new SavedBuffer
                {
                    Buffer = _data,
                    BaseOffset = _baseOffset,
                    End = _end,
                    DiscardCount = _discardCount,
                    VersionSaved = _versionCounter
                }
            );

            _data = null;
            _end = 0;
            _discardCount = 0;
        }

        void CreateNewBuffer(long offset, int count)
        {
            SaveBuffer();

            _data = new byte[Math.Min(2 << (int)Math.Log(count - 1, 2), _maxSize)];
            _baseOffset = offset;
        }

        void SwapBuffers(SavedBuffer savedBuffer)
        {
            _savedBuffers.Remove(savedBuffer);
            SaveBuffer();
            _data = savedBuffer.Buffer;
            _baseOffset = savedBuffer.BaseOffset;
            _end = savedBuffer.End;
            _discardCount = savedBuffer.DiscardCount;
        }

        void CalcBuffer(long offset, int count, out int readStart, out int readEnd)
        {
            readStart = 0;
            readEnd = 0;
            if (offset < _baseOffset)
            {
                // try to overlap the end...
                if (offset + _maxSize <= _baseOffset)
                {
                    // nope...
                    if (_baseOffset - (offset + _maxSize) > _maxSize)
                    {
                        // it's probably best to cache this buffer for a bit
                        CreateNewBuffer(offset, count);
                    }
                    else
                    {
                        // don't worry about caching...
                        EnsureBufferSize(count, false, 0);
                    }
                    _baseOffset = offset;
                    readEnd = count;
                }
                else
                {
                    // we have at least some overlap
                    readEnd = (int)(offset - _baseOffset);
                    EnsureBufferSize(Math.Min((int)(offset + _maxSize - _baseOffset), _end) - readEnd, true, readEnd);
                    readEnd = (int)(offset - _baseOffset) - readEnd;
                }
            }
            else
            {
                // try to overlap the beginning...
                if (offset >= _baseOffset + _maxSize)
                {
                    // nope...
                    if (offset - (_baseOffset + _maxSize) > _maxSize)
                    {
                        CreateNewBuffer(offset, count);
                    }
                    else
                    {
                        EnsureBufferSize(count, false, 0);
                    }
                    _baseOffset = offset;
                    readEnd = count;
                }
                else
                {
                    // we have at least some overlap
                    readEnd = (int)(offset + count - _baseOffset);
                    var ofs = Math.Max(readEnd - _maxSize, 0);
                    EnsureBufferSize(readEnd - ofs, true, ofs);
                    readStart = _end;
                    // re-pull in case EnsureBufferSize had to discard...
                    readEnd = (int)(offset + count - _baseOffset);
                }
            }
        }

        void EnsureBufferSize(int reqSize, bool copyContents, int copyOffset)
        {
            byte[] newBuf = _data;
            if (reqSize > _data.Length)
            {
                if (reqSize > _maxSize)
                {
                    if (_wrapper.Source.CanSeek || reqSize - _discardCount <= _maxSize)
                    {
                        // lose some of the earlier data...
                        var ofs = reqSize - _maxSize;
                        copyOffset += ofs;
                        reqSize = _maxSize;
                    }
                    else
                    {
                        throw new InvalidOperationException("Not enough room in the buffer!  Increase the maximum size and try again.");
                    }
                }
                else
                {
                    // find the new size
                    var size = _data.Length;
                    while (size < reqSize)
                    {
                        size *= 2;
                    }
                    reqSize = size;
                }

                // if we discarded some bytes above, don't resize the buffer unless we have to...
                if (reqSize > _data.Length)
                {
                    newBuf = new byte[reqSize];
                }
            }

            if (copyContents)
            {
                // adjust the position of the data
                if ((copyOffset > 0 && copyOffset < _end) || (copyOffset == 0 && newBuf != _data))
                {
                    // copy forward
                    Buffer.BlockCopy(_data, copyOffset, newBuf, 0, _end - copyOffset);

                    // adjust our discard count
                    if ((_discardCount -= copyOffset) < 0) _discardCount = 0;
                }
                else if (copyOffset < 0 && -copyOffset < _end)
                {
                    // copy backward
                    // be clever... if we're moving to a new buffer or the ranges don't overlap, just use a block copy
                    if (newBuf != _data || _end <= -copyOffset)
                    {
                        Buffer.BlockCopy(_data, 0, newBuf, -copyOffset, Math.Max(_end, Math.Min(_end, _data.Length + copyOffset)));
                    }
                    else
                    {
                        // this shouldn't happen often, so we can get away with a full buffer refill
                        _end = copyOffset;
                    }

                    // adjust our discard count
                    _discardCount = 0;
                }
                else
                {
                    _end = copyOffset;
                    _discardCount = 0;
                }

                // adjust our markers
                _baseOffset += copyOffset;
                _end -= copyOffset;
                if (_end > newBuf.Length) _end = newBuf.Length;
            }
            else
            {
                _discardCount = 0;
                // we can't set _baseOffset since our caller hasn't told us what it should be...
                _end = 0;
            }

            _data = newBuf;
        }

        int FillBuffer(long offset, int count, int readStart, int readEnd)
        {
            var readOffset = _baseOffset + readStart;
            var readCount = readEnd - readStart;

            lock (_wrapper.LockObject)
            {
                readCount = PrepareStreamForRead(readCount, readOffset);

                ReadStream(readStart, readCount, readOffset);

                // check for full read...
                if (_end < readStart + readCount)
                {
                    count = Math.Max(0, (int)(_baseOffset + _end - offset));
                }
                else if (!_minimalRead && _end < _data.Length)
                {
                    // try to finish filling the buffer
                    readCount = _data.Length - _end;
                    readCount = PrepareStreamForRead(readCount, _baseOffset + _end);
                    _end += _wrapper.Source.Read(_data, _end, readCount);
                }
            }
            return count;
        }

        int PrepareStreamForRead(int readCount, long readOffset)
        {
            if (readCount > 0 && _wrapper.Source.Position != readOffset)
            {
                if (readOffset < _wrapper.EofOffset)
                {
                    if (_wrapper.Source.CanSeek)
                    {
                        _wrapper.Source.Position = readOffset;
                    }
                    else
                    {
                        // ugh, gotta read bytes until we've reached the desired offset
                        var seekCount = readOffset - _wrapper.Source.Position;
                        if (seekCount < 0)
                        {
                            // not so fast... we can't seek backwards.  This technically shouldn't happen, but just in case...
                            readCount = 0;
                        }
                        else
                        {
                            while (--seekCount >= 0)
                            {
                                if (_wrapper.Source.ReadByte() == -1)
                                {
                                    // crap... we just threw away a bunch of bytes for no reason
                                    _wrapper.EofOffset = _wrapper.Source.Position;
                                    readCount = 0;
                                    break;
                                }
                            }
                        }
                    }
                }
                else
                {
                    readCount = 0;
                }
            }
            return readCount;
        }

        void ReadStream(int readStart, int readCount, long readOffset)
        {
            while (readCount > 0 && readOffset < _wrapper.EofOffset)
            {
                var temp = _wrapper.Source.Read(_data, readStart, readCount);
                if (temp == 0)
                {
                    break;
                }
                readStart += temp;
                readOffset += temp;
                readCount -= temp;
            }

            if (readStart > _end)
            {
                _end = readStart;
            }
        }

        /// <summary>
        /// Tells the buffer that it no longer needs to maintain any bytes before the indicated offset.
        /// </summary>
        /// <param name="offset">The offset to discard through.</param>
        public void DiscardThrough(long offset)
        {
            var count = (int)(offset - _baseOffset);
            _discardCount = Math.Max(count, _discardCount);

            if (_discardCount >= _data.Length) CommitDiscard();
        }

        void CommitDiscard()
        {
            if (_discardCount >= _data.Length || _discardCount >= _end)
            {
                // we have been told to discard the entire buffer
                _baseOffset += _discardCount;
                _end = 0;
            }
            else
            {
                // just discard the first part...
                Buffer.BlockCopy(_data, _discardCount, _data, 0, _end - _discardCount);
                _baseOffset += _discardCount;
                _end -= _discardCount;
            }
            _discardCount = 0;
        }
    }
}
#endregion //StreamReadBuffer

#region Utils
namespace NVorbis
{
    static class Utils
    {
        static internal int ilog(int x)
        {
            int cnt = 0;
            while (x > 0)
            {
                ++cnt;
                x >>= 1;    // this is safe because we'll never get here if the sign bit is set
            }
            return cnt;
        }

        static internal uint BitReverse(uint n)
        {
            return BitReverse(n, 32);
        }

        static internal uint BitReverse(uint n, int bits)
        {
            n = ((n & 0xAAAAAAAA) >> 1) | ((n & 0x55555555) << 1);
            n = ((n & 0xCCCCCCCC) >> 2) | ((n & 0x33333333) << 2);
            n = ((n & 0xF0F0F0F0) >> 4) | ((n & 0x0F0F0F0F) << 4);
            n = ((n & 0xFF00FF00) >> 8) | ((n & 0x00FF00FF) << 8);
            return ((n >> 16) | (n << 16)) >> (32 - bits);
        }

        // make it so we can twiddle bits in a float...
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        struct FloatBits
        {
            [System.Runtime.InteropServices.FieldOffset(0)]
            public float Float;
            [System.Runtime.InteropServices.FieldOffset(0)]
            public uint Bits;
        }

        static internal float ClipValue(float value, ref bool clipped)
        {
            /************
             * There is some magic happening here... IEEE 754 single precision floats are built such that:
             *   1) The only difference between x and -x is the sign bit (31)
             *   2) If x is further from 0 than y, the bitwise value of x is greater than the bitwise value of y (ignoring the sign bit)
             * 
             * With those assumptions, we can just look for the bitwise magnitude to be too large...
             */

            FloatBits fb;
            fb.Bits = 0;
            fb.Float = value;

            // as a courtesy to those writing out 24-bit and 16-bit samples, our full scale is 0.99999994 instead of 1.0
            if ((fb.Bits & 0x7FFFFFFF) > 0x3f7fffff) // 0x3f7fffff == 0.99999994f
            {
                clipped = true;
                fb.Bits = 0x3f7fffff | (fb.Bits & 0x80000000);
            }
            return fb.Float;
        }

        static internal float ConvertFromVorbisFloat32(uint bits)
        {
            // do as much as possible with bit tricks in integer math
            var sign = ((int)bits >> 31);   // sign-extend to the full 32-bits
            var exponent = (double)((int)((bits & 0x7fe00000) >> 21) - 788);  // grab the exponent, remove the bias, store as double (for the call to System.Math.Pow(...))
            var mantissa = (float)(((bits & 0x1fffff) ^ sign) + (sign & 1));  // grab the mantissa and apply the sign bit.  store as float

            // NB: We could use bit tricks to calc the exponent, but it can't be more than 63 in either direction.
            //     This creates an issue, since the exponent field allows for a *lot* more than that.
            //     On the flip side, larger exponent values don't seem to be used by the Vorbis codebooks...
            //     Either way, we'll play it safe and let the BCL calculate it.

            // now switch to single-precision and calc the return value
            return mantissa * (float)System.Math.Pow(2.0, exponent);
        }

        // this is a no-allocation way to sum an int queue
        static internal int Sum(System.Collections.Generic.Queue<int> queue)
        {
            var value = 0;
            for (int i = 0; i < queue.Count; i++)
            {
                var temp = queue.Dequeue();
                value += temp;
                queue.Enqueue(temp);
            }
            return value;
        }
    }
}
#endregion //Utils

#region VorbisCodebook
namespace NVorbis
{
    class VorbisCodebook
    {
        internal static VorbisCodebook Init(VorbisStreamDecoder vorbis, DataPacket packet, int number)
        {
            var temp = new VorbisCodebook();
            temp.BookNum = number;
            temp.Init(packet);
            return temp;
        }

        private VorbisCodebook()
        {

        }

        internal void Init(DataPacket packet)
        {
            // first, check the sync pattern
            var chkVal = packet.ReadBits(24);
            if (chkVal != 0x564342UL) throw new InvalidDataException();

            // get the counts
            Dimensions = (int)packet.ReadBits(16);
            Entries = (int)packet.ReadBits(24);

            // init the storage
            Lengths = new int[Entries];

            InitTree(packet);
            InitLookupTable(packet);
        }

        void InitTree(DataPacket packet)
        {
            bool sparse;
            int total = 0;

            if (packet.ReadBit())
            {
                // ordered
                var len = (int)packet.ReadBits(5) + 1;
                for (var i = 0; i < Entries; )
                {
                    var cnt = (int)packet.ReadBits(Utils.ilog(Entries - i));

                    while (--cnt >= 0)
                    {
                        Lengths[i++] = len;
                    }

                    ++len;
                }
                total = 0;
                sparse = false;
            }
            else
            {
                // unordered
                sparse = packet.ReadBit();
                for (var i = 0; i < Entries; i++)
                {
                    if (!sparse || packet.ReadBit())
                    {
                        Lengths[i] = (int)packet.ReadBits(5) + 1;
                        ++total;
                    }
                    else
                    {
                        Lengths[i] = -1;
                    }
                }
            }
            MaxBits = Lengths.Max();

            int sortedCount = 0;
            int[] codewordLengths = null;
            if (sparse && total >= Entries >> 2)
            {
                codewordLengths = new int[Entries];
                Array.Copy(Lengths, codewordLengths, Entries);

                sparse = false;
            }

            // compute size of sorted tables
            if (sparse)
            {
                sortedCount = total;
            }
            else
            {
                sortedCount = 0;
            }

            int sortedEntries = sortedCount;

            int[] values = null;
            int[] codewords = null;
            if (!sparse)
            {
                codewords = new int[Entries];
            }
            else if (sortedEntries != 0)
            {
                codewordLengths = new int[sortedEntries];
                codewords = new int[sortedEntries];
                values = new int[sortedEntries];
            }

            if (!ComputeCodewords(sparse, sortedEntries, codewords, codewordLengths, len: Lengths, n: Entries, values: values)) throw new InvalidDataException();

            PrefixList = Huffman.BuildPrefixedLinkedList(values ?? Enumerable.Range(0, codewords.Length).ToArray(), codewordLengths ?? Lengths, codewords, out PrefixBitLength, out PrefixOverflowTree);
        }

        bool ComputeCodewords(bool sparse, int sortedEntries, int[] codewords, int[] codewordLengths, int[] len, int n, int[] values)
        {
            int i, k, m = 0;
            uint[] available = new uint[32];

            for (k = 0; k < n; ++k) if (len[k] > 0) break;
            if (k == n) return true;

            AddEntry(sparse, codewords, codewordLengths, 0, k, m++, len[k], values);

            for (i = 1; i <= len[k]; ++i) available[i] = 1U << (32 - i);

            for (i = k + 1; i < n; ++i)
            {
                uint res;
                int z = len[i], y;
                if (z <= 0) continue;

                while (z > 0 && available[z] == 0) --z;
                if (z == 0) return false;
                res = available[z];
                available[z] = 0;
                AddEntry(sparse, codewords, codewordLengths, Utils.BitReverse(res), i, m++, len[i], values);

                if (z != len[i])
                {
                    for (y = len[i]; y > z; --y)
                    {
                        available[y] = res + (1U << (32 - y));
                    }
                }
            }

            return true;
        }

        void AddEntry(bool sparse, int[] codewords, int[] codewordLengths, uint huffCode, int symbol, int count, int len, int[] values)
        {
            if (sparse)
            {
                codewords[count] = (int)huffCode;
                codewordLengths[count] = len;
                values[count] = symbol;
            }
            else
            {
                codewords[symbol] = (int)huffCode;
            }
        }

        void InitLookupTable(DataPacket packet)
        {
            MapType = (int)packet.ReadBits(4);
            if (MapType == 0) return;

            var minValue = Utils.ConvertFromVorbisFloat32(packet.ReadUInt32());
            var deltaValue = Utils.ConvertFromVorbisFloat32(packet.ReadUInt32());
            var valueBits = (int)packet.ReadBits(4) + 1;
            var sequence_p = packet.ReadBit();

            var lookupValueCount = Entries * Dimensions;
            var lookupTable = new float[lookupValueCount];
            if (MapType == 1)
            {
                lookupValueCount = lookup1_values();
            }

            var multiplicands = new uint[lookupValueCount];
            for (var i = 0; i < lookupValueCount; i++)
            {
                multiplicands[i] = (uint)packet.ReadBits(valueBits);
            }

            // now that we have the initial data read in, calculate the entry tree
            if (MapType == 1)
            {
                for (var idx = 0; idx < Entries; idx++)
                {
                    var last = 0.0;
                    var idxDiv = 1;
                    for (var i = 0; i < Dimensions; i++)
                    {
                        var moff = (idx / idxDiv) % lookupValueCount;
                        var value = (float)multiplicands[moff] * deltaValue + minValue + last;
                        lookupTable[idx * Dimensions + i] = (float)value;

                        if (sequence_p) last = value;

                        idxDiv *= lookupValueCount;
                    }
                }
            }
            else
            {
                for (var idx = 0; idx < Entries; idx++)
                {
                    var last = 0.0;
                    var moff = idx * Dimensions;
                    for (var i = 0; i < Dimensions; i++)
                    {
                        var value = multiplicands[moff] * deltaValue + minValue + last;
                        lookupTable[idx * Dimensions + i] = (float)value;

                        if (sequence_p) last = value;

                        ++moff;
                    }
                }
            }

            LookupTable = lookupTable;
        }

        int lookup1_values()
        {
            var r = (int)Math.Floor(Math.Exp(Math.Log(Entries) / Dimensions));

            if (Math.Floor(Math.Pow(r + 1, Dimensions)) <= Entries) ++r;

            return r;
        }

        internal int BookNum;

        internal int Dimensions;

        internal int Entries;

        int[] Lengths;

        float[] LookupTable;

        internal int MapType;

        HuffmanListNode PrefixOverflowTree;
        System.Collections.Generic.List<HuffmanListNode> PrefixList;
        int PrefixBitLength;
        int MaxBits;


        internal float this[int entry, int dim]
        {
            get
            {
                return LookupTable[entry * Dimensions + dim];
            }
        }

        internal int DecodeScalar(DataPacket packet)
        {
            int bitCnt;
            var bits = (int)packet.TryPeekBits(PrefixBitLength, out bitCnt);
            if (bitCnt == 0) return -1;

            // try to get the value from the prefix list...
            var node = PrefixList[bits];
            if (node != null)
            {
                packet.SkipBits(node.Length);
                return node.Value;
            }

            // nope, not possible... run the tree
            bits = (int)packet.TryPeekBits(MaxBits, out bitCnt);

            node = PrefixOverflowTree;
            do
            {
                if (node.Bits == (bits & node.Mask))
                {
                    packet.SkipBits(node.Length);
                    return node.Value;
                }
            } while ((node = node.Next) != null);
            return -1;
        }
    }
}
#endregion //VorbisCodebook

#region VorbisFloor
namespace NVorbis
{
    abstract class VorbisFloor
    {
        internal static VorbisFloor Init(VorbisStreamDecoder vorbis, DataPacket packet)
        {
            var type = (int)packet.ReadBits(16);

            VorbisFloor floor = null;
            switch (type)
            {
                case 0: floor = new Floor0(vorbis); break;
                case 1: floor = new Floor1(vorbis); break;
            }
            if (floor == null) throw new InvalidDataException();

            floor.Init(packet);
            return floor;
        }

        VorbisStreamDecoder _vorbis;

        protected VorbisFloor(VorbisStreamDecoder vorbis)
        {
            _vorbis = vorbis;
        }

        abstract protected void Init(DataPacket packet);

        abstract internal PacketData UnpackPacket(DataPacket packet, int blockSize, int channel);

        abstract internal void Apply(PacketData packetData, float[] residue);

        abstract internal class PacketData
        {
            internal int BlockSize;
            abstract protected bool HasEnergy { get; }
            internal bool ForceEnergy { get; set; }
            internal bool ForceNoEnergy { get; set; }

            internal bool ExecuteChannel
            {
                // if we have energy or are forcing energy, return !ForceNoEnergy, else false
                get { return (ForceEnergy | HasEnergy) & !ForceNoEnergy; }
            }
        }

        class Floor0 : VorbisFloor
        {
            internal Floor0(VorbisStreamDecoder vorbis) : base(vorbis) { }

            int _order, _rate, _bark_map_size, _ampBits, _ampOfs, _ampDiv;
            VorbisCodebook[] _books;
            int _bookBits;
            Dictionary<int, float[]> _wMap;
            Dictionary<int, int[]> _barkMaps;

            protected override void Init(DataPacket packet)
            {
                // this is pretty well stolen directly from libvorbis...  BSD license
                _order = (int)packet.ReadBits(8);
                _rate = (int)packet.ReadBits(16);
                _bark_map_size = (int)packet.ReadBits(16);
                _ampBits = (int)packet.ReadBits(6);
                _ampOfs = (int)packet.ReadBits(8);
                _books = new VorbisCodebook[(int)packet.ReadBits(4) + 1];

                if (_order < 1 || _rate < 1 || _bark_map_size < 1 || _books.Length == 0) throw new InvalidDataException();

                _ampDiv = (1 << _ampBits) - 1;

                for (int i = 0; i < _books.Length; i++)
                {
                    var num = (int)packet.ReadBits(8);
                    if (num < 0 || num >= _vorbis.Books.Length) throw new InvalidDataException();
                    var book = _vorbis.Books[num];

                    if (book.MapType == 0 || book.Dimensions < 1) throw new InvalidDataException();

                    _books[i] = book;
                }
                _bookBits = Utils.ilog(_books.Length);

                _barkMaps = new Dictionary<int, int[]>();
                _barkMaps[_vorbis.Block0Size] = SynthesizeBarkCurve(_vorbis.Block0Size / 2);
                _barkMaps[_vorbis.Block1Size] = SynthesizeBarkCurve(_vorbis.Block1Size / 2);

                _wMap = new Dictionary<int, float[]>();
                _wMap[_vorbis.Block0Size] = SynthesizeWDelMap(_vorbis.Block0Size / 2);
                _wMap[_vorbis.Block1Size] = SynthesizeWDelMap(_vorbis.Block1Size / 2);

                _reusablePacketData = new PacketData0[_vorbis._channels];
                for (int i = 0; i < _reusablePacketData.Length; i++)
                {
                    _reusablePacketData[i] = new PacketData0() { Coeff = new float[_order + 1] };
                }
            }

            int[] SynthesizeBarkCurve(int n)
            {
                var scale = _bark_map_size / toBARK(_rate / 2);

                var map = new int[n + 1];

                for (int i = 0; i < n - 1; i++)
                {
                    map[i] = Math.Min(_bark_map_size - 1, (int)Math.Floor(toBARK((_rate / 2f) / n * i) * scale));
                }
                map[n] = -1;
                return map;
            }

            static float toBARK(double lsp)
            {
                return (float)(13.1 * Math.Atan(0.00074 * lsp) + 2.24 * Math.Atan(0.0000000185 * lsp * lsp) + .0001 * lsp);
            }

            float[] SynthesizeWDelMap(int n)
            {
                var wdel = (float)(Math.PI / _bark_map_size);

                var map = new float[n];
                for (int i = 0; i < n; i++)
                {
                    map[i] = 2f * (float)Math.Cos(wdel * i);
                }
                return map;
            }

            class PacketData0 : PacketData
            {
                protected override bool HasEnergy
                {
                    get { return Amp > 0f; }
                }

                internal float[] Coeff;
                internal float Amp;
            }

            PacketData0[] _reusablePacketData;

            internal override PacketData UnpackPacket(DataPacket packet, int blockSize, int channel)
            {
                var data = _reusablePacketData[channel];
                data.BlockSize = blockSize;
                data.ForceEnergy = false;
                data.ForceNoEnergy = false;

                data.Amp = packet.ReadBits(_ampBits);
                if (data.Amp > 0f)
                {
                    // this is pretty well stolen directly from libvorbis...  BSD license
                    Array.Clear(data.Coeff, 0, data.Coeff.Length);

                    data.Amp = (float)(data.Amp / _ampDiv * _ampOfs);

                    var bookNum = (uint)packet.ReadBits(_bookBits);
                    if (bookNum >= _books.Length)
                    {
                        // we ran out of data or the packet is corrupt...  0 the floor and return
                        data.Amp = 0;
                        return data;
                    }
                    var book = _books[bookNum];

                    // first, the book decode...
                    for (int i = 0; i < _order; )
                    {
                        var entry = book.DecodeScalar(packet);
                        if (entry == -1)
                        {
                            // we ran out of data or the packet is corrupt...  0 the floor and return
                            data.Amp = 0;
                            return data;
                        }
                        for (int j = 0; i < _order && j < book.Dimensions; j++, i++)
                        {
                            data.Coeff[i] = book[entry, j];
                        }
                    }

                    // then, the "averaging"
                    var last = 0f;
                    for (int j = 0; j < _order; )
                    {
                        for (int k = 0; j < _order && k < book.Dimensions; j++, k++)
                        {
                            data.Coeff[j] += last;
                        }
                        last = data.Coeff[j - 1];
                    }
                }
                return data;
            }

            internal override void Apply(PacketData packetData, float[] residue)
            {
                var data = packetData as PacketData0;
                if (data == null) throw new ArgumentException("Incorrect packet data!");

                var n = data.BlockSize / 2;

                if (data.Amp > 0f)
                {
                    // this is pretty well stolen directly from libvorbis...  BSD license
                    var barkMap = _barkMaps[data.BlockSize];
                    var wMap = _wMap[data.BlockSize];

                    int i = 0;
                    for (i = 0; i < _order; i++)
                    {
                        data.Coeff[i] = 2f * (float)Math.Cos(data.Coeff[i]);
                    }

                    i = 0;
                    while (i < n)
                    {
                        int j;
                        var k = barkMap[i];
                        var p = .5f;
                        var q = .5f;
                        var w = wMap[k];
                        for (j = 1; j < _order; j += 2)
                        {
                            q *= w - data.Coeff[j - 1];
                            p *= w - data.Coeff[j];
                        }
                        if (j == _order)
                        {
                            // odd order filter; slightly assymetric
                            q *= w - data.Coeff[j - 1];
                            p *= p * (4f - w * w);
                            q *= q;
                        }
                        else
                        {
                            // even order filter; still symetric
                            p *= p * (2f - w);
                            q *= q * (2f + w);
                        }

                        // calc the dB of this bark section
                        q = data.Amp / (float)Math.Sqrt(p + q) - _ampOfs;

                        // now convert to a linear sample multiplier
                        q = (float)Math.Exp(q * 0.11512925f);

                        residue[i] *= q;

                        while (barkMap[++i] == k) residue[i] *= q;
                    }
                }
                else
                {
                    Array.Clear(residue, 0, n);
                }
            }
        }

        class Floor1 : VorbisFloor
        {
            internal Floor1(VorbisStreamDecoder vorbis) : base(vorbis) { }

            int[] _partitionClass, _classDimensions, _classSubclasses, _xList, _classMasterBookIndex, _hNeigh, _lNeigh, _sortIdx;
            int _multiplier, _range, _yBits;
            VorbisCodebook[] _classMasterbooks;
            VorbisCodebook[][] _subclassBooks;
            int[][] _subclassBookIndex;

            static int[] _rangeLookup = { 256, 128, 86, 64 };
            static int[] _yBitsLookup = { 8, 7, 7, 6 };

            protected override void Init(DataPacket packet)
            {
                _partitionClass = new int[(int)packet.ReadBits(5)];
                for (int i = 0; i < _partitionClass.Length; i++)
                {
                    _partitionClass[i] = (int)packet.ReadBits(4);
                }

                var maximum_class = _partitionClass.Max();
                _classDimensions = new int[maximum_class + 1];
                _classSubclasses = new int[maximum_class + 1];
                _classMasterbooks = new VorbisCodebook[maximum_class + 1];
                _classMasterBookIndex = new int[maximum_class + 1];
                _subclassBooks = new VorbisCodebook[maximum_class + 1][];
                _subclassBookIndex = new int[maximum_class + 1][];
                for (int i = 0; i <= maximum_class; i++)
                {
                    _classDimensions[i] = (int)packet.ReadBits(3) + 1;
                    _classSubclasses[i] = (int)packet.ReadBits(2);
                    if (_classSubclasses[i] > 0)
                    {
                        _classMasterBookIndex[i] = (int)packet.ReadBits(8);
                        _classMasterbooks[i] = _vorbis.Books[_classMasterBookIndex[i]];
                    }

                    _subclassBooks[i] = new VorbisCodebook[1 << _classSubclasses[i]];
                    _subclassBookIndex[i] = new int[_subclassBooks[i].Length];
                    for (int j = 0; j < _subclassBooks[i].Length; j++)
                    {
                        var bookNum = (int)packet.ReadBits(8) - 1;
                        if (bookNum >= 0) _subclassBooks[i][j] = _vorbis.Books[bookNum];
                        _subclassBookIndex[i][j] = bookNum;
                    }
                }

                _multiplier = (int)packet.ReadBits(2);

                _range = _rangeLookup[_multiplier];
                _yBits = _yBitsLookup[_multiplier];

                ++_multiplier;

                var rangeBits = (int)packet.ReadBits(4);

                var xList = new List<int>();
                xList.Add(0);
                xList.Add(1 << rangeBits);

                for (int i = 0; i < _partitionClass.Length; i++)
                {
                    var classNum = _partitionClass[i];
                    for (int j = 0; j < _classDimensions[classNum]; j++)
                    {
                        xList.Add((int)packet.ReadBits(rangeBits));
                    }
                }
                _xList = xList.ToArray();

                // precalc the low and high neighbors (and init the sort table)
                _lNeigh = new int[xList.Count];
                _hNeigh = new int[xList.Count];
                _sortIdx = new int[xList.Count];
                _sortIdx[0] = 0;
                _sortIdx[1] = 1;
                for (int i = 2; i < _lNeigh.Length; i++)
                {
                    _lNeigh[i] = 0;
                    _hNeigh[i] = 1;
                    _sortIdx[i] = i;
                    for (int j = 2; j < i; j++)
                    {
                        var temp = _xList[j];
                        if (temp < _xList[i])
                        {
                            if (temp > _xList[_lNeigh[i]]) _lNeigh[i] = j;
                        }
                        else
                        {
                            if (temp < _xList[_hNeigh[i]]) _hNeigh[i] = j;
                        }
                    }
                }

                // precalc the sort table
                for (int i = 0; i < _sortIdx.Length - 1; i++)
                {
                    for (int j = i + 1; j < _sortIdx.Length; j++)
                    {
                        if (_xList[i] == _xList[j]) throw new InvalidDataException();

                        if (_xList[_sortIdx[i]] > _xList[_sortIdx[j]])
                        {
                            // swap the sort indexes
                            var temp = _sortIdx[i];
                            _sortIdx[i] = _sortIdx[j];
                            _sortIdx[j] = temp;
                        }
                    }
                }

                // pre-create our packet data instances
                _reusablePacketData = new PacketData1[_vorbis._channels];
                for (int i = 0; i < _reusablePacketData.Length; i++)
                {
                    _reusablePacketData[i] = new PacketData1();
                }
            }

            class PacketData1 : PacketData
            {
                protected override bool HasEnergy
                {
                    get { return PostCount > 0; }
                }

                public int[] Posts = new int[64];
                public int PostCount;
            }

            PacketData1[] _reusablePacketData;

            internal override PacketData UnpackPacket(DataPacket packet, int blockSize, int channel)
            {
                var data = _reusablePacketData[channel];
                data.BlockSize = blockSize;
                data.ForceEnergy = false;
                data.ForceNoEnergy = false;
                data.PostCount = 0;
                Array.Clear(data.Posts, 0, 64);

                // hoist ReadPosts to here since that's all we're doing...
                if (packet.ReadBit())
                {
                    var postCount = 2;
                    data.Posts[0] = (int)packet.ReadBits(_yBits);
                    data.Posts[1] = (int)packet.ReadBits(_yBits);

                    for (int i = 0; i < _partitionClass.Length; i++)
                    {
                        var clsNum = _partitionClass[i];
                        var cdim = _classDimensions[clsNum];
                        var cbits = _classSubclasses[clsNum];
                        var csub = (1 << cbits) - 1;
                        var cval = 0U;
                        if (cbits > 0)
                        {
                            if ((cval = (uint)_classMasterbooks[clsNum].DecodeScalar(packet)) == uint.MaxValue)
                            {
                                // we read a bad value...  bail
                                postCount = 0;
                                break;
                            }
                        }
                        for (int j = 0; j < cdim; j++)
                        {
                            var book = _subclassBooks[clsNum][cval & csub];
                            cval >>= cbits;
                            if (book != null)
                            {
                                if ((data.Posts[postCount] = book.DecodeScalar(packet)) == -1)
                                {
                                    // we read a bad value... bail
                                    postCount = 0;
                                    i = _partitionClass.Length;
                                    break;
                                }
                            }
                            ++postCount;
                        }
                    }

                    data.PostCount = postCount;
                }

                return data;
            }

            internal override void Apply(PacketData packetData, float[] residue)
            {
                var data = packetData as PacketData1;
                if (data == null) throw new ArgumentException("Incorrect packet data!", "packetData");

                var n = data.BlockSize / 2;

                if (data.PostCount > 0)
                {
                    var stepFlags = UnwrapPosts(data);

                    var lx = 0;
                    var ly = data.Posts[0] * _multiplier;
                    for (int i = 1; i < data.PostCount; i++)
                    {
                        var idx = _sortIdx[i];

                        if (stepFlags[idx])
                        {
                            var hx = _xList[idx];
                            var hy = data.Posts[idx] * _multiplier;
                            if (lx < n) RenderLineMulti(lx, ly, Math.Min(hx, n), hy, residue);
                            lx = hx;
                            ly = hy;
                        }
                        if (lx >= n) break;
                    }

                    if (lx < n)
                    {
                        RenderLineMulti(lx, ly, n, ly, residue);
                    }
                }
                else
                {
                    Array.Clear(residue, 0, n);
                }
            }

            bool[] _stepFlags = new bool[64];
            int[] _finalY = new int[64];

            bool[] UnwrapPosts(PacketData1 data)
            {
                Array.Clear(_stepFlags, 2, 62);
                _stepFlags[0] = true;
                _stepFlags[1] = true;

                Array.Clear(_finalY, 2, 62);
                _finalY[0] = data.Posts[0];
                _finalY[1] = data.Posts[1];

                for (int i = 2; i < data.PostCount; i++)
                {
                    var lowOfs = _lNeigh[i];
                    var highOfs = _hNeigh[i];

                    var predicted = RenderPoint(_xList[lowOfs], _finalY[lowOfs], _xList[highOfs], _finalY[highOfs], _xList[i]);

                    var val = data.Posts[i];
                    var highroom = _range - predicted;
                    var lowroom = predicted;
                    int room;
                    if (highroom < lowroom)
                    {
                        room = highroom * 2;
                    }
                    else
                    {
                        room = lowroom * 2;
                    }
                    if (val != 0)
                    {
                        _stepFlags[lowOfs] = true;
                        _stepFlags[highOfs] = true;
                        _stepFlags[i] = true;

                        if (val >= room)
                        {
                            if (highroom > lowroom)
                            {
                                _finalY[i] = val - lowroom + predicted;
                            }
                            else
                            {
                                _finalY[i] = predicted - val + highroom - 1;
                            }
                        }
                        else
                        {
                            if ((val % 2) == 1)
                            {
                                // odd
                                _finalY[i] = predicted - ((val + 1) / 2);
                            }
                            else
                            {
                                // even
                                _finalY[i] = predicted + (val / 2);
                            }
                        }
                    }
                    else
                    {
                        _stepFlags[i] = false;
                        _finalY[i] = predicted;
                    }
                }

                for (int i = 0; i < data.PostCount; i++)
                {
                    data.Posts[i] = _finalY[i];
                }

                return _stepFlags;
            }

            int RenderPoint(int x0, int y0, int x1, int y1, int X)
            {
                var dy = y1 - y0;
                var adx = x1 - x0;
                var ady = Math.Abs(dy);
                var err = ady * (X - x0);
                var off = err / adx;
                if (dy < 0)
                {
                    return y0 - off;
                }
                else
                {
                    return y0 + off;
                }
            }

            void RenderLineMulti(int x0, int y0, int x1, int y1, float[] v)
            {
                var dy = y1 - y0;
                var adx = x1 - x0;
                var ady = Math.Abs(dy);
                var sy = 1 - (((dy >> 31) & 1) * 2);
                var b = dy / adx;
                var x = x0;
                var y = y0;
                var err = -adx;

                v[x0] *= inverse_dB_table[y0];
                ady -= Math.Abs(b) * adx;

                while (++x < x1)
                {
                    y += b;
                    err += ady;
                    if (err >= 0)
                    {
                        err -= adx;
                        y += sy;
                    }
                    v[x] *= inverse_dB_table[y];
                }
            }

            #region dB inversion table

            static readonly float[] inverse_dB_table = {
                                                        1.0649863e-07f, 1.1341951e-07f, 1.2079015e-07f, 1.2863978e-07f, 
                                                        1.3699951e-07f, 1.4590251e-07f, 1.5538408e-07f, 1.6548181e-07f, 
                                                        1.7623575e-07f, 1.8768855e-07f, 1.9988561e-07f, 2.1287530e-07f, 
                                                        2.2670913e-07f, 2.4144197e-07f, 2.5713223e-07f, 2.7384213e-07f, 
                                                        2.9163793e-07f, 3.1059021e-07f, 3.3077411e-07f, 3.5226968e-07f, 
                                                        3.7516214e-07f, 3.9954229e-07f, 4.2550680e-07f, 4.5315863e-07f, 
                                                        4.8260743e-07f, 5.1396998e-07f, 5.4737065e-07f, 5.8294187e-07f, 
                                                        6.2082472e-07f, 6.6116941e-07f, 7.0413592e-07f, 7.4989464e-07f, 
                                                        7.9862701e-07f, 8.5052630e-07f, 9.0579828e-07f, 9.6466216e-07f, 
                                                        1.0273513e-06f, 1.0941144e-06f, 1.1652161e-06f, 1.2409384e-06f, 
                                                        1.3215816e-06f, 1.4074654e-06f, 1.4989305e-06f, 1.5963394e-06f, 
                                                        1.7000785e-06f, 1.8105592e-06f, 1.9282195e-06f, 2.0535261e-06f, 
                                                        2.1869758e-06f, 2.3290978e-06f, 2.4804557e-06f, 2.6416497e-06f, 
                                                        2.8133190e-06f, 2.9961443e-06f, 3.1908506e-06f, 3.3982101e-06f, 
                                                        3.6190449e-06f, 3.8542308e-06f, 4.1047004e-06f, 4.3714470e-06f, 
                                                        4.6555282e-06f, 4.9580707e-06f, 5.2802740e-06f, 5.6234160e-06f, 
                                                        5.9888572e-06f, 6.3780469e-06f, 6.7925283e-06f, 7.2339451e-06f, 
                                                        7.7040476e-06f, 8.2047000e-06f, 8.7378876e-06f, 9.3057248e-06f, 
                                                        9.9104632e-06f, 1.0554501e-05f, 1.1240392e-05f, 1.1970856e-05f, 
                                                        1.2748789e-05f, 1.3577278e-05f, 1.4459606e-05f, 1.5399272e-05f, 
                                                        1.6400004e-05f, 1.7465768e-05f, 1.8600792e-05f, 1.9809576e-05f, 
                                                        2.1096914e-05f, 2.2467911e-05f, 2.3928002e-05f, 2.5482978e-05f, 
                                                        2.7139006e-05f, 2.8902651e-05f, 3.0780908e-05f, 3.2781225e-05f, 
                                                        3.4911534e-05f, 3.7180282e-05f, 3.9596466e-05f, 4.2169667e-05f, 
                                                        4.4910090e-05f, 4.7828601e-05f, 5.0936773e-05f, 5.4246931e-05f, 
                                                        5.7772202e-05f, 6.1526565e-05f, 6.5524908e-05f, 6.9783085e-05f, 
                                                        7.4317983e-05f, 7.9147585e-05f, 8.4291040e-05f, 8.9768747e-05f, 
                                                        9.5602426e-05f, 0.00010181521f, 0.00010843174f, 0.00011547824f, 
                                                        0.00012298267f, 0.00013097477f, 0.00013948625f, 0.00014855085f, 
                                                        0.00015820453f, 0.00016848555f, 0.00017943469f, 0.00019109536f, 
                                                        0.00020351382f, 0.00021673929f, 0.00023082423f, 0.00024582449f, 
                                                        0.00026179955f, 0.00027881276f, 0.00029693158f, 0.00031622787f, 
                                                        0.00033677814f, 0.00035866388f, 0.00038197188f, 0.00040679456f, 
                                                        0.00043323036f, 0.00046138411f, 0.00049136745f, 0.00052329927f, 
                                                        0.00055730621f, 0.00059352311f, 0.00063209358f, 0.00067317058f, 
                                                        0.00071691700f, 0.00076350630f, 0.00081312324f, 0.00086596457f, 
                                                        0.00092223983f, 0.00098217216f, 0.0010459992f,  0.0011139742f, 
                                                        0.0011863665f,  0.0012634633f,  0.0013455702f,  0.0014330129f, 
                                                        0.0015261382f,  0.0016253153f,  0.0017309374f,  0.0018434235f, 
                                                        0.0019632195f,  0.0020908006f,  0.0022266726f,  0.0023713743f, 
                                                        0.0025254795f,  0.0026895994f,  0.0028643847f,  0.0030505286f, 
                                                        0.0032487691f,  0.0034598925f,  0.0036847358f,  0.0039241906f, 
                                                        0.0041792066f,  0.0044507950f,  0.0047400328f,  0.0050480668f, 
                                                        0.0053761186f,  0.0057254891f,  0.0060975636f,  0.0064938176f, 
                                                        0.0069158225f,  0.0073652516f,  0.0078438871f,  0.0083536271f, 
                                                        0.0088964928f,  0.009474637f,   0.010090352f,   0.010746080f, 
                                                        0.011444421f,   0.012188144f,   0.012980198f,   0.013823725f, 
                                                        0.014722068f,   0.015678791f,   0.016697687f,   0.017782797f, 
                                                        0.018938423f,   0.020169149f,   0.021479854f,   0.022875735f, 
                                                        0.024362330f,   0.025945531f,   0.027631618f,   0.029427276f, 
                                                        0.031339626f,   0.033376252f,   0.035545228f,   0.037855157f, 
                                                        0.040315199f,   0.042935108f,   0.045725273f,   0.048696758f, 
                                                        0.051861348f,   0.055231591f,   0.058820850f,   0.062643361f, 
                                                        0.066714279f,   0.071049749f,   0.075666962f,   0.080584227f, 
                                                        0.085821044f,   0.091398179f,   0.097337747f,   0.10366330f, 
                                                        0.11039993f,    0.11757434f,    0.12521498f,    0.13335215f, 
                                                        0.14201813f,    0.15124727f,    0.16107617f,    0.17154380f, 
                                                        0.18269168f,    0.19456402f,    0.20720788f,    0.22067342f, 
                                                        0.23501402f,    0.25028656f,    0.26655159f,    0.28387361f, 
                                                        0.30232132f,    0.32196786f,    0.34289114f,    0.36517414f, 
                                                        0.38890521f,    0.41417847f,    0.44109412f,    0.46975890f, 
                                                        0.50028648f,    0.53279791f,    0.56742212f,    0.60429640f, 
                                                        0.64356699f,    0.68538959f,    0.72993007f,    0.77736504f, 
                                                        0.82788260f,    0.88168307f,    0.9389798f,     1.0f
                                                        };

            #endregion
        }
    }
}
#endregion //VorbisFloor

#region VorbisMapping
namespace NVorbis
{
    abstract class VorbisMapping
    {
        internal static VorbisMapping Init(VorbisStreamDecoder vorbis, DataPacket packet)
        {
            var type = (int)packet.ReadBits(16);

            VorbisMapping mapping = null;
            switch (type)
            {
                case 0: mapping = new Mapping0(vorbis); break;
            }
            if (mapping == null) throw new InvalidDataException();

            mapping.Init(packet);
            return mapping;
        }

        VorbisStreamDecoder _vorbis;

        protected VorbisMapping(VorbisStreamDecoder vorbis)
        {
            _vorbis = vorbis;
        }

        abstract protected void Init(DataPacket packet);

        internal Submap[] Submaps;

        internal Submap[] ChannelSubmap;

        internal CouplingStep[] CouplingSteps;

        class Mapping0 : VorbisMapping
        {
            internal Mapping0(VorbisStreamDecoder vorbis) : base(vorbis) { }

            protected override void Init(DataPacket packet)
            {
                var submapCount = 1;
                if (packet.ReadBit()) submapCount += (int)packet.ReadBits(4);

                // square polar mapping
                var couplingSteps = 0;
                if (packet.ReadBit())
                {
                    couplingSteps = (int)packet.ReadBits(8) + 1;
                }

                var couplingBits = Utils.ilog(_vorbis._channels - 1);
                CouplingSteps = new CouplingStep[couplingSteps];
                for (int j = 0; j < couplingSteps; j++)
                {
                    var magnitude = (int)packet.ReadBits(couplingBits);
                    var angle = (int)packet.ReadBits(couplingBits);
                    if (magnitude == angle || magnitude > _vorbis._channels - 1 || angle > _vorbis._channels - 1)
                        throw new InvalidDataException();
                    CouplingSteps[j] = new CouplingStep { Angle = angle, Magnitude = magnitude };
                }

                // reserved bits
                if (packet.ReadBits(2) != 0UL) throw new InvalidDataException();

                // channel multiplex
                var mux = new int[_vorbis._channels];
                if (submapCount > 1)
                {
                    for (int c = 0; c < ChannelSubmap.Length; c++)
                    {
                        mux[c] = (int)packet.ReadBits(4);
                        if (mux[c] >= submapCount) throw new InvalidDataException();
                    }
                }

                // submaps
                Submaps = new Submap[submapCount];
                for (int j = 0; j < submapCount; j++)
                {
                    packet.ReadBits(8); // unused placeholder
                    var floorNum = (int)packet.ReadBits(8);
                    if (floorNum >= _vorbis.Floors.Length) throw new InvalidDataException();
                    var residueNum = (int)packet.ReadBits(8);
                    if (residueNum >= _vorbis.Residues.Length) throw new InvalidDataException();

                    Submaps[j] = new Submap
                    {
                        Floor = _vorbis.Floors[floorNum],
                        Residue = _vorbis.Residues[floorNum]
                    };
                }

                ChannelSubmap = new Submap[_vorbis._channels];
                for (int c = 0; c < ChannelSubmap.Length; c++)
                {
                    ChannelSubmap[c] = Submaps[mux[c]];
                }
            }
        }

        internal class Submap
        {
            internal Submap() { }

            internal VorbisFloor Floor;
            internal VorbisResidue Residue;
        }

        internal class CouplingStep
        {
            internal CouplingStep() { }

            internal int Magnitude;
            internal int Angle;
        }
    }
}
#endregion VorbisMapping

#region VorbisMode
namespace NVorbis
{
    class VorbisMode
    {
        const float M_PI = 3.1415926539f; //(float)Math.PI;
        const float M_PI2 = M_PI / 2;

        internal static VorbisMode Init(VorbisStreamDecoder vorbis, DataPacket packet)
        {
            var mode = new VorbisMode(vorbis);
            mode.BlockFlag = packet.ReadBit();
            mode.WindowType = (int)packet.ReadBits(16);
            mode.TransformType = (int)packet.ReadBits(16);
            var mapping = (int)packet.ReadBits(8);

            if (mode.WindowType != 0 || mode.TransformType != 0 || mapping >= vorbis.Maps.Length) throw new InvalidDataException();

            mode.Mapping = vorbis.Maps[mapping];
            mode.BlockSize = mode.BlockFlag ? vorbis.Block1Size : vorbis.Block0Size;

            // now pre-calc the window(s)...
            if (mode.BlockFlag)
            {
                // long block
                mode._windows = new float[4][];
                mode._windows[0] = new float[vorbis.Block1Size];
                mode._windows[1] = new float[vorbis.Block1Size];
                mode._windows[2] = new float[vorbis.Block1Size];
                mode._windows[3] = new float[vorbis.Block1Size];
            }
            else
            {
                // short block
                mode._windows = new float[1][];
                mode._windows[0] = new float[vorbis.Block0Size];
            }
            mode.CalcWindows();

            return mode;
        }

        VorbisStreamDecoder _vorbis;

        float[][] _windows;

        private VorbisMode(VorbisStreamDecoder vorbis)
        {
            _vorbis = vorbis;
        }

        void CalcWindows()
        {
            // 0: prev = s, next = s || BlockFlag = false
            // 1: prev = l, next = s
            // 2: prev = s, next = l
            // 3: prev = l, next = l

            for (int idx = 0; idx < _windows.Length; idx++)
            {
                var array = _windows[idx];

                var left = ((idx & 1) == 0 ? _vorbis.Block0Size : _vorbis.Block1Size) / 2;
                var wnd = BlockSize;
                var right = ((idx & 2) == 0 ? _vorbis.Block0Size : _vorbis.Block1Size) / 2;

                var leftbegin = wnd / 4 - left / 2;
                var rightbegin = wnd - wnd / 4 - right / 2;

                for (int i = 0; i < left; i++)
                {
                    var x = (float)Math.Sin((i + .5) / left * M_PI2);
                    x *= x;
                    array[leftbegin + i] = (float)Math.Sin(x * M_PI2);
                }

                for (int i = leftbegin + left; i < rightbegin; i++)
                {
                    array[i] = 1.0f;
                }

                for (int i = 0; i < right; i++)
                {
                    var x = (float)Math.Sin((right - i - .5) / right * M_PI2);
                    x *= x;
                    array[rightbegin + i] = (float)Math.Sin(x * M_PI2);
                }
            }
        }

        internal bool BlockFlag;
        internal int WindowType;
        internal int TransformType;
        internal VorbisMapping Mapping;
        internal int BlockSize;

        internal float[] GetWindow(bool prev, bool next)
        {
            if (BlockFlag)
            {
                if (next)
                {
                    if (prev) return _windows[3];
                    return _windows[2];
                }
                else if (prev)
                {
                    return _windows[1];
                }
            }

            return _windows[0];
        }
    }
}
#endregion //VorbisMode

#region VorbisReader
namespace NVorbis
{
    public class VorbisReader : IDisposable
    {
        int _streamIdx;

        IContainerReader _containerReader;
        List<VorbisStreamDecoder> _decoders;
        List<int> _serials;

        VorbisReader()
        {
            ClipSamples = true;

            _decoders = new List<VorbisStreamDecoder>();
            _serials = new List<int>();

        }

        public VorbisReader(string fileName)
            : this(File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.Read), true)
        {
        }

        public VorbisReader(Stream stream, bool closeStreamOnDispose)
            : this()
        {
            var bufferedStream = new BufferedReadStream(stream);
            bufferedStream.CloseBaseStream = closeStreamOnDispose;

            // try Ogg first
            var oggContainer = new Ogg.ContainerReader(bufferedStream, closeStreamOnDispose);
            if (!LoadContainer(oggContainer))
            {
                // oops, not Ogg!
                // we don't support any other container types yet, so error out
                // TODO: Add Matroska fallback
                bufferedStream.Close();
                throw new InvalidDataException("Could not determine container type!");
            }
            _containerReader = oggContainer;

            if (_decoders.Count == 0) throw new InvalidDataException("No Vorbis data found!");
        }

        public VorbisReader(IContainerReader containerReader)
            : this()
        {
            if (!LoadContainer(containerReader))
            {
                throw new InvalidDataException("Container did not initialize!");
            }
            _containerReader = containerReader;

            if (_decoders.Count == 0) throw new InvalidDataException("No Vorbis data found!");
        }

        public VorbisReader(IPacketProvider packetProvider)
            : this()
        {
            var ea = new NewStreamEventArgs(packetProvider);
            NewStream(this, ea);
            if (ea.IgnoreStream) throw new InvalidDataException("No Vorbis data found!");
        }

        bool LoadContainer(IContainerReader containerReader)
        {
            containerReader.NewStream += NewStream;
            if (!containerReader.Init())
            {
                containerReader.NewStream -= NewStream;
                return false;
            }
            return true;
        }

        void NewStream(object sender, NewStreamEventArgs ea)
        {
            var packetProvider = ea.PacketProvider;
            var decoder = new VorbisStreamDecoder(packetProvider);
            if (decoder.TryInit())
            {
                _decoders.Add(decoder);
                _serials.Add(packetProvider.StreamSerial);
            }
            else
            {
                // This is almost certainly not a Vorbis stream
                ea.IgnoreStream = true;
            }
        }

        public void Dispose()
        {
            if (_decoders != null)
            {
                foreach (var decoder in _decoders)
                {
                    decoder.Dispose();
                }
                _decoders.Clear();
                _decoders = null;
            }

            if (_containerReader != null)
            {
                _containerReader.NewStream -= NewStream;
                _containerReader.Dispose();
                _containerReader = null;
            }
        }

        VorbisStreamDecoder ActiveDecoder
        {
            get
            {
                if (_decoders == null) throw new ObjectDisposedException("VorbisReader");
                return _decoders[_streamIdx];
            }
        }

        #region Public Interface

        /// <summary>
        /// Gets the number of channels in the current selected Vorbis stream
        /// </summary>
        public int Channels { get { return ActiveDecoder._channels; } }

        /// <summary>
        /// Gets the sample rate of the current selected Vorbis stream
        /// </summary>
        public int SampleRate { get { return ActiveDecoder._sampleRate; } }

        /// <summary>
        /// Gets the encoder's upper bitrate of the current selected Vorbis stream
        /// </summary>
        public int UpperBitrate { get { return ActiveDecoder._upperBitrate; } }

        /// <summary>
        /// Gets the encoder's nominal bitrate of the current selected Vorbis stream
        /// </summary>
        public int NominalBitrate { get { return ActiveDecoder._nominalBitrate; } }

        /// <summary>
        /// Gets the encoder's lower bitrate of the current selected Vorbis stream
        /// </summary>
        public int LowerBitrate { get { return ActiveDecoder._lowerBitrate; } }

        /// <summary>
        /// Gets the encoder's vendor string for the current selected Vorbis stream
        /// </summary>
        public string Vendor { get { return ActiveDecoder._vendor; } }

        /// <summary>
        /// Gets the comments in the current selected Vorbis stream
        /// </summary>
        public string[] Comments { get { return ActiveDecoder._comments; } }

        /// <summary>
        /// Gets whether the previous short sample count was due to a parameter change in the stream.
        /// </summary>
        public bool IsParameterChange { get { return ActiveDecoder.IsParameterChange; } }

        /// <summary>
        /// Gets the number of bits read that are related to framing and transport alone
        /// </summary>
        public long ContainerOverheadBits { get { return ActiveDecoder.ContainerBits; } }

        /// <summary>
        /// Gets or sets whether to automatically apply clipping to samples returned by <see cref="VorbisReader.ReadSamples"/>.
        /// </summary>
        public bool ClipSamples { get; set; }

        /// <summary>
        /// Gets stats from each decoder stream available
        /// </summary>
        public IVorbisStreamStatus[] Stats
        {
            get { return _decoders.Select(d => d).Cast<IVorbisStreamStatus>().ToArray(); }
        }

        /// <summary>
        /// Gets the currently-selected stream's index
        /// </summary>
        public int StreamIndex
        {
            get { return _streamIdx; }
        }

        /// <summary>
        /// Reads decoded samples from the current logical stream
        /// </summary>
        /// <param name="buffer">The buffer to write the samples to</param>
        /// <param name="offset">The offset into the buffer to write the samples to</param>
        /// <param name="count">The number of samples to write</param>
        /// <returns>The number of samples written</returns>
        public int ReadSamples(float[] buffer, int offset, int count)
        {
            if (offset < 0) throw new ArgumentOutOfRangeException("offset");
            if (count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException("count");

            count = ActiveDecoder.ReadSamples(buffer, offset, count);

            if (ClipSamples)
            {
                var decoder = _decoders[_streamIdx];
                for (int i = 0; i < count; i++, offset++)
                {
                    buffer[offset] = Utils.ClipValue(buffer[offset], ref decoder._clipped);
                }
            }

            return count;
        }

        /// <summary>
        /// Clears the parameter change flag so further samples can be requested.
        /// </summary>
        public void ClearParameterChange()
        {
            ActiveDecoder.IsParameterChange = false;
        }

        /// <summary>
        /// Returns the number of logical streams found so far in the physical container
        /// </summary>
        public int StreamCount
        {
            get { return _decoders.Count; }
        }

        /// <summary>
        /// Searches for the next stream in a concatenated file
        /// </summary>
        /// <returns><c>True</c> if a new stream was found, otherwise <c>false</c>.</returns>
        public bool FindNextStream()
        {
            if (_containerReader == null) return false;
            return _containerReader.FindNextStream();
        }

        /// <summary>
        /// Switches to an alternate logical stream.
        /// </summary>
        /// <param name="index">The logical stream index to switch to</param>
        /// <returns><c>True</c> if the properties of the logical stream differ from those of the one previously being decoded. Otherwise, <c>False</c>.</returns>
        public bool SwitchStreams(int index)
        {
            if (index < 0 || index >= StreamCount) throw new ArgumentOutOfRangeException("index");

            if (_decoders == null) throw new ObjectDisposedException("VorbisReader");

            if (_streamIdx == index) return false;

            var curDecoder = _decoders[_streamIdx];
            _streamIdx = index;
            var newDecoder = _decoders[_streamIdx];

            return curDecoder._channels != newDecoder._channels || curDecoder._sampleRate != newDecoder._sampleRate;
        }

        /// <summary>
        /// Gets or Sets the current timestamp of the decoder.  Is the timestamp before the next sample to be decoded
        /// </summary>
        public TimeSpan DecodedTime
        {
            get
            {
                return TimeSpan.FromSeconds((double)ActiveDecoder.CurrentPosition / SampleRate);
            }
            set
            {
                ActiveDecoder.SeekTo((long)(value.TotalSeconds * SampleRate));
            }

        }

        /// <summary>
        /// Gets or Sets the current position of the next sample to be decoded.
        /// </summary>
        public long DecodedPosition
        {
            get
            {
                return ActiveDecoder.CurrentPosition;
            }
            set
            {
                ActiveDecoder.SeekTo(value);
            }
        }

        /// <summary>
        /// Gets the total length of the current logical stream
        /// </summary>
        public TimeSpan TotalTime
        {
            get
            {
                var decoder = ActiveDecoder;
                if (decoder.CanSeek)
                {
                    return TimeSpan.FromSeconds((double)decoder.GetLastGranulePos() / decoder._sampleRate);
                }
                else
                {
                    return TimeSpan.MaxValue;
                }
            }
        }

        public long TotalSamples
        {
            get
            {
                var decoder = ActiveDecoder;
                if (decoder.CanSeek)
                {
                    return decoder.GetLastGranulePos();
                }
                else
                {
                    return long.MaxValue;
                }
            }
        }

        #endregion
    }
}
#endregion //VorbisReader

#region VorbisResidue
namespace NVorbis
{
    abstract class VorbisResidue
    {
        internal static VorbisResidue Init(VorbisStreamDecoder vorbis, DataPacket packet)
        {
            var type = (int)packet.ReadBits(16);

            VorbisResidue residue = null;
            switch (type)
            {
                case 0: residue = new Residue0(vorbis); break;
                case 1: residue = new Residue1(vorbis); break;
                case 2: residue = new Residue2(vorbis); break;
            }
            if (residue == null) throw new InvalidDataException();

            residue.Init(packet);
            return residue;
        }

        static int icount(int v)
        {
            var ret = 0;
            while (v != 0)
            {
                ret += (v & 1);
                v >>= 1;
            }
            return ret;
        }

        VorbisStreamDecoder _vorbis;
        float[][] _residue;

        protected VorbisResidue(VorbisStreamDecoder vorbis)
        {
            _vorbis = vorbis;

            _residue = new float[_vorbis._channels][];
            for (int i = 0; i < _vorbis._channels; i++)
            {
                _residue[i] = new float[_vorbis.Block1Size];
            }
        }

        protected float[][] GetResidueBuffer(int channels)
        {
            var temp = _residue;
            if (channels < _vorbis._channels)
            {
                temp = new float[channels][];
                Array.Copy(_residue, temp, channels);
            }
            for (int i = 0; i < channels; i++)
            {
                Array.Clear(temp[i], 0, temp[i].Length);
            }
            return temp;
        }

        abstract internal float[][] Decode(DataPacket packet, bool[] doNotDecode, int channels, int blockSize);

        abstract protected void Init(DataPacket packet);

        // residue type 0... samples are grouped by channel, then stored with non-interleaved dimensions (d0, d0, d0, d0, ..., d1, d1, d1, d1, ..., d2, d2, d2, d2, etc...)
        class Residue0 : VorbisResidue
        {
            int _begin;
            int _end;
            int _partitionSize;
            int _classifications;
            int _maxStages;

            VorbisCodebook[][] _books;
            VorbisCodebook _classBook;

            int[] _cascade, _entryCache;
            int[][] _decodeMap;
            int[][][] _partWordCache;

            internal Residue0(VorbisStreamDecoder vorbis) : base(vorbis) { }

            protected override void Init(DataPacket packet)
            {
                // this is pretty well stolen directly from libvorbis...  BSD license
                _begin = (int)packet.ReadBits(24);
                _end = (int)packet.ReadBits(24);
                _partitionSize = (int)packet.ReadBits(24) + 1;
                _classifications = (int)packet.ReadBits(6) + 1;
                _classBook = _vorbis.Books[(int)packet.ReadBits(8)];

                _cascade = new int[_classifications];
                var acc = 0;
                for (int i = 0; i < _classifications; i++)
                {
                    var low_bits = (int)packet.ReadBits(3);
                    if (packet.ReadBit())
                    {
                        _cascade[i] = (int)packet.ReadBits(5) << 3 | low_bits;
                    }
                    else
                    {
                        _cascade[i] = low_bits;
                    }
                    acc += icount(_cascade[i]);
                }

                var bookNums = new int[acc];
                for (var i = 0; i < acc; i++)
                {
                    bookNums[i] = (int)packet.ReadBits(8);
                    if (_vorbis.Books[bookNums[i]].MapType == 0) throw new InvalidDataException();
                }

                var entries = _classBook.Entries;
                var dim = _classBook.Dimensions;
                var partvals = 1;
                while (dim > 0)
                {
                    partvals *= _classifications;
                    if (partvals > entries) throw new InvalidDataException();
                    --dim;
                }

                // now the lookups
                dim = _classBook.Dimensions;

                _books = new VorbisCodebook[_classifications][];

                acc = 0;
                var maxstage = 0;
                int stages;
                for (int j = 0; j < _classifications; j++)
                {
                    stages = Utils.ilog(_cascade[j]);
                    _books[j] = new VorbisCodebook[stages];
                    if (stages > 0)
                    {
                        maxstage = Math.Max(maxstage, stages);
                        for (int k = 0; k < stages; k++)
                        {
                            if ((_cascade[j] & (1 << k)) > 0)
                            {
                                _books[j][k] = _vorbis.Books[bookNums[acc++]];
                            }
                        }
                    }
                }
                _maxStages = maxstage;

                _decodeMap = new int[partvals][];
                for (int j = 0; j < partvals; j++)
                {
                    var val = j;
                    var mult = partvals / _classifications;
                    _decodeMap[j] = new int[_classBook.Dimensions];
                    for (int k = 0; k < _classBook.Dimensions; k++)
                    {
                        var deco = val / mult;
                        val -= deco * mult;
                        mult /= _classifications;
                        _decodeMap[j][k] = deco;
                    }
                }

                _entryCache = new int[_partitionSize];

                _partWordCache = new int[_vorbis._channels][][];
                var maxPartWords = ((_end - _begin) / _partitionSize + _classBook.Dimensions - 1) / _classBook.Dimensions;
                for (int ch = 0; ch < _vorbis._channels; ch++)
                {
                    _partWordCache[ch] = new int[maxPartWords][];
                }
            }

            internal override float[][] Decode(DataPacket packet, bool[] doNotDecode, int channels, int blockSize)
            {
                var residue = GetResidueBuffer(doNotDecode.Length);

                // this is pretty well stolen directly from libvorbis...  BSD license
                var end = _end < blockSize / 2 ? _end : blockSize / 2;
                var n = end - _begin;

                if (n > 0 && doNotDecode.Contains(false))
                {
                    var partVals = n / _partitionSize;

                    var partWords = (partVals + _classBook.Dimensions - 1) / _classBook.Dimensions;
                    for (int j = 0; j < channels; j++)
                    {
                        Array.Clear(_partWordCache[j], 0, partWords);
                    }

                    for (int s = 0; s < _maxStages; s++)
                    {
                        for (int i = 0, l = 0; i < partVals; l++)
                        {
                            if (s == 0)
                            {
                                for (int j = 0; j < channels; j++)
                                {
                                    var idx = _classBook.DecodeScalar(packet);
                                    if (idx >= 0 && idx < _decodeMap.Length)
                                    {
                                        _partWordCache[j][l] = _decodeMap[idx];
                                    }
                                    else
                                    {
                                        i = partVals;
                                        s = _maxStages;
                                        break;
                                    }
                                }
                            }
                            for (int k = 0; i < partVals && k < _classBook.Dimensions; k++, i++)
                            {
                                var offset = _begin + i * _partitionSize;
                                for (int j = 0; j < channels; j++)
                                {
                                    var idx = _partWordCache[j][l][k];
                                    if ((_cascade[idx] & (1 << s)) != 0)
                                    {
                                        var book = _books[idx][s];
                                        if (book != null)
                                        {
                                            if (WriteVectors(book, packet, residue, j, offset, _partitionSize))
                                            {
                                                // bad packet...  exit now and try to use what we already have
                                                i = partVals;
                                                s = _maxStages;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                return residue;
            }

            virtual protected bool WriteVectors(VorbisCodebook codebook, DataPacket packet, float[][] residue, int channel, int offset, int partitionSize)
            {
                var res = residue[channel];
                var step = partitionSize / codebook.Dimensions;

                for (int i = 0; i < step; i++)
                {
                    if ((_entryCache[i] = codebook.DecodeScalar(packet)) == -1)
                    {
                        return true;
                    }
                }
                for (int i = 0; i < codebook.Dimensions; i++)
                {
                    for (int j = 0; j < step; j++, offset++)
                    {
                        res[offset] += codebook[_entryCache[j], i];
                    }
                }
                return false;
            }
        }

        // residue type 1... samples are grouped by channel, then stored with interleaved dimensions (d0, d1, d2, d0, d1, d2, etc...)
        class Residue1 : Residue0
        {
            internal Residue1(VorbisStreamDecoder vorbis) : base(vorbis) { }

            protected override bool WriteVectors(VorbisCodebook codebook, DataPacket packet, float[][] residue, int channel, int offset, int partitionSize)
            {
                var res = residue[channel];

                for (int i = 0; i < partitionSize; )
                {
                    var entry = codebook.DecodeScalar(packet);
                    if (entry == -1)
                    {
                        return true;
                    }
                    for (int j = 0; j < codebook.Dimensions; i++, j++)
                    {
                        res[offset + i] += codebook[entry, j];
                    }
                }

                return false;
            }
        }

        // residue type 2... basically type 0, but samples are interleaved between channels (ch0, ch1, ch0, ch1, etc...)
        class Residue2 : Residue0
        {
            int _channels;

            internal Residue2(VorbisStreamDecoder vorbis) : base(vorbis) { }

            // We can use the type 0 logic by saying we're doing a single channel buffer big enough to hold the samples for all channels
            // This works because WriteVectors(...) "knows" the correct channel count and processes the data accordingly.
            internal override float[][] Decode(DataPacket packet, bool[] doNotDecode, int channels, int blockSize)
            {
                _channels = channels;

                return base.Decode(packet, doNotDecode, 1, blockSize * channels);
            }

            protected override bool WriteVectors(VorbisCodebook codebook, DataPacket packet, float[][] residue, int channel, int offset, int partitionSize)
            {
                var chPtr = 0;

                offset /= _channels;
                for (int c = 0; c < partitionSize; )
                {
                    var entry = codebook.DecodeScalar(packet);
                    if (entry == -1)
                    {
                        return true;
                    }
                    for (var d = 0; d < codebook.Dimensions; d++, c++)
                    {
                        residue[chPtr][offset] += codebook[entry, d];
                        if (++chPtr == _channels)
                        {
                            chPtr = 0;
                            offset++;
                        }
                    }
                }

                return false;
            }
        }
    }
}
#endregion //VorbisResidue

#region VorbisStreamDecoder
namespace NVorbis
{
    class VorbisStreamDecoder : IVorbisStreamStatus, IDisposable
    {
        internal int _upperBitrate;
        internal int _nominalBitrate;
        internal int _lowerBitrate;

        internal string _vendor;
        internal string[] _comments;

        internal int _channels;
        internal int _sampleRate;
        internal int Block0Size;
        internal int Block1Size;

        internal VorbisCodebook[] Books;
        internal VorbisTime[] Times;
        internal VorbisFloor[] Floors;
        internal VorbisResidue[] Residues;
        internal VorbisMapping[] Maps;
        internal VorbisMode[] Modes;

        int _modeFieldBits;

        #region Stat Fields

        internal long _glueBits;
        internal long _metaBits;
        internal long _bookBits;
        internal long _timeHdrBits;
        internal long _floorHdrBits;
        internal long _resHdrBits;
        internal long _mapHdrBits;
        internal long _modeHdrBits;
        internal long _wasteHdrBits;

        internal long _modeBits;
        internal long _floorBits;
        internal long _resBits;
        internal long _wasteBits;

        internal long _samples;

        internal int _packetCount;

        internal System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();

        #endregion

        IPacketProvider _packetProvider;
        DataPacket _parameterChangePacket;

        List<int> _pagesSeen;
        int _lastPageSeen;

        bool _eosFound;

        object _seekLock = new object();

        internal VorbisStreamDecoder(IPacketProvider packetProvider)
        {
            _packetProvider = packetProvider;
            _packetProvider.ParameterChange += SetParametersChanging;

            _pagesSeen = new List<int>();
            _lastPageSeen = -1;
        }

        internal bool TryInit()
        {
            // try to process the stream header...
            if (!ProcessStreamHeader(_packetProvider.PeekNextPacket()))
            {
                return false;
            }

            // seek past the stream header packet
            _packetProvider.GetNextPacket().Done();

            // load the comments header...
            var packet = _packetProvider.GetNextPacket();
            if (!LoadComments(packet))
            {
                throw new InvalidDataException("Comment header was not readable!");
            }
            packet.Done();

            // load the book header...
            packet = _packetProvider.GetNextPacket();
            if (!LoadBooks(packet))
            {
                throw new InvalidDataException("Book header was not readable!");
            }
            packet.Done();

            // get the decoding logic bootstrapped
            InitDecoder();

            return true;
        }

        void SetParametersChanging(object sender, ParameterChangeEventArgs e)
        {
            _parameterChangePacket = e.FirstPacket;
        }

        public void Dispose()
        {
            if (_packetProvider != null)
            {
                var temp = _packetProvider;
                _packetProvider = null;
                temp.ParameterChange -= SetParametersChanging;
                temp.Dispose();
            }
        }

        #region Header Decode

        void ProcessParameterChange(DataPacket packet)
        {
            _parameterChangePacket = null;

            // try to do a stream header...
            var wasPeek = false;
            var doFullReset = false;
            if (ProcessStreamHeader(packet))
            {
                packet.Done();
                wasPeek = true;
                doFullReset = true;
                packet = _packetProvider.PeekNextPacket();
                if (packet == null) throw new InvalidDataException("Couldn't get next packet!");
            }

            // try to do a comment header...
            if (LoadComments(packet))
            {
                if (wasPeek)
                {
                    _packetProvider.GetNextPacket().Done();
                }
                else
                {
                    packet.Done();
                }
                wasPeek = true;
                packet = _packetProvider.PeekNextPacket();
                if (packet == null) throw new InvalidDataException("Couldn't get next packet!");
            }

            // try to do a book header...
            if (LoadBooks(packet))
            {
                if (wasPeek)
                {
                    _packetProvider.GetNextPacket().Done();
                }
                else
                {
                    packet.Done();
                }
            }

            ResetDecoder(doFullReset);
        }

        bool ProcessStreamHeader(DataPacket packet)
        {
            if (!packet.ReadBytes(7).SequenceEqual(new byte[] { 0x01, 0x76, 0x6f, 0x72, 0x62, 0x69, 0x73 }))
            {
                // don't mark the packet as done... it might be used elsewhere
                _glueBits += packet.Length * 8;
                return false;
            }

            if (!_pagesSeen.Contains((_lastPageSeen = packet.PageSequenceNumber))) _pagesSeen.Add(_lastPageSeen);

            _glueBits += 56;

            var startPos = packet.BitsRead;

            if (packet.ReadInt32() != 0) throw new InvalidDataException("Only Vorbis stream version 0 is supported.");

            _channels = packet.ReadByte();
            _sampleRate = packet.ReadInt32();
            _upperBitrate = packet.ReadInt32();
            _nominalBitrate = packet.ReadInt32();
            _lowerBitrate = packet.ReadInt32();

            Block0Size = 1 << (int)packet.ReadBits(4);
            Block1Size = 1 << (int)packet.ReadBits(4);

            if (_nominalBitrate == 0)
            {
                if (_upperBitrate > 0 && _lowerBitrate > 0)
                {
                    _nominalBitrate = (_upperBitrate + _lowerBitrate) / 2;
                }
            }

            _metaBits += packet.BitsRead - startPos + 8;

            _wasteHdrBits += 8 * packet.Length - packet.BitsRead;

            return true;
        }

        bool LoadComments(DataPacket packet)
        {
            if (!packet.ReadBytes(7).SequenceEqual(new byte[] { 0x03, 0x76, 0x6f, 0x72, 0x62, 0x69, 0x73 }))
            {
                return false;
            }

            if (!_pagesSeen.Contains((_lastPageSeen = packet.PageSequenceNumber))) _pagesSeen.Add(_lastPageSeen);

            _glueBits += 56;

            var buffer = packet.ReadBytes(packet.ReadInt32());
            _vendor = Encoding.UTF8.GetString(buffer, 0, buffer.Length);

            _comments = new string[packet.ReadInt32()];
            for (int i = 0; i < _comments.Length; i++)
            {
                buffer = packet.ReadBytes(packet.ReadInt32());
                _comments[i] = Encoding.UTF8.GetString(buffer, 0, buffer.Length);
            }

            _metaBits += packet.BitsRead - 56;
            _wasteHdrBits += 8 * packet.Length - packet.BitsRead;

            return true;
        }

        bool LoadBooks(DataPacket packet)
        {
            if (!packet.ReadBytes(7).SequenceEqual(new byte[] { 0x05, 0x76, 0x6f, 0x72, 0x62, 0x69, 0x73 }))
            {
                return false;
            }

            if (!_pagesSeen.Contains((_lastPageSeen = packet.PageSequenceNumber))) _pagesSeen.Add(_lastPageSeen);

            var bits = packet.BitsRead;

            _glueBits += packet.BitsRead;

            // get books
            Books = new VorbisCodebook[packet.ReadByte() + 1];
            for (int i = 0; i < Books.Length; i++)
            {
                Books[i] = VorbisCodebook.Init(this, packet, i);
            }

            _bookBits += packet.BitsRead - bits;
            bits = packet.BitsRead;

            // get times
            Times = new VorbisTime[(int)packet.ReadBits(6) + 1];
            for (int i = 0; i < Times.Length; i++)
            {
                Times[i] = VorbisTime.Init(this, packet);
            }

            _timeHdrBits += packet.BitsRead - bits;
            bits = packet.BitsRead;

            // get floor
            Floors = new VorbisFloor[(int)packet.ReadBits(6) + 1];
            for (int i = 0; i < Floors.Length; i++)
            {
                Floors[i] = VorbisFloor.Init(this, packet);
            }

            _floorHdrBits += packet.BitsRead - bits;
            bits = packet.BitsRead;

            // get residue
            Residues = new VorbisResidue[(int)packet.ReadBits(6) + 1];
            for (int i = 0; i < Residues.Length; i++)
            {
                Residues[i] = VorbisResidue.Init(this, packet);
            }

            _resHdrBits += packet.BitsRead - bits;
            bits = packet.BitsRead;

            // get map
            Maps = new VorbisMapping[(int)packet.ReadBits(6) + 1];
            for (int i = 0; i < Maps.Length; i++)
            {
                Maps[i] = VorbisMapping.Init(this, packet);
            }

            _mapHdrBits += packet.BitsRead - bits;
            bits = packet.BitsRead;

            // get mode settings
            Modes = new VorbisMode[(int)packet.ReadBits(6) + 1];
            for (int i = 0; i < Modes.Length; i++)
            {
                Modes[i] = VorbisMode.Init(this, packet);
            }

            _modeHdrBits += packet.BitsRead - bits;

            // check the framing bit
            if (!packet.ReadBit()) throw new InvalidDataException();

            ++_glueBits;

            _wasteHdrBits += 8 * packet.Length - packet.BitsRead;

            _modeFieldBits = Utils.ilog(Modes.Length - 1);

            return true;
        }

        #endregion

        #region Data Decode

        float[] _prevBuffer;
        RingBuffer _outputBuffer;
        Queue<int> _bitsPerPacketHistory;
        Queue<int> _sampleCountHistory;
        int _preparedLength;
        internal bool _clipped = false;

        Stack<DataPacket> _resyncQueue;

        long _currentPosition;
        long _reportedPosition;

        VorbisMode _mode;
        bool _prevFlag, _nextFlag;
        bool[] _noExecuteChannel;
        VorbisFloor.PacketData[] _floorData;
        float[][] _residue;
        bool _isParameterChange;

        void InitDecoder()
        {
            _currentPosition = 0L;

            _resyncQueue = new Stack<DataPacket>();

            _bitsPerPacketHistory = new Queue<int>();
            _sampleCountHistory = new Queue<int>();

            ResetDecoder(true);
        }

        void ResetDecoder(bool isFullReset)
        {
            // this is called when:
            //  - init (true)
            //  - parameter change w/ stream header (true)
            //  - parameter change w/o stream header (false)
            //  - the decoder encounters a "hiccup" in the data stream (false)
            //  - a seek happens (false)

            // save off the existing "good" data
            if (_preparedLength > 0)
            {
                SaveBuffer();
            }
            if (isFullReset)
            {
                _noExecuteChannel = new bool[_channels];
                _floorData = new VorbisFloor.PacketData[_channels];

                _residue = new float[_channels][];
                for (int i = 0; i < _channels; i++)
                {
                    _residue[i] = new float[Block1Size];
                }

                _outputBuffer = new RingBuffer(Block1Size * 2 * _channels);
                _outputBuffer.Channels = _channels;
            }
            else
            {
                _outputBuffer.Clear();
            }
            _preparedLength = 0;
        }

        void SaveBuffer()
        {
            var buf = new float[_preparedLength * _channels];
            ReadSamples(buf, 0, buf.Length);
            _prevBuffer = buf;
        }

        bool UnpackPacket(DataPacket packet)
        {
            // make sure we're on an audio packet
            if (packet.ReadBit())
            {
                // we really can't do anything... count the bits as waste
                return false;
            }

            // get mode and prev/next flags
            var modeBits = _modeFieldBits;
            _mode = Modes[(int)packet.ReadBits(_modeFieldBits)];
            if (_mode.BlockFlag)
            {
                _prevFlag = packet.ReadBit();
                _nextFlag = packet.ReadBit();
                modeBits += 2;
            }
            else
            {
                _prevFlag = _nextFlag = false;
            }

            if (packet.IsShort) return false;

            var startBits = packet.BitsRead;

            var halfBlockSize = _mode.BlockSize / 2;

            // read the noise floor data (but don't decode yet)
            for (int i = 0; i < _channels; i++)
            {
                _floorData[i] = _mode.Mapping.ChannelSubmap[i].Floor.UnpackPacket(packet, _mode.BlockSize, i);
                _noExecuteChannel[i] = !_floorData[i].ExecuteChannel;

                // go ahead and clear the residue buffers
                Array.Clear(_residue[i], 0, halfBlockSize);
            }

            // make sure we handle no-energy channels correctly given the couplings...
            foreach (var step in _mode.Mapping.CouplingSteps)
            {
                if (_floorData[step.Angle].ExecuteChannel || _floorData[step.Magnitude].ExecuteChannel)
                {
                    _floorData[step.Angle].ForceEnergy = true;
                    _floorData[step.Magnitude].ForceEnergy = true;
                }
            }

            var floorBits = packet.BitsRead - startBits;
            startBits = packet.BitsRead;

            foreach (var subMap in _mode.Mapping.Submaps)
            {
                for (int j = 0; j < _channels; j++)
                {
                    if (_mode.Mapping.ChannelSubmap[j] != subMap)
                    {
                        _floorData[j].ForceNoEnergy = true;
                    }
                }

                var rTemp = subMap.Residue.Decode(packet, _noExecuteChannel, _channels, _mode.BlockSize);
                for (int c = 0; c < _channels; c++)
                {
                    var r = _residue[c];
                    var rt = rTemp[c];
                    for (int i = 0; i < halfBlockSize; i++)
                    {
                        r[i] += rt[i];
                    }
                }
            }

            _glueBits += 1;
            _modeBits += modeBits;
            _floorBits += floorBits;
            _resBits += packet.BitsRead - startBits;
            _wasteBits += 8 * packet.Length - packet.BitsRead;

            _packetCount += 1;

            return true;
        }

        void DecodePacket()
        {
            // inverse coupling
            var steps = _mode.Mapping.CouplingSteps;
            var halfSizeW = _mode.BlockSize / 2;
            for (int i = steps.Length - 1; i >= 0; i--)
            {
                if (_floorData[steps[i].Angle].ExecuteChannel || _floorData[steps[i].Magnitude].ExecuteChannel)
                {
                    var magnitude = _residue[steps[i].Magnitude];
                    var angle = _residue[steps[i].Angle];

                    // we only have to do the first half; MDCT ignores the last half
                    for (int j = 0; j < halfSizeW; j++)
                    {
                        float newM, newA;

                        if (magnitude[j] > 0)
                        {
                            if (angle[j] > 0)
                            {
                                newM = magnitude[j];
                                newA = magnitude[j] - angle[j];
                            }
                            else
                            {
                                newA = magnitude[j];
                                newM = magnitude[j] + angle[j];
                            }
                        }
                        else
                        {
                            if (angle[j] > 0)
                            {
                                newM = magnitude[j];
                                newA = magnitude[j] + angle[j];
                            }
                            else
                            {
                                newA = magnitude[j];
                                newM = magnitude[j] - angle[j];
                            }
                        }

                        magnitude[j] = newM;
                        angle[j] = newA;
                    }
                }
            }

            // apply floor / dot product / MDCT (only run if we have sound energy in that channel)
            for (int c = 0; c < _channels; c++)
            {
                var floorData = _floorData[c];
                var res = _residue[c];
                if (floorData.ExecuteChannel)
                {
                    _mode.Mapping.ChannelSubmap[c].Floor.Apply(floorData, res);
                    Mdct.Reverse(res, _mode.BlockSize);
                }
                else
                {
                    // since we aren't doing the IMDCT, we have to explicitly clear the back half of the block
                    Array.Clear(res, halfSizeW, halfSizeW);
                }
            }
        }

        int OverlapSamples()
        {
            // window
            var window = _mode.GetWindow(_prevFlag, _nextFlag);
            // this is applied as part of the lapping operation

            // now lap the data into the buffer...

            var sizeW = _mode.BlockSize;
            var right = sizeW;
            var center = right >> 1;
            var left = 0;
            var begin = -center;
            var end = center;

            if (_mode.BlockFlag)
            {
                // if the flag is true, it's a long block
                // if the flag is false, it's a short block
                if (!_prevFlag)
                {
                    // previous block was short
                    left = Block1Size / 4 - Block0Size / 4;  // where to start in pcm[][]
                    center = left + Block0Size / 2;     // adjust the center so we're correctly clearing the buffer...
                    begin = Block0Size / -2 - left;     // where to start in _outputBuffer[,]
                }

                if (!_nextFlag)
                {
                    // next block is short
                    right -= sizeW / 4 - Block0Size / 4;
                    end = sizeW / 4 + Block0Size / 4;
                }
            }
            // short blocks don't need any adjustments

            var idx = _outputBuffer.Length / _channels + begin;
            for (var c = 0; c < _channels; c++)
            {
                _outputBuffer.Write(c, idx, left, center, right, _residue[c], window);
            }

            var newPrepLen = _outputBuffer.Length / _channels - end;
            var samplesDecoded = newPrepLen - _preparedLength;
            _preparedLength = newPrepLen;

            return samplesDecoded;
        }

        void UpdatePosition(int samplesDecoded, DataPacket packet)
        {
            _samples += samplesDecoded;

            if (packet.IsResync)
            {
                // during a resync, we have to go through and watch for the next "marker"
                _currentPosition = -packet.PageGranulePosition;
                // _currentPosition will now be end of the page...  wait for the value to change, then go back and repopulate the granule positions accordingly...
                _resyncQueue.Push(packet);
            }
            else
            {
                if (samplesDecoded > 0)
                {
                    _currentPosition += samplesDecoded;
                    packet.GranulePosition = _currentPosition;

                    if (_currentPosition < 0)
                    {
                        if (packet.PageGranulePosition > -_currentPosition)
                        {
                            // we now have a valid granuleposition...  populate the queued packets' GranulePositions
                            var gp = _currentPosition - samplesDecoded;
                            while (_resyncQueue.Count > 0)
                            {
                                var pkt = _resyncQueue.Pop();

                                var temp = pkt.GranulePosition + gp;
                                pkt.GranulePosition = gp;
                                gp = temp;
                            }
                        }
                        else
                        {
                            packet.GranulePosition = -samplesDecoded;
                            _resyncQueue.Push(packet);
                        }
                    }
                    else if (packet.IsEndOfStream && _currentPosition > packet.PageGranulePosition)
                    {
                        var diff = (int)(_currentPosition - packet.PageGranulePosition);
                        if (diff >= 0)
                        {
                            _preparedLength -= diff;
                            _currentPosition -= diff;
                        }
                        else
                        {
                            // uh-oh.  We're supposed to have more samples to this point...
                            _preparedLength = 0;
                        }
                        packet.GranulePosition = packet.PageGranulePosition;
                        _eosFound = true;
                    }
                }
            }
        }

        void DecodeNextPacket()
        {
            _sw.Start();

            DataPacket packet = null;
            try
            {
                // get the next packet
                var packetProvider = _packetProvider;
                if (packetProvider != null)
                {
                    packet = packetProvider.GetNextPacket();
                }

                // if the packet is null, we've hit the end or the packet reader has been disposed...
                if (packet == null)
                {
                    _eosFound = true;
                    return;
                }

                // keep our page count in sync
                if (!_pagesSeen.Contains((_lastPageSeen = packet.PageSequenceNumber))) _pagesSeen.Add(_lastPageSeen);

                // check for resync
                if (packet.IsResync)
                {
                    ResetDecoder(false); // if we're a resync, our current decoder state is invalid...
                }

                // check for parameter change
                if (packet == _parameterChangePacket)
                {
                    _isParameterChange = true;
                    ProcessParameterChange(packet);
                    return;
                }

                if (!UnpackPacket(packet))
                {
                    packet.Done();
                    _wasteBits += 8 * packet.Length;
                    return;
                }
                packet.Done();

                // we can now safely decode all the data without having to worry about a corrupt or partial packet

                DecodePacket();
                var samplesDecoded = OverlapSamples();

                // we can do something cool here...  mark down how many samples were decoded in this packet
                if (packet.GranuleCount.HasValue == false)
                {
                    packet.GranuleCount = samplesDecoded;
                }

                // update our position

                UpdatePosition(samplesDecoded, packet);

                // a little statistical housekeeping...
                var sc = Utils.Sum(_sampleCountHistory) + samplesDecoded;

                _bitsPerPacketHistory.Enqueue((int)packet.BitsRead);
                _sampleCountHistory.Enqueue(samplesDecoded);

                while (sc > _sampleRate)
                {
                    _bitsPerPacketHistory.Dequeue();
                    sc -= _sampleCountHistory.Dequeue();
                }
            }
            catch
            {
                if (packet != null)
                {
                    packet.Done();
                }
                throw;
            }
            finally
            {
                _sw.Stop();
            }
        }

        internal int GetPacketLength(DataPacket curPacket, DataPacket lastPacket)
        {
            // if we don't have a previous packet, or we're re-syncing, this packet has no audio data to return
            if (lastPacket == null || curPacket.IsResync) return 0;

            // make sure they are audio packets
            if (curPacket.ReadBit()) return 0;
            if (lastPacket.ReadBit()) return 0;

            // get the current packet's information
            var modeIdx = (int)curPacket.ReadBits(_modeFieldBits);
            if (modeIdx < 0 || modeIdx >= Modes.Length) return 0;
            var mode = Modes[modeIdx];

            // get the last packet's information
            modeIdx = (int)lastPacket.ReadBits(_modeFieldBits);
            if (modeIdx < 0 || modeIdx >= Modes.Length) return 0;
            var prevMode = Modes[modeIdx];

            // now calculate the totals...
            return mode.BlockSize / 4 + prevMode.BlockSize / 4;
        }

        #endregion

        internal int ReadSamples(float[] buffer, int offset, int count)
        {
            int samplesRead = 0;

            lock (_seekLock)
            {
                if (_prevBuffer != null)
                {
                    // get samples from the previous buffer's data
                    var cnt = Math.Min(count, _prevBuffer.Length);
                    Buffer.BlockCopy(_prevBuffer, 0, buffer, offset, cnt * sizeof(float));

                    // if we have samples left over, rebuild the previous buffer array...
                    if (cnt < _prevBuffer.Length)
                    {
                        var buf = new float[_prevBuffer.Length - cnt];
                        Buffer.BlockCopy(_prevBuffer, cnt * sizeof(float), buf, 0, (_prevBuffer.Length - cnt) * sizeof(float));
                        _prevBuffer = buf;
                    }
                    else
                    {
                        // if no samples left over, clear the previous buffer
                        _prevBuffer = null;
                    }

                    // reduce the desired sample count & increase the desired sample offset
                    count -= cnt;
                    offset += cnt;
                    samplesRead = cnt;
                }
                else if (_isParameterChange)
                {
                    throw new InvalidOperationException("Currently pending a parameter change.  Read new parameters before requesting further samples!");
                }

                int minSize = count + Block1Size * _channels;
                _outputBuffer.EnsureSize(minSize);

                while (_preparedLength * _channels < count && !_eosFound && !_isParameterChange)
                {
                    DecodeNextPacket();

                    // we can safely assume the _prevBuffer was null when we entered this loop
                    if (_prevBuffer != null)
                    {
                        // uh-oh... something is wrong...
                        return ReadSamples(buffer, offset, _prevBuffer.Length);
                    }
                }

                if (_preparedLength * _channels < count)
                {
                    // we can safely assume we've read the last packet...
                    count = _preparedLength * _channels;
                }

                _outputBuffer.CopyTo(buffer, offset, count);
                _preparedLength -= count / _channels;
                _reportedPosition = _currentPosition - _preparedLength;
            }

            return samplesRead + count;
        }

        internal bool IsParameterChange
        {
            get { return _isParameterChange; }
            set
            {
                if (value) throw new InvalidOperationException("Only clearing is supported!");
                _isParameterChange = value;
            }
        }

        internal bool CanSeek
        {
            get { return _packetProvider.CanSeek; }
        }

        internal void SeekTo(long granulePos)
        {
            if (!_packetProvider.CanSeek) throw new NotSupportedException();

            if (granulePos < 0) throw new ArgumentOutOfRangeException("granulePos");

            DataPacket packet;
            if (granulePos > 0)
            {
                packet = _packetProvider.FindPacket(granulePos, GetPacketLength);
                if (packet == null) throw new ArgumentOutOfRangeException("granulePos");
            }
            else
            {
                packet = _packetProvider.GetPacket(4);
            }

            lock (_seekLock)
            {
                // seek the stream
                _packetProvider.SeekToPacket(packet, 1);

                // now figure out where we are and how many samples we need to discard...
                // note that we use the granule position of the "current" packet, since it will be discarded no matter what

                // get the packet that we'll decode next
                var dataPacket = _packetProvider.PeekNextPacket();

                // now read samples until we are exactly at the granule position requested
                CurrentPosition = dataPacket.GranulePosition;
                var cnt = (int)((granulePos - CurrentPosition) * _channels);
                if (cnt > 0)
                {
                    var seekBuffer = new float[cnt];
                    while (cnt > 0)
                    {
                        var temp = ReadSamples(seekBuffer, 0, cnt);
                        if (temp == 0) break;   // we're at the end...
                        cnt -= temp;
                    }
                }
            }
        }

        internal long CurrentPosition
        {
            get { return _reportedPosition; }
            private set
            {
                _reportedPosition = value;
                _currentPosition = value;
                _preparedLength = 0;
                _eosFound = false;

                ResetDecoder(false);
                _prevBuffer = null;
            }
        }

        internal long GetLastGranulePos()
        {
            return _packetProvider.GetGranuleCount();
        }

        internal long ContainerBits
        {
            get { return _packetProvider.ContainerBits; }
        }

        public void ResetStats()
        {
            // only reset the stream info...  don't mess with the container, book, and hdr bits...

            _clipped = false;
            _packetCount = 0;
            _floorBits = 0L;
            _glueBits = 0L;
            _modeBits = 0L;
            _resBits = 0L;
            _wasteBits = 0L;
            _samples = 0L;
            _sw.Reset();
        }

        public int EffectiveBitRate
        {
            get
            {
                if (_samples == 0L) return 0;

                var decodedSeconds = (double)(_currentPosition - _preparedLength) / _sampleRate;

                return (int)(AudioBits / decodedSeconds);
            }
        }

        public int InstantBitRate
        {
            get
            {
                var samples = _sampleCountHistory.Sum();
                if (samples > 0)
                {
                    return (int)((long)_bitsPerPacketHistory.Sum() * _sampleRate / samples);
                }
                else
                {
                    return -1;
                }
            }
        }

        public TimeSpan PageLatency
        {
            get
            {
                return TimeSpan.FromTicks(_sw.ElapsedTicks / PagesRead);
            }
        }

        public TimeSpan PacketLatency
        {
            get
            {
                return TimeSpan.FromTicks(_sw.ElapsedTicks / _packetCount);
            }
        }

        public TimeSpan SecondLatency
        {
            get
            {
                return TimeSpan.FromTicks((_sw.ElapsedTicks / _samples) * _sampleRate);
            }
        }

        public long OverheadBits
        {
            get
            {
                return _glueBits + _metaBits + _timeHdrBits + _wasteHdrBits + _wasteBits + _packetProvider.ContainerBits;
            }
        }

        public long AudioBits
        {
            get
            {
                return _bookBits + _floorHdrBits + _resHdrBits + _mapHdrBits + _modeHdrBits + _modeBits + _floorBits + _resBits;
            }
        }

        public int PagesRead
        {
            get { return _pagesSeen.IndexOf(_lastPageSeen) + 1; }
        }

        public int TotalPages
        {
            get { return _packetProvider.GetTotalPageCount(); }
        }

        public bool Clipped
        {
            get { return _clipped; }
        }
    }
}
#endregion //VorbisStreamDecoder

#region VorbisTime
namespace NVorbis
{
    abstract class VorbisTime
    {
        internal static VorbisTime Init(VorbisStreamDecoder vorbis, DataPacket packet)
        {
            var type = (int)packet.ReadBits(16);

            VorbisTime time = null;
            switch (type)
            {
                case 0: time = new Time0(vorbis); break;
            }
            if (time == null) throw new InvalidDataException();

            time.Init(packet);
            return time;
        }

        VorbisStreamDecoder _vorbis;

        protected VorbisTime(VorbisStreamDecoder vorbis)
        {
            _vorbis = vorbis;
        }

        abstract protected void Init(DataPacket packet);

        class Time0 : VorbisTime
        {
            internal Time0(VorbisStreamDecoder vorbis) : base(vorbis) { }

            protected override void Init(DataPacket packet)
            {

            }
        }
    }
}
#endregion //VorbisTime

namespace liwq
{
    public class OggDecoder
    {
        public ushort Channels { get; private set; }
        public uint SamplesPerSecond { get; private set; }
        public byte[] Data { get; private set; }
        public bool Decode(Stream stream)
        {
            NVorbis.VorbisReader vorbis = new NVorbis.VorbisReader(stream, true);
            float[] buffer = new float[1024];
            List<byte> result = new List<byte>();
            int count;
            while ((count = vorbis.ReadSamples(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    short temp = (short)(32767f * buffer[i]);
                    if (temp > 32767)
                    {
                        result.Add(0xFF);
                        result.Add(0x7F);
                    }
                    else if (temp < -32768)
                    {
                        result.Add(0x80);
                        result.Add(0x00);
                    }
                    result.Add((byte)temp);
                    result.Add((byte)(temp >> 8));
                }
            }
            this.Channels = (ushort)vorbis.Channels;
            this.SamplesPerSecond = (uint)vorbis.SampleRate;
            this.Data = result.ToArray();
            return true;
        }
        public MemoryStream ToWavStream()
        {
            MemoryStream stream = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8);
            writer.Write(new char[4] { 'R', 'I', 'F', 'F' }, 0, 4);
            writer.Write(12 + 18 + this.Data.Length);
            writer.Write(new char[4] { 'W', 'A', 'V', 'E' }, 0, 4);
            writer.Write(new char[4] { 'f', 'm', 't', ' ' }, 0, 4);
            writer.Write(18);

            //typedef struct { 
            //    WORD  wFormatTag; 
            //    WORD  nChannels; 
            //    DWORD nSamplesPerSec; 
            //    DWORD nAvgBytesPerSec; 
            //    WORD  nBlockAlign; 
            //    WORD  wBitsPerSample; 
            //    WORD  cbSize; 
            //} WAVEFORMATEX; 
            writer.Write((short)1);
            writer.Write(this.Channels);
            writer.Write(this.SamplesPerSecond);
            writer.Write(this.SamplesPerSecond * this.Channels * 2);
            writer.Write((short)(2 * this.Channels));
            writer.Write((short)16);
            writer.Write((short)0);

            writer.Write(new char[4] { 'd', 'a', 't', 'a' }, 0, 4);
            writer.Write(this.Data.Length);
            writer.Write(this.Data);
            writer.Flush();

            stream.Seek(0, SeekOrigin.Begin);
            return stream;
        }

    }
}