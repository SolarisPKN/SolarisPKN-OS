using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Blake3;

namespace SolarisPKN.Paste;

/// <summary>
/// RESEARCH / OPT-IN ONLY: native Windows NO_BUFFERING + OVERLAPPED source reads,
/// with page/sector-aligned unmanaged buffers and a bounded queue.
/// Writes are buffered by Windows to avoid altering flush and tail semantics.
/// A separate buffered source handle copies the non-64KiB-aligned tail.
/// BLAKE3 source is computed in offset order; destination is flushed then re-read
/// independently with BLAKE3 before the caller can publish the staging file.
/// No SetFileValidData, kernel service, Defender modification or user-file test.
/// </summary>
internal static unsafe class NativeDirectReadEngine
{
    private const int Align=64*1024;
    private const uint GENERIC_READ=0x80000000;
    private const uint FILE_SHARE_READ=0x00000001;
    private const uint OPEN_EXISTING=3;
    private const uint FILE_FLAG_NO_BUFFERING=0x20000000;
    private const uint FILE_FLAG_OVERLAPPED=0x40000000;
    private const uint FILE_FLAG_SEQUENTIAL_SCAN=0x08000000;
    private const int ERROR_IO_PENDING=997;
    private const int ERROR_INVALID_PARAMETER=87;
    private const int ERROR_NOT_SUPPORTED=50;
    private const int ERROR_INVALID_FUNCTION=1;
    private const int ERROR_HANDLE_EOF=38;

