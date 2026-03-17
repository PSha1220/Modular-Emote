using System;
using UnityEngine;
using UnityEngine.Serialization;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

[HelpURL("https://psha1220.github.io/modular-emote-docs/publisher-guide/components/VRCEmoteInstaller-Publisher/")]
[AddComponentMenu("ME Psha VRC Emote Installer")]
public class PshaVRCEmoteInstaller : MonoBehaviour, IEditorOnly
{
    public enum EmoteControlType { None = 0, Button = 1, Toggle = 2 }


    [Tooltip("VRCEmote menu to customize. Leave empty to auto detect at build time.")]
    [PshaAssetGuidReferenceType(typeof(VRCExpressionsMenu))]
    public PshaAssetGuidReference targetMenuAsset;

    [Tooltip("Avatar Action layer controller (leave empty to use the descriptor Action layer)")]
    [PshaAssetGuidReferenceType(typeof(RuntimeAnimatorController))]
    public PshaAssetGuidReference actionLayerAsset;

    [Tooltip("Avatar FX layer controller (leave empty to use the descriptor FX layer)")]
    [PshaAssetGuidReferenceType(typeof(RuntimeAnimatorController))]
    public PshaAssetGuidReference fxLayerAsset;


    [SerializeField, HideInInspector, FormerlySerializedAs("targetMenu")]
    private VRCExpressionsMenu _targetMenuObsolete;

    [SerializeField, HideInInspector, FormerlySerializedAs("actionLayer")]
    private RuntimeAnimatorController _actionLayerObsolete;

    [SerializeField, HideInInspector, FormerlySerializedAs("fxLayer")]
    private RuntimeAnimatorController _fxLayerObsolete;

    [SerializeField, HideInInspector]
    public int[] targetMenuPath = Array.Empty<int>();

    [Tooltip("Name shown in the VRCEmote menu")]
    public string emoteName = "[ME]New Emote";

    [Tooltip("VRCEmote slot number (1 to 8)")]
    [Range(1, 8)]
    public int slotIndex = 1;

    [Tooltip("VRCEmote Menu Icon")]
    public Texture2D menuIcon;

    [Tooltip("VRCEmote control type (None keeps the existing type)")]
    public EmoteControlType controlType = EmoteControlType.Toggle;

    [Tooltip("Force Write Defaults off for all states in ME layers.")]
    public bool meWriteDefaultsOff = true;

    [Tooltip("Replace the VRC Emote menu icon with the ME icon.")]
    public bool changeEmoteMenuIcon = true;


    [Tooltip("ME Action template controller for merging")]
    public RuntimeAnimatorController actionMELayer;

    [Tooltip("ME FX template controller for merging")]
    public RuntimeAnimatorController fxMELayer;

    [Tooltip("State name that starts the emote branch in the Action layer")]
    public string startActionState;

    [Tooltip("State name that ends the emote branch in the Action layer")]
    public string endActionState;

    [Tooltip("StateMachine path or name to use as the Action merge scope (optional)")]
    public string actionMergeScope;

    [Tooltip("Whether to show the merge scope field in the inspector")]
    public bool showActionMergeScopeInInspector;

    [Tooltip("Whether to use the merge ME FX layer template")]
    public bool useMergeMEFxLayer;

    [Tooltip("Automatically rename this GameObject during avatar build when using merged ME FX")]
    public bool autoRenameObjectName = true;

    [Tooltip("Optional build-time object name override. Leave empty to use the current GameObject name")]
    public string objectName = string.Empty;

    [Tooltip("Whether to use additional ME FX layers")]
    public bool useAdditionalMEFxLayers;

    [Tooltip("Additional ME FX layers to merge (up to 2 recommended)")]
    public RuntimeAnimatorController[] additionalMEFxLayers;

    [SerializeField, HideInInspector] private int valueInternal = 1;
    [SerializeField, HideInInspector] private string parameterNameInternal = "VRCEmote";

    [SerializeField, HideInInspector]
    private int _cachedAvatarDescriptorInstanceId;

