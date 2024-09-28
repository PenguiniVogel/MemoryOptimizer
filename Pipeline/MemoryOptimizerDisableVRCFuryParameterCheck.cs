#if UNITY_EDITOR

using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace JeTeeS.MemoryOptimizer
{
    public class MemoryOptimizerDisableVRCFuryParameterCheck : IVRCSDKPreprocessAvatarCallback
    {
        // run before VRCFury does
        public int callbackOrder => -20000;
        
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
#if VRCFury_Installed
#if !VRCFury_Tested
            Debug.LogWarning($"The pipeline is running on an untested version of VRCFury, probably fine though.");
#endif
            if (AssetDatabase.LoadAssetAtPath<MonoScript>("Packages/com.vrcfury.vrcfury/Editor/VF/Service/FinalValidationService.cs") is MonoScript scriptToPatch)
            {
                if (AssetDatabase.LoadAssetAtPath<TextAsset>("Packages/dev.jetees.memoryoptimizer/Patcher/VRCFury-FinalValidationService.txt") is TextAsset patch)
                {
                    File.WriteAllText(AssetDatabase.GetAssetPath(scriptToPatch), patch.text);
                    EditorUtility.SetDirty(scriptToPatch);
                }
            }
#endif
            
            return true;
        }
    }
}

#endif
