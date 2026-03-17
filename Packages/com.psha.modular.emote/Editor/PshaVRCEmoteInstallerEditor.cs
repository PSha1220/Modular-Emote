#if UNITY_EDITOR
using System.Collections.Generic;
using System;
using System.Text.RegularExpressions;
using System.Text;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;


[CustomEditor(typeof(PshaVRCEmoteInstaller))]
public class PshaVRCEmoteInstallerEditor : Editor
{

    // Keep labels Editor Language and Use System Language (Auto) unchanged
    private static readonly Dictionary<string, Dictionary<string, string>> s_langMaps
        = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> s_langTriedLoad
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static string NormalizeLang(string lang)
    {
        if (string.IsNullOrEmpty(lang)) return "en-us";
        lang = lang.Trim().ToLowerInvariant();

        if (lang == "ko") return "ko-kr";
        if (lang == "ja") return "ja-jp";
        if (lang == "en") return "en-us";

        if (lang == "zh") return "zh-hans";
        if (lang == "zh-cn") return "zh-hans";
        if (lang == "zh-sg") return "zh-hans";
        if (lang == "zh-hans") return "zh-hans";

        if (lang == "zh-tw") return "zh-hant";
        if (lang == "zh-hk") return "zh-hant";
        if (lang == "zh-mo") return "zh-hant";
        if (lang == "zh-hant") return "zh-hant";

        return lang;
    }

    private static Dictionary<string, string> EnsureLangLoaded(string lang)
    {
        lang = NormalizeLang(lang);

        if (s_langMaps.TryGetValue(lang, out var cached))
            return cached;

        if (s_langTriedLoad.Contains(lang))
            return null;

        s_langTriedLoad.Add(lang);

        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            string[] candidates =
       {
    lang,
    lang == "zh-cn" || lang == "zh" ? "zh-hans" : null,
    lang == "zh-tw" || lang == "zh-hk" ? "zh-hant" : null
};

            string chosen = null;
            string[] guids = null;

            for (int c = 0; c < candidates.Length; c++)
            {
                var code = candidates[c];
                if (string.IsNullOrEmpty(code)) continue;

                guids = AssetDatabase.FindAssets($"PshaVRCEmote.{code} t:TextAsset");
                if (guids != null && guids.Length > 0)
                {
                    chosen = code;
                    break;
                }
            }
            if (guids != null && guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (ta != null && !string.IsNullOrEmpty(ta.text))
                {
                    foreach (var kv in ParseFlatJsonStringMap(ta.text))
                        if (!string.IsNullOrEmpty(kv.Key))
                            map[kv.Key] = kv.Value ?? string.Empty;
                }
            }
        }
        catch
        {
        }
        s_langMaps[lang] = map;
        return map;
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseFlatJsonStringMap(string json)
    {
        if (string.IsNullOrEmpty(json)) yield break;

        if (json.Length > 0 && json[0] == '\uFEFF') json = json.Substring(1);

        int i = 0;
        while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
        if (i < json.Length && json[i] == '{') i++;

        while (i < json.Length)
        {
            SkipWs(json, ref i);
            if (i >= json.Length) yield break;
            if (json[i] == '}') yield break;

            string key = ReadJsonString(json, ref i);
            SkipWs(json, ref i);

            if (i < json.Length && json[i] == ':') i++;
            SkipWs(json, ref i);

            string value = ReadJsonString(json, ref i);
            yield return new KeyValuePair<string, string>(key, value);

            SkipWs(json, ref i);
            if (i < json.Length && json[i] == ',') i++;
        }

        static void SkipWs(string s, ref int p)
        {
            while (p < s.Length && char.IsWhiteSpace(s[p])) p++;
        }

        static string ReadJsonString(string s, ref int p)
        {
            SkipWs(s, ref p);
            if (p >= s.Length) return string.Empty;

            if (s[p] != '"')
            {
                int start = p;
                while (p < s.Length && s[p] != ',' && s[p] != '}' && !char.IsWhiteSpace(s[p])) p++;
                return s.Substring(start, p - start);
            }

            p++;
            var sb = new StringBuilder();
            while (p < s.Length)
            {
                char c = s[p++];
                if (c == '"') break;

                if (c == '\\' && p < s.Length)
                {
                    char esc = s[p++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (p + 4 <= s.Length)
                            {
                                string hex = s.Substring(p, 4);
                                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int code))
                                    sb.Append((char)code);
                                p += 4;
                            }
                            break;
                        default:
                            sb.Append(esc);
                            break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }


    private static string Tr(string key, string fallback)
    {
        // Keep these keys unlocalized
        if (key == "psha.btn_setup" || key == "psha.dialog_setup_title")
            return fallback;

        var lang = NormalizeLang(GetEffectiveLanguageCode());

        // Always query the selected language json first
        var map = EnsureLangLoaded(lang);
        if (map != null && map.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
            return v;

        return fallback ?? string.Empty;
    }

    private static string Trf(string key, string fallback, params object[] args)
    {
        string fmt = Tr(key, fallback);
        try { return string.Format(fmt, args); }
        catch { return fmt; }
    }

    private static GUIContent GC(string labelKey, string labelFallback, string ttKey = null, string ttFallback = null)
    {
        if (!string.IsNullOrEmpty(ttKey))
            return new GUIContent(Tr(labelKey, labelFallback), Tr(ttKey, ttFallback ?? string.Empty));
        return new GUIContent(Tr(labelKey, labelFallback));
    }
    private void DrawControlTypePopupField()
    {
        // Keep serialized enum values stable (None=0, Button=1, Toggle=2),
        // but show UI order as: Toggle, Button, None.
        var label = GC("psha.type", "Type", "psha.tt.type", "Control type. None keeps the existing type.");

        var options = new[]
        {
            new GUIContent(Tr("psha.type.toggle", "Toggle")),
            new GUIContent(Tr("psha.type.button", "Button")),
            new GUIContent(Tr("psha.type.none", "None")),
        };

        // enumValueIndex: 0=None, 1=Button, 2=Toggle
        int enumIndex = Mathf.Clamp(_controlTypeProp.enumValueIndex, 0, 2);

        int displayIndex;
        if (_controlTypeProp.hasMultipleDifferentValues)
        {
            displayIndex = 0; // For mixed value display only
        }
        else
        {
            displayIndex = (enumIndex == 2) ? 0 : (enumIndex == 1) ? 1 : 2; // Toggle, Button, None
        }

        EditorGUI.showMixedValue = _controlTypeProp.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        displayIndex = EditorGUILayout.Popup(label, displayIndex, options);
        if (EditorGUI.EndChangeCheck())
        {
            int newEnumIndex = (displayIndex == 0) ? 2 : (displayIndex == 1) ? 1 : 0;
            _controlTypeProp.enumValueIndex = newEnumIndex;
        }
        EditorGUI.showMixedValue = false;
    }



    SerializedProperty _emoteNameProp;
    SerializedProperty _slotIndexProp;
    SerializedProperty _menuIconProp;
    SerializedProperty _controlTypeProp;

    SerializedProperty _targetMenuProp;


    SerializedProperty _targetMenuPathProp;
    SerializedProperty _actionLayerProp;
    SerializedProperty _fxLayerProp;


    SerializedProperty _MEactionProp;
    SerializedProperty _MEfxMotionProp;


    SerializedProperty _startActionStateProp;
    SerializedProperty _endActionStateProp;

    SerializedProperty _showActionMergeScopeProp;
    SerializedProperty _actionMergeScopeProp;

    SerializedProperty _meWriteDefaultsOffProp;

    SerializedProperty _useMergeMEFxProp;
    SerializedProperty _autoRenameObjectNameProp;
    SerializedProperty _objectNameProp;
    SerializedProperty _useAdditionalMEFxLayersProp;
    SerializedProperty _additionalMEFxLayersProp;


    SerializedProperty _changeEmoteMenuIconProp;
    static bool s_devFoldout = false;
    static bool s_previewFoldout = true;
    static bool s_advancedFoldout = false;

    void OnEnable()
    {
        // Always start collapsed for distribution stability
        s_devFoldout = false;

        CacheSerializedProps();
    }
    void CacheSerializedProps()
    {
        _emoteNameProp = serializedObject.FindProperty("emoteName");
        _slotIndexProp = serializedObject.FindProperty("slotIndex");
        _menuIconProp = serializedObject.FindProperty("menuIcon");
        _controlTypeProp = serializedObject.FindProperty("controlType");

        _targetMenuProp = serializedObject.FindProperty("targetMenuAsset");
        _targetMenuPathProp = serializedObject.FindProperty("targetMenuPath");

        _actionLayerProp = serializedObject.FindProperty("actionLayerAsset");
        _fxLayerProp = serializedObject.FindProperty("fxLayerAsset");

        _MEactionProp = serializedObject.FindProperty("actionMELayer");
        _MEfxMotionProp = serializedObject.FindProperty("fxMELayer");

        _startActionStateProp = serializedObject.FindProperty("startActionState");
        _endActionStateProp = serializedObject.FindProperty("endActionState");
        _showActionMergeScopeProp = serializedObject.FindProperty("showActionMergeScopeInInspector");
        _actionMergeScopeProp = serializedObject.FindProperty("actionMergeScope");

        _meWriteDefaultsOffProp = serializedObject.FindProperty("meWriteDefaultsOff");

        _useMergeMEFxProp = serializedObject.FindProperty("useMergeMEFxLayer");
        _autoRenameObjectNameProp = serializedObject.FindProperty("autoRenameObjectName");
        _objectNameProp = serializedObject.FindProperty("objectName");
        _useAdditionalMEFxLayersProp = serializedObject.FindProperty("useAdditionalMEFxLayers");
        _additionalMEFxLayersProp = serializedObject.FindProperty("additionalMEFxLayers");

        _changeEmoteMenuIconProp = serializedObject.FindProperty("changeEmoteMenuIcon");
    }


    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        if (_slotIndexProp == null || _useMergeMEFxProp == null ||
            _autoRenameObjectNameProp == null || _objectNameProp == null ||
            _showActionMergeScopeProp == null || _actionMergeScopeProp == null ||
            _MEfxMotionProp == null || _useAdditionalMEFxLayersProp == null ||
            _additionalMEFxLayersProp == null)
        {
            CacheSerializedProps();
        }

        if (_slotIndexProp == null || _useMergeMEFxProp == null ||
            _autoRenameObjectNameProp == null || _objectNameProp == null ||
            _showActionMergeScopeProp == null || _actionMergeScopeProp == null ||
            _MEfxMotionProp == null || _useAdditionalMEFxLayersProp == null ||
            _additionalMEFxLayersProp == null)
        {
            EditorGUILayout.HelpBox(
                "Inspector is initializing or scripts were reloaded. Please reselect the object.",
                MessageType.Warning);
            return;
        }

        var mgr = (PshaVRCEmoteInstaller)target;


        DrawPlacementGuidance(mgr);


        if (_useMergeMEFxProp.boolValue)
        {
            DrawEmoteNameMismatchWarning(mgr);
            DrawSharedFxLayerWarning(mgr);
        }

        EditorGUILayout.LabelField(Tr("psha.content", "Content"), EditorStyles.boldLabel);
        EditorGUILayout.Space(2);


        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.BeginHorizontal();


            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField(Tr("psha.name", "Name"));

                string rawName = mgr.emoteName;
                string displayName = string.IsNullOrEmpty(rawName)
                    ? Tr("psha.no_name", "(No Name)")
                    : rawName.Replace("<br>", "\n");

                var nameStyle = new GUIStyle(EditorStyles.label)
                {
                    richText = true,
                    wordWrap = true,
                    fontStyle = FontStyle.Bold
                };
                nameStyle.fontSize = EditorStyles.label.fontSize + 2;

                EditorGUILayout.LabelField(displayName, nameStyle);
            }


            GUILayout.FlexibleSpace();

            Rect previewRect = GUILayoutUtility.GetRect(
                80, 80,
                GUILayout.Width(80),
                GUILayout.Height(80)
            );

            if (mgr.menuIcon != null)
            {
                EditorGUI.DrawTextureTransparent(previewRect, mgr.menuIcon, ScaleMode.ScaleToFit);
            }
            else
            {
                var centered = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleCenter
                };
                EditorGUI.LabelField(previewRect, Tr("psha.no_icon", "No Icon"), centered);
            }

            EditorGUILayout.EndHorizontal();


            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(Tr("psha.target_slot", "Target Slot No. (1 to 8)"));

            EditorGUI.indentLevel++;
            int slot = _slotIndexProp.intValue;
            slot = EditorGUILayout.IntSlider(slot, 1, 8);
            EditorGUI.indentLevel--;
            _slotIndexProp.intValue = slot;
        }

