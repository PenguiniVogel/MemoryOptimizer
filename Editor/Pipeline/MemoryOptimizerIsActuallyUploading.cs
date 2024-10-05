using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace JeTeeS.MemoryOptimizer.Pipeline
{
    internal class MemoryOptimizerIsActuallyUploading : IVRCSDKPreprocessAvatarCallback
    {
        private static bool _isActuallyUploading = false;
        
        public int callbackOrder => int.MinValue;
        
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            EditorApplication.delayCall += () => _isActuallyUploading = false;

            _isActuallyUploading = IsActuallyUploadingCheck();
            
            return true;
        }

        private static bool IsActuallyUploadingCheck()
        {
            if (Application.isPlaying)
            {
                return false;
            }
            
            var stack = new StackTrace().GetFrames();
            
            if (stack is null)
            {
                return true;
            }
            
            var preprocessFrame = stack
                .Select((frame, i) => (frame, i))
                .Where(f => f.frame.GetMethod().Name == "OnPreprocessAvatar" && (f.frame.GetMethod().DeclaringType?.FullName ?? "").StartsWith("VRC."))
                .Select(pair => pair.i)
                .DefaultIfEmpty(-1)
                .Last();
            
            if (preprocessFrame < 0)
            {
                return false;
            }
            
            if (preprocessFrame >= stack.Length - 1)
            {
                return true;
            }

            var callingClass = stack[preprocessFrame + 1].GetMethod().DeclaringType?.FullName;
            return callingClass is null || callingClass.StartsWith("VRC.");
        }

        public static bool Check()
        {
            return _isActuallyUploading;
        }
    }
}