using System.Collections.Specialized;
using System.Windows.Forms;

namespace SolarisPKN.Paste;

/// <summary>
/// Runs the real clipboard FileDrop parser, planner and verified copier using
/// *synthetic IDataObject*, without reading or changing the user's clipboard.
/// Isolated to a fresh temporary directory with no user file operations.
/// </summary>
internal static class ClipboardIntegrationSelfTest
{
    public static void Run()
    {
        string temp=Path.Combine(Path.GetTempPath(),
            "SolarisPKN-Paste-Clipboard-Test-"+Guid.NewGuid().ToString("N"));
        var report=Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
            "SolarisPKN","Paste","clipboard-integration-test.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        Directory.CreateDirectory(temp);
        var checks=new List<string>();
        try
        {
            string source=Path.Combine(temp,"source");
            string target=Path.Combine(temp,"destination");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(target);
            string file=Path.Combine(source,"clipboard-smoke.txt");
            File.WriteAllText(file,"alpha - FileDrop copy");

            var fileList=new StringCollection {file};
            var copyObject=new DataObject();
            copyObject.SetFileDropList(fileList);
            copyObject.SetData("Preferred DropEffect",
                new MemoryStream(BitConverter.GetBytes(1)));

            var snapshot=ClipboardInput.FromDataObject(copyObject);
            Check(snapshot.Paths.Count==1 &&
                  string.Equals(snapshot.Paths[0],file,StringComparison.OrdinalIgnoreCase) &&
                  !snapshot.Cut,"Explorer FileDrop COPY recognized");
            var opts=new PasteOptions { AskOnConflict=false,AutoHardwareProfile=false,
                SmallFileOptimization=false,SmallFilePipeline=false };
            var plan=PasteEngine.Analyze(snapshot.Paths,target,snapshot.Cut,opts);
            Check(plan.Count(WorkKind.New)==1,"Clipboard source plans a new file");

            PasteQueue.Execute(plan);
            string copied=Path.Combine(target,"clipboard-smoke.txt");
            Check(File.Exists(copied) &&
                  File.ReadAllText(copied)=="alpha - FileDrop copy" &&
                  PasteEngine.Blake3File(copied)==PasteEngine.Blake3File(file),
                  "Actual clipboard-driven COPY writes verified bytes");

            // Regression: copying a file into its own directory must create a
            // sibling with a new name, never overwrite the clipboard source.
            var sameFolder=PasteEngine.Analyze(snapshot.Paths,source,false,opts);
            Check(sameFolder.Count(WorkKind.New)==1 &&
                  sameFolder.Items[0].Destination!=file &&
                  sameFolder.Items[0].Destination.EndsWith("clipboard-smoke - Copia.txt",
                      StringComparison.OrdinalIgnoreCase),
                  "Same-directory COPY plans a separate sibling");
            PasteQueue.Execute(sameFolder);
            Check(File.ReadAllText(file)=="alpha - FileDrop copy" &&
                  File.ReadAllText(Path.Combine(source,"clipboard-smoke - Copia.txt"))==
                    "alpha - FileDrop copy",
                  "Same-directory COPY preserves original and writes duplicate");
            var secondCopy=PasteEngine.Analyze(snapshot.Paths,source,false,opts);
            Check(secondCopy.Items.Single().Destination.EndsWith(
                  "clipboard-smoke - Copia (2).txt",StringComparison.OrdinalIgnoreCase),
                  "Repeated same-directory COPY chooses next free filename");
            var sameFolderCut=PasteEngine.Analyze(snapshot.Paths,source,true,opts);
            Check(sameFolderCut.Items.Count==0 && File.Exists(file),
                  "Same-directory CUT does not delete original");

            File.WriteAllText(file,"beta - clipboard update");
            plan=PasteEngine.Analyze(snapshot.Paths,target,snapshot.Cut,opts);
            Check(plan.Count(WorkKind.Update)==1,
                "Clipboard source plans replacement of an existing file");
            PasteQueue.Execute(plan);
            Check(File.ReadAllText(copied)=="beta - clipboard update" &&
                  File.ReadAllText(file)=="beta - clipboard update",
                "Actual clipboard-driven UPDATE verified and source preserved");

            var cutObject=new DataObject();
            cutObject.SetFileDropList(fileList);
            cutObject.SetData("Preferred DropEffect",
                new MemoryStream(BitConverter.GetBytes(2)));
            var cutSnapshot=ClipboardInput.FromDataObject(cutObject);
            Check(cutSnapshot.Cut,"Explorer FileDrop CUT detected without moving files");

            bool rejected=false;
            try { ClipboardInput.FromDataObject(new DataObject()); }
            catch(InvalidOperationException) {rejected=true;}
            Check(rejected,"Missing clipboard files provide a clear error");

            File.WriteAllText(report,string.Join(Environment.NewLine,checks)+
                Environment.NewLine+"Total: 11 PASS, 0 FAIL"+Environment.NewLine);
        }
        catch(Exception e)
        {
            File.WriteAllText(report,string.Join(Environment.NewLine,checks)+
                Environment.NewLine+"FAIL: "+e+Environment.NewLine);
            throw;
        }
        finally
        {
            if(Directory.Exists(temp))
                Directory.Delete(temp,recursive:true);
        }

        void Check(bool success,string label)
        {
            if(!success)throw new IOException("Clipboard integration regression: "+label);
            checks.Add("PASS "+label);
        }
    }
}
