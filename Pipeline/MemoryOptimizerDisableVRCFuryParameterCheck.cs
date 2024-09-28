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
            return MemoryOptimizerVRCFuryPatcher.PatchVRCFuryScripts();
        }
    }
}

#endif
