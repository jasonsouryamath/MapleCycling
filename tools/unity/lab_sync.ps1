# Unity "Lab 2": a second copy of the project so two agents can run Unity at the same time.
# Dot-sourced by run_steps.ps1 and lab_setup.ps1.
#  * Assets are HARD LINKS to this project's files (no extra disk). Unity saves by replacing a file,
#    which breaks the link, so only files Unity actually changes take real space in the lab.
#  * Every top-level folder that isn't a Unity folder (reference, tools, docs...) is a JUNCTION back
#    into this project, so captures and outputs land here directly.
#  * Sync-LabIn mirrors this project's Assets into the lab before a batch. Sync-LabOut copies the lab's
#    Assets changes, new files and deletions back afterwards. If a file changed on BOTH sides during the
#    batch, this project's version wins and the file is reported as a CONFLICT.
$MainPath = "C:\Users\jason\OneDrive\Desktop\MapleRide"
$LabPath  = "C:\Users\jason\UnityLab2\MapleRide"
$UnityOwnDirs = @("Assets", "Library", "Packages", "ProjectSettings", "UserSettings", "Temp", "Logs", "obj", ".git", ".vs")

if (-not ("LabSync" -as [type])) {
Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class LabSync {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateHardLink(string newName, string existing, IntPtr sa);

    // Mirror everything Unity sees (links cost no space). Leaving files out makes the lab's Unity delete their
    // .meta files, and that deletion would be copied back. Only temp files are skipped; Unity ignores them anyway.
    public static bool Excluded(string rel) {
        string l = rel.ToLowerInvariant();
        return l.EndsWith(".tmp") || l.EndsWith("~");
    }

    // size|mtime read from the file record (directory listings go stale for hard-linked names)
    public static string Stat(string p) {
        var fi = new FileInfo(p);
        return fi.Exists ? fi.Length + "|" + fi.LastWriteTimeUtc.Ticks : null;
    }

    public static Dictionary<string, string> Scan(string root, string sub) {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string top = Path.Combine(root, sub);
        if (!Directory.Exists(top)) return d;
        foreach (var fi in new DirectoryInfo(top).EnumerateFiles("*", SearchOption.AllDirectories)) {
            string rel = fi.FullName.Substring(root.Length).TrimStart('\\');
            if (Excluded(rel)) continue;
            fi.Refresh();
            d[rel] = fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
        }
        return d;
    }

    static void Unprotect(string p) { if (File.Exists(p)) File.SetAttributes(p, FileAttributes.Normal); }

    public static void Link(string dst, string src) {
        Directory.CreateDirectory(Path.GetDirectoryName(dst));
        Unprotect(dst);
        if (File.Exists(dst)) File.Delete(dst);
        if (!CreateHardLink(dst, src, IntPtr.Zero)) File.Copy(src, dst, true);
    }

    public static int LinkTree(string src, string dst) {
        int n = 0;
        if (!Directory.Exists(src)) return 0;
        foreach (var d in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dst + d.Substring(src.Length));
        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)) {
            string t = dst + f.Substring(src.Length);
            if (File.Exists(t)) continue;
            try { Link(t, f); n++; } catch (Exception) { }
        }
        return n;
    }

    // main -> lab. Returns the snapshot both sides share afterwards.
    public static Dictionary<string, string> Mirror(string main, string lab, string sub, List<string> log) {
        var m = Scan(main, sub);
        var l = Scan(lab, sub);
        int linked = 0, removed = 0, failed = 0;
        string mTop = Path.Combine(main, sub), lTop = Path.Combine(lab, sub);
        Directory.CreateDirectory(lTop);
        foreach (var d in Directory.EnumerateDirectories(mTop, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(lTop + d.Substring(mTop.Length));
        foreach (var kv in m) {
            string cur;
            if (l.TryGetValue(kv.Key, out cur) && cur == kv.Value) continue;
            try { Link(Path.Combine(lab, kv.Key), Path.Combine(main, kv.Key)); linked++; }
            catch (Exception e) { failed++; if (failed < 6) log.Add("link failed: " + kv.Key + " (" + e.Message + ")"); }
        }
        foreach (var kv in l) {
            if (m.ContainsKey(kv.Key)) continue;
            try { string p = Path.Combine(lab, kv.Key); Unprotect(p); File.Delete(p); removed++; } catch (Exception) { failed++; }
        }
        var dirs = new List<string>(Directory.EnumerateDirectories(lTop, "*", SearchOption.AllDirectories));
        dirs.Sort((a, b) => b.Length.CompareTo(a.Length));
        foreach (var d in dirs)
            if (!Directory.Exists(mTop + d.Substring(lTop.Length))) { try { Directory.Delete(d, true); } catch (Exception) { } }
        log.Add(string.Format("{0}: {1} files in sync, {2} re-linked, {3} removed, {4} failed", sub, m.Count, linked, removed, failed));
        return m;
    }

    // lab -> main, after a batch
    public static void CopyBack(string main, string lab, string sub, Dictionary<string, string> snap, DateTime since, List<string> log) {
        var l = Scan(lab, sub);
        int copied = 0, deleted = 0, conflicts = 0, metaKept = 0;
        foreach (var kv in l) {
            string s; snap.TryGetValue(kv.Key, out s);
            if (s == kv.Value) continue;
            string mp = Path.Combine(main, kv.Key);
            string mc = Stat(mp);
            if (mc == kv.Value) continue;
            // both Unitys imported the same NEW asset at once: this project's .meta (GUID) wins, which is expected
            if (mc != s && s == null && kv.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) { metaKept++; continue; }
            if (mc != s) { conflicts++; log.Add("CONFLICT (kept this project's copy): " + kv.Key); continue; }
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(mp));
                Unprotect(mp);
                File.Copy(Path.Combine(lab, kv.Key), mp, true);
                copied++;
            } catch (Exception e) { log.Add("copy-back failed: " + kv.Key + " (" + e.Message + ")"); }
        }
        foreach (var kv in snap) {
            if (l.ContainsKey(kv.Key)) continue;
            string mp = Path.Combine(main, kv.Key);
            if (Stat(mp) != kv.Value) continue;
            try { Unprotect(mp); File.Delete(mp); deleted++; } catch (Exception) { }
        }
        string mTop = Path.Combine(main, sub), lTop = Path.Combine(lab, sub);
        var dirs = new List<string>(Directory.EnumerateDirectories(mTop, "*", SearchOption.AllDirectories));
        dirs.Sort((a, b) => b.Length.CompareTo(a.Length));
        foreach (var d in dirs) {
            if (Directory.Exists(lTop + d.Substring(mTop.Length))) continue;
            if (Directory.GetCreationTimeUtc(d) > since) continue;
            try { if (Directory.GetFileSystemEntries(d).Length == 0) Directory.Delete(d); } catch (Exception) { }
        }
        log.Add(string.Format("{0}: copied back {1} changed/new, deleted {2}, conflicts {3}, new-asset .meta already made here {4}", sub, copied, deleted, conflicts, metaKept));
    }
}
'@
}

function Ensure-LabJunctions {
    foreach ($d in Get-ChildItem $MainPath -Directory -Force) {
        if ($UnityOwnDirs -contains $d.Name) { continue }
        $t = Join-Path $LabPath $d.Name
        if (-not (Test-Path $t)) { New-Item -ItemType Junction -Path $t -Target $d.FullName | Out-Null }
    }
}

function Sync-LabIn {
    $log = New-Object System.Collections.Generic.List[string]
    Ensure-LabJunctions
    foreach ($d in "Packages", "ProjectSettings") {
        robocopy (Join-Path $MainPath $d) (Join-Path $LabPath $d) /MIR /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    }
    $snap = [LabSync]::Mirror($MainPath, $LabPath, "Assets", $log)
    $log | ForEach-Object { Write-Host "   [lab2 sync-in] $_" }
    return @{ Snap = $snap; Since = [DateTime]::UtcNow }
}

function Sync-LabOut($state) {
    $log = New-Object System.Collections.Generic.List[string]
    [LabSync]::CopyBack($MainPath, $LabPath, "Assets", $state.Snap, $state.Since, $log)
    $log | ForEach-Object { Write-Host "   [lab2 sync-out] $_" }
}
