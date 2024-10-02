using System;
using System.Linq;
using JeTeeS.MemoryOptimizer.Patcher;
using JeTeeS.TES.HelperFunctions;
using UnityEditor;
using UnityEngine;
using static JeTeeS.MemoryOptimizer.Shared.MemoryOptimizerConstants;

namespace JeTeeS.MemoryOptimizer
{
    [CustomEditor(typeof(MemoryOptimizerComponent))]
    internal class MemoryOptimizerComponentEditor : Editor
    {
        private MemoryOptimizerComponent _component;
        
        private void Awake()
        {
            _component ??= (MemoryOptimizerComponent)target;
            
            _component.Load();
        }

        public override void OnInspectorGUI()
        {
            bool hasErrors = _component.componentIssues.Any(x => x.level >= 3);
            
#if MemoryOptimizer_VRCFury_IsInstalled
            if (MemoryOptimizerVRCFuryPatcher.AreVRCFuryScriptsPatched())
            {
                EditorGUILayout.HelpBox("VRCFury is patched already!", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("VRCFury requires to be patched in order for this to correctly work when uploading.\nPatching will be done automatically during upload, however it is recommended todo this before.", MessageType.Warning);
                using (new MemoryOptimizerWindow.SqueezeScope((0, 0, MemoryOptimizerWindow.SqueezeScope.SqueezeScopeType.Horizontal, EditorStyles.helpBox)))
                {
                    if (GUILayout.Button("Patch VRCFury") && EditorUtility.DisplayDialog("Patch VRCFury?", "This will patch some VRCFury files to disable upload check hooks for parameters as we preprocess after, this will cause a script rebuild.\nContinue?", "Yes, Patch!", "No."))
                    {
                        MemoryOptimizerVRCFuryPatcher.PatchVRCFuryScripts();
                    }
                }
            }
            
            GUILayout.Space(5);
#endif
            if (_component.componentIssues.Any())
            {
                using (new MemoryOptimizerWindow.SqueezeScope((0, 0, MemoryOptimizerWindow.SqueezeScope.SqueezeScopeType.Horizontal, EditorStyles.helpBox)))
                {
                    GUILayout.Label("Problems:");
                }

                foreach (var issue in _component.componentIssues)
                {
                    var type = issue.level switch
                    {
                        0 => MessageType.None,
                        1 => MessageType.Info,
                        2 => MessageType.Warning,
                        3 => MessageType.Error,
                        _ => MessageType.None
                    };
                    
                    EditorGUILayout.HelpBox(issue.message, type);
                }
                
                GUILayout.Space(5);
            }
            
            using (new MemoryOptimizerWindow.SqueezeScope((0, 0, MemoryOptimizerWindow.SqueezeScope.SqueezeScopeType.Horizontal, EditorStyles.helpBox)))
            {
                GUI.enabled = !hasErrors;
                
                if (GUILayout.Button("Configure"))
                {
                    _component.Load();
                
                    MemoryOptimizerWindow._component = _component;
                    EditorWindow window = EditorWindow.GetWindow(typeof(MemoryOptimizerWindow), false, "Memory Optimizer (Configuration)", true);
                    window.minSize = new Vector2(600, 900);
                }

                GUI.enabled = true;
            }

            var foldoutState = EditorGUILayout.Foldout(EditorPrefs.GetBool(EditorKeyInspectComponent), "Component Configuration");
            EditorPrefs.SetBool(EditorKeyInspectComponent, foldoutState);
            if (foldoutState)
            {
                GUI.enabled = false;

                using (new MemoryOptimizerWindow.SqueezeScope((0, 0, MemoryOptimizerWindow.SqueezeScope.SqueezeScopeType.Horizontal, EditorStyles.helpBox)))
                {
                    GUILayout.Label("Write Defaults:");
                    GUILayout.Label($"{wdOptions[_component.wdOption]}", GUILayout.Width(100));
                }
                
                using (new MemoryOptimizerWindow.SqueezeScope((0, 0, MemoryOptimizerWindow.SqueezeScope.SqueezeScopeType.Horizontal, EditorStyles.helpBox)))
                {
                    GUILayout.Label("Change Detection");

                    GUI.backgroundColor = _component.changeDetection ? Color.green : Color.red;
                    GUILayout.Button(_component.changeDetection ? "On" : "Off", GUILayout.Width(100));
                    GUI.backgroundColor = Color.white;
                }
                
                using (new MemoryOptimizerWindow.SqueezeScope((0, 0, MemoryOptimizerWindow.SqueezeScope.SqueezeScopeType.Horizontal, EditorStyles.helpBox)))
                {
                    GUILayout.Label("Sync Steps:");
                    GUILayout.Label($"{_component.syncSteps}", GUILayout.Width(100));
                }
            
                using (new MemoryOptimizerWindow.SqueezeScope((0, 0, MemoryOptimizerWindow.SqueezeScope.SqueezeScopeType.Horizontal, EditorStyles.helpBox)))
                {
                    GUILayout.Label("Step Delay:");
                    GUILayout.Label($"{_component.stepDelay}s", GUILayout.Width(100));
                }
                
                GUI.enabled = true;
                
                foldoutState = EditorGUILayout.Foldout(EditorPrefs.GetBool(EditorKeyInspectParameters), "Parameter Configurations");
                EditorPrefs.SetBool(EditorKeyInspectParameters, foldoutState);
                if (foldoutState)
                {
                    foreach (var savedParameterConfiguration in _component.savedParameterConfigurations)
                    {
                        var (parameterName, parameterType, isSelected, willOptimize) = (savedParameterConfiguration.param.name, savedParameterConfiguration.param.valueType, savedParameterConfiguration.selected, savedParameterConfiguration.willBeOptimized);

                        using (new MemoryOptimizerWindow.SqueezeScope((0, 0, MemoryOptimizerWindow.SqueezeScope.SqueezeScopeType.Horizontal)))
                        {
                            GUILayout.Space(5);

                            EditorGUILayout.HelpBox($"{parameterName} - {paramTypes[(int)parameterType]}\n -> {savedParameterConfiguration.info}", MessageType.None);
                        
                            GUI.backgroundColor = isSelected ? (willOptimize ? Color.green : Color.yellow) : Color.red;
                            GUI.enabled = false;
                            GUILayout.Button($"{(isSelected ? (willOptimize ? "Will optimize." : "Can't be optimized." ) : "Won't be optimized.")}", GUILayout.Width(203));
                            GUI.enabled = true;
                            GUI.backgroundColor = Color.white;
                        }
                    }
                }
            }
        }
    }
}