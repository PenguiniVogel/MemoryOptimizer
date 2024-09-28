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