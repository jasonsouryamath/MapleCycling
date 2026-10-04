using UnityEngine;

/// <summary>
/// Coral's independent rider controller. The shared solver understands the project's
/// glTF humanoid bone convention, while this distinct component keeps Coral's setup
/// separate from Kuro's character and armature instances.
/// </summary>
[DisallowMultipleComponent]
public sealed class CoralBikeRig : KuroBikeRig
{
}
