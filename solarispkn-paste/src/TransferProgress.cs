using System.Diagnostics;

namespace SolarisPKN.Paste;

/// <summary>
/// Opt-in telemetry for a transfer UI. The hot path only updates atomic counters;
/// the UI reads snapshots every 250 ms (Visual) or five seconds (Ultra).
/// No disk reads, process queries,
/// formatting, graph drawing or filesystem polling are performed by this class.
/// </summary>
internal sealed class TransferProgress
{
    public const int VisualUiIntervalMilliseconds=250; // 4 frames/s, scrolling graph
    public const int UltraUiIntervalMilliseconds=5000; // minimum redraw/CPU
    public const int UiIntervalMilliseconds=UltraUiIntervalMilliseconds; // legacy benchmarks
    public static int IntervalFor(bool ultra) =>
        ultra?UltraUiIntervalMilliseconds:VisualUiIntervalMilliseconds;
    private readonly long _started=Stopwatch.GetTimestamp();
    private long _completedBytes;
    private long _activeFileBytes;
    private int _completedFiles;
    private string _filename="";
    private string _phase="Preparando";
    private int _finished;
    private int _metadataOnly;

    public long TotalBytes { get; }
    public int TotalFiles { get; }

    internal TransferProgress(PastePlan plan)
    {
        var items=plan.Items.Where(x=>x.Kind is WorkKind.New or WorkKind.Update).ToArray();
        TotalBytes=items.Sum(x=>Math.Max(0,x.Size));
        TotalFiles=items.Length;
    }

    internal TransferProgress(long totalBytes,int totalFiles)
    {
        if(totalBytes<0 || totalFiles<0)throw new ArgumentOutOfRangeException();
        TotalBytes=totalBytes;
        TotalFiles=totalFiles;
    }

    public void BeginFile(string name)
    {
        Volatile.Write(ref _filename,name);
        Volatile.Write(ref _phase,"Copiando");
        Volatile.Write(ref _activeFileBytes,0);
    }

    public void Transferred(long bytesWithinFile)
    {
        Volatile.Write(ref _activeFileBytes,Math.Max(0,bytesWithinFile));
    }

    public void Verifying() => Volatile.Write(ref _phase,"Verificando BLAKE3");

    public void Completed(WorkItem file)
    {
        Volatile.Write(ref _activeFileBytes,0);
        Interlocked.Add(ref _completedBytes,Math.Max(0,file.Size));
        Interlocked.Increment(ref _completedFiles);
        Volatile.Write(ref _filename,file.RelativePath);
        Volatile.Write(ref _phase,"Verificado");
    }

    public void MetadataOnly() => Volatile.Write(ref _metadataOnly,1);

    public void Finished()
    {
        Volatile.Write(ref _activeFileBytes,0);
        Volatile.Write(ref _phase,"Completado");
        Volatile.Write(ref _finished,1);
    }

    public TransferSnapshot Read()
    {
        long committed=Volatile.Read(ref _completedBytes);
        long active=Volatile.Read(ref _activeFileBytes);
        long processed=Math.Clamp(committed+active,0,TotalBytes);
        int files=Math.Clamp(Volatile.Read(ref _completedFiles),0,TotalFiles);
        bool done=Volatile.Read(ref _finished)==1;
        double fraction=TotalBytes>0?(double)processed/TotalBytes:
            TotalFiles>0?(double)files/TotalFiles:(done?1:0);
        if(!done)fraction=Math.Min(fraction,0.999);
        else fraction=1;
        return new TransferSnapshot(processed,TotalBytes,files,TotalFiles,
            fraction,Volatile.Read(ref _filename),Volatile.Read(ref _phase),
            Stopwatch.GetElapsedTime(_started),done,
            Volatile.Read(ref _metadataOnly)==1);
    }
}
internal sealed record TransferSnapshot(
    long TransferredBytes,long TotalBytes,int FilesDone,int TotalFiles,
    double Fraction,string FileName,string Phase,TimeSpan Elapsed,bool Finished,
    bool MetadataOnly);
