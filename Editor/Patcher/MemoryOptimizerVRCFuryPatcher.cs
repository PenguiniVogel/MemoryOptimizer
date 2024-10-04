using System.IO;
using UnityEditor;
using UnityEngine;

namespace JeTeeS.MemoryOptimizer.Patcher
{
    internal static class MemoryOptimizerVRCFuryPatcher
    {
        private static bool _refIsPatched = false;
        private static MonoScript _refFinalValidationService = null;

        private static void Load()
        {
            if (_refFinalValidationService is null || !_refIsPatched)
            {
                if (AssetDatabase.LoadAssetAtPath<MonoScript>("Packages/com.vrcfury.vrcfury/Editor/VF/Service/FinalValidationService.cs") is { } scriptToPatch)
                {
                    _refFinalValidationService = scriptToPatch;
                    _refIsPatched = scriptToPatch.text.Contains("patched by >>> dev.jetees.memoryoptimizer.MemoryOptimizerVRCFuryPatcher");
                }
            }
        }
        
        public static bool AreVRCFuryScriptsPatched()
        {
#if MemoryOptimizer_VRCFury_IsInstalled
#if !MemoryOptimizer_VRCFury_IsTested
            Debug.LogWarning($"The pipeline is running on an untested version of VRCFury, probably fine though.");
#endif
            Load();

            return _refIsPatched;
#else
            return true;
#endif
        }
        
        public static bool PatchVRCFuryScripts()
        {
#if MemoryOptimizer_VRCFury_IsInstalled
#if !MemoryOptimizer_VRCFury_IsTested
            Debug.LogWarning($"The pipeline is running on an untested version of VRCFury, probably fine though.");
#endif
            Load();
            
            if (_refIsPatched)
            {
                Debug.Log("MemoryOptimizerVRCFuryPatcher: Already patched, skipping.");
                return true;
            }
            
            if (_refFinalValidationService is not null)
            {
                if (AssetDatabase.LoadAssetAtPath<TextAsset>("Packages/dev.jetees.memoryoptimizer/Editor/Patcher/VRCFury-FinalValidationService.txt") is { } patch)
                {
                    File.WriteAllText(AssetDatabase.GetAssetPath(_refFinalValidationService), patch.text);
                    EditorUtility.SetDirty(_refFinalValidationService);
                    Debug.Log("<color=yellow>[MemoryOptimizer]</color> MemoryOptimizerVRCFuryPatcher patched VF.Service.FinalValidationService!");
                }
            }

            return AreVRCFuryScriptsPatched();
#else
            return true;
#endif
        }
    }
}
