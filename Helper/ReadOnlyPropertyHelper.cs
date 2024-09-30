using System;
using UnityEditor;
using UnityEngine;

namespace JeTeeS.MemoryOptimizer.Helper
{
    internal static class ReadOnlyPropertyHelper
    {
        [AttributeUsage(AttributeTargets.Field)]
        internal class ReadOnlyAttribute : PropertyAttribute { }
        
#if UNITY_EDITOR
        [CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
        internal class ReadOnlyAttributeDrawer : PropertyDrawer
        {
            public override void OnGUI(Rect rect, SerializedProperty prop, GUIContent label)
            {
                bool wasEnabled = GUI.enabled;
                GUI.enabled = false;
                EditorGUI.PropertyField(rect, prop);
                GUI.enabled = wasEnabled;
            }
            
            public override float GetPropertyHeight(SerializedProperty prop, GUIContent label)
            {
                return EditorGUI.GetPropertyHeight(prop, label, true);
            }
        }
#endif
    }
}