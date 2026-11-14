using System.Security.Cryptography;
using WorkJournal.Web.Services.HistoricalResearch;
namespace WorkJournal.Application.Research;
public sealed record BackupEntry(string RelativePath,string Sha256);
public sealed record BackupReceipt(BackupEntry[] Files,string Status);
public static class OperationalBackup
{
    // Caller must stop workers first. New destination only; no overwrite or live production restore.
    public static BackupReceipt CopyAndVerify(string source,string destination)
    {
        source=Path.GetFullPath(source);destination=Path.GetFullPath(destination);
        if(!Directory.Exists(source)||Directory.Exists(destination)||destination.StartsWith(source+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("OFFLINE_NEW_BACKUP_DESTINATION_REQUIRED");
        var paths=Directory.GetFileSystemEntries(source,"*",SearchOption.AllDirectories);
        if((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0||paths.Any(p=>(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0))throw new InvalidDataException("SYMLINK_BACKUP_FORBIDDEN");
        var entries=paths.Where(File.Exists).Where(p=>!p.EndsWith(".lock",StringComparison.Ordinal)).Select(p=>new BackupEntry(Path.GetRelativePath(source,p),Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))))).OrderBy(e=>e.RelativePath,StringComparer.Ordinal).ToArray();
        Directory.CreateDirectory(destination);
        foreach(var e in entries){var to=Path.Combine(destination,e.RelativePath);Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(Path.Combine(source,e.RelativePath),to,false);}
        foreach(var e in entries)if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(destination,e.RelativePath))))!=e.Sha256||Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(source,e.RelativePath))))!=e.Sha256)throw new InvalidDataException("BACKUP_DIGEST_MISMATCH_OR_SOURCE_CHANGED");
        return new(entries,"BYTE_VERIFIED_REPLAY_REQUIRED");
    }
}
