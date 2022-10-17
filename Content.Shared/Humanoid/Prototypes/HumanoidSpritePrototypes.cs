using Content.Shared.Humanoid.Markings;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Humanoid.Prototypes;

/// <summary>
///     Humanoid species sprite layer. This is what defines the base layer of
///     a humanoid species sprite, and also defines how markings can appear over
///     that sprite (or at least, the layer this sprite is on).
/// </summary>
[Prototype("humanoidBaseSprite")]
public sealed class HumanoidSpeciesSpriteLayer : IPrototype
{
    [IdDataField]
    public string ID { get; } = default!;
    /// <summary>
    ///     The base sprite for this sprite layer. This is what
    ///     will replace the empty layer tagged by the enum
    ///     tied to this layer.
    ///
    ///     If this is null, no sprite will be displayed, and the
    ///     layer will be invisible until otherwise set.
    /// </summary>
    [DataField("baseSprite")]
    public SpriteSpecifier? BaseSprite { get; }

    /// <summary>
    ///     The alpha of this layer. Ensures that
    ///     this layer will start with this percentage
    ///     of alpha.
    /// </summary>
    [DataField("layerAlpha")]
    public float LayerAlpha { get; } = 1.0f;

    /// <summary>
    ///     If this sprite layer should allow markings or not.
    /// </summary>
    [DataField("allowsMarkings")]
    public bool AllowsMarkings { get; } = true;

    /// <summary>
    ///     If this layer should always match the
    ///     skin tone in a character profile.
    /// </summary>
    [DataField("matchSkin")]
    public bool MatchSkin { get; } = true;

    /// <summary>
    ///     If any markings that go on this layer should
    ///     match the skin tone of this part, including
    ///     alpha.
    /// </summary>
    [DataField("markingsMatchSkin")]
    public bool MarkingsMatchSkin { get; }
}
