using JeTeeS.MemoryOptimizer.Patcher;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace JeTeeS.MemoryOptimizer.Pipeline
{
    internal class MemoryOptimizerDisableVRCFuryParameterCheck : IVRCSDKPreprocessAvatarCallback
    {
        // run before VRCFury does
        public int callbackOrder => -20000;
        
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            return MemoryOptimizerVRCFuryPatcher.PatchVRCFuryScripts();
        }
    }
}
