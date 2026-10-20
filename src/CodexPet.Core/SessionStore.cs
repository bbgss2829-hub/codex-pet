using System.Text;
namespace CodexPet.Core;

public sealed class SessionFile(string path)
{
    public string Path {get;}=path;
    public SessionCounter Counter {get;private set;}=new();
    public long Offset {get;private set;}
    public DateTime WriteUtc {get;private set;}
    public bool ReadError {get;private set;} public int OversizedLines {get;private set;}
    readonly MemoryStream pending=new();
    bool skipping;
    // Read only complete newline-terminated UTF-8 records. Partial records remain in memory.
    public void Update()
    {
        try
        {
            var info=new FileInfo(Path);
            if(info.Length==Offset && info.LastWriteTimeUtc==WriteUtc) return;
            if(info.Length<Offset || (info.Length==Offset && info.LastWriteTimeUtc!=WriteUtc))
            { Offset=0; Counter=new(); pending.GetBuffer().AsSpan(0,(int)pending.Length).Clear(); pending.SetLength(0); if(pending.Capacity>65536)pending.Capacity=0; skipping=false; }
            using var f=new FileStream(Path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            f.Position=Offset; byte[] buffer=new byte[65536]; int n;
            while((n=f.Read(buffer))>0)
            {
                for(int i=0;i<n;i++)
                {
                    if(buffer[i]==10)
                    {
                        if(!skipping && pending.Length>0) Counter.Accept(Encoding.UTF8.GetString(pending.GetBuffer(),0,(int)pending.Length));
                        pending.GetBuffer().AsSpan(0,(int)pending.Length).Clear(); pending.SetLength(0); if(pending.Capacity>65536)pending.Capacity=0; skipping=false;
                    }
                    else if(!skipping)
                    {
                        if(pending.Length>=4*1024*1024) { pending.GetBuffer().AsSpan(0,(int)pending.Length).Clear(); pending.SetLength(0); pending.Capacity=0; skipping=true; OversizedLines++; }
                        else pending.WriteByte(buffer[i]);
                    }
                }
                Offset+=n;
            }
            WriteUtc=info.LastWriteTimeUtc; ReadError=false;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) { ReadError=true; }
    }
}
public sealed class SessionStore
{
    readonly Dictionary<string,SessionFile> files=new(StringComparer.OrdinalIgnoreCase);
    public string Root {get;}
    public bool Missing {get;private set;}
    public SessionStore(string? root=null) => Root=root ?? System.IO.Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME") ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex"),"sessions");
    public IReadOnlyList<SessionFile> Scan()
    {
        Missing=!Directory.Exists(Root); if(Missing) return [];
        try
        {
            var paths=Directory.EnumerateFiles(Root,"*.jsonl",SearchOption.AllDirectories).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach(var old in files.Keys.Where(k=>!paths.Contains(k)).ToArray()) files.Remove(old);
            foreach(var path in paths)
            {
                if(!files.TryGetValue(path,out var f)) files[path]=f=new(path);
                f.Update();
            }
            return files.Values.Where(f=>!f.Counter.IsChild).GroupBy(f=>string.IsNullOrEmpty(f.Counter.Id)?f.Path:f.Counter.Id).Select(g=>g.OrderByDescending(f=>f.WriteUtc).First()).OrderByDescending(f=>f.WriteUtc).ToArray();
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) { Missing=true; return []; }
    }
}