        EditorGUILayout.Space();


        var devNotices = CalcDeveloperOptionsNotices(mgr);
        s_devFoldout = DrawFoldoutWithNoticeIcons(
            s_devFoldout,
            Tr("psha.dev_options", "Developer Options"),
            devNotices
        );
        if (s_devFoldout)
        {

            EditorGUI.indentLevel++;

            using (new EditorGUILayout.VerticalScope("box"))
            using (new GuiModeScope(CalcStableLabelWidth(), wideMode: true))
            {
                EditorGUILayout.LabelField(Tr("psha.vrc_emote_settings", "VRC Emote Settings"), EditorStyles.boldLabel);

                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(_targetMenuProp,
                    GC("psha.target_menu", "Target VRC Emote Menu", "psha.tt.target_menu",
                       "VRCEmote menu to customize. Leave empty to auto detect at build time."));
                if (EditorGUI.EndChangeCheck())
                {
                    UpdateTargetMenuPathFromCurrentDescriptor((PshaVRCEmoteInstaller)target);
                }

                // Warn when the selected menu does not belong to this avatar
                DrawTargetMenuAvatarMismatchWarning(mgr);

                if (_useMergeMEFxProp.boolValue && _autoRenameObjectNameProp.boolValue)
                {
                    EditorGUILayout.PropertyField(
                        _objectNameProp,
                        GC("psha.object_name", "Object Name", "psha.tt.object_name", "Optional build-time object name override. Leave empty to use the current GameObject name.")
                    );
                }

                EditorGUILayout.Space(4);



                EditorGUILayout.LabelField(Tr("psha.menu_settings", "Menu Settings"), EditorStyles.miniBoldLabel);

                EditorGUILayout.PropertyField(_emoteNameProp, GC("psha.emote_name", "Emote Name", "psha.tt.emote_name", "Name displayed in the VRC Emote menu."));
                EditorGUILayout.PropertyField(_menuIconProp, GC("psha.menu_icon", "Menu Icon", "psha.tt.menu_icon", "Icon displayed in the VRC Emote menu."));
                DrawMenuIconBuildErrorUI(mgr, _menuIconProp);

                DrawControlTypePopupField();

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.IntField(Tr("psha.value", "Value"), mgr.Value);
                    EditorGUILayout.TextField(Tr("psha.parameter", "Parameter"), $"{mgr.ParameterName}, Int");
                }

                EditorGUILayout.Space(4);


                EditorGUILayout.LabelField(Tr("psha.avatar_layer_settings", "Avatar Layer Settings"), EditorStyles.miniBoldLabel);

                EditorGUILayout.PropertyField(_actionLayerProp, GC("psha.avatar_action_layer", "Avatar Action Layer", "psha.tt.avatar_action_layer", "Avatar Action layer controller. If empty, uses the descriptor default."));
                EditorGUILayout.PropertyField(_fxLayerProp, GC("psha.avatar_fx_layer", "Avatar FX Layer", "psha.tt.avatar_fx_layer", "Avatar FX layer controller."));

                // Warn when Action or FX controller does not match this avatar
                DrawAnimLayerAvatarMismatchWarning(mgr);


                EditorGUILayout.Space(2);


                EditorGUILayout.LabelField(Tr("psha.me_merge_settings", "ME Layer Merge Settings"), EditorStyles.miniBoldLabel);

                EditorGUILayout.PropertyField(_MEactionProp, GC("psha.me_action_layer", "ME Action Layer", "psha.tt.me_action_layer", "ME Action template controller to merge."));


                if (_useMergeMEFxProp.boolValue)
                {
                    EditorGUILayout.PropertyField(_MEfxMotionProp, GC("psha.me_fx_layer", "ME FX Layer", "psha.tt.me_fx_layer", "ME FX template controller to merge."));
                }

                EditorGUILayout.Space(4);


                DrawActionStateSettingsValidated(mgr);


                EditorGUILayout.PropertyField(
                    _meWriteDefaultsOffProp,
                    GC("psha.me_write_defaults_off", "ME Write Default OFF", "psha.tt.me_write_defaults_off", "Force Write Defaults OFF for states inside ME layers.")
                );

                EditorGUILayout.Space(8);


                s_advancedFoldout = EditorGUILayout.Foldout(
                    s_advancedFoldout,
                    Tr("psha.advanced_options", "Advanced Options"),
                    true
                );

                if (s_advancedFoldout)
                {

                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(
                        _showActionMergeScopeProp,
                        GC("psha.track_action_sub_sm", "Action SM Root Tracking", "psha.tt.track_action_sub_sm", "Automatically track merge scope by Start/End and VRCEmote conditions.")
                    );
                    if (EditorGUI.EndChangeCheck())
                    {

                        if (!_showActionMergeScopeProp.boolValue)
                        {
                            _actionMergeScopeProp.stringValue = string.Empty;
                        }
                    }

                    EditorGUILayout.Space(4);


                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(
                        _useMergeMEFxProp,
                        GC("psha.use_merge_me_fx", "Use Merge ME FX", "psha.tt.use_merge_me_fx", "Use ME FX merge template.")
                    );
                    if (EditorGUI.EndChangeCheck())
                    {

                        if (!_useMergeMEFxProp.boolValue)
                        {
                            _MEfxMotionProp.objectReferenceValue = null;

                            _useAdditionalMEFxLayersProp.boolValue = false;

                            if (_additionalMEFxLayersProp != null && _additionalMEFxLayersProp.isArray)
                            {
                                for (int i = 0; i < _additionalMEFxLayersProp.arraySize; i++)
                                {
                                    var el = _additionalMEFxLayersProp.GetArrayElementAtIndex(i);
                                    el.objectReferenceValue = null;
                                }
                                _additionalMEFxLayersProp.arraySize = 0;
                            }
                        }
                    }


                    if (_useMergeMEFxProp.boolValue)
                    {
                        EditorGUI.indentLevel++;

                        EditorGUILayout.PropertyField(
                            _autoRenameObjectNameProp,
                            GC("psha.auto_rename_object_name", "Auto Rename Object", "psha.tt.auto_rename_object_name", "Automatically rename this GameObject during avatar build when using merged ME FX.")
                        );

                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PropertyField(
                            _useAdditionalMEFxLayersProp,
                            GC("psha.additional_me_fx", "+ Additional ME FX", "psha.tt.additional_me_fx", "Merge additional ME FX layers (up to 2).")
                        );
                        if (EditorGUI.EndChangeCheck())
                        {

                            if (!_useAdditionalMEFxLayersProp.boolValue)
                            {
                                if (_additionalMEFxLayersProp != null && _additionalMEFxLayersProp.isArray)
                                {
                                    for (int i = 0; i < _additionalMEFxLayersProp.arraySize; i++)
                                    {
                                        var el = _additionalMEFxLayersProp.GetArrayElementAtIndex(i);
                                        el.objectReferenceValue = null;
                                    }
                                    _additionalMEFxLayersProp.arraySize = 0;
                                }
                            }
                        }

                        if (_useAdditionalMEFxLayersProp.boolValue)
                        {
                            EditorGUI.indentLevel++;


                            EditorGUILayout.HelpBox(
                                Tr(
                                    "psha.additional_fx_perf_info",
                                    "Using multiple FX layers can negatively impact avatar animation performance and optimization.\n" +
                                    "Use additional ME FX layers only when necessary."
                                ),
                                MessageType.Info
                            );


                            int size = _additionalMEFxLayersProp.arraySize;
                            int newSize = EditorGUILayout.IntSlider(GC("psha.layer_count", "Layer Count", "psha.tt.layer_count", "Additional ME FX layer count."), size, 0, 2);
                            if (newSize != size)
                            {

                                if (newSize < size)
                                {
                                    for (int j = newSize; j < size; j++)
                                    {
                                        var el = _additionalMEFxLayersProp.GetArrayElementAtIndex(j);
                                        el.objectReferenceValue = null;
                                    }
                                }

                                _additionalMEFxLayersProp.arraySize = newSize;
                            }

                            for (int i = 0; i < _additionalMEFxLayersProp.arraySize; i++)
                            {
                                var element = _additionalMEFxLayersProp.GetArrayElementAtIndex(i);
                                EditorGUILayout.PropertyField(
                                    element,
                                    new GUIContent(Trf("psha.me_fx_layer_n", " + ME FX Layer {0}", i + 1), Tr("psha.tt.me_fx_layer", "Additional ME FX template controller."))
                                );
                            }

                            EditorGUI.indentLevel--;
                        }
                        EditorGUI.indentLevel--;
                    }


                    if (_changeEmoteMenuIconProp != null)
                    {
                        EditorGUILayout.PropertyField(
                            _changeEmoteMenuIconProp,
                            GC("psha.replace_me_menu_icon", "Replace ME Menu Icon", "psha.replace_me_menu_icon_tt", "Replace the VRC Emote menu icon with the ME icon.")
                        );
                    }

                    EditorGUILayout.Space(4);
                }


                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(6);



            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(Tr("psha.btn_setup", "Setup VRC Emote")))
            {
                SetupVRCEmote(mgr);
                UpdateTargetMenuPathFromCurrentDescriptor(mgr);
                serializedObject.Update();
                serializedObject.ApplyModifiedProperties();
                PshaVRCEmoteInstallerAutoRetarget.ClearNotOnAvatarOverrides((PshaVRCEmoteInstaller)target);
            }


