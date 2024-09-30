using System;
using JeTeeS.MemoryOptimizer.Patcher;
using JeTeeS.TES.HelperFunctions;
using UnityEditor;
using UnityEngine;

namespace JeTeeS.MemoryOptimizer
{
    [CustomEditor(typeof(MemoryOptimizerComponent))]
    internal class MemoryOptimizerComponentEditor : Editor
    {
        private const string EditorKeyInspectComponent = "dev.jetees.memoryoptimizer_inspectcomponent";
        private const string EditorKeyInspectParameters = "dev.jetees.memoryoptimizer_inspectparameters";
        
        public override void OnInspectorGUI()
        {
            var typed = ((MemoryOptimizerComponent)target);
            
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
            using (new MemoryOptimizerWindow.SqueezeScope((0, 0, MemoryOptimizerWindow.SqueezeScope.SqueezeScopeType.Horizontal, EditorStyles.helpBox)))
            {
                if (GUILayout.Button("Configure"))
                {
                    typed.Load();
                
                    MemoryOptimizerWindow._component = typed;
                    EditorWindow window = EditorWindow.GetWindow(typeof(MemoryOptimizerWindow), false, "Memory Optimizer (Configuration)", true);
                    window.minSize = new Vector2(600, 900);
                }
            }

            var foldoutState = EditorGUILayout.Foldout(EditorPrefs.GetBool(EditorKeyInspectComponent), "Component Configuration");
            EditorPrefs.SetBool(EditorKeyInspectComponent, foldoutState);
            if (foldoutState)
            {
                GUI.enabled = false;

                using (new MemoryOptimizerWindow.SqueezeScope((0, 0, MemoryOptimizerWindow.SqueezeScope.SqueezeScopeType.Horizontal, EditorStyles.helpBox)))
                {
                    GUILayout.Label("Write Defaults:");
                    GUILayout.Label($"{MemoryOptimizerWindow.wdOptions[typed.wdOption]}", GUILayout.Width(100));
                }
                
                using (new MemoryOptimizerWindow.SqueezeScope((0, 0, MemoryOptimizerWindow.SqueezeScope.SqueezeScopeType.Horizontal, EditorStyles.helpBox)))
                {
                    GUILayout.Label("Change Detection");

                    GUI.backgroundColor = typed.changeDetection ? Color.green : Color.red;
                    GUILayout.Button(typed.changeDetection ? "On" : "Off", GUILayout.Width(100));
                    GUI.backgroundColor = Color.white;
                }
                
                using (new MemoryOptimizerWindow.SqueezeScope((0, 0, MemoryOptimizerWindow.SqueezeScope.SqueezeScopeType.Horizontal, EditorStyles.helpBox)))
                {
                    GUILayout.Label("Sync Steps:");
                    GUILayout.Label($"{typed.syncSteps}", GUILayout.Width(100));
                }
            
                using (new MemoryOptimizerWindow.SqueezeScope((0, 0, MemoryOptimizerWindow.SqueezeScope.SqueezeScopeType.Horizontal, EditorStyles.helpBox)))
                {
                    GUILayout.Label("Step Delay:");
                    GUILayout.Label($"{typed.stepDelay}s", GUILayout.Width(100));
                }

                using (new MemoryOptimizerWindow.SqueezeScope((0, 0, MemoryOptimizerWindow.SqueezeScope.SqueezeScopeType.Horizontal, EditorStyles.helpBox)))
                {
                    GUILayout.Label("Save Path Override:");
                    GUILayout.Label($"{typed.savePathOverride?.ToString() ?? "None"}", GUILayout.Width(100));
                }
                
                GUI.enabled = true;
                
                foldoutState = EditorGUILayout.Foldout(EditorPrefs.GetBool(EditorKeyInspectParameters), "Parameter Configurations");
                EditorPrefs.SetBool(EditorKeyInspectParameters, foldoutState);
                if (foldoutState)
                {
                    foreach (var savedParameterConfiguration in typed.savedParameterConfigurations)
                    {
                        var (parameterName, parameterType, isSelected, willOptimize) = (savedParameterConfiguration.param.name, savedParameterConfiguration.param.valueType.TranslateParameterValueType(), savedParameterConfiguration.selected, savedParameterConfiguration.willBeOptimized);

                        using (new MemoryOptimizerWindow.SqueezeScope((0, 0, MemoryOptimizerWindow.SqueezeScope.SqueezeScopeType.Horizontal)))
                        {
                            GUILayout.Space(5);
                        
                            GUILayout.Label(new GUIContent(parameterName, savedParameterConfiguration.info), GUILayout.MinWidth(typed.longestParameterName * 8));
                            GUILayout.Label($"{parameterType}", GUILayout.Width(50));
                        
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