    [StructLayout(LayoutKind.Sequential)]
    private struct WinOverlapped
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public uint Offset;
        public uint OffsetHigh;
        public IntPtr Event;
    }

    [DllImport("kernel32.dll", EntryPoint="CreateFileW",
        CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern SafeFileHandle CreateFileW(string path,uint access,
        uint share,IntPtr security,uint creation,uint flags,IntPtr template);

    [DllImport("kernel32.dll",EntryPoint="ReadFile",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(SafeFileHandle file,IntPtr buffer,
        uint count,IntPtr numberRead,IntPtr overlapped);

    [DllImport("kernel32.dll",EntryPoint="GetOverlappedResult",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOverlappedResult(SafeFileHandle file,IntPtr overlapped,
        out uint bytes,[MarshalAs(UnmanagedType.Bool)]bool wait);

    [DllImport("kernel32.dll",EntryPoint="CreateEventW",SetLastError=true)]
    private static extern IntPtr CreateEvent(IntPtr security,
        [MarshalAs(UnmanagedType.Bool)]bool manualReset,
        [MarshalAs(UnmanagedType.Bool)]bool initialState,string? name);

    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll",EntryPoint="GetDiskFreeSpaceW",
        CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpace(string root,out uint sectorsPerCluster,
        out uint bytesPerSector,out uint freeClusters,out uint totalClusters);

    private sealed class AlignedRegion : MemoryManager<byte>
    {
        private byte* _pointer;
        private readonly int _capacity;

        internal AlignedRegion(int capacity)
        {
            if(capacity <= 0 || capacity % Align != 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity=capacity;
            _pointer=(byte*)NativeMemory.AlignedAlloc((nuint)capacity,(nuint)Align);
            if(_pointer==null)throw new OutOfMemoryException("Cannot allocate aligned direct-I/O buffer");
        }

        internal IntPtr Address => (IntPtr)_pointer;

        public override Span<byte> GetSpan()
        {
            if(_pointer==null)throw new ObjectDisposedException(nameof(AlignedRegion));
            return new Span<byte>(_pointer,_capacity);
        }
        public override MemoryHandle Pin(int elementIndex=0)
        {
            if(_pointer==null)throw new ObjectDisposedException(nameof(AlignedRegion));
            if(elementIndex<0||elementIndex>_capacity)
                throw new ArgumentOutOfRangeException(nameof(elementIndex));
            return new MemoryHandle(_pointer+elementIndex,default,this);
        }
        public override void Unpin(){}
        protected override void Dispose(bool disposing)
        {
            if(_pointer!=null)
            {
                NativeMemory.AlignedFree(_pointer);
                _pointer=null;
            }
        }
    }

    private sealed class PendingRead : IDisposable
    {
        private readonly SafeFileHandle _file;
        private readonly AlignedRegion _memory;
        private IntPtr _event;
        private IntPtr _native;
        private bool _issued;
        private bool _complete;
        private int _resultBytes;
        internal long Offset { get; }
        internal int Size { get; }
        internal Memory<byte> Memory => _memory.Memory[..Size];
        internal Span<byte> Data => _memory.GetSpan()[..Size];

        internal PendingRead(SafeFileHandle file,long offset,int length)
        {
            _file=file;
            Offset=offset;
            Size=length;
            _memory=new AlignedRegion(length);
            try
            {
                _event=CreateEvent(IntPtr.Zero,true,false,null);
                if(_event==IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot create I/O event");
                _native=Marshal.AllocHGlobal(Marshal.SizeOf<WinOverlapped>());
                var operation=new WinOverlapped {
                    Offset=unchecked((uint)offset),
                    OffsetHigh=unchecked((uint)(offset>>32)),
                    Event=_event
                };
                Marshal.StructureToPtr(operation,_native,false);
                bool immediate=ReadFile(_file,_memory.Address,(uint)length,IntPtr.Zero,_native);
                if(!immediate)
                {
                    int error=Marshal.GetLastWin32Error();
                    if(error!=ERROR_IO_PENDING)
                        throw new Win32Exception(error,$"Direct read offset={offset} len={length}");
                }
                _issued=true;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal int Finish()
        {
            if(!_issued)throw new InvalidOperationException("Direct read was not issued");
            if(!_complete)
            {
                if(!GetOverlappedResult(_file,_native,out uint result,true))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        $"Direct read did not complete at offset={Offset}");
                _resultBytes=checked((int)result);
                _complete=true;
            }
            return _resultBytes;
        }

        public void Dispose()
        {
            // Even when caller gets an exception, never free an event, native
            // OVERLAPPED or buffer while the kernel still owns a pending read.
            if(_issued && !_complete)
            {
                try { _=GetOverlappedResult(_file,_native,out _,true); }
                catch { /* release after completion attempt, original error preserved */ }
            }
            if(_native!=IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_native);
                _native=IntPtr.Zero;
            }
            if(_event!=IntPtr.Zero)
            {
                CloseHandle(_event);
                _event=IntPtr.Zero;
            }
            ((IDisposable)_memory).Dispose();
        }
    }

    internal static string CopyVerified(string source,string temporary,long expectedLength,
        string? expectedDigest,int depth,int blockMiB,bool preallocate,
        Action<string>? progress,CancellationToken cancel,TransferProgress? metrics=null)
    {
        if(!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Direct I/O needs Windows");
        if(expectedLength<0)throw new ArgumentOutOfRangeException(nameof(expectedLength));
        if(depth is <1 or >8)throw new ArgumentOutOfRangeException(nameof(depth));
        if(blockMiB is <1 or >8)throw new ArgumentOutOfRangeException(nameof(blockMiB));
        cancel.ThrowIfCancellationRequested();
        if(File.Exists(temporary)||Directory.Exists(temporary))
            throw new IOException("Refusing to overwrite an existing temporary file.");

        var before=new FileInfo(source);
        if(!before.Exists || before.Length!=expectedLength)
            throw new IOException("Source size changed before direct I/O.");
        var sourceTicks=before.LastWriteTimeUtc.Ticks;
        string root=Path.GetPathRoot(Path.GetFullPath(source))!;
        if(!GetDiskFreeSpace(root,out _,out uint sector,out _,out _))
            throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot inspect source disk alignment.");
        // 64KiB aligned buffers and offsets work with conventional 512B/4KiB
        // sectors, even where physical and logical sector sizes differ.
        // Do NOT attempt exotic sectors which exceed our alignment guarantee.
        if(sector == 0 || sector>Align || (Align%sector)!=0)
            throw new NotSupportedException("Unsupported direct-I/O sector size "+sector);

        long fullLength=expectedLength-(expectedLength%Align);
        int blockBytes=blockMiB*1024*1024;
        var queue=new Queue<PendingRead>(depth);
        long nextOffset=0;
        long totalWritten=0;
        long lastUpdate=0;
        using var digest=Hasher.New();

        // Opening a separate handle for the final tail is deliberate: direct
        // reads must not request unaligned lengths/offsets past EOF.
        using var direct=CreateFileW(source,GENERIC_READ,FILE_SHARE_READ,
            IntPtr.Zero,OPEN_EXISTING,FILE_FLAG_NO_BUFFERING|
                FILE_FLAG_OVERLAPPED|FILE_FLAG_SEQUENTIAL_SCAN,IntPtr.Zero);
        if(direct.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Direct I/O unsupported or source could not be opened.");
        using var target=new FileStream(temporary,new FileStreamOptions {
            Mode=FileMode.CreateNew,
            Access=FileAccess.Write,Share=FileShare.None,BufferSize=128*1024,
            Options=FileOptions.SequentialScan,
            PreallocationSize=preallocate ? expectedLength : 0
        });
        void IssueRead()
        {
            if(nextOffset>=fullLength)return;
            int size=checked((int)Math.Min((long)blockBytes,fullLength-nextOffset));
            var item=new PendingRead(direct,nextOffset,size);
            queue.Enqueue(item);
            nextOffset+=size;
        }
        try
        {
            for(int n=0;n<depth;n++)IssueRead();
            while(queue.Count>0)
            {
                cancel.ThrowIfCancellationRequested();
                using var chunk=queue.Dequeue();
                int read=chunk.Finish();
                if(read!=chunk.Size)
                    throw new IOException($"Unaligned or incomplete direct read: {read}/{chunk.Size}");
                digest.Update(chunk.Data);
                target.Write(chunk.Data);
                totalWritten+=read;
                metrics?.Transferred(totalWritten);
                long now=Stopwatch.GetTimestamp();
                if(progress is not null && now-lastUpdate>=Stopwatch.Frequency)
                {
                    progress($"Direct-I/O: {totalWritten:N0}/{expectedLength:N0} bytes");
                    lastUpdate=now;
                }
                IssueRead();
            }
            // Preserve data including the unaligned trailing 1..65535 bytes.
            if(fullLength<expectedLength)
            {
                int tail=checked((int)(expectedLength-fullLength));
                byte[] buffer=ArrayPool<byte>.Shared.Rent(tail);
                try
                {
                    using var buffered=new FileStream(source,FileMode.Open,FileAccess.Read,
                        FileShare.Read,64*1024,FileOptions.RandomAccess);
                    buffered.Position=fullLength;
                    buffered.ReadExactly(buffer.AsSpan(0,tail));
                    digest.Update(buffer.AsSpan(0,tail));
                    target.Write(buffer.AsSpan(0,tail));
                    totalWritten+=tail;
                    metrics?.Transferred(totalWritten);
                }
                finally {ArrayPool<byte>.Shared.Return(buffer); }
            }
            if(totalWritten!=expectedLength)
                throw new IOException("Direct-I/O byte count does not match source size.");
            target.Flush(flushToDisk:true);
        }
        finally
        {
            // Explicitly drain every outstanding native operation on failure
            // before disposing the direct file handle.
            while(queue.Count>0)
                queue.Dequeue().Dispose();
        }

        string readHash=digest.Finalize().ToString();
        var after=new FileInfo(source);
        after.Refresh();
        if(after.Length!=expectedLength || after.LastWriteTimeUtc.Ticks!=sourceTicks)
            throw new IOException("Source changed during direct read.");
        if(expectedDigest is not null &&
           !string.Equals(expectedDigest,readHash,StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source BLAKE3 does not match the preview.");
        cancel.ThrowIfCancellationRequested();
        metrics?.Verifying();
        // Target handle must close before independent verification.
        target.Dispose();
        string writtenHash=PasteEngine.Blake3File(temporary);
        if(!string.Equals(readHash,writtenHash,StringComparison.OrdinalIgnoreCase))
            throw new IOException("Direct I/O BLAKE3 mismatch in destination staging.");
        return readHash;
    }
}