            if (GUILayout.Button(Tr("psha.btn_default", "Default"), GUILayout.Width(70)))
            {

                Undo.RecordObjects(targets, Tr("psha.undo_reset", "Reset Psha VRC Emote Installer"));

                foreach (var t in targets)
                {
                    if (t is PshaVRCEmoteInstaller inst)
                    {
                        inst.ResetToDefaults();
                        EditorUtility.SetDirty(inst);
                    }
                }


                serializedObject.Update();
            }
            EditorGUILayout.EndHorizontal();
        }


        EditorGUILayout.Space();


        s_previewFoldout = EditorGUILayout.Foldout(s_previewFoldout, Tr("psha.preview_foldout", "Show Applied Changes (Preview)"));
        if (s_previewFoldout)
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                DrawCombinedPreview(mgr);
            }
        }
        DrawEditorLanguageSection();
        serializedObject.ApplyModifiedProperties();
    }





    private void DrawPlacementGuidance(PshaVRCEmoteInstaller mgr)
    {
        if (mgr == null) return;


        var descriptor = mgr.GetComponentInParent<VRCAvatarDescriptor>();
        if (descriptor == null)
        {
            EditorGUILayout.HelpBox(
                Tr(
                    "psha.warn_need_descriptor",
                    "For this component to work correctly, it must be placed under the avatar GameObject that has a VRCAvatarDescriptor.\n" +
                    "Move this object under the avatar root (the GameObject with the descriptor)."
                ),
                MessageType.Warning
            );
            EditorGUILayout.Space(4);
        }
    }






    private void DrawEmoteNameMismatchWarning(PshaVRCEmoteInstaller mgr)
    {
        if (mgr == null) return;

        if (!mgr.useMergeMEFxLayer) return;

        if (mgr.autoRenameObjectName && !string.IsNullOrWhiteSpace(mgr.objectName))
            return;

        var emoteName = GetPlainObjectNameFromEmoteName(mgr.emoteName);
        var objName = mgr.gameObject != null ? mgr.gameObject.name : null;

        if (string.IsNullOrEmpty(emoteName)) return;

        if (!string.Equals(emoteName, objName, System.StringComparison.Ordinal))
        {
            EditorGUILayout.HelpBox(
                Tr(
                    "psha.warn_name_mismatch",
                    "The emote name does not match the GameObject name.\n" +
                    "Results may differ when using ME FX layers.\n" +
                    "Click 'Setup VRC Emote' or make the names match."
                ),
                MessageType.Warning
            );
            EditorGUILayout.Space(4);
        }
    }






    private void DrawSharedFxLayerWarning(PshaVRCEmoteInstaller mgr)
    {
        if (mgr == null) return;


        if (!mgr.useMergeMEFxLayer || mgr.fxMELayer == null) return;


        var descriptor = mgr.GetComponentInParent<VRCAvatarDescriptor>();
        if (descriptor == null) return;


        var all = descriptor.GetComponentsInChildren<PshaVRCEmoteInstaller>(true);
        PshaVRCEmoteInstaller duplicate = null;

        foreach (var other in all)
        {
            if (other == null) continue;
            if (other == mgr) continue;

            // exclude inactive objects (matches Pass rule)
            if (!other.gameObject.activeInHierarchy) continue;

            // (optional) exclude disabled component too
            // if (!other.enabled) continue;

            if (!other.useMergeMEFxLayer) continue;
            if (other.fxMELayer == null) continue;

            if (other.fxMELayer == mgr.fxMELayer)
            {
                duplicate = other;
                break;
            }
        }

        if (duplicate != null)
        {
            string objName = duplicate.gameObject != null
                ? duplicate.gameObject.name
                : Tr("psha.no_gameobject", "(No GameObject)");

            EditorGUILayout.HelpBox(
                Trf(
                    "psha.warn_shared_fx",
                    "There are multiple ME emotes sharing the same ME FX layer.\n" +
                    "Results may differ when using ME FX layers.\n" +
                    "Duplicate ME FX layer object name: {0}",
                    objName
                ),
                MessageType.Warning
            );
            EditorGUILayout.Space(4);
        }
    }



    private const string kSessionWarnKey_AutoDetectMenu = "PshaVRCEmoteInstaller.AutoDetectVRCEmoteMenuWarned.";



    // Avatar mismatch warnings


    private void DrawTargetMenuAvatarMismatchWarning(PshaVRCEmoteInstaller mgr)
    {
#if VRC_SDK_VRCSDK3
        if (mgr == null) return;
        if (!TryGetAvatarDescriptor(mgr, out var desc) || desc == null) return;

        if (SoftRefHasProblem_TargetMenu(_targetMenuProp, desc))
        {
            EditorGUILayout.HelpBox(
                Tr(
                    "psha.warn_target_menu_mismatch",
                    "Target VRC Emote Menu does not belong to this avatar (or the asset is missing).\n" +
                    "Click 'Setup VRC Emote' or clear the field to use auto-detect."
                ),
                MessageType.Warning
            );
            EditorGUILayout.Space(4);
        }
#endif
    }

    private void DrawAnimLayerAvatarMismatchWarning(PshaVRCEmoteInstaller mgr)
    {
#if VRC_SDK_VRCSDK3
        if (mgr == null) return;
        if (!TryGetAvatarDescriptor(mgr, out var desc) || desc == null) return;

        bool actionBad = SoftRefHasProblem_AnimatorLayer(_actionLayerProp, desc, VRCAvatarDescriptor.AnimLayerType.Action);
        bool fxBad = SoftRefHasProblem_AnimatorLayer(_fxLayerProp, desc, VRCAvatarDescriptor.AnimLayerType.FX);

        if (actionBad || fxBad)
        {
            EditorGUILayout.HelpBox(
                Tr(
                    "psha.warn_anim_layer_mismatch",
                    "Avatar Action/FX layer reference does not match this avatar (or the asset is missing).\n" +
                    "Click 'Setup VRC Emote' or clear the fields to use the descriptor layers."
                ),
                MessageType.Warning
            );
            EditorGUILayout.Space(4);
        }
#endif
    }

#if VRC_SDK_VRCSDK3
    private static bool TryGetAvatarDescriptor(PshaVRCEmoteInstaller mgr, out VRCAvatarDescriptor desc)
    {
        desc = null;
        if (mgr == null) return false;
        desc = mgr.GetComponentInParent<VRCAvatarDescriptor>();
        return desc != null;
    }

    private static bool SoftRefHasProblem_TargetMenu(SerializedProperty menuRefProp, VRCAvatarDescriptor desc)
    {
        if (menuRefProp == null || desc == null) return false;

        var guidProp = menuRefProp.FindPropertyRelative("guid");
        var lidProp = menuRefProp.FindPropertyRelative("localId");
        var hintProp = menuRefProp.FindPropertyRelative("nameHint");

        string guid = guidProp != null ? guidProp.stringValue : null;
        long lid = lidProp != null ? lidProp.longValue : 0;
        string hint = hintProp != null ? hintProp.stringValue : null;

        if (string.IsNullOrEmpty(guid)) return false; // empty == auto-detect

        var resolved = PshaAssetGuidReference.Resolve(guid, lid, typeof(VRCExpressionsMenu)) as VRCExpressionsMenu;
        if (resolved == null) return true; // missing

        if (!string.IsNullOrEmpty(hint) && !string.Equals(resolved.name, hint, StringComparison.Ordinal))
            return true; // hint mismatch

        var root = desc.expressionsMenu;
        if (root == null) return true;

        return !MenuTreeContains(root, resolved);
    }

    private static bool SoftRefHasProblem_AnimatorLayer(
        SerializedProperty layerRefProp,
        VRCAvatarDescriptor desc,
        VRCAvatarDescriptor.AnimLayerType layerType)
    {
        if (layerRefProp == null || desc == null) return false;

        var guidProp = layerRefProp.FindPropertyRelative("guid");
        var lidProp = layerRefProp.FindPropertyRelative("localId");
        var hintProp = layerRefProp.FindPropertyRelative("nameHint");

        string guid = guidProp != null ? guidProp.stringValue : null;
        long lid = lidProp != null ? lidProp.longValue : 0;
        string hint = hintProp != null ? hintProp.stringValue : null;

        if (string.IsNullOrEmpty(guid)) return false; // empty == descriptor layer

        var resolved = PshaAssetGuidReference.Resolve(guid, lid, typeof(RuntimeAnimatorController)) as RuntimeAnimatorController;
        if (resolved == null) return true; // missing

        if (!string.IsNullOrEmpty(hint) && !string.Equals(resolved.name, hint, StringComparison.Ordinal))
            return true; // hint mismatch

        return !MatchesDescriptorLayerControllerGuid(desc, layerType, guid, lid);
    }

    private static bool MenuTreeContains(VRCExpressionsMenu root, VRCExpressionsMenu target)
    {
        if (root == null || target == null) return false;

        var visited = new HashSet<VRCExpressionsMenu>();
        var stack = new Stack<VRCExpressionsMenu>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (cur == null) continue;
            if (!visited.Add(cur)) continue;

            if (ReferenceEquals(cur, target)) return true;

            var controls = cur.controls;
            if (controls == null) continue;

            for (int i = 0; i < controls.Count; i++)
            {
                var c = controls[i];
                if (c == null) continue;
                if (c.subMenu != null) stack.Push(c.subMenu);
            }
        }

        return false;
    }

    private static bool MatchesDescriptorLayerControllerGuid(
        VRCAvatarDescriptor desc,
        VRCAvatarDescriptor.AnimLayerType layerType,
        string targetGuid,
        long targetLocalId)
    {
        if (desc == null) return true;
        if (string.IsNullOrEmpty(targetGuid)) return true;

        var layers = desc.baseAnimationLayers;
        if (layers == null) return false;

        VRCAvatarDescriptor.CustomAnimLayer found = default;
        bool hasFound = false;

        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i].type == layerType)
            {
                found = layers[i];
                hasFound = true;
                break;
            }
        }

        if (!hasFound) return false;

        // Default layers are treated as mismatch
        if (found.isDefault) return false;

        var controller = found.animatorController;
        if (controller == null) return false;

        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(controller, out var guid, out long lid))
            return false;

        if (guid != targetGuid) return false;

        if (targetLocalId == 0) return true;
        return lid == targetLocalId;
    }
