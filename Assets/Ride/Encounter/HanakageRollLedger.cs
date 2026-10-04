using System;
using UnityEngine;

/// <summary>
/// Remembers that a given run has already rolled for Hanakage, and what it rolled.
///
/// WHY THIS EXISTS (handoff section 3 / 18): at the production rate she is a 1-in-30 sighting,
/// which makes the roll the single most valuable event in the encounter and therefore the single
/// most attractive thing to farm. The in-memory <c>_rolled</c> flag on the encounter already
/// stops the obvious attack - riding back below the trigger and forward again - but it dies with
/// the component. Anything that destroys and recreates the encounter without ending the ride
/// (a checkpoint restore, a trigger volume reload, re-running the staging pass, an additive
/// scene swap) would hand the player a fresh roll on the same run.
///
/// So the roll is written to a small ledger keyed by <see cref="RideSession.RunToken"/> the
/// instant it is made, and the encounter rehydrates from it on Resolve. Same run = same answer,
/// no matter how many times the component is rebuilt.
///
/// The ledger is intentionally SMALL and SHORT-LIVED. It is not a save file; it holds the last
/// few run tokens so that a rebuild within a run finds its own entry, and it is not expected to
/// survive a process restart in any meaningful way (a restart puts the rider back on the start
/// line, which is a genuinely new run - see RideSession.RunToken).
/// </summary>
public static class HanakageRollLedger
{
    /// <summary>PROVISIONAL: how many recent runs are remembered. Small on purpose.</summary>
    public const int Capacity = 8;

    private const string Key = "mapleride.hanakage.rollledger.v1";
    private const char RecordSep = ';';
    private const char FieldSep = '|';

    /// <summary>What a completed roll decided.</summary>
    public enum RollResult { Declined = 0, Spawned = 1 }

    [Serializable]
    private struct Entry
    {
        public string token;
        public string courseId;
        public int lapIndex;
        public RollResult result;
    }

    private static Entry[] _cache;

    // ------------------------------------------------------------------ api

    /// <summary>True if this run has already rolled; <paramref name="result"/> is what it got.</summary>
    public static bool TryGet(string runToken, out RollResult result, out string courseId,
                              out int lapIndex)
    {
        result = RollResult.Declined;
        courseId = "";
        lapIndex = -1;
        if (string.IsNullOrEmpty(runToken)) return false;

        var records = Load();
        for (int i = 0; i < records.Length; i++)
        {
            if (!string.Equals(records[i].token, runToken, StringComparison.Ordinal)) continue;
            result = records[i].result;
            courseId = records[i].courseId;
            lapIndex = records[i].lapIndex;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Records the roll for a run. Idempotent by token: a second call for the same run is
    /// ignored rather than overwriting, so a rebuilt encounter can never talk the ledger into a
    /// better answer.
    /// </summary>
    public static void Record(string runToken, string courseId, int lapIndex, RollResult result)
    {
        if (string.IsNullOrEmpty(runToken)) return;
        if (TryGet(runToken, out _, out _, out _)) return;

        var records = Load();
        int keep = Mathf.Min(records.Length, Capacity - 1);
        var next = new Entry[keep + 1];
        next[0] = new Entry
        {
            token = runToken,
            courseId = courseId ?? "",
            lapIndex = lapIndex,
            result = result,
        };
        Array.Copy(records, 0, next, 1, keep);
        Save(next);
    }

    /// <summary>Wipes the ledger. For the test harness and for a deliberate "forget my rides".</summary>
    public static void Clear()
    {
        _cache = Array.Empty<Entry>();
        PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.Save();
    }

    /// <summary>Number of runs currently remembered. Diagnostics only.</summary>
    public static int Count => Load().Length;

    // ------------------------------------------------------------------ storage

    private static Entry[] Load()
    {
        if (_cache != null) return _cache;

        string raw = PlayerPrefs.GetString(Key, "");
        if (string.IsNullOrEmpty(raw))
        {
            _cache = Array.Empty<Entry>();
            return _cache;
        }

        var parts = raw.Split(RecordSep);
        var list = new System.Collections.Generic.List<Entry>(parts.Length);
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;
            var f = part.Split(FieldSep);
            if (f.Length < 4) continue;
            int lap;
            int res;
            if (!int.TryParse(f[2], out lap)) lap = -1;
            if (!int.TryParse(f[3], out res)) res = 0;
            list.Add(new Entry
            {
                token = f[0],
                courseId = f[1],
                lapIndex = lap,
                result = res == 1 ? RollResult.Spawned : RollResult.Declined,
            });
        }
        _cache = list.ToArray();
        return _cache;
    }

    private static void Save(Entry[] records)
    {
        _cache = records;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < records.Length; i++)
        {
            if (i > 0) sb.Append(RecordSep);
            // Tokens and course ids are generated ids, but strip the separators anyway rather
            // than trust that they always will be.
            sb.Append(Sanitize(records[i].token)).Append(FieldSep)
              .Append(Sanitize(records[i].courseId)).Append(FieldSep)
              .Append(records[i].lapIndex).Append(FieldSep)
              .Append((int)records[i].result);
        }
        PlayerPrefs.SetString(Key, sb.ToString());
        PlayerPrefs.Save();
    }

    private static string Sanitize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace(RecordSep, '_').Replace(FieldSep, '_');
    }
}
