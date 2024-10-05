using System.Collections.Generic;
using System.Reflection;
using JeTeeS.MemoryOptimizer.Helper;
using JeTeeS.MemoryOptimizer.Patcher;
using UnityEditor.Callbacks;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace JeTeeS.MemoryOptimizer.Pipeline
{
    internal static class MemoryOptimizerWrapVRCFuryUploadPipeline
    {
        [DidReloadScripts]
        public static void FindAndWrap()
        {
#if MemoryOptimizer_VRCFury_IsInstalled
            var pipelineCallbacks = ReflectionHelper.FindTypeInAssemblies("VRC.SDKBase.Editor.BuildPipeline.VRCBuildPipelineCallbacks");
            var pipelineCallbacksField = pipelineCallbacks?.GetField("_preprocessAvatarCallbacks", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var pipelineCallbacksValues = pipelineCallbacksField?.GetValue(null);
            if (pipelineCallbacksValues is List<IVRCSDKPreprocessAvatarCallback> callbacks)
            {
                foreach (var callback in callbacks.ToArray())
                {
                    var callbackType = callback.GetType();
                    var callbackTypeName = callbackType.Name;
                    var callbackAssembly = string.IsNullOrEmpty(callbackType.AssemblyQualifiedName) ? "" : callbackType.AssemblyQualifiedName;

                    if (callbackTypeName.Equals("VrcPreuploadHook") && callbackAssembly.Contains("VF.Hook"))
                    {
                        var wrap = new MemoryOptimizerVRCFuryUploadPipelineWrapper(callback);
                        callbacks.Remove(callback);
                        callbacks.Add(wrap);

                        Debug.Log($"<color=yellow>[MemoryOptimizer]</color> wrapped the VRCFury Upload Pipeline! ({callbackAssembly} -> {wrap.GetType().AssemblyQualifiedName})");
                        
                        break;
                    }
                }
                
                pipelineCallbacksField.SetValue(null, callbacks);
            }
#endif
        }
        
        internal class MemoryOptimizerVRCFuryUploadPipelineWrapper : IVRCSDKPreprocessAvatarCallback
        {
            private readonly IVRCSDKPreprocessAvatarCallback _wrapped;
            
            public int callbackOrder => _wrapped?.callbackOrder ?? 0;

            public MemoryOptimizerVRCFuryUploadPipelineWrapper()
            {
                _wrapped = null;
            }
            
            public MemoryOptimizerVRCFuryUploadPipelineWrapper(IVRCSDKPreprocessAvatarCallback wrap)
            {
                _wrapped = wrap;
            }
            
            public bool OnPreprocessAvatar(GameObject avatarGameObject)
            {
                if (_wrapped is null) return true;

                MemoryOptimizerVRCFuryPatcher.FindAndPatch();

                return _wrapped.OnPreprocessAvatar(avatarGameObject);
            }
        }
    }
}
