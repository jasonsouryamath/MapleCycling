using System.IO;
using UnityEngine;

/// <summary>
/// Single source of truth for the repo-relative folders the editor tooling writes to.
///
/// WHY THIS EXISTS. The repository was restructured on 2026-09-15: the Unity project was promoted
/// out of the former <c>MapleRide/unity_project/</c> subfolder up to the repo root, and every
/// non-project folder (good_graphics, backups, logs, improve, docs, ...) was moved under
/// <c>MapleRide/reference/</c>.
///
/// Roughly forty editor scripts derived their output directory as
/// <c>Directory.GetParent(Application.dataPath).Parent.FullName</c> - i.e. "two levels above
/// Assets", which was the repo root while Assets lived at <c>MapleRide/unity_project/Assets</c>.
/// With Assets now at <c>MapleRide/Assets</c> that same expression resolves to the user's DESKTOP,
/// so every diagnostic capture in the project would silently start writing PNGs next to the
/// MapleRide folder instead of into the render set anyone reviews. Nothing would error and no
/// capture would fail; the renders would simply stop appearing where they are looked for, which
/// is the most expensive kind of breakage this project has.
///
/// Both layouts are probed rather than assumed, so this stays correct if the tree moves again.
/// </summary>
public static class MapleRidePaths
{
    /// <summary>The MapleRide repository root, whichever layout the project is in.</summary>
    public static string RepoRoot
    {
        get
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            // Legacy layout: Assets sat in <repo>/Assets.
            var parent = Directory.GetParent(projectRoot);
            if (parent != null && Directory.Exists(Path.Combine(parent.FullName, "tools")) &&
                !Directory.Exists(Path.Combine(projectRoot, "tools")))
                return parent.FullName;
            return projectRoot;
        }
    }

    /// <summary>
    /// Folder holding the reviewable render sets. Post-restructure that is
    /// <c>&lt;repo&gt;/reference/good_graphics</c>; the pre-restructure location is still honoured
    /// so an older checkpoint restored in place keeps working.
    /// </summary>
    public static string Renders
    {
        get
        {
            string root = RepoRoot;
            string reference = Path.Combine(root, "reference", "good_graphics");
            if (Directory.Exists(reference)) return reference;
            string legacy = Path.Combine(root, "good_graphics");
            if (Directory.Exists(legacy)) return legacy;
            Directory.CreateDirectory(reference);
            return reference;
        }
    }

    /// <summary>A named subfolder of <see cref="Renders"/>, created on demand.</summary>
    public static string RenderDir(params string[] parts)
    {
        string dir = Renders;
        foreach (var p in parts) dir = Path.Combine(dir, p);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Sibling reference folders (backups, logs, improve, docs, ...).</summary>
    public static string Reference(string name)
    {
        string root = RepoRoot;
        string under = Path.Combine(root, "reference", name);
        if (Directory.Exists(under)) return under;
        string legacy = Path.Combine(root, name);
        return Directory.Exists(legacy) ? legacy : under;
    }
}
