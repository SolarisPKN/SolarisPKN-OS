using System.Text.Json;
namespace SolarisPKN.Paste;
internal static class DeviceProfileDiagnostic
{
    internal static void Run()
    {
        const string report=@"D:\researchs\SolarisPKN-Paste\reports\device-profile-20261107.json";
        if(File.Exists(report))throw new IOException("Profile report exists, refuse overwrite");
        var profiles=new List<object>();
        foreach(var drive in new[]{@"C:\",@"D:\",@"E:\"})
        {
            var fs=FileSystemProfile.Detect(drive);
            profiles.Add(new {
                drive,storageBusType=StorageTopology.BusType(drive),
                fs.Root,fs.FileSystem,fs.ClusterBytes,fs.Rotational,fs.AutoWriters,
                solidStateExplicit=StorageTopology.IsConfirmedSolidState(drive),
                queueKey=StorageTopology.QueueKey(drive)
            });
        }
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        File.WriteAllText(report,JsonSerializer.Serialize(profiles,
            new JsonSerializerOptions{WriteIndented=true}));
    }
}