#endif


    private void DrawCombinedPreview(PshaVRCEmoteInstaller current)
    {

        var descriptor = current.GetComponentInParent<VRCAvatarDescriptor>();
        if (descriptor == null)
        {
            EditorGUILayout.HelpBox(Tr("psha.no_descriptor_short", "Could not find a parent VRCAvatarDescriptor."), MessageType.Info);
            return;
        }

        var rootMenu = descriptor.expressionsMenu;
        if (rootMenu == null)
        {
            EditorGUILayout.HelpBox(Tr("psha.menu_empty", "The avatar Expressions Menu is empty."), MessageType.Info);
            return;
        }


        var explicitMenu = ResolveMenuFromGuidRef(_targetMenuProp);
        var autoEmoteMenu = FindEmoteMenu(rootMenu);


        // auto detect warning: only when we actually need auto detect (explicitMenu is empty),
        // and only once per editor session per avatar descriptor.
        if (explicitMenu == null && autoEmoteMenu == null)
        {
            string key = kSessionWarnKey_AutoDetectMenu + descriptor.GetInstanceID();
            if (!SessionState.GetBool(key, false))
            {
                Debug.LogWarning(
                    "[PshaVRCEmoteInstallerPass] Failed to auto detect the VRCEmote menu. " +
                    "If targetMenu/path cannot be resolved, menu patching may be skipped."
                );
                SessionState.SetBool(key, true);
            }
        }


        VRCExpressionsMenu previewMenu = explicitMenu ?? autoEmoteMenu;
        if (previewMenu == null)
        {
            if (explicitMenu == null)
            {
                EditorGUILayout.HelpBox(
                    Tr("psha.preview_no_menu", "Target VRC emote menu is empty, and the emote menu could not be auto detected on the avatar."),
                    MessageType.Info
                );
            }
            else
            {
                EditorGUILayout.HelpBox(
                    Tr("psha.preview_target_no_controls", "The target VRC emote menu has no controls."),
                    MessageType.Warning
                );
            }
            return;
        }

        bool previewIsAuto = (explicitMenu == null);

        if (previewIsAuto)
        {
            EditorGUILayout.HelpBox(
                Trf("psha.preview_auto_detected", "Auto detected emote menu for preview: {0}", previewMenu.name),
                MessageType.None
            );
        }
        else
        {
            EditorGUILayout.HelpBox(
                Trf("psha.preview_menu", "Preview menu: {0}", previewMenu.name),
                MessageType.None
            );
        }

        if (previewMenu.controls == null || previewMenu.controls.Count == 0)
        {
            EditorGUILayout.HelpBox(Tr("psha.preview_menu_no_controls", "The preview menu has no controls."), MessageType.Warning);
            return;
        }



        var finalList = CollectFinalSlotWinners(descriptor);
        if (finalList.Count == 0)
        {
            EditorGUILayout.HelpBox(
                Tr("psha.preview_no_installers", "There are no active PshaVRCEmoteInstaller components, so there is nothing to preview."),
                MessageType.Info
            );
            return;
        }

        var affectingManagers = new List<PshaVRCEmoteInstaller>();
        foreach (var m in finalList)
        {
            if (m == null) continue;

            var targetMenu = ResolveTargetMenuForPreview(m, rootMenu, autoEmoteMenu);
            if (targetMenu == previewMenu)
            {
                affectingManagers.Add(m);
            }
        }

        int controlCount = previewMenu.controls.Count;


        var beforeNames = new string[controlCount];
        var beforeTypes = new VRCExpressionsMenu.Control.ControlType[controlCount];
        var afterNames = new string[controlCount];
        var afterTypes = new VRCExpressionsMenu.Control.ControlType[controlCount];
        var finalManager = new PshaVRCEmoteInstaller[controlCount];
        var changed = new bool[controlCount];


        for (int i = 0; i < controlCount; i++)
        {
            var ctrl = previewMenu.controls[i];
            beforeNames[i] = ctrl != null ? ctrl.name : "";
            beforeTypes[i] = ctrl != null ? ctrl.type : VRCExpressionsMenu.Control.ControlType.Button;

            afterNames[i] = beforeNames[i];
            afterTypes[i] = beforeTypes[i];
            finalManager[i] = null;
            changed[i] = false;
        }


        for (int i = affectingManagers.Count - 1; i >= 0; i--)
        {
            var m = affectingManagers[i];
            if (m == null) continue;
            if (controlCount == 0) break;

            int slot = Mathf.Clamp(m.slotIndex, 1, 8);
            int idx = FindEmoteControlIndex(previewMenu, slot);
            if (idx < 0) continue;

            bool didChange = false;


            if (!string.IsNullOrEmpty(m.emoteName))
            {
                var raw = m.emoteName;
                var processed = raw.Replace("<br>", " ");
                afterNames[idx] = processed;
                didChange = true;
            }


            if (m.controlType != PshaVRCEmoteInstaller.EmoteControlType.None)
            {
                afterTypes[idx] = (m.controlType == PshaVRCEmoteInstaller.EmoteControlType.Toggle)
                    ? VRCExpressionsMenu.Control.ControlType.Toggle
                    : VRCExpressionsMenu.Control.ControlType.Button;
                didChange = true;
            }

            if (didChange)
            {
                finalManager[idx] = m;
                changed[idx] = true;
            }
        }



        float noWidth = 25f;
        float beforeNameWidth = 60f;
        float afterNameWidth = 110f;
        float typeWidth = 60f;


        var nameColStyle = new GUIStyle(EditorStyles.label)
        {
            richText = true,
            wordWrap = false,
            clipping = TextClipping.Clip
        };

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(Tr("psha.preview_no", "No."), GUILayout.Width(noWidth));
        EditorGUILayout.LabelField(Tr("psha.preview_name_before", "Name →→"), GUILayout.Width(beforeNameWidth));
        EditorGUILayout.LabelField(Tr("psha.preview_name_after", "Name (After)"), GUILayout.Width(afterNameWidth));
        EditorGUILayout.LabelField(Tr("psha.preview_type_before", "Type →→"), GUILayout.Width(typeWidth));
        EditorGUILayout.LabelField(Tr("psha.preview_type_after", "Type"), GUILayout.Width(typeWidth));
        EditorGUILayout.EndHorizontal();


        for (int i = 0; i < controlCount; i++)
        {
            var ctrl = previewMenu.controls[i];
            if (ctrl == null) continue;

            int no = i + 1;

            string beforeName = beforeNames[i];
            string beforeType = beforeTypes[i].ToString();


            string afterNameStr = changed[i] ? afterNames[i] : "-";
            string afterTypeStr = changed[i] ? afterTypes[i].ToString() : "-";

            bool ownedByCurrent = (finalManager[i] == current);

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField(no.ToString(), GUILayout.Width(noWidth));
            EditorGUILayout.LabelField(beforeName, nameColStyle, GUILayout.Width(beforeNameWidth));

            if (ownedByCurrent && changed[i])
            {
                var boldName = new GUIStyle(nameColStyle)
                {
                    fontStyle = FontStyle.Bold
                };
                EditorGUILayout.LabelField(afterNameStr, boldName, GUILayout.Width(afterNameWidth));
            }
            else
            {
                EditorGUILayout.LabelField(afterNameStr, nameColStyle, GUILayout.Width(afterNameWidth));
            }

            EditorGUILayout.LabelField(beforeType, GUILayout.Width(typeWidth));

            if (ownedByCurrent && changed[i])
            {
                var bold = new GUIStyle(EditorStyles.label)
                {
                    fontStyle = FontStyle.Bold
                };
                EditorGUILayout.LabelField(afterTypeStr, bold, GUILayout.Width(typeWidth));
            }
            else
            {
                EditorGUILayout.LabelField(afterTypeStr, GUILayout.Width(typeWidth));
            }

            EditorGUILayout.EndHorizontal();
        }
    }


    private void SetupVRCEmote(PshaVRCEmoteInstaller mgr)
    {
        if (mgr == null) return;

        var descriptor = mgr.GetComponentInParent<VRCAvatarDescriptor>();
        if (descriptor == null)
        {
            EditorUtility.DisplayDialog(
                Tr("psha.dialog_setup_title", "Setup VRC Emote"),
                Tr("psha.dialog_no_descriptor", "Could not find a parent VRCAvatarDescriptor.\nMake sure this object is placed under the avatar root."),
                Tr("psha.ok", "OK")
            );
            return;
        }


        var layers = descriptor.baseAnimationLayers;

        AnimatorController actionController = null;
        bool actionIsDefault = false;

        if (layers != null && layers.Length > 0)
        {
            foreach (var layer in layers)
            {
                switch (layer.type)
                {
                    case VRCAvatarDescriptor.AnimLayerType.Action:
                        {
                            // Policy: leaving empty => use descriptor at build time.
                            // So during Setup, we only write the soft-ref when the layer is explicitly overridden by a controller.
                            actionIsDefault = layer.isDefault;

                            if (layer.isDefault || layer.animatorController == null)
                            {
                                // Clear (None)
                                PshaAssetGuidReference.SetToSerializedProperty(_actionLayerProp, null);
                                actionController = null;
                            }
                            else
                            {
                                PshaAssetGuidReference.SetToSerializedProperty(_actionLayerProp, layer.animatorController);
                                actionController = layer.animatorController as AnimatorController;
                            }

                            break;
                        }

                    case VRCAvatarDescriptor.AnimLayerType.FX:
                        {
                            // Same policy as Action: if FX is Default or empty, keep it as None (so descriptor FX is used).
                            if (layer.isDefault || layer.animatorController == null)
                            {
                                PshaAssetGuidReference.SetToSerializedProperty(_fxLayerProp, null);
                            }
                            else
                            {
                                PshaAssetGuidReference.SetToSerializedProperty(_fxLayerProp, layer.animatorController);
                            }

                            break;
                        }
                }
            }
        }


        string startStateName = null;
        string endStateName = null;

        if (actionController != null)
        {
            TryDetectEmoteStartEnd(actionController, out startStateName, out endStateName);
        }

        // Default or null Action layer is treated as mismatch
        bool assumeBuiltinDefault = (actionController == null) || actionIsDefault;

        string detectedMergeScopeName = null;
        bool detectedMergeScope = false;

        if (!assumeBuiltinDefault &&
            actionController != null &&
            !string.IsNullOrEmpty(startStateName) &&
            !string.IsNullOrEmpty(endStateName))
        {
            if (TryDetectEmoteMergeScope(actionController, startStateName, endStateName, out var mergeScopeName))
            {
                detectedMergeScopeName = mergeScopeName;
                detectedMergeScope = !string.IsNullOrEmpty(detectedMergeScopeName);
            }
        }

        if (assumeBuiltinDefault)
        {
            startStateName = kDefaultActionStartState; // "Prepare Standing"
            endStateName = kDefaultActionEndState;   // "BlendOut Stand"

            _showActionMergeScopeProp.boolValue = false;
            _actionMergeScopeProp.stringValue = string.Empty;
        }
        else if (detectedMergeScope)
        {
            if (string.Equals(detectedMergeScopeName, kDefaultActionScope, System.StringComparison.Ordinal))
            {
                _showActionMergeScopeProp.boolValue = false;
                _actionMergeScopeProp.stringValue = string.Empty;
            }
            else
            {
                _showActionMergeScopeProp.boolValue = true;
                _actionMergeScopeProp.stringValue = detectedMergeScopeName;
            }
        }

        // Only overwrite when a value was found
        if (!string.IsNullOrEmpty(startStateName))
            _startActionStateProp.stringValue = startStateName;

        if (!string.IsNullOrEmpty(endStateName))
            _endActionStateProp.stringValue = endStateName;


        var rootMenu = descriptor.expressionsMenu;
        if (rootMenu == null)
        {
            // If expressions menu is not assigned, clear target menu references


            PshaAssetGuidReference.SetToSerializedProperty(_targetMenuProp, null);
            if (_targetMenuPathProp != null)
                _targetMenuPathProp.arraySize = 0;

            _targetMenuProp.serializedObject.ApplyModifiedProperties();

            EditorUtility.DisplayDialog(
                Tr("psha.dialog_setup_title", "Setup VRC Emote"),
                Tr("psha.dialog_menu_not_assigned", "The avatar Expressions Menu is not assigned."),
                Tr("psha.ok", "OK")
            );
            return;
        }

        var emoteMenu = FindEmoteMenu(rootMenu);
        if (emoteMenu != null)
        {
            PshaAssetGuidReference.SetToSerializedProperty(_targetMenuProp, emoteMenu);
            UpdateTargetMenuPathFromCurrentDescriptor(mgr);
        }
        else
        {
            // If auto detect fails, clear targetMenu and path
            PshaAssetGuidReference.SetToSerializedProperty(_targetMenuProp, null);
            if (_targetMenuPathProp != null) _targetMenuPathProp.arraySize = 0;
            _targetMenuProp.serializedObject.ApplyModifiedProperties();

            EditorUtility.DisplayDialog(
                "Setup VRC Emote",
                Tr(
                    "psha.dialog_failed_auto_detect_menu",
                    "Failed to auto detect the emote menu.\nPlease assign the target VRC emote menu manually."
                ),
                Tr("psha.ok", "OK")
            );
        }



        bool hasExplicitBuildObjectName =
            mgr.useMergeMEFxLayer &&
            mgr.autoRenameObjectName &&
            !string.IsNullOrWhiteSpace(mgr.objectName);

        var plainEmoteObjectName = GetPlainObjectNameFromEmoteName(mgr.emoteName);

        if (!hasExplicitBuildObjectName && !string.IsNullOrEmpty(plainEmoteObjectName) && mgr.gameObject != null)
        {
            if (mgr.gameObject.name != plainEmoteObjectName)
            {
                Undo.RecordObject(mgr.gameObject, "Rename Emote Object");
                mgr.gameObject.name = plainEmoteObjectName;
                EditorUtility.SetDirty(mgr.gameObject);
            }
        }
    }


    private static string GetPlainObjectNameFromEmoteName(string emoteName)
    {
        if (string.IsNullOrEmpty(emoteName)) return string.Empty;
        var plain = Regex.Replace(emoteName, @"<\s*br\s*/?\s*>", string.Empty, RegexOptions.IgnoreCase);
        plain = Regex.Replace(plain, @"<[^>]+>", string.Empty);
        plain = plain.Replace("\r", string.Empty).Replace("\n", string.Empty);
        plain = plain.Trim();

        return plain;
    }


    private void UpdateTargetMenuPathFromCurrentDescriptor(PshaVRCEmoteInstaller mgr)
    {
        if (mgr == null || _targetMenuPathProp == null || _targetMenuProp == null) return;

        var descriptor = mgr.GetComponentInParent<VRCAvatarDescriptor>();
        var rootMenu = descriptor != null ? descriptor.expressionsMenu : null;

        var targetMenu = ResolveMenuFromGuidRef(_targetMenuProp);
        if (rootMenu == null || targetMenu == null)
        {
            _targetMenuPathProp.ClearArray();
            serializedObject.ApplyModifiedProperties();
            return;
        }

        if (TryComputeMenuPath(rootMenu, targetMenu, out var path))
        {
            _targetMenuPathProp.arraySize = path.Count;
            for (int i = 0; i < path.Count; i++)
                _targetMenuPathProp.GetArrayElementAtIndex(i).intValue = path[i];
        }
        else
        {
            _targetMenuPathProp.ClearArray();
        }

        serializedObject.ApplyModifiedProperties();
    }

    private static VRCExpressionsMenu ResolveMenuFromGuidRef(SerializedProperty guidRefProp)
    {
        if (guidRefProp == null) return null;
        var guid = guidRefProp.FindPropertyRelative("guid")?.stringValue;
        var lid = guidRefProp.FindPropertyRelative("localId")?.longValue ?? 0;
        return PshaAssetGuidReference.Resolve(guid, lid, typeof(VRCExpressionsMenu)) as VRCExpressionsMenu;
    }

    private static bool TryComputeMenuPath(
        VRCExpressionsMenu root,
        VRCExpressionsMenu target,
        out System.Collections.Generic.List<int> path
    )
    {
        path = null;
        if (root == null || target == null) return false;
        if (ReferenceEquals(root, target))
        {
            path = new System.Collections.Generic.List<int>();
            return true;
        }

        var visited = new System.Collections.Generic.HashSet<VRCExpressionsMenu>();
        var tmp = new System.Collections.Generic.List<int>();

        bool found = Dfs(root);
        if (found) path = tmp;
        return found;

        bool Dfs(VRCExpressionsMenu cur)
        {
            if (cur == null || !visited.Add(cur)) return false;
            if (cur.controls == null) return false;

            for (int i = 0; i < cur.controls.Count; i++)
            {
                var c = cur.controls[i];
                if (c?.subMenu == null) continue;

                tmp.Add(i);
                if (ReferenceEquals(c.subMenu, target)) return true;
                if (Dfs(c.subMenu)) return true;
                tmp.RemoveAt(tmp.Count - 1);
            }
            return false;
        }
    }


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

            if (ctrl.parameter.name == "VRCEmote" &&
                (ctrl.type == VRCExpressionsMenu.Control.ControlType.Button ||
                 ctrl.type == VRCExpressionsMenu.Control.ControlType.Toggle))
            {
                emoteCount++;
            }
        }

        return emoteCount >= 4;
    }




    private class EmoteStateInfo
    {
        public int OutgoingStandingEmoteTransitions;
        public int IncomingStandingEmoteTransitions;
    }











    private static void TryDetectEmoteStartEnd(
        AnimatorController controller,
        out string startStateName,
        out string endStateName)
    {
        startStateName = null;
        endStateName = null;

        if (controller == null) return;

        var map = new Dictionary<AnimatorState, EmoteStateInfo>();

        foreach (var layer in controller.layers)
        {
            if (layer.stateMachine == null) continue;
            CollectStandingEmoteStateInfo(layer.stateMachine, map);
        }

        if (map.Count == 0) return;

        AnimatorState bestStart = null;
        int bestStartCount = 0;
        AnimatorState bestEnd = null;
        int bestEndCount = 0;

        foreach (var kvp in map)
        {
            var state = kvp.Key;
            var info = kvp.Value;
            if (state == null) continue;


            if (IsSitLikeName(state.name))
                continue;

            if (info.OutgoingStandingEmoteTransitions > bestStartCount)
            {
                bestStartCount = info.OutgoingStandingEmoteTransitions;
                bestStart = state;
            }

            if (info.IncomingStandingEmoteTransitions > bestEndCount)
            {
                bestEndCount = info.IncomingStandingEmoteTransitions;
                bestEnd = state;
            }
        }

        if (bestStart != null && bestStartCount > 0)
            startStateName = bestStart.name;

        if (bestEnd != null && bestEndCount > 0)
            endStateName = bestEnd.name;
    }


    private static void CollectStandingEmoteStateInfo(
        AnimatorStateMachine sm,
        Dictionary<AnimatorState, EmoteStateInfo> map)
    {
        if (sm == null) return;


        foreach (var child in sm.states)
        {
            var state = child.state;
            if (state == null) continue;

            if (!map.TryGetValue(state, out var info))
            {
                info = new EmoteStateInfo();
                map[state] = info;
            }

            bool fromSit = IsSitLikeName(state.name);


            foreach (var t in state.transitions)
            {
                ProcessEmoteTransition(state, t, fromSit, map);
            }
        }


        foreach (var any in sm.anyStateTransitions)
        {
            ProcessEmoteTransition(null, any, false, map);
        }


        foreach (var sub in sm.stateMachines)
        {
            CollectStandingEmoteStateInfo(sub.stateMachine, map);
        }
    }






    private static void ProcessEmoteTransition(
        AnimatorState fromState,
        AnimatorTransitionBase t,
        bool fromSit,
        Dictionary<AnimatorState, EmoteStateInfo> map)
    {
        if (t == null) return;

        var conditions = t.conditions;
        if (conditions == null || conditions.Length == 0) return;

        bool hasVrcEmote = false;
        bool hasStandingSlot = false;
        bool isSitTransition = false;

        for (int i = 0; i < conditions.Length; i++)
        {
            var cond = conditions[i];
            if (cond.parameter != "VRCEmote") continue;

            hasVrcEmote = true;

            if (cond.mode == AnimatorConditionMode.Equals)
            {
                int value = Mathf.RoundToInt(cond.threshold);

                if (value >= 1 && value <= 8)
                {
                    hasStandingSlot = true;
                }
                else if (value >= 9)
                {

                    isSitTransition = true;
                }
            }
        }


        if (!hasVrcEmote) return;


        if (isSitTransition) return;




        if (fromState != null && !fromSit && hasStandingSlot)
        {
            if (!map.TryGetValue(fromState, out var fromInfo))
            {
                fromInfo = new EmoteStateInfo();
                map[fromState] = fromInfo;
            }

            fromInfo.OutgoingStandingEmoteTransitions++;
        }




        var dest = t.destinationState;
        if (dest != null && !IsSitLikeName(dest.name))
        {
            if (!map.TryGetValue(dest, out var destInfo))
            {
                destInfo = new EmoteStateInfo();
                map[dest] = destInfo;
            }

            destInfo.IncomingStandingEmoteTransitions++;
        }
    }













    private static bool IsSitLikeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;

        var lower = name.ToLowerInvariant();


        if (lower.Contains(" sit") || lower.Contains("sit ")
            || lower.Contains("_sit") || lower.Contains("sit_"))
        {
            return true;
        }

        return false;
    }








    private static bool TryDetectEmoteMergeScope(
        AnimatorController controller,
        string startStateName,
        string endStateName,
        out string scopeName)
    {
        scopeName = null;

        if (controller == null ||
            string.IsNullOrEmpty(startStateName) ||
            string.IsNullOrEmpty(endStateName))
        {
            return false;
        }

        var candidates = new List<AnimatorStateMachine>();

        foreach (var layer in controller.layers)
        {
            if (layer.stateMachine == null) continue;
            CollectCandidateStateMachinesByStates(
                layer.stateMachine,
                startStateName,
                endStateName,
                candidates);
        }

        if (candidates.Count == 0)
            return false;


        foreach (var sm in candidates)
        {
            if (ContainsVRCEmoteEqualsInRange(sm, 1, 8))
            {
                scopeName = sm.name;
                return true;
            }
        }

        return false;
    }





    private static void CollectCandidateStateMachinesByStates(
        AnimatorStateMachine sm,
        string startStateName,
        string endStateName,
        List<AnimatorStateMachine> result)
    {
        if (sm == null) return;

        bool hasStart = false;
        bool hasEnd = false;

        foreach (var child in sm.states)
        {
            var state = child.state;
            if (state == null) continue;

            if (state.name == startStateName) hasStart = true;
            if (state.name == endStateName) hasEnd = true;
        }

        if (hasStart && hasEnd)
        {
            result.Add(sm);
        }


        foreach (var sub in sm.stateMachines)
        {
            CollectCandidateStateMachinesByStates(
                sub.stateMachine,
                startStateName,
                endStateName,
                result);
        }
    }






    private static bool ContainsVRCEmoteEqualsInRange(
        AnimatorStateMachine sm,
        int minSlot,
        int maxSlot)
    {
        if (sm == null) return false;


        foreach (var child in sm.states)
        {
            var state = child.state;
            if (state == null) continue;

            foreach (var t in state.transitions)
            {
                if (TransitionHasVRCEmoteEqualsInRange(t, minSlot, maxSlot))
                    return true;
            }
        }


        foreach (var t in sm.anyStateTransitions)
        {
            if (TransitionHasVRCEmoteEqualsInRange(t, minSlot, maxSlot))
                return true;
        }


        foreach (var t in sm.entryTransitions)
        {
            if (TransitionHasVRCEmoteEqualsInRange(t, minSlot, maxSlot))
                return true;
        }


        foreach (var sub in sm.stateMachines)
        {
            var subSm = sub.stateMachine;
            if (subSm == null) continue;

            var transitions = sm.GetStateMachineTransitions(subSm);
            foreach (var t in transitions)
            {
                if (TransitionHasVRCEmoteEqualsInRange(t, minSlot, maxSlot))
                    return true;
            }
        }


        foreach (var sub in sm.stateMachines)
        {
            if (ContainsVRCEmoteEqualsInRange(sub.stateMachine, minSlot, maxSlot))
                return true;
        }

        return false;
    }




    private static bool TransitionHasVRCEmoteEqualsInRange(
        AnimatorTransitionBase t,
        int minSlot,
        int maxSlot)
    {
        if (t == null) return false;

        var conditions = t.conditions;
        if (conditions == null || conditions.Length == 0) return false;

        for (int i = 0; i < conditions.Length; i++)
        {
            var cond = conditions[i];
            if (cond.parameter != "VRCEmote") continue;
            if (cond.mode != AnimatorConditionMode.Equals) continue;

            int value = Mathf.RoundToInt(cond.threshold);
            if (value >= minSlot && value <= maxSlot)
            {
                return true;
            }
        }

        return false;
    }








    private const string EmoteParamName = "VRCEmote";

    private static List<PshaVRCEmoteInstaller> CollectFinalSlotWinners(VRCAvatarDescriptor descriptor)
    {
        var all = descriptor != null
            ? descriptor.GetComponentsInChildren<PshaVRCEmoteInstaller>(true)
            : Array.Empty<PshaVRCEmoteInstaller>();

        var list = new List<PshaVRCEmoteInstaller>(all);
        list.RemoveAll(m => m == null || !m.gameObject.activeInHierarchy);

        list.Sort((a, b) => CompareHierarchyOrder(a.transform, b.transform));

        var used = new bool[9];
        var finalList = new List<PshaVRCEmoteInstaller>();
        foreach (var m in list)
        {
            int slot = Mathf.Clamp(m.slotIndex, 1, 8);
            if (used[slot]) continue;
            used[slot] = true;
            finalList.Add(m);
        }

        return finalList;
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


    private static int CompareHierarchyOrder(Transform a, Transform b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a == null) return 1;
        if (b == null) return -1;

        var pathA = BuildSiblingIndexPath(a);
        var pathB = BuildSiblingIndexPath(b);

        int min = System.Math.Min(pathA.Count, pathB.Count);
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




    private const string PrefKey_UseSystemLanguage = "PshaVRCEmote.EditorLanguage.UseSystem";
    private const string PrefKey_LanguageCode = "PshaVRCEmote.EditorLanguage.Code";

    private const string LegacyPrefKey_UseSystemLanguage = "PshaVRCEmote.EditorLang.UseSystem";
    private const string LegacyPrefKey_LanguageCode = "PshaVRCEmote.EditorLang.Code";

    private static bool s_prefsMigrated = false;

    private static void EnsurePrefsMigrated()
    {
        if (s_prefsMigrated) return;
        s_prefsMigrated = true;

        if (!UnityEditor.EditorPrefs.HasKey(PrefKey_UseSystemLanguage) && UnityEditor.EditorPrefs.HasKey(LegacyPrefKey_UseSystemLanguage))
        {
            UnityEditor.EditorPrefs.SetBool(PrefKey_UseSystemLanguage, UnityEditor.EditorPrefs.GetBool(LegacyPrefKey_UseSystemLanguage, true));
        }

        if (!UnityEditor.EditorPrefs.HasKey(PrefKey_LanguageCode) && UnityEditor.EditorPrefs.HasKey(LegacyPrefKey_LanguageCode))
        {
            UnityEditor.EditorPrefs.SetString(PrefKey_LanguageCode, UnityEditor.EditorPrefs.GetString(LegacyPrefKey_LanguageCode, "en-us"));
        }
    }

    private struct GuiModeScope : IDisposable
    {
        private readonly bool _oldWide;
        private readonly float _oldLabelWidth;

        public GuiModeScope(float labelWidth, bool wideMode = true)
        {
            _oldWide = EditorGUIUtility.wideMode;
            _oldLabelWidth = EditorGUIUtility.labelWidth;

            EditorGUIUtility.wideMode = wideMode;
            EditorGUIUtility.labelWidth = labelWidth;
        }

        public void Dispose()
        {
            EditorGUIUtility.wideMode = _oldWide;
            EditorGUIUtility.labelWidth = _oldLabelWidth;
        }
    }

    private static float CalcStableLabelWidth()
    {
        // Clamp label width to view width
        float w = EditorGUIUtility.currentViewWidth;
        return Mathf.Clamp(w * 0.32f, 120f, 170f);
    }


    private struct LangItem
    {
        public string Code;
        public string Display;
        public LangItem(string code, string display) { Code = code; Display = display; }
    }

    private static readonly LangItem[] s_langItems =
    {
    new LangItem("en-us",  "English (United States)"),
    new LangItem("ja-jp",  "日本語 (日本)"),
    new LangItem("ko-kr",  "한국어 (Korea)"),
    new LangItem("zh-hans","中文（简体）"),
    new LangItem("zh-hant","中文（繁體）"),
};

    private static bool GetUseSystemLanguage()
    {
        EnsurePrefsMigrated();
        return UnityEditor.EditorPrefs.GetBool(PrefKey_UseSystemLanguage, true);
    }

    private static void SetUseSystemLanguage(bool v)
    {
        EnsurePrefsMigrated();
        UnityEditor.EditorPrefs.SetBool(PrefKey_UseSystemLanguage, v);
    }

    private static string GetSavedLanguageCode()
    {
        EnsurePrefsMigrated();
        return UnityEditor.EditorPrefs.GetString(PrefKey_LanguageCode, "en-us");
    }

    private static void SetSavedLanguageCode(string code)
    {
        EnsurePrefsMigrated();
        UnityEditor.EditorPrefs.SetString(PrefKey_LanguageCode, code ?? "en-us");
    }

    private static string GetSystemLanguageCode()
    {

        switch (UnityEngine.Application.systemLanguage)
        {
            case UnityEngine.SystemLanguage.Japanese: return "ja-jp";
            case UnityEngine.SystemLanguage.Korean: return "ko-kr";
            case UnityEngine.SystemLanguage.ChineseSimplified: return "zh-hans";
            case UnityEngine.SystemLanguage.ChineseTraditional: return "zh-hant";
            default: return "en-us";
        }
    }

    private static string GetEffectiveLanguageCode()
    {
        return GetUseSystemLanguage() ? GetSystemLanguageCode() : GetSavedLanguageCode();
    }

    private static string CodeToDisplay(string code)
    {
        for (int i = 0; i < s_langItems.Length; i++)
            if (string.Equals(s_langItems[i].Code, code, StringComparison.OrdinalIgnoreCase))
                return s_langItems[i].Display;


        return code ?? "en-us";
    }

    private static VRCExpressionsMenu ResolveTargetMenuForPreview(
        PshaVRCEmoteInstaller inst,
        VRCExpressionsMenu rootMenu,
        VRCExpressionsMenu autoEmoteMenu
    )
    {
        if (inst == null) return autoEmoteMenu;

        // Prefer path
        if (rootMenu != null && inst.targetMenuPath != null && inst.targetMenuPath.Length > 0)
        {
            var cur = rootMenu;
            foreach (var idx in inst.targetMenuPath)
            {
                if (cur?.controls == null || idx < 0 || idx >= cur.controls.Count) { cur = null; break; }
                cur = cur.controls[idx]?.subMenu;
            }
            if (cur != null) return cur;
        }

        // 2) soft ref
        var byGuid = inst.targetMenuAsset.Get<VRCExpressionsMenu>(inst);
        if (byGuid != null) return byGuid;

        // 3) fallback
        return autoEmoteMenu;
    }


    // Action state and merge scope validation


    static bool s_actionValidateStylesInit;
    static GUIStyle s_textFieldRed;
    static GUIStyle s_rightMiniLabel;
    static GUIStyle s_rightMiniLabelRed;

    static void EnsureActionValidateStyles()
    {
        if (s_actionValidateStylesInit) return;
        s_actionValidateStylesInit = true;

        s_textFieldRed = new GUIStyle(EditorStyles.textField);
        s_textFieldRed.normal.textColor = kWarnRed;
        s_textFieldRed.focused.textColor = kWarnRed;
        s_textFieldRed.hover.textColor = kWarnRed;
        s_textFieldRed.active.textColor = kWarnRed;

        s_rightMiniLabel = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleRight,
            clipping = TextClipping.Clip
        };

        s_rightMiniLabelRed = new GUIStyle(s_rightMiniLabel);
        s_rightMiniLabelRed.normal.textColor = kWarnRed;
    }

    // Default / null Action layer assumed names
    private const string kDefaultActionStartState = "Prepare Standing";
    private const string kDefaultActionEndState = "BlendOut Stand";
    private const string kDefaultActionScope = "Action";

    // Match PshaAssetGuidReferenceEditor red tone
    private static readonly Color kWarnRed = new Color(1f, 0.3f, 0.3f, 1f);