    [SerializeField, HideInInspector]
    private bool _autoRenameObjectNameInitialized;

    private void OnValidate()
    {
        slotIndex = Mathf.Clamp(slotIndex, 1, 8);
        valueInternal = slotIndex;
        parameterNameInternal = "VRCEmote";

        if (!_autoRenameObjectNameInitialized)
        {
            autoRenameObjectName = true;
            _autoRenameObjectNameInitialized = true;
        }

        if (objectName == null)
            objectName = string.Empty;

#if UNITY_EDITOR

        if (!targetMenuAsset.IsSet && _targetMenuObsolete != null)
        {
            // Store GUID reference
            TryMigrate(_targetMenuObsolete, ref targetMenuAsset);
            _targetMenuObsolete = null;
            UnityEditor.EditorUtility.SetDirty(this);
        }
        if (!actionLayerAsset.IsSet && _actionLayerObsolete != null)
        {
            TryMigrate(_actionLayerObsolete, ref actionLayerAsset);
            _actionLayerObsolete = null;
            UnityEditor.EditorUtility.SetDirty(this);
        }
        if (!fxLayerAsset.IsSet && _fxLayerObsolete != null)
        {
            TryMigrate(_fxLayerObsolete, ref fxLayerAsset);
            _fxLayerObsolete = null;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }

#if UNITY_EDITOR
    private static void TryMigrate(UnityEngine.Object obj, ref PshaAssetGuidReference dst)
    {
        if (obj == null) return;
        if (UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out var g, out long lid))
        {
            dst.guid = g;
            dst.localId = lid;
            dst.nameHint = obj.name;
        }
        else
        {
            dst.guid = string.Empty;
            dst.localId = 0;
            dst.nameHint = obj.name;
        }
    }
#endif

    private void Reset()
    {
        ResetToDefaults();
    }

    public void ResetToDefaults()
    {
        emoteName = "[ME]New Emote";
        slotIndex = 1;

        // Clear soft references to avoid prefab object references
        targetMenuAsset = default;
        actionLayerAsset = default;
        fxLayerAsset = default;

        // Reset targetMenuPath to default to avoid prefab dependency
        targetMenuPath = Array.Empty<int>();


        controlType = EmoteControlType.Toggle;
        meWriteDefaultsOff = true;
        changeEmoteMenuIcon = true;


        actionMELayer = null;
        fxMELayer = null;

        // Action merge settings
        startActionState = string.Empty;
        endActionState = string.Empty;
        actionMergeScope = string.Empty;
        showActionMergeScopeInInspector = false;

        // FX merge settings
        useMergeMEFxLayer = false;
        autoRenameObjectName = true;
        objectName = string.Empty;
        _autoRenameObjectNameInitialized = true;
        useAdditionalMEFxLayers = false;
        additionalMEFxLayers = Array.Empty<RuntimeAnimatorController>();

        // Internal synced fields
        valueInternal = slotIndex;
        parameterNameInternal = "VRCEmote";

#if UNITY_EDITOR
        try
        {
            const string defaultIconPath =
                "Packages/com.psha.modular.emote/Runtime/Image/PshaModularVRCEmote_EXMenuIcon.png";

            var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(defaultIconPath);
            if (tex != null)
            {
                menuIcon = tex;
            }
            else
            {
                var script = UnityEditor.MonoScript.FromMonoBehaviour(this);
                menuIcon = UnityEditor.AssetPreview.GetMiniThumbnail(script);
            }
        }
        catch
        {
            menuIcon = null;
        }
#endif
    }


#if UNITY_EDITOR
    public void ResolveReferences()
    {
        targetMenuAsset.Get<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu>(this);
        actionLayerAsset.Get<RuntimeAnimatorController>(this);
        fxLayerAsset.Get<RuntimeAnimatorController>(this);
    }
#endif

    public string GetResolvedBuildObjectName()
    {
        if (!string.IsNullOrWhiteSpace(objectName))
            return objectName.Trim();

        return gameObject != null ? gameObject.name : string.Empty;
    }

    public int Value => valueInternal;
    public string ParameterName => parameterNameInternal;
}