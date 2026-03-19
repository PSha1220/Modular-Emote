#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Immutable;
using nadena.dev.ndmf;
using nadena.dev.ndmf.vrchat;
using nadena.dev.ndmf.animator;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

public static class PshaVRCEmoteInstallerPass
{
    private const string EmoteParamName = "VRCEmote";

    private const string DefaultMenuIconAssetPath = "Packages/com.psha.modular.emote/Runtime/Image/PshaModularVRCEmote_EXMenuIcon.png";


    private static Texture2D s_meMenuIcon;
    private static bool s_meMenuIconSearched;

    private static Texture2D GetPshaEmoteMenuIcon(PshaVRCEmoteInstaller anyInstaller)
    {
        if (s_meMenuIconSearched) return s_meMenuIcon;
        s_meMenuIconSearched = true;

        try
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultMenuIconAssetPath);
            if (tex == null) return null;

            s_meMenuIcon = tex;
            return s_meMenuIcon;
        }
        catch
        {
            return null;
        }
    }


    private const string StartSettingsStateName = "[ME] StartState Transition Settings";


    public static void Execute(BuildContext context)
    {
        Execute_AnimatorOnly(context);
        Execute_MenuOnly(context);
    }

    private static List<PshaVRCEmoteInstaller> CollectFinalInstallers(GameObject avatarRoot)
    {
        var managers = avatarRoot.GetComponentsInChildren<PshaVRCEmoteInstaller>(true);
        if (managers == null || managers.Length == 0) return null;

        var list = new List<PshaVRCEmoteInstaller>(managers);
        list.RemoveAll(m => m == null || !m.gameObject.activeInHierarchy);
        if (list.Count == 0) return null;

        list.Sort((a, b) => CompareHierarchyOrder(a.transform, b.transform));

        var finalList = FilterFinalSlotWinnersInHierarchyOrder(list);
        if (finalList.Count == 0) return null;

        return finalList;
    }

    public static void Execute_AnimatorOnly(BuildContext context)
    {
        var avatarRoot = context.AvatarRootObject;
        if (avatarRoot == null) return;

        var finalList = CollectFinalInstallers(avatarRoot);
        if (finalList == null) return;

        ApplyBuildObjectRenames(finalList);

        SetupActionTemplates_BC(context, finalList);
        SetupFxTemplates_BC(context, finalList);
    }

    private static void ApplyBuildObjectRenames(List<PshaVRCEmoteInstaller> installers)
    {
        if (installers == null || installers.Count == 0) return;

        foreach (var installer in installers)
        {
            if (installer == null || installer.gameObject == null) continue;
            if (!installer.useMergeMEFxLayer) continue;

            var resolvedName = installer.GetResolvedBuildObjectName();
            if (string.IsNullOrWhiteSpace(resolvedName)) continue;

            if (!string.Equals(installer.gameObject.name, resolvedName, StringComparison.Ordinal))
            {
                installer.gameObject.name = resolvedName;
            }
        }
    }

    public static void Execute_MenuOnly(BuildContext context)
    {
        var avatarRoot = context.AvatarRootObject;
        if (avatarRoot == null) return;

        var descriptor = context.VRChatAvatarDescriptor();
        if (descriptor == null) return;

        var rootMenu = descriptor.expressionsMenu;
        if (rootMenu == null)
        {
            Debug.LogWarning(
                $"[PshaVRCEmoteInstallerPass] descriptor.expressionsMenu is null on {avatarRoot.name}. " +
                "Skipped ExpressionsMenu patch."
            );
            return;
        }

        var finalList = CollectFinalInstallers(avatarRoot);
        if (finalList == null) return;

        bool mayNeedAuto = false;
        foreach (var m in finalList)
        {
            if (m == null) continue;
            if (!m.targetMenuAsset.IsSet || m.targetMenuPath == null || m.targetMenuPath.Length == 0)
            {
                mayNeedAuto = true;
                break;
            }
        }

        VRCExpressionsMenu autoEmoteMenu = FindEmoteMenu(rootMenu);
        if (mayNeedAuto && autoEmoteMenu == null)
        {
            Debug.LogWarning(
                "[PshaVRCEmoteInstallerPass] Failed to auto detect the VRCEmote menu. " +
                "If your targetMenu reference/path cannot be resolved during build, menu patching will be skipped."
            );
        }

        var modifications = new Dictionary<VRCExpressionsMenu, List<PshaVRCEmoteInstaller>>();
        foreach (var m in finalList)
        {
            if (m == null) continue;

            var target = ResolveTargetMenu_NoScore(rootMenu, m, autoEmoteMenu);
            if (target == null) continue;

            if (!modifications.TryGetValue(target, out var g))
            {
                g = new List<PshaVRCEmoteInstaller>();
                modifications.Add(target, g);
            }
            g.Add(m);
        }

        if (modifications.Count == 0) return;

        var cloneMap = new Dictionary<VRCExpressionsMenu, VRCExpressionsMenu>();
        var toSave = new List<UnityEngine.Object>();

        var clonedRoot = CloneAndPatchMenu(rootMenu, modifications, cloneMap, toSave);

        SaveAssetsSafe(context, toSave);

        descriptor.expressionsMenu = clonedRoot;

        Debug.Log($"[PshaVRCEmoteInstallerPass] Applied Psha Modular VRC Emote settings to the cloned menu tree for {avatarRoot.name}.");
    }







    private static int CompareHierarchyOrder(Transform a, Transform b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a == null) return 1;
        if (b == null) return -1;

        var pathA = BuildSiblingIndexPath(a);
        var pathB = BuildSiblingIndexPath(b);

        int min = Math.Min(pathA.Count, pathB.Count);
        for (int i = 0; i < min; i++)
        {
            int cmp = pathA[i].CompareTo(pathB[i]);
            if (cmp != 0) return cmp;
        }


        int lenCmp = pathA.Count.CompareTo(pathB.Count);
        if (lenCmp != 0) return lenCmp;


        return a.GetInstanceID().CompareTo(b.GetInstanceID());
    }

    private static List<int> BuildSiblingIndexPath(Transform t)
    {

        var path = new List<int>(8);
        while (t != null)
        {
            path.Add(t.GetSiblingIndex());
            t = t.parent;
        }
        path.Reverse();
        return path;
    }






    private static List<PshaVRCEmoteInstaller> FilterFinalSlotWinnersInHierarchyOrder(List<PshaVRCEmoteInstaller> sortedInstallers)
    {
        var result = new List<PshaVRCEmoteInstaller>(8);
        if (sortedInstallers == null || sortedInstallers.Count == 0) return result;

        var used = new bool[9];
        foreach (var inst in sortedInstallers)
        {
            if (inst == null) continue;
            int slot = Mathf.Clamp(inst.slotIndex, 1, 8);
            if (used[slot]) continue;

            used[slot] = true;
            result.Add(inst);
        }
        return result;
    }





    private static void SetupActionTemplates_BC(
    BuildContext context,
    List<PshaVRCEmoteInstaller> installers
)
    {
        if (installers == null || installers.Count == 0) return;


        var animatorCtx = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
        var vcc = animatorCtx.ControllerContext;


        if (!vcc.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.Action, out var vAction))
        {

            return;
        }


        foreach (var installer in installers)
        {
            if (installer == null) continue;
            if (installer.actionMELayer == null) continue;
            MergeActionSlot_BC(vAction, installer, vcc);
        }
    }






    private static void SetupFxTemplates_BC(
        BuildContext context,
        List<PshaVRCEmoteInstaller> installers
    )
    {
        if (installers == null || installers.Count == 0) return;

        var animatorCtx = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
        var vcc = animatorCtx.ControllerContext;

        if (!vcc.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var vFx))
        {
            return;
        }


        foreach (var installer in installers)
        {
            if (installer == null) continue;

            bool wantsMainFx = installer.useMergeMEFxLayer;
            bool wantsAdditionalFx = installer.useAdditionalMEFxLayers;
            if (!wantsMainFx && !wantsAdditionalFx) continue;

            MergeFxForSlot_BC(vFx, installer, vcc);
        }
    }






    private static void MergeFxForSlot_BC(
        VirtualAnimatorController vFx,
        PshaVRCEmoteInstaller installer,
        VirtualControllerContext vcc
    )
    {
        if (vFx == null || installer == null || vcc == null) return;

        int slot = Mathf.Clamp(installer.slotIndex, 1, 8);




        if (installer.useMergeMEFxLayer)
        {
            if (installer.fxMELayer == null)
            {
                Debug.LogWarning(
                    $"[PshaVRCEmoteInstallerPass] Slot {slot}: 'Use Merge ME FX' is enabled, but fxMELayer is not set."
                );
            }
            else if (installer.fxMELayer is AnimatorController templateCtrl)
            {
                MergeSingleFxTemplate_BC(
                    vFx,
                    templateCtrl,
                    slot,
                    vcc,
                    installer.meWriteDefaultsOff,
                    layerNameOverride: $"PshaEmoteFX_{slot}",
                    cloneVariant: 0
                );
            }
            else
            {
                Debug.LogWarning(
                    $"[PshaVRCEmoteInstallerPass] Slot {slot}: fxMELayer is not an AnimatorController, cannot merge the FX template. ({installer.fxMELayer.GetType().Name})"
                );
            }
        }




        if (installer.useAdditionalMEFxLayers)
        {
            if (installer.additionalMEFxLayers == null || installer.additionalMEFxLayers.Length == 0)
            {
                Debug.LogWarning(
                    $"[PshaVRCEmoteInstallerPass] Slot {slot}: '+Additional ME FX' is enabled, but additionalMEFxLayers is empty."
                );
                return;
            }


            int count = Mathf.Min(2, installer.additionalMEFxLayers.Length);

            bool anyMerged = false;

            for (int i = 0; i < count; i++)
            {
                var rac = installer.additionalMEFxLayers[i];
                if (rac == null) continue;

                var exCtrl = rac as AnimatorController;
                if (exCtrl == null)
                {
                    Debug.LogWarning(
                        $"[PshaVRCEmoteInstallerPass] Slot {slot}: additionalMEFxLayers[{i}] is not an AnimatorController, cannot merge the extra FX template. ({rac.GetType().Name})"
                    );
                    continue;
                }

                string layerName = $"PshaEmoteFX_{slot}_Ex{i + 1}";

                MergeSingleFxTemplate_BC(
                    vFx,
                    exCtrl,
                    slot,
                    vcc,
                    installer.meWriteDefaultsOff,
                    layerNameOverride: layerName,
                    cloneVariant: i + 1
                );

                anyMerged = true;
            }

            if (!anyMerged)
            {
                Debug.LogWarning(
                    $"[PshaVRCEmoteInstallerPass] Slot {slot}: '+Additional ME FX' is enabled, but the Ex1 or Ex2 template is not set."
                );
            }
        }
    }








    private static void MergeSingleFxTemplate_BC(
        VirtualAnimatorController vFx,
        AnimatorController templateCtrl,
        int slotIndex,
        VirtualControllerContext vcc,
        bool meWriteDefaultsOff
    )
    {
        MergeSingleFxTemplate_BC(
            vFx,
            templateCtrl,
            slotIndex,
            vcc,
            meWriteDefaultsOff,
            layerNameOverride: null,
            cloneVariant: 0
        );
    }













    private static void MergeSingleFxTemplate_BC(
        VirtualAnimatorController vFx,
        AnimatorController templateCtrl,
        int slotIndex,
        VirtualControllerContext vcc,
        bool meWriteDefaultsOff,
        string layerNameOverride,
        int cloneVariant
    )
    {
        if (vFx == null || templateCtrl == null || vcc == null) return;

        VirtualAnimatorController vTemplateController = null;

        try
        {

            var layerKey = new
            {
                Key = "PshaEmoteFxTemplate",
                Slot = Mathf.Clamp(slotIndex, 1, 8),
                Template = templateCtrl.GetInstanceID(),
                Variant = cloneVariant
            };

            vTemplateController = vcc.CloneContext.CloneDistinct(templateCtrl, layerKey);
        }
        catch (MissingMethodException)
        {

            vTemplateController = vcc.Clone(templateCtrl);
        }

        if (vTemplateController == null)
        {
            Debug.LogWarning("[PshaVRCEmoteInstallerPass] Failed to clone the FX template VirtualController.");
            return;
        }


        MergeVirtualControllerParameters(
            vFx,
            vTemplateController,
            skipVRCEmoteParameter: false
        );


        var vTemplateLayer = vTemplateController.Layers.FirstOrDefault();
        if (vTemplateLayer == null)
        {
            Debug.LogWarning("[PshaVRCEmoteInstallerPass] The FX template has no layers (layers[0] missing).");
            return;
        }

        vTemplateLayer.DefaultWeight = 1.0f;

        var templateSM = vTemplateLayer.StateMachine;
        if (templateSM == null)
        {
            Debug.LogWarning("[PshaVRCEmoteInstallerPass] The FX template layer StateMachine is null.");
            return;
        }



        if (meWriteDefaultsOff)
        {
            ApplyWriteDefaultsToStateMachine(templateSM, false);
        }


        var clampedSlot = Mathf.Clamp(slotIndex, 1, 8);
        PatchTemplateEmoteConditions(templateSM, clampedSlot);


        vTemplateLayer.Name = string.IsNullOrEmpty(layerNameOverride)
            ? $"PshaEmoteFX_{clampedSlot}"
            : layerNameOverride;



        vFx.AddLayer(new LayerPriority(0), vTemplateLayer);
    }









    private static void MergeTemplateParametersIntoVirtualController(
        VirtualAnimatorController vController,
        AnimatorController templateCtrl,
        bool skipVRCEmoteParameter = true
    )
    {
        if (vController == null || templateCtrl == null) return;

        var templateParams = templateCtrl.parameters;
        if (templateParams == null || templateParams.Length == 0) return;

        var parameters = vController.Parameters;
        if (parameters == null)
        {
            parameters = ImmutableDictionary<string, AnimatorControllerParameter>.Empty;
        }

        foreach (var src in templateParams)
        {
            if (src == null) continue;


            if (skipVRCEmoteParameter && src.name == EmoteParamName) continue;


            if (parameters.TryGetValue(src.name, out var existing))
            {


                continue;
            }


            var cloned = new AnimatorControllerParameter
            {
                name = src.name,
                type = src.type,
                defaultBool = src.defaultBool,
                defaultFloat = src.defaultFloat,
                defaultInt = src.defaultInt
            };

            parameters = parameters.Add(cloned.name, cloned);
        }

        vController.Parameters = parameters;
    }








    private static void MergeVirtualControllerParameters(
        VirtualAnimatorController target,
        VirtualAnimatorController source,
        bool skipVRCEmoteParameter = true
    )
    {
        if (target == null || source == null) return;

        var srcParams = source.Parameters;
        if (srcParams == null || srcParams.Count == 0) return;

        var destParams = target.Parameters;
        if (destParams == null)
        {
            destParams = ImmutableDictionary<string, AnimatorControllerParameter>.Empty;
        }

        foreach (var kv in srcParams)
        {
            var name = kv.Key;
            var src = kv.Value;
            if (src == null) continue;


            if (skipVRCEmoteParameter && name == EmoteParamName) continue;


            if (destParams.ContainsKey(name)) continue;


            destParams = destParams.Add(name, src);
        }

        target.Parameters = destParams;
    }













    private static void MergeActionSlot_BC(
        VirtualAnimatorController vAction,
        PshaVRCEmoteInstaller installer,
        VirtualControllerContext vcc
    )
    {

        var firstLayer = vAction.Layers.FirstOrDefault();
        if (firstLayer == null || firstLayer.StateMachine == null) return;

        var rootSM = firstLayer.StateMachine;



        var searchRootSM = rootSM;

        bool useMergeScope =
            installer.showActionMergeScopeInInspector &&
            !string.IsNullOrEmpty(installer.actionMergeScope);

        if (useMergeScope)
        {
            var scoped = FindVirtualStateMachineByName(rootSM, installer.actionMergeScope);
            if (scoped == null)
            {
                Debug.LogWarning(
                    $"[PshaVRCEmoteInstallerPass] Slot {installer.slotIndex}: " +
                    $"Could not find the StateMachine specified by actionMergeScope \"{installer.actionMergeScope}\" " +
                    $"Not found in the virtual Action layer, skipping Action merge (B to C) for this slot."
                );
                return;
            }

            searchRootSM = scoped;
        }


        if (string.IsNullOrEmpty(installer.startActionState) ||
            string.IsNullOrEmpty(installer.endActionState))
        {
            return;
        }



        var startState = FindVirtualStateByName(searchRootSM, installer.startActionState);
        var endState = FindVirtualStateByName(searchRootSM, installer.endActionState);

        if (startState == null || endState == null)
        {
            Debug.LogWarning(
                $"[PshaVRCEmoteInstallerPass] Slot {installer.slotIndex}: " +
                $"Within the actionMergeScope \"{installer.actionMergeScope}\" tree, " +
                $"Could not find startActionState or endActionState, skipping Action merge (B to C)."
            );
            return;
        }


        if (!TryFindCommonParentStateMachine(searchRootSM, startState, endState, out var parentSM) ||
            parentSM == null)
        {
            Debug.LogWarning(
                $"[PshaVRCEmoteInstallerPass] Slot {installer.slotIndex}: " +
                "In the mergeScope that contains startActionState and endActionState, " +
                "They are not in the same VirtualStateMachine, skipping Action merge (B to C) for this slot."
            );
            return;
        }


        var templateCtrl = installer.actionMELayer as AnimatorController;
        if (templateCtrl == null || templateCtrl.layers == null || templateCtrl.layers.Length == 0)
        {
            Debug.LogWarning(
                $"[PshaVRCEmoteInstallerPass] Slot {installer.slotIndex}: Template AnimatorController has no layers, skipping."
            );
            return;
        }

        var templateSM = templateCtrl.layers[0].stateMachine;
        if (templateSM == null)
        {
            Debug.LogWarning(
                $"[PshaVRCEmoteInstallerPass] Slot {installer.slotIndex}: Template has no root StateMachine, skipping."
            );
            return;
        }




        MergeTemplateParametersIntoVirtualController(vAction, templateCtrl);




        var settingsBehaviour = FindModularTransitionSettings(templateSM);










        VirtualAnimatorController vTemplateController = null;

        try
        {
            var layerKey = new
            {
                Key = "PshaEmoteTemplate",
                Slot = Mathf.Clamp(installer.slotIndex, 1, 8),
                Template = templateCtrl.GetInstanceID()
            };

            vTemplateController = vcc.CloneContext.CloneDistinct(templateCtrl, layerKey);
        }
        catch (MissingMethodException)
        {
            vTemplateController = vcc.Clone(templateCtrl);
        }

        if (vTemplateController == null)
        {
            Debug.LogWarning(
                $"[PshaVRCEmoteInstallerPass] Slot {installer.slotIndex}: Failed to clone the action template VirtualAnimatorController."
            );
            return;
        }


        var vTemplateLayer = vTemplateController.Layers.FirstOrDefault();

        if (vTemplateLayer == null || vTemplateLayer.StateMachine == null)
        {
            Debug.LogWarning(
                $"[PshaVRCEmoteInstallerPass] Slot {installer.slotIndex}: " +
                "Could not find layer 0 or its StateMachine in the action template, skipping merge."
            );
            return;
        }

        var vTemplateSM = vTemplateLayer.StateMachine;



        if (installer.meWriteDefaultsOff)
        {
            ApplyWriteDefaultsToStateMachine(vTemplateSM, false);
        }


        var children = parentSM.StateMachines;
        children = children.Add(new VirtualStateMachine.VirtualChildStateMachine
        {
            StateMachine = vTemplateSM,
            Position = parentSM.ParentStateMachinePosition
        });

        parentSM.StateMachines = children;


        int slot = Mathf.Clamp(installer.slotIndex, 1, 8);





        vTemplateSM.Name = $"PshaEmote_{slot}";

        PatchTemplateEmoteConditions(vTemplateSM, slot);



        var templateEntry = FindTemplateEntryState(vTemplateSM);
        if (templateEntry == null)
        {
            Debug.LogWarning(
                $"[PshaVRCEmoteInstallerPass] Slot {installer.slotIndex}: Could not find a template entry state, " +
                "Failed to build the Start to template entry transition."
            );
            return;
        }


        if (settingsBehaviour != null)
        {


            RebuildSlotTransitionFromSettings(startState, templateEntry, slot, settingsBehaviour);
        }
        else
        {


            RedirectSlotTransitionsToTemplate(startState, templateEntry, slot);
        }




        ConnectTemplateExitToEndState(parentSM, vTemplateSM, endState);
    }






    private static VirtualState FindVirtualStateByName(VirtualStateMachine root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name)) return null;


        foreach (var state in root.AllStates())
        {
            if (state.Name == name) return state;
        }

        return null;
    }





    private static VirtualStateMachine FindVirtualStateMachineByName(
        VirtualStateMachine root,
        string name
    )
    {
        if (root == null || string.IsNullOrEmpty(name)) return null;

        var stack = new Stack<VirtualStateMachine>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var sm = stack.Pop();
            if (sm == null) continue;


            if (sm.Name == name) return sm;

            var subMachines = sm.StateMachines;
            if (subMachines == null || subMachines.Count == 0) continue;

            foreach (var child in subMachines)
            {
                if (child.StateMachine != null)
                {
                    stack.Push(child.StateMachine);
                }
            }
        }

        return null;
    }







    private static VirtualStateMachine FindParentStateMachineOfState(
        VirtualStateMachine rootSM,
        VirtualState target
    )
    {
        if (rootSM == null || target == null) return null;


        var stack = new Stack<VirtualStateMachine>();
        stack.Push(rootSM);

        while (stack.Count > 0)
        {
            var sm = stack.Pop();
            if (sm == null) continue;


            var states = sm.States;
            if (states != null && states.Count > 0)
            {
                foreach (var child in states)
                {
                    var state = child.State;
                    if (state == null) continue;

                    if (ReferenceEquals(state, target))
                    {
                        return sm;
                    }
                }
            }


            var subMachines = sm.StateMachines;
            if (subMachines != null && subMachines.Count > 0)
            {
                foreach (var childSM in subMachines)
                {
                    var sub = childSM.StateMachine;
                    if (sub != null) stack.Push(sub);
                }
            }
        }


        return null;
    }





    private static bool TryFindCommonParentStateMachine(
        VirtualStateMachine rootSM,
        VirtualState startState,
        VirtualState endState,
        out VirtualStateMachine parentSM
    )
    {
        parentSM = null;

        if (rootSM == null || startState == null || endState == null)
            return false;

        var startParent = FindParentStateMachineOfState(rootSM, startState);
        var endParent = FindParentStateMachineOfState(rootSM, endState);

        if (startParent != null && ReferenceEquals(startParent, endParent))
        {
            parentSM = startParent;
            return true;
        }

        return false;
    }




    private static VRCExpressionsMenu ResolveTargetMenu_NoScore(
    VRCExpressionsMenu rootMenu,
    PshaVRCEmoteInstaller installer,
    VRCExpressionsMenu autoEmoteMenu
)
    {
        if (installer == null || rootMenu == null) return null;

        // Prefer targetMenuPath if set
        if (installer.targetMenuPath != null && installer.targetMenuPath.Length > 0)
        {
            var cur = rootMenu;
            foreach (var idx in installer.targetMenuPath)
            {
                if (cur?.controls == null) { cur = null; break; }
                if (idx < 0 || idx >= cur.controls.Count) { cur = null; break; }

                var c = cur.controls[idx];
                cur = c?.subMenu;
            }

            if (cur != null) return cur;
        }

        // Then try SoftRef if it resolves and is in the menu tree
        VRCExpressionsMenu explicitMenu = installer.targetMenuAsset.Resolve<VRCExpressionsMenu>();
        if (explicitMenu != null && IsMenuInTree(rootMenu, explicitMenu))
            return explicitMenu;

        // Fallback to the auto detected menu
        return autoEmoteMenu;
    }


    private static bool TryFollowMenuPath(VRCExpressionsMenu root, int[] path, out VRCExpressionsMenu menu)
    {
        menu = root;
        if (root == null || path == null) return false;

        for (int i = 0; i < path.Length; i++)
        {
            if (menu == null || menu.controls == null) return false;

            int idx = path[i];
            if (idx < 0 || idx >= menu.controls.Count) return false;

            var c = menu.controls[idx];
            if (c == null || c.subMenu == null) return false;

            menu = c.subMenu;
        }

        return true;
    }

    private static bool IsMenuInTree(VRCExpressionsMenu root, VRCExpressionsMenu target)
    {
        if (root == null || target == null) return false;

        var visited = new HashSet<VRCExpressionsMenu>();
        var queue = new Queue<VRCExpressionsMenu>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (cur == null || !visited.Add(cur)) continue;
            if (ReferenceEquals(cur, target)) return true;

            if (cur.controls == null) continue;
            foreach (var ctrl in cur.controls)
            {
                if (ctrl?.subMenu != null) queue.Enqueue(ctrl.subMenu);
            }
        }

        return false;
    }

    #region ExpressionsMenu Clone and Patch




    private static VRCExpressionsMenu FindEmoteMenu(VRCExpressionsMenu root)
    {
        if (root == null) return null;

        var visited = new HashSet<VRCExpressionsMenu>();
        var queue = new Queue<VRCExpressionsMenu>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var menu = queue.Dequeue();
            if (menu == null || visited.Contains(menu)) continue;
            visited.Add(menu);

            if (IsVRCEmoteMenu(menu))
                return menu;

            if (menu.controls == null) continue;

            foreach (var ctrl in menu.controls)
            {
                if (ctrl?.subMenu != null)
                    queue.Enqueue(ctrl.subMenu);
            }
        }

        return null;
    }




    private static bool IsVRCEmoteMenu(VRCExpressionsMenu menu)
    {
        if (menu.controls == null || menu.controls.Count == 0) return false;

        int emoteCount = 0;
        foreach (var ctrl in menu.controls)
        {
            if (ctrl == null || ctrl.parameter == null) continue;

            if (ctrl.parameter.name == EmoteParamName &&
                (ctrl.type == VRCExpressionsMenu.Control.ControlType.Button ||
                 ctrl.type == VRCExpressionsMenu.Control.ControlType.Toggle))
            {
                emoteCount++;
            }
        }


        return emoteCount >= 4;
    }


    private static bool PatchEmoteConditionsOnTransition(VirtualTransitionBase transition, int slotIndex)
    {
        if (transition == null) return false;

        var conditions = transition.Conditions;
        if (conditions == null || conditions.Count == 0) return false;

        bool modified = false;

        for (int i = 0; i < conditions.Count; i++)
        {
            var cond = conditions[i];
            if (cond.parameter != EmoteParamName) continue;


            switch (cond.mode)
            {
                case AnimatorConditionMode.Equals:
                case AnimatorConditionMode.NotEqual:
                case AnimatorConditionMode.Greater:
                case AnimatorConditionMode.Less:
                    if (!Mathf.Approximately(cond.threshold, slotIndex))
                    {
                        cond.threshold = slotIndex;
                        conditions = conditions.SetItem(i, cond);
                        modified = true;
                    }
                    break;

                default:
                    break;
            }
        }

        if (modified) transition.Conditions = conditions;
        return modified;
    }






    private static void ApplyWriteDefaultsToStateMachine(
        VirtualStateMachine rootSM,
        bool writeDefaults
    )
    {
        if (rootSM == null) return;

        foreach (var state in rootSM.AllStates())
        {
            if (state == null) continue;
            state.WriteDefaultValues = writeDefaults;
        }
    }






    private static void PatchTemplateEmoteConditions(VirtualStateMachine templateSM, int slotIndex)
    {
        if (templateSM == null) return;


        foreach (var state in templateSM.AllStates())
        {
            if (state == null) continue;

            var transitions = state.Transitions;
            if (transitions == null || transitions.Count == 0) continue;

            bool anyChanged = false;

            for (int i = 0; i < transitions.Count; i++)
            {
                var t = transitions[i];
                if (t == null) continue;

                if (PatchEmoteConditionsOnTransition(t, slotIndex))
                {
                    transitions = transitions.SetItem(i, t);
                    anyChanged = true;
                }
            }

            if (anyChanged) state.Transitions = transitions;
        }


        foreach (var sm in EnumerateStateMachines(templateSM))
        {

            {
                var list = sm.AnyStateTransitions;
                if (list != null && list.Count > 0)
                {
                    bool anyChanged = false;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var t = list[i];
                        if (t == null) continue;

                        if (PatchEmoteConditionsOnTransition(t, slotIndex))
                        {
                            list = list.SetItem(i, t);
                            anyChanged = true;
                        }
                    }
                    if (anyChanged) sm.AnyStateTransitions = list;
                }
            }


            {
                var list = sm.EntryTransitions;
                if (list != null && list.Count > 0)
                {
                    bool anyChanged = false;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var t = list[i];
                        if (t == null) continue;

                        if (PatchEmoteConditionsOnTransition(t, slotIndex))
                        {
                            list = list.SetItem(i, t);
                            anyChanged = true;
                        }
                    }
                    if (anyChanged) sm.EntryTransitions = list;
                }
            }


            {
                var dict = sm.StateMachineTransitions;
                if (dict != null && dict.Count > 0)
                {
                    bool dictChanged = false;
                    foreach (var kv in dict)
                    {
                        var fromSm = kv.Key;
                        var list = kv.Value;
                        if (list == null || list.Count == 0) continue;

                        bool listChanged = false;
                        for (int i = 0; i < list.Count; i++)
                        {
                            var t = list[i];
                            if (t == null) continue;

                            if (PatchEmoteConditionsOnTransition(t, slotIndex))
                            {
                                list = list.SetItem(i, t);
                                listChanged = true;
                            }
                        }

                        if (listChanged)
                        {
                            dict = dict.SetItem(fromSm, list);
                            dictChanged = true;
                        }
                    }

                    if (dictChanged) sm.StateMachineTransitions = dict;
                }
            }
        }
    }







    private static ModularEmoteTransitionSettings FindModularTransitionSettings(
        AnimatorStateMachine templateSM
    )
    {
        if (templateSM == null) return null;



        var states = templateSM.states;
        if (states == null || states.Length == 0) return null;

        foreach (var child in states)
        {
            var state = child.state;
            if (state == null) continue;

            if (state.name != StartSettingsStateName) continue;


            var behaviours = state.behaviours;
            if (behaviours == null || behaviours.Length == 0) continue;

            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ModularEmoteTransitionSettings settings)
                {
                    return settings;
                }
            }
        }

        return null;
    }





    private static TransitionInterruptionSource MapInterruptionSource(
        ModularEmoteTransitionSettings.TransitionInterruptionSource src
    )
    {
        switch (src)
        {
            case ModularEmoteTransitionSettings.TransitionInterruptionSource.None:
                return TransitionInterruptionSource.None;
            case ModularEmoteTransitionSettings.TransitionInterruptionSource.Source:
                return TransitionInterruptionSource.Source;
            case ModularEmoteTransitionSettings.TransitionInterruptionSource.Destination:
                return TransitionInterruptionSource.Destination;
            case ModularEmoteTransitionSettings.TransitionInterruptionSource.SourceThenDestination:
                return TransitionInterruptionSource.SourceThenDestination;
            case ModularEmoteTransitionSettings.TransitionInterruptionSource.DestinationThenSource:
                return TransitionInterruptionSource.DestinationThenSource;
            default:
                return TransitionInterruptionSource.None;
        }
    }








    private static void RebuildSlotTransitionFromSettings(
        VirtualState startState,
        VirtualState templateEntry,
        int slotIndex,
        ModularEmoteTransitionSettings settings
    )
    {
        if (startState == null || templateEntry == null) return;

        var transitions = startState.Transitions;
        if (transitions == null)
        {
            transitions = ImmutableList<VirtualStateTransition>.Empty;
        }


        for (int i = transitions.Count - 1; i >= 0; i--)
        {
            var t = transitions[i];
            if (t == null) continue;

            if (IsEmoteTransitionForSlot(t, slotIndex))
            {
                transitions = transitions.RemoveAt(i);
            }
        }


        var newTransition = VirtualStateTransition.Create();


        newTransition.SetDestination(templateEntry);


        if (settings != null)
        {

            newTransition.ExitTime = settings.transitionHasExitTime
                ? (float?)settings.transitionExitTime
                : null;

            newTransition.HasFixedDuration = settings.useFixedDuration;
            newTransition.Duration = settings.transitionDuration;
            newTransition.Offset = settings.transitionOffset;
            newTransition.OrderedInterruption = settings.orderedInterruption;
            newTransition.InterruptionSource = MapInterruptionSource(settings.interruptionSource);
        }
        else
        {

            newTransition.ExitTime = null;
            newTransition.HasFixedDuration = true;
            newTransition.Duration = 0.1f;
            newTransition.Offset = 0f;
            newTransition.OrderedInterruption = false;
            newTransition.InterruptionSource = TransitionInterruptionSource.None;
        }



        var conditions = ImmutableList<AnimatorCondition>.Empty;


        var baseCond = new AnimatorCondition
        {
            parameter = EmoteParamName,
            mode = AnimatorConditionMode.Equals,
            threshold = slotIndex
        };
        conditions = conditions.Add(baseCond);


        if (settings != null && settings.conditions != null)
        {
            foreach (var c in settings.conditions)
            {
                if (c == null) continue;
                if (string.IsNullOrEmpty(c.parameter)) continue;

                if (c.parameter == EmoteParamName) continue;

                var extra = new AnimatorCondition
                {
                    parameter = c.parameter
                };

                switch (c.type)
                {
                    case ModularEmoteTransitionSettings.Condition.ParameterType.Bool:

                        extra.mode = c.boolValue
                            ? AnimatorConditionMode.If
                            : AnimatorConditionMode.IfNot;
                        extra.threshold = 0f;
                        break;

                    case ModularEmoteTransitionSettings.Condition.ParameterType.Int:

                        switch (c.intComparison)
                        {
                            case ModularEmoteTransitionSettings.Condition.IntComparison.Greater:
                                extra.mode = AnimatorConditionMode.Greater;
                                break;
                            case ModularEmoteTransitionSettings.Condition.IntComparison.Equal:
                                extra.mode = AnimatorConditionMode.Equals;
                                break;
                            case ModularEmoteTransitionSettings.Condition.IntComparison.Less:
                                extra.mode = AnimatorConditionMode.Less;
                                break;
                            case ModularEmoteTransitionSettings.Condition.IntComparison.NotEqual:
                                extra.mode = AnimatorConditionMode.NotEqual;
                                break;
                            default:
                                extra.mode = AnimatorConditionMode.Equals;
                                break;
                        }

                        extra.threshold = c.intValue;
                        break;

                    case ModularEmoteTransitionSettings.Condition.ParameterType.Float:

                        switch (c.floatComparison)
                        {
                            case ModularEmoteTransitionSettings.Condition.FloatComparison.Greater:
                                extra.mode = AnimatorConditionMode.Greater;
                                break;
                            case ModularEmoteTransitionSettings.Condition.FloatComparison.Equal:
                                extra.mode = AnimatorConditionMode.Equals;
                                break;
                            case ModularEmoteTransitionSettings.Condition.FloatComparison.Less:
                                extra.mode = AnimatorConditionMode.Less;
                                break;
                            case ModularEmoteTransitionSettings.Condition.FloatComparison.NotEqual:
                                extra.mode = AnimatorConditionMode.NotEqual;
                                break;
                            default:
                                extra.mode = AnimatorConditionMode.Equals;
                                break;
                        }

                        extra.threshold = c.floatValue;
                        break;

                    case ModularEmoteTransitionSettings.Condition.ParameterType.Trigger:

                        extra.mode = AnimatorConditionMode.If;
                        extra.threshold = 0f;
                        break;

                    default:

                        continue;
                }

                conditions = conditions.Add(extra);
            }
        }

        newTransition.Conditions = conditions;


        transitions = transitions.Add(newTransition);
        startState.Transitions = transitions;
    }







    private static bool IsEmoteTransitionForSlot(VirtualTransitionBase t, int slotIndex)
    {
        if (t == null) return false;

        var conditions = t.Conditions;
        if (conditions == null || conditions.Count == 0) return false;

        bool hasVrcEmote = false;
        bool isSitTransition = false;
        bool matchesSlot = false;

        for (int i = 0; i < conditions.Count; i++)
        {
            var cond = conditions[i];
            if (cond.parameter != EmoteParamName) continue;

            hasVrcEmote = true;

            if (cond.mode == AnimatorConditionMode.Equals)
            {
                int value = Mathf.RoundToInt(cond.threshold);

                if (value == slotIndex)
                {
                    matchesSlot = true;
                }
                else if (value >= 9)
                {

                    isSitTransition = true;
                }
            }
        }


        if (!hasVrcEmote) return false;


        if (isSitTransition) return false;


        return matchesSlot;
    }






    private static VirtualState FindTemplateEntryState(VirtualStateMachine templateSM)
    {
        if (templateSM == null) return null;


        if (templateSM.DefaultState != null)
        {

            if (templateSM.DefaultState.Name != StartSettingsStateName)
                return templateSM.DefaultState;
        }


        var states = templateSM.States;
        if (states != null && states.Count > 0)
        {
            foreach (var child in states)
            {
                var s = child.State;
                if (s == null) continue;
                if (s.Name == StartSettingsStateName) continue;
                return s;
            }
        }


        foreach (var s in templateSM.AllStates())
        {
            if (s == null) continue;
            if (s.Name == StartSettingsStateName) continue;
            return s;
        }

        return null;
    }





    private static void RedirectSlotTransitionsToTemplate(
        VirtualState startState,
        VirtualState templateStart,
        int slotIndex
    )
    {
        if (startState == null || templateStart == null) return;

        var transitions = startState.Transitions;
        if (transitions == null || transitions.Count == 0) return;

        bool anyChanged = false;

        for (int i = 0; i < transitions.Count; i++)
        {
            var t = transitions[i];
            if (t == null) continue;


            if (!IsEmoteTransitionForSlot(t, slotIndex)) continue;



            t.SetDestination(templateStart);



            transitions = transitions.SetItem(i, t);
            anyChanged = true;
        }

        if (anyChanged)
        {
            startState.Transitions = transitions;
        }
    }









    private static void ConnectTemplateExitToEndState(
        VirtualStateMachine parentSM,
        VirtualStateMachine vTemplateSM,
        VirtualState endState
    )
    {
        if (parentSM == null || vTemplateSM == null || endState == null) return;



        var smTransitions = parentSM.StateMachineTransitions;
        if (smTransitions == null)
        {
            smTransitions = ImmutableDictionary<VirtualStateMachine, ImmutableList<VirtualTransition>>.Empty;
        }


        if (!smTransitions.TryGetValue(vTemplateSM, out var transitionsForTemplate))
        {
            transitionsForTemplate = ImmutableList<VirtualTransition>.Empty;
        }


        var newTransition = VirtualTransition.Create();



        newTransition.SetDestination(endState);




        transitionsForTemplate = transitionsForTemplate.Add(newTransition);


        smTransitions = smTransitions.SetItem(vTemplateSM, transitionsForTemplate);


        parentSM.StateMachineTransitions = smTransitions;
    }



    private static IEnumerable<VirtualStateMachine> EnumerateStateMachines(VirtualStateMachine root)
    {
        if (root == null) yield break;

        var stack = new Stack<VirtualStateMachine>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var sm = stack.Pop();
            if (sm == null) continue;

            yield return sm;

            var children = sm.StateMachines;
            if (children == null || children.Count == 0) continue;

            foreach (var child in children)
            {
                if (child.StateMachine != null) stack.Push(child.StateMachine);
            }
        }
    }






    private static void ConnectTemplateEndToEndState(
        VirtualState templateEnd,
        VirtualState endState
    )
    {
        if (templateEnd == null || endState == null) return;


        var transitions = templateEnd.Transitions;
        if (transitions == null)
        {
            transitions = ImmutableList<VirtualStateTransition>.Empty;
        }


        var newTransition = VirtualStateTransition.Create();



        newTransition.SetDestination(endState);








        transitions = transitions.Add(newTransition);
        templateEnd.Transitions = transitions;
    }






    private static VRCExpressionsMenu CloneAndPatchMenu(
    VRCExpressionsMenu menu,
    Dictionary<VRCExpressionsMenu, List<PshaVRCEmoteInstaller>> modifications,
    Dictionary<VRCExpressionsMenu, VRCExpressionsMenu> cloneMap,
    List<UnityEngine.Object> toSave
)
    {
        if (menu == null) return null;

        if (cloneMap.TryGetValue(menu, out var existingClone))
            return existingClone;

        var clone = ScriptableObject.Instantiate(menu);
        // keep original name
        // clone.name = menu.name + "_PshaEmote";
        clone.name = menu.name;



        cloneMap[menu] = clone;


        toSave?.Add(clone);



        if (modifications.TryGetValue(menu, out var mgrs))
        {

            for (int i = mgrs.Count - 1; i >= 0; i--)
            {
                var m = mgrs[i];
                if (m == null) continue;
                if (clone.controls == null || clone.controls.Count == 0) break;

                int slot = Mathf.Clamp(m.slotIndex, 1, 8);
                int idx = FindEmoteControlIndex(clone, slot);
                if (idx < 0) continue;

                var ctrl = clone.controls[idx];


                if (ctrl == null) continue;


                if (!string.IsNullOrEmpty(m.emoteName))
                    ctrl.name = m.emoteName;


                if (m.menuIcon != null)
                    ctrl.icon = m.menuIcon;


                if (m.controlType != PshaVRCEmoteInstaller.EmoteControlType.None)
                {
                    ctrl.type = (m.controlType == PshaVRCEmoteInstaller.EmoteControlType.Toggle)
                        ? VRCExpressionsMenu.Control.ControlType.Toggle
                        : VRCExpressionsMenu.Control.ControlType.Button;
                }


                ctrl.value = m.Value;


                if (ctrl.parameter == null)
                    ctrl.parameter = new VRCExpressionsMenu.Control.Parameter();

                ctrl.parameter.name = m.ParameterName;

                clone.controls[idx] = ctrl;

            }
        }




        if (clone.controls != null)
        {

            Texture2D pshaMenuIcon = null;

            for (int i = 0; i < clone.controls.Count; i++)
            {
                var ctrl = clone.controls[i];
                if (ctrl == null || ctrl.subMenu == null) continue;

                if (!modifications.TryGetValue(ctrl.subMenu, out var targetMgrs) ||
                    targetMgrs == null || targetMgrs.Count == 0)
                    continue;

                bool applyIcon = true;
                PshaVRCEmoteInstaller firstMgr = null;

                for (int j = 0; j < targetMgrs.Count; j++)
                {
                    var m = targetMgrs[j];
                    if (m == null) continue;

                    applyIcon = m.changeEmoteMenuIcon;
                    firstMgr = m;
                    break;
                }

                if (!applyIcon) continue;

                if (pshaMenuIcon == null)
                    pshaMenuIcon = GetPshaEmoteMenuIcon(firstMgr);

                if (pshaMenuIcon == null)
                    continue;

                ctrl.icon = pshaMenuIcon;
                clone.controls[i] = ctrl;
            }
        }



        if (clone.controls != null)
        {
            for (int i = 0; i < clone.controls.Count; i++)
            {
                var ctrl = clone.controls[i];
                if (ctrl == null || ctrl.subMenu == null) continue;

                ctrl.subMenu = CloneAndPatchMenu(ctrl.subMenu, modifications, cloneMap, toSave);
                clone.controls[i] = ctrl;
            }
        }

        return clone;
    }






    private static void SaveAssetsSafe(BuildContext context, IEnumerable<UnityEngine.Object> assets)
    {
        if (context?.AssetSaver == null || assets == null) return;
        foreach (var a in assets)
        {
            if (a != null) context.AssetSaver.SaveAsset(a);
        }
    }


    private static int FindEmoteControlIndex(VRCExpressionsMenu menu, int slot)
    {
        if (menu?.controls == null) return -1;

        for (int i = 0; i < menu.controls.Count; i++)
        {
            var c = menu.controls[i];
            if (c?.parameter == null) continue;

            if (c.parameter.name == EmoteParamName && Mathf.RoundToInt(c.value) == slot)
                return i;
        }

        int idx = slot - 1;
        if (0 <= idx && idx < menu.controls.Count) return idx;

        return -1;
    }

    #endregion


}
#endif
