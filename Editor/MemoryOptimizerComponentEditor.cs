using System;
using UnityEditor;
using UnityEngine;

namespace JeTeeS.MemoryOptimizer
{
    [CustomEditor(typeof(MemoryOptimizerComponent))]
    public class MemoryOptimizerComponentEditor : Editor
    {
        private void OnEnable()
        {
            ((MemoryOptimizerComponent)target).Load();
        }

        public override void OnInspectorGUI()
        {
#if VRCFury_Installed
            if (MemoryOptimizerVRCFuryPatcher.AreVRCFuryScriptsPatched())
            {
                GUILayout.Label("VRCFury is patched already!");
            }
            else
            {
                GUILayout.Label("VRCFury requires to be patched in order for this to correctly work when uploading.");
                GUILayout.Label("Patching will be done automatically during upload, however it is recommended todo this before.");
                if (GUILayout.Button("Patch VRCFury") && EditorUtility.DisplayDialog("Patch VRCFury?", "This will patch some VRCFury files to disable upload check hooks for parameters as we preprocess after, this will cause a script rebuild.\nContinue?", "Yes, Patch!", "No."))
                {
                    MemoryOptimizerVRCFuryPatcher.PatchVRCFuryScripts();
                }
            }
            
            GUILayout.Space(5);
#endif
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(MemoryOptimizerComponent.syncSteps)));
            
            GUILayout.Space(5);

            var syncSteps = serializedObject.FindProperty(nameof(MemoryOptimizerComponent.syncSteps));
            var disableChangeDetection = false;
            if (syncSteps.intValue <= 2)
            {
                disableChangeDetection = true;
                syncSteps.intValue = Math.Max(0, syncSteps.intValue);
            }

            if (syncSteps.intValue > 32)
            {
                syncSteps.intValue = 32;
            }
            
            EditorGUI.BeginDisabledGroup(disableChangeDetection);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(MemoryOptimizerComponent.changeDetection)));
            EditorGUI.EndDisabledGroup();
            
            GUILayout.Space(5);
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(MemoryOptimizerComponent.savedParameterConfigurations)));
            
            GUILayout.Space(5);
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(MemoryOptimizerComponent.exclusions)));
            
            serializedObject.ApplyModifiedProperties();

            EditorGUI.BeginDisabledGroup(true);
            
            GUILayout.Space(5);
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(MemoryOptimizerComponent.totalParameterCost)));
            
            GUILayout.Space(5);
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(MemoryOptimizerComponent.optimizedParameterCost)));
            
            EditorGUI.EndDisabledGroup();
            
            ((MemoryOptimizerComponent)target).Refresh();
        }
    }
}