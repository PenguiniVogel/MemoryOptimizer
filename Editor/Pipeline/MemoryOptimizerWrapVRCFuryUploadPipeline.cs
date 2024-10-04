using System;
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

                var status = false;
                using (new PreprocessAvatarMemoryOptimizerScope(avatarGameObject))
                {
                    Debug.Log("<color=yellow>[MemoryOptimizer]</color> Hello from wrapped VRCFury Upload Pipeline!");
                    
                    // run VRCFury
                    status = _wrapped.OnPreprocessAvatar(avatarGameObject);
                }

                return status;
            }

            internal class PreprocessAvatarMemoryOptimizerScope : IDisposable
            {
                private readonly MemoryOptimizerComponent _component;
                private Type _validationService;
                private FieldInfo _validationServiceField;
                
                public PreprocessAvatarMemoryOptimizerScope(GameObject avatarObject)
                {
                    if (avatarObject?.GetComponent<MemoryOptimizerComponent>() is { } component)
                    {
                        _component = component;
                    }
                    
                    if (_component is not null)
                    {
                        Init();
                    }
                }

                private void Init()
                {
                    try
                    {
                        if (!MemoryOptimizerVRCFuryPatcher.AreVRCFuryScriptsPatched())
                        {
                            MemoryOptimizerVRCFuryPatcher.PatchVRCFuryScripts();
                        }
                        
                        _validationService = ReflectionHelper.FindTypeInAssemblies("VF.Service.FinalValidationService");
                        _validationServiceField = _validationService?.GetField("_checkDisabledByMemoryOptimizer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        
                        _validationServiceField?.SetValue(null, true);
                    }
                    catch (Exception e)
                    {
                        Debug.Log($"<color=yellow>[MemoryOptimizer]</color> Setting the VF.Service.FinalValidationService flag resulted in an error: {e}");
                    }
                }
                
                public void Dispose()
                {
                    _validationServiceField?.SetValue(null, false);
                }
            }
        }
    }
}
