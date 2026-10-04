using UnityEngine;

/// <summary>No-op batch entry point: launching Unity on it proves the whole project compiles
/// (the runner log shows "error CS" otherwise). Used by the parallel redesign workstreams.</summary>
public static class MinatoCompileCheck
{
    public static void Run() => Debug.Log("[compile-check] project compiled OK");
}