#if VRC_SDK_VRCSDK3
    private int CalcActionStateValidationErrorCount(VRCAvatarDescriptor descriptor)
    {
        if (descriptor == null) return 0;

        // SerializedProperty null-safe
        bool showScope = _showActionMergeScopeProp != null && _showActionMergeScopeProp.boolValue;

        string startName = _startActionStateProp?.stringValue ?? string.Empty;
        string endName = _endActionStateProp?.stringValue ?? string.Empty;
        string scopeStr = _actionMergeScopeProp?.stringValue ?? string.Empty;

        int err = 0;

        // Get Action controller
        bool hasActionInfo = TryGetDescriptorActionAnimatorController(descriptor, out var actionCtrl, out bool actionIsDefault);

        bool assumeBuiltinDefault = !hasActionInfo || actionIsDefault || actionCtrl == null;

        // Default or null policy affects scope warnings
        if (assumeBuiltinDefault)
        {
            if (showScope)
            {
                bool scopeMismatch = !string.IsNullOrEmpty(scopeStr)
                                     && !string.Equals(scopeStr, kDefaultActionScope, StringComparison.Ordinal);
                if (scopeMismatch) err++;
            }
            return err;
        }

        // Validate custom Action controller
        if (actionCtrl.layers == null || actionCtrl.layers.Length == 0 || actionCtrl.layers[0].stateMachine == null)
        {

            return err + 1;
        }

        var rootSM = actionCtrl.layers[0].stateMachine;

        // Optional scope
        bool scopeSpecified = showScope && !string.IsNullOrEmpty(scopeStr);
        bool scopeValid = true;
        AnimatorStateMachine searchRoot = rootSM;

        if (scopeSpecified)
            scopeValid = TryFindStateMachineByNameOrPath(rootSM, scopeStr, out searchRoot);

        if (scopeSpecified && !scopeValid) err++; // scope invalid error

        // Start and end states must share the same parent state machine
        AnimatorStateMachine startParent = null;
        AnimatorStateMachine endParent = null;

        bool startFound = !string.IsNullOrEmpty(startName) &&
                          TryFindStateWithParent(searchRoot, startName, out _, out startParent);

        bool endFound = !string.IsNullOrEmpty(endName) &&
                        TryFindStateWithParent(searchRoot, endName, out _, out endParent);

        bool startInvalid = !string.IsNullOrEmpty(startName) && !startFound;
        bool endInvalid = !string.IsNullOrEmpty(endName) && !endFound;
        bool parentMismatch = startFound && endFound && !ReferenceEquals(startParent, endParent);

        if (startInvalid || endInvalid) err++;     // Start/End invalid error
        if (parentMismatch) err++;                 // parent mismatch error

        return err;
    }




    private int CalcActionStateValidationWarningCount(VRCAvatarDescriptor descriptor)
    {
        if (descriptor == null) return 0;

        // SerializedProperty null-safe
        bool showScope = _showActionMergeScopeProp != null && _showActionMergeScopeProp.boolValue;
        string scopeStr = _actionMergeScopeProp?.stringValue ?? string.Empty;

        // Get Action controller
        bool hasActionInfo = TryGetDescriptorActionAnimatorController(descriptor, out var actionCtrl, out bool actionIsDefault);

        bool assumeBuiltinDefault = !hasActionInfo || actionIsDefault || actionCtrl == null;

        // Under default or null policy, these warnings are suppressed
        if (assumeBuiltinDefault) return 0;

        // Warn if root state machine cannot be read
        var rootSM = (actionCtrl.layers != null && actionCtrl.layers.Length > 0) ? actionCtrl.layers[0].stateMachine : null;

        // psha.err_action_layer_missing_root (MessageType.Warning)
        if (rootSM == null) return 1;

        int warn = 0;

        // psha.action_sub_sm_root_empty_warn (MessageType.Warning)
        if (showScope && string.IsNullOrEmpty(scopeStr))
            warn++;

        return warn;
    }
