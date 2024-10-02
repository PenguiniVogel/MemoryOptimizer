#if UNITY_EDITOR

using JeTeeS.TES.HelperFunctions;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;
using static JeTeeS.MemoryOptimizer.Shared.MemoryOptimizerConstants;

namespace JeTeeS.MemoryOptimizer.Helper
{
    internal static class MemoryOptimizerHelper
    {
        public static bool IsSystemInstalled(VRCAvatarDescriptor descriptor)
        {
            return IsSystemInstalled(TESHelperFunctions.FindFXLayer(descriptor));
        }
        
        public static bool IsSystemInstalled(AnimatorController controller)
        {
            if (controller is null)
            {
                return false;
            }
            
            if (controller.FindHiddenIdentifier(syncingLayerIdentifier).Count == 1)
            {
                return true;
            }
            
            if (controller.FindHiddenIdentifier(mainBlendTreeIdentifier).Count == 1)
            {
                return true;
            }

            return false;
        }
    }
}

#endif
