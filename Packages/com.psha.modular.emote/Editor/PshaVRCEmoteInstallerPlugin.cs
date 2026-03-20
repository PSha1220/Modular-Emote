#if UNITY_EDITOR
using nadena.dev.ndmf;

[assembly: ExportsPlugin(typeof(PshaVRCEmoteInstallerPlugin))]

public sealed class PshaVRCEmoteInstallerPlugin : Plugin<PshaVRCEmoteInstallerPlugin>
{
    public override string QualifiedName => "psha.modular-vrc-emote.installer";
    public override string DisplayName => "Psha Modular VRC Emote";

    protected override void Configure()
    {
        InPhase(BuildPhase.Transforming)
            .AfterPlugin("nadena.dev.modular-avatar")
            .Run("Psha Modular VRC Emote", PshaVRCEmoteInstallerPass.Execute);
    }
}
#endif