#endif


    // Developer options notice icons

    private struct FoldoutNoticeCounts
    {
        public int error;
        public int warning;
        public int info;

        public int Total => error + warning + info;
    }

    private FoldoutNoticeCounts CalcDeveloperOptionsNotices(PshaVRCEmoteInstaller mgr)
    {
        var n = new FoldoutNoticeCounts();

        {
            var issue = GetIconImportIssue(mgr != null ? mgr.menuIcon : null, out _, out _, out _);
            if (issue != IconImportIssue.None)
                n.error++;
        }

#if VRC_SDK_VRCSDK3
        if (mgr != null && TryGetAvatarDescriptor(mgr, out var desc) && desc != null)
        {
            // same condition as DrawTargetMenuAvatarMismatchWarning
            if (SoftRefHasProblem_TargetMenu(_targetMenuProp, desc))
                n.warning++;

            // same condition as DrawAnimLayerAvatarMismatchWarning
            bool actionBad = SoftRefHasProblem_AnimatorLayer(
                _actionLayerProp, desc, VRCAvatarDescriptor.AnimLayerType.Action);
            bool fxBad = SoftRefHasProblem_AnimatorLayer(
                _fxLayerProp, desc, VRCAvatarDescriptor.AnimLayerType.FX);

            if (actionBad || fxBad)
                n.warning++;

            n.warning += CalcActionStateValidationWarningCount(desc);

            n.error += CalcActionStateValidationErrorCount(desc);
        }
#endif

        // Info message that appears in Advanced Options ("Using multiple FX layers...")
        if (_useMergeMEFxProp != null && _useAdditionalMEFxLayersProp != null
            && _useMergeMEFxProp.boolValue
            && _useAdditionalMEFxLayersProp.boolValue)
        {
            n.info++;
        }

        return n;
    }

    private static bool DrawFoldoutWithNoticeIcons(bool expanded, string label, FoldoutNoticeCounts notice)
    {
        Rect r = GUILayoutUtility.GetRect(0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
        Rect ind = EditorGUI.IndentedRect(r);

        int iconCount =
            (notice.error > 0 ? 1 : 0) +
            (notice.warning > 0 ? 1 : 0) +
            (notice.info > 0 ? 1 : 0);

        float iconSize = ind.height;          // usually ~18
        float pad = 2f;
        float iconsWidth = iconCount > 0 ? (iconCount * iconSize + (iconCount - 1) * pad) : 0f;

        Rect foldoutRect = ind;
        foldoutRect.xMax -= iconsWidth;

        expanded = EditorGUI.Foldout(foldoutRect, expanded, label, true);

        if (iconCount > 0)
        {
            float x = ind.xMax - iconsWidth;

            // order: error -> warning -> info (left to right)
            if (notice.error > 0)
            {
                DrawNoticeIcon(ref x, ind, iconSize, pad, "console.erroricon.sml", $"Error ({notice.error})");
            }
            if (notice.warning > 0)
            {
                DrawNoticeIcon(ref x, ind, iconSize, pad, "console.warnicon.sml", $"Warning ({notice.warning})");
            }
            if (notice.info > 0)
            {
                DrawNoticeIcon(ref x, ind, iconSize, pad, "console.infoicon.sml", $"Info ({notice.info})");
            }
        }

        return expanded;
    }

    private static void DrawNoticeIcon(ref float x, Rect lineRect, float size, float pad, string iconName, string tooltip)
    {
        var gc = EditorGUIUtility.IconContent(iconName);
        if (gc == null || gc.image == null)
        {
            // fallback (some Unity versions differ)
            gc = EditorGUIUtility.IconContent(iconName.Replace(".sml", ""));
        }

        var c = new GUIContent(gc) { tooltip = tooltip };
        var ir = new Rect(x, lineRect.y, size, lineRect.height);
        GUI.Label(ir, c);
        x += size + pad;
    }


    private const int kIconMaxSize = 256;

    private enum IconImportIssue
    {
        None,
        NonAsset,
        TooLarge,
        Uncompressed,
        TooLargeAndUncompressed,
    }

    private static IconImportIssue GetIconImportIssue(Texture2D tex, out TextureImporter importer, out string path, out string reason)
    {
        importer = null;
        path = null;
        reason = null;

        if (tex == null) return IconImportIssue.None;

        path = AssetDatabase.GetAssetPath(tex);
        if (string.IsNullOrEmpty(path))
        {
            reason = Tr("psha.icon_issue_non_asset", "Icon is not an asset reference");
            return IconImportIssue.NonAsset;
        }

        importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return IconImportIssue.None;

        bool sizeBad = importer.maxTextureSize > kIconMaxSize;

        bool compressionBad = false;
        try
        {
            compressionBad = importer.textureCompression == TextureImporterCompression.Uncompressed;
        }
        catch
        {
            compressionBad = false;
        }

        if (sizeBad && compressionBad)
        {
            reason = Trf("psha.icon_issue_max_size_uncompressed", "Max Size {0}, Compression Uncompressed", importer.maxTextureSize);
            return IconImportIssue.TooLargeAndUncompressed;
        }

        if (sizeBad)
        {
            reason = Trf("psha.icon_issue_max_size", "Max Size {0}", importer.maxTextureSize);
            return IconImportIssue.TooLarge;
        }

        if (compressionBad)
        {
            reason = Tr("psha.icon_issue_uncompressed", "Compression Uncompressed");
            return IconImportIssue.Uncompressed;
        }

        return IconImportIssue.None;
    }

    private static void FixIconImport(TextureImporter importer)
    {
        if (importer == null) return;

        if (importer.maxTextureSize > kIconMaxSize)
            importer.maxTextureSize = kIconMaxSize;

        importer.textureCompression = TextureImporterCompression.Compressed;

        if (importer.mipmapEnabled)
            importer.mipmapEnabled = false;

        importer.SaveAndReimport();
    }

    private void DrawMenuIconBuildErrorUI(PshaVRCEmoteInstaller mgr, SerializedProperty menuIconProp)
    {
        if (mgr == null) return;

        var tex = mgr.menuIcon;
        var issue = GetIconImportIssue(tex, out var importer, out var path, out var reason);
        if (issue == IconImportIssue.None) return;

        string msg;
        if (issue == IconImportIssue.NonAsset)
            msg = Tr("psha.err_menu_icon_non_asset", "Menu Icon will not be included in build.\nAssign a Texture2D asset.\n");
        else
            msg = Trf("psha.err_menu_icon_will_fail_validation", "Menu Icon import settings will fail VRC validation. ({0})\n", reason);

        if (!string.IsNullOrEmpty(path))
            msg += path;

        EditorGUILayout.HelpBox(msg, MessageType.Error);

        if (issue != IconImportIssue.NonAsset && importer != null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(Tr("psha.btn_fix_icon_settings", "Fix Icon Settings (256, Compressed)"), GUILayout.Width(220)))
                {
                    FixIconImport(importer);
                    if (menuIconProp != null) menuIconProp.serializedObject.Update();
                }
            }
        }
    }


    private void DrawActionStateSettingsValidated(PshaVRCEmoteInstaller mgr)
    {
        EnsureActionValidateStyles();

        EditorGUILayout.LabelField(Tr("psha.state_settings", "State Settings"), EditorStyles.miniBoldLabel);

        var descriptor = mgr != null ? mgr.GetComponentInParent<VRCAvatarDescriptor>() : null;

        // Disable input outside the avatar hierarchy
        if (descriptor == null)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(
                    _startActionStateProp,
                    GC("psha.start_action_state", "Start Action State", "psha.tt.start_action_state",
                        "State name where the emote branch starts in the Action layer.")
                );

                EditorGUILayout.PropertyField(
                    _endActionStateProp,
                    GC("psha.end_action_state", "End Action State", "psha.tt.end_action_state",
                        "State name where the emote branch ends in the Action layer.")
                );

                if (_showActionMergeScopeProp.boolValue)
                {
                    EditorGUILayout.PropertyField(
                        _actionMergeScopeProp,
                        GC("psha.action_sub_sm_root", "Action SM Root", "psha.tt.action_sub_sm_root",
                            "Sub state machine name/path used as the merge scope.")
                    );
                }
            }
            EditorGUILayout.Space(2);
            return;
        }

        // Cache current input values
        bool showScope = _showActionMergeScopeProp.boolValue;
        string startName = _startActionStateProp.stringValue ?? string.Empty;
        string endName = _endActionStateProp.stringValue ?? string.Empty;
        string scopeStr = _actionMergeScopeProp.stringValue ?? string.Empty;

        // Get Action controller
        bool hasActionInfo = TryGetDescriptorActionAnimatorController(descriptor, out var actionCtrl, out bool actionIsDefault);

        // Default or null is treated as mismatch
        bool assumeBuiltinDefault = !hasActionInfo || actionIsDefault || actionCtrl == null;
        if (assumeBuiltinDefault)
        {
            bool startMismatch = !string.Equals(startName, kDefaultActionStartState, StringComparison.Ordinal);
            bool endMismatch = !string.Equals(endName, kDefaultActionEndState, StringComparison.Ordinal);
            bool scopeMismatch = showScope && !string.Equals(scopeStr, kDefaultActionScope, StringComparison.Ordinal);

            DrawValidatedStringField(
                _startActionStateProp,
                GC("psha.start_action_state", "Start Action State", "psha.tt.start_action_state",
                    "State name where the emote branch starts in the Action layer."),
                invalid: startMismatch,
                rightHint: null
            );

            DrawValidatedStringField(
                _endActionStateProp,
                GC("psha.end_action_state", "End Action State", "psha.tt.end_action_state",
                    "State name where the emote branch ends in the Action layer."),
                invalid: endMismatch,
                rightHint: null
            );

            if (showScope)
            {
                DrawValidatedStringField(
                    _actionMergeScopeProp,
                    GC("psha.action_sub_sm_root", "Action SM Root", "psha.tt.action_sub_sm_root",
                        "Sub state machine name/path used as the merge scope."),
                    invalid: scopeMismatch,
                    rightHint: null
                );

                // Keep empty value warning
                if (string.IsNullOrEmpty(scopeStr))
                {
                    EditorGUILayout.HelpBox(
                        Tr("psha.action_sub_sm_root_empty_warn",
                            "Action Sub StateMachine Root is empty.\nUse 'Setup VRC Emote' or enter the Action Sub StateMachine Root."),
                        MessageType.Warning
                    );
                    EditorGUILayout.Space(4);
                }
                else if (scopeMismatch)
                {
                    // Do not show not found text
                    EditorGUILayout.HelpBox(
                        Tr("psha.action_sub_sm_root_invalid_err_default",
                            "For Default/empty Action layer, Action SM Root is assumed to be 'Action'."),
                        MessageType.Error
                    );
                }
            }

            EditorGUILayout.Space(2);
            return;
        }


        var rootSM = (actionCtrl.layers != null && actionCtrl.layers.Length > 0) ? actionCtrl.layers[0].stateMachine : null;
        if (rootSM == null)
        {
            // If root state machine is unreadable, show warning
            EditorGUILayout.PropertyField(_startActionStateProp, GC("psha.start_action_state", "Start Action State"));
            EditorGUILayout.PropertyField(_endActionStateProp, GC("psha.end_action_state", "End Action State"));
            if (showScope) EditorGUILayout.PropertyField(_actionMergeScopeProp, GC("psha.action_sub_sm_root", "Action SM Root"));

            EditorGUILayout.HelpBox(
                Tr("psha.err_action_layer_missing_root",
                    "Could not read Action layer root StateMachine from the descriptor controller."),
                MessageType.Warning
            );
            return;
        }

        // Optional scope
        bool scopeSpecified = showScope && !string.IsNullOrEmpty(scopeStr);
        bool scopeValid = true;
        AnimatorStateMachine searchRoot = rootSM;

        if (scopeSpecified)
            scopeValid = TryFindStateMachineByNameOrPath(rootSM, scopeStr, out searchRoot);

        // States must be inside scope and share the same parent state machine
        AnimatorStateMachine startParent = null;
        AnimatorStateMachine endParent = null;

        bool startFound = !string.IsNullOrEmpty(startName) &&
                          TryFindStateWithParent(searchRoot, startName, out _, out startParent);

        bool endFound = !string.IsNullOrEmpty(endName) &&
                        TryFindStateWithParent(searchRoot, endName, out _, out endParent);

        bool startInvalid = !string.IsNullOrEmpty(startName) && !startFound;
        bool endInvalid = !string.IsNullOrEmpty(endName) && !endFound;
        bool parentMismatch = startFound && endFound && !ReferenceEquals(startParent, endParent);

        DrawValidatedStringField(
            _startActionStateProp,
            GC("psha.start_action_state", "Start Action State", "psha.tt.start_action_state",
                "State name where the emote branch starts in the Action layer."),
            invalid: startInvalid || parentMismatch,
            rightHint: parentMismatch ? Tr("psha.hint_parent_mismatch", "Different parent") : null
        );

        DrawValidatedStringField(
            _endActionStateProp,
            GC("psha.end_action_state", "End Action State", "psha.tt.end_action_state",
                "State name where the emote branch ends in the Action layer."),
            invalid: endInvalid || parentMismatch,
            rightHint: parentMismatch ? Tr("psha.hint_parent_mismatch", "Different parent") : null
        );

        if (showScope)
        {
            DrawValidatedStringField(
                _actionMergeScopeProp,
                GC("psha.action_sub_sm_root", "Action SM Root", "psha.tt.action_sub_sm_root",
                    "Sub state machine name/path used as the merge scope."),
                invalid: scopeSpecified && !scopeValid,
                rightHint: null // Do not show scope not found text
            );

            if (string.IsNullOrEmpty(scopeStr))
            {
                EditorGUILayout.HelpBox(
                    Tr("psha.action_sub_sm_root_empty_warn",
                        "Action Sub StateMachine Root is empty.\nUse 'Setup VRC Emote' or enter the Action Sub StateMachine Root."),
                    MessageType.Warning
                );
                EditorGUILayout.Space(4);
            }
            else if (scopeSpecified && !scopeValid)
            {
                EditorGUILayout.HelpBox(
                    Tr("psha.action_sub_sm_root_invalid_err",
                        "The specified Action SM Root is invalid for the current Action controller.\n" +
                        "Please press 'Setup VRC Emote' or set a valid Action SM root."),
                    MessageType.Error
                );
            }
        }

        if (startInvalid || endInvalid)
        {
            // Use invalid state without not found text
            EditorGUILayout.HelpBox(
                Tr("psha.err_action_states_invalid",
                    "Start/End Action State values are invalid for the current Action controller/scope."),
                MessageType.Error
            );
        }

        if (parentMismatch)
        {
            EditorGUILayout.HelpBox(
                Tr("psha.err_start_end_parent_mismatch",
                    "Start/End Action States must be under the same parent StateMachine (within the selected scope)."),
                MessageType.Error
            );
        }

        EditorGUILayout.Space(2);
    }


    static void DrawValidatedStringField(SerializedProperty prop, GUIContent label, bool invalid, string rightHint)
    {
        EnsureActionValidateStyles();

        var rect = EditorGUILayout.GetControlRect(true);
        EditorGUI.BeginProperty(rect, label, prop);

        // Let PrefixLabel handle indent and width; draw only the value field
        var fieldRect = EditorGUI.PrefixLabel(rect, label);

        string current = prop.stringValue ?? string.Empty;
        var style = invalid ? s_textFieldRed : EditorStyles.textField;

        EditorGUI.BeginChangeCheck();
        string next = EditorGUI.DelayedTextField(fieldRect, GUIContent.none, current, style);
        if (EditorGUI.EndChangeCheck())
            prop.stringValue = next;

        // Draw the right side hint inside the text field
        if (!string.IsNullOrEmpty(rightHint) && Event.current.type == EventType.Repaint)
        {
            var hintRect = new Rect(fieldRect.x + 6, fieldRect.y, fieldRect.width - 12, fieldRect.height);
            var hintStyle = invalid ? s_rightMiniLabelRed : s_rightMiniLabel;
            hintStyle.Draw(hintRect, new GUIContent(rightHint), false, false, false, false);
        }

        EditorGUI.EndProperty();
    }

    static bool TryGetDescriptorActionAnimatorController(
        VRCAvatarDescriptor descriptor,
        out AnimatorController controller,
        out bool isDefault)
    {
        controller = null;
        isDefault = false;

        if (descriptor == null) return false;

        var layers = descriptor.baseAnimationLayers;
        if (layers == null) return false;

        foreach (var l in layers)
        {
            if (l.type != VRCAvatarDescriptor.AnimLayerType.Action) continue;

            isDefault = l.isDefault;

            if (l.animatorController == null)
                return true;

            if (l.animatorController is AnimatorOverrideController aoc)
            {
                controller = aoc.runtimeAnimatorController as AnimatorController;
                return true;
            }

            controller = l.animatorController as AnimatorController;
            return true;
        }

        return false;
    }

    static bool TryFindStateMachineByNameOrPath(AnimatorStateMachine root, string nameOrPath, out AnimatorStateMachine found)
    {
        found = null;
        if (root == null || string.IsNullOrEmpty(nameOrPath)) return false;

        // Path is relative to the root
        if (nameOrPath.IndexOf('/') >= 0 || nameOrPath.IndexOf('\\') >= 0)
        {
            var parts = nameOrPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            var cur = root;

            foreach (var p in parts)
            {
                bool ok = false;
                foreach (var child in cur.stateMachines)
                {
                    var sm = child.stateMachine;
                    if (sm != null && sm.name == p)
                    {
                        cur = sm;
                        ok = true;
                        break;
                    }
                }
                if (!ok) return false;
            }

            found = cur;
            return true;
        }

        // Depth first search first match
        var stack = new Stack<AnimatorStateMachine>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var sm = stack.Pop();
            if (sm == null) continue;

            if (sm.name == nameOrPath)
            {
                found = sm;
                return true;
            }

            foreach (var child in sm.stateMachines)
                if (child.stateMachine != null) stack.Push(child.stateMachine);
        }

        return false;
    }

    static bool TryFindStateWithParent(
        AnimatorStateMachine root,
        string stateName,
        out AnimatorState foundState,
        out AnimatorStateMachine parent)
    {
        foundState = null;
        parent = null;
        if (root == null || string.IsNullOrEmpty(stateName)) return false;

        var stack = new Stack<AnimatorStateMachine>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var sm = stack.Pop();
            if (sm == null) continue;

            foreach (var child in sm.states)
            {
                var st = child.state;
                if (st != null && st.name == stateName)
                {
                    foundState = st;
                    parent = sm;
                    return true;
                }
            }

            foreach (var childSm in sm.stateMachines)
                if (childSm.stateMachine != null) stack.Push(childSm.stateMachine);
        }

        return false;
    }


    private static void DrawEditorLanguageSection()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

        using (new EditorGUILayout.VerticalScope("box"))
        using (new GuiModeScope(CalcStableLabelWidth(), wideMode: true))
        {

            Rect row = EditorGUILayout.GetControlRect();
            Rect labelRect = row;
            labelRect.width = EditorGUIUtility.labelWidth;

            Rect buttonRect = row;
            buttonRect.xMin = labelRect.xMax;

            EditorGUI.LabelField(labelRect, "Editor Language");

            string systemCode = GetSystemLanguageCode();
            bool useSystem = GetUseSystemLanguage();

            string savedCode = GetSavedLanguageCode();
            bool sameAsSystem = string.Equals(savedCode, systemCode, StringComparison.OrdinalIgnoreCase);

            bool autoChecked = useSystem || sameAsSystem;

            string effectiveCode = useSystem ? systemCode : savedCode;
            string effectiveDisplay = CodeToDisplay(effectiveCode);

            if (GUI.Button(buttonRect, effectiveDisplay, EditorStyles.popup))
            {
                var menu = new GenericMenu();


                for (int i = 0; i < s_langItems.Length; i++)
                {
                    var item = s_langItems[i];
                    bool on = string.Equals(item.Code, effectiveCode, StringComparison.OrdinalIgnoreCase);

                    menu.AddItem(new GUIContent(item.Display), on, () =>
                    {
                        SetSavedLanguageCode(item.Code);



                        bool sameAsSystem = string.Equals(item.Code, systemCode, StringComparison.OrdinalIgnoreCase);
                        SetUseSystemLanguage(sameAsSystem);
                    });
                }


                menu.AddSeparator("");


                menu.AddItem(new GUIContent("Use System Language (Auto)"), autoChecked, () =>
                {
                    bool now = GetUseSystemLanguage();
                    SetUseSystemLanguage(!now);
                });


                menu.DropDown(buttonRect);
            }
        }
    }
}
#endif
