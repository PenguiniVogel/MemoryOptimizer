using System.Reflection;
using HarmonyLib;
using JeTeeS.MemoryOptimizer.Helper;
using UnityEngine;

namespace JeTeeS.MemoryOptimizer.Patcher
{
    public static class FinalValidationServicePatch
    {
        public static bool PatchedCheckParams()
        {
            Debug.Log("<color=yellow>[MemoryOptimizer]</color> MemoryOptimizerVRCFuryPatcher hello from patched CheckParams!");
            // skip original method
            return false;
        }
    }
    
    internal static class MemoryOptimizerVRCFuryPatcher
    {
        private const string HarmonyId = "dev.jetees.memoryoptimizer.Patcher";
        private static bool _refIsPatched = false;
        
        public static void FindAndPatch()
        {
#if MemoryOptimizer_VRCFury_IsInstalled
            // check that we are patched
            if (!Harmony.HasAnyPatches(HarmonyId))
            {
                _refIsPatched = false;
            }
            
            // if we are patched, just skip
            if (_refIsPatched)
            {
                return;
            }
            
            var harmony = new Harmony(HarmonyId);
            
            // make sure we unpatch everything from us first, just in case
            harmony.UnpatchAll(HarmonyId);

            if (ReflectionHelper.FindTypeInAssemblies("VF.Service.FinalValidationService") is not { } vfService)
            {
                Debug.LogError("<color=yellow>[MemoryOptimizer]</color> MemoryOptimizerVRCFuryPatcher failed: did not find type VF.Service.FinalValidationService");
                return;
            }

            if (vfService.GetMethod("CheckParams", BindingFlags.NonPublic | BindingFlags.Instance) is not { } original)
            {
                Debug.LogError("<color=yellow>[MemoryOptimizer]</color> MemoryOptimizerVRCFuryPatcher failed: did not find method VF.Service.FinalValidationService.CheckParams");
                return;
            }

            if (typeof(FinalValidationServicePatch).GetMethod("PatchedCheckParams", BindingFlags.Public | BindingFlags.Static) is not { } patch)
            {
                Debug.LogError("<color=yellow>[MemoryOptimizer]</color> MemoryOptimizerVRCFuryPatcher failed: did not find method JeTeeS.MemoryOptimizer.PatcherFinalValidationService.PatchedCheckParams");
                return;
            }
            
            harmony.Patch(original, new HarmonyMethod(patch));
            
            _refIsPatched = true;
            
            Debug.Log($"<color=yellow>[MemoryOptimizer]</color> MemoryOptimizerVRCFuryPatcher harmony patched {patch} to {original}");
#endif
        }
    }
}
