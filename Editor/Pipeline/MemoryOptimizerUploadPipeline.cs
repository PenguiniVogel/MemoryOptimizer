using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using JeTeeS.MemoryOptimizer.Helper;
using JeTeeS.MemoryOptimizer.Shared;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase.Editor.BuildPipeline;
using static JeTeeS.MemoryOptimizer.Shared.MemoryOptimizerConstants;
using static JeTeeS.TES.HelperFunctions.TESHelperFunctions;

namespace JeTeeS.MemoryOptimizer.Pipeline
{
    internal class MemoryOptimizerUploadPipeline : IVRCSDKPreprocessAvatarCallback
    {
        // VRCFury runs at -10000
        // VRChat strips components at -1024
        // So we preprocess in between but not too close to the stripping of components 
        public int callbackOrder => -2048;
        
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            var vrcAvatarDescriptor = avatarGameObject.GetComponent<VRCAvatarDescriptor>();
            var memoryOptimizer = avatarGameObject.GetComponent<MemoryOptimizerComponent>();

            // if we don't have a component don't process
            if (memoryOptimizer is null)
            {
                return true;
            }
            
            // generate temporary fx layer if we don't find one, as parameters can be used in other layers as well
            // but need to be synced on FX
            var fxLayer = FindFXLayer(vrcAvatarDescriptor) ?? GenerateTemporaryFXLayer(vrcAvatarDescriptor);
            var avatarParameters = vrcAvatarDescriptor.expressionParameters;
            var parameters = Array.Empty<VRCExpressionParameters.Parameter>();
            
            // skip if:
            // - no parameters (null or empty)
            // - parameters are within budget, optimizing should only be done if we actually need it
            // - the system is already installed
            {
                var skipReasons = new List<string>();
                
                if (avatarParameters is null || avatarParameters?.parameters.Length <= 0 || (avatarParameters?.IsWithinBudget() ?? true))
                {
                    skipReasons.Add(" - No parameters to optimize.");
                }

                if (MemoryOptimizerHelper.IsSystemInstalled(fxLayer))
                {
                    skipReasons.Add(" - System is already installed.");
                }

                if (skipReasons.Any())
                {
                    Debug.LogWarning($"<color=yellow>[MemoryOptimizer]</color> System was not installed:\n{string.Join("\n", skipReasons)}");
                    return true;
                }
            }
            
            // make the avatar upload clone reference copies to not modify original assets
            {
                // setup copies
                var pathBase = $"Packages/dev.jetees.memoryoptimizer/Temp/Generated_{avatarGameObject.name.SanitizeFileName()}/";
                var (copiedFxLayerSuccess, copiedFxLayer) = MakeCopyOf<AnimatorController>(fxLayer, pathBase, $"{prefix}_GeneratedFxLayer");
                var (copiedExpressionParametersSuccess, copiedExpressionParameters) = MakeCopyOf<VRCExpressionParameters>(vrcAvatarDescriptor.expressionParameters, pathBase, $"{prefix}_GeneratedExpressionParameters");
                
                // throw error when copy fails
                if (!copiedFxLayerSuccess || !copiedExpressionParametersSuccess)
                {
                    return false;
                }
                
                fxLayer = copiedFxLayer;
                parameters = copiedExpressionParameters.parameters;

                vrcAvatarDescriptor.expressionParameters = copiedExpressionParameters;
                for (int i = 0, l = vrcAvatarDescriptor.baseAnimationLayers.Length; i < l; i++)
                {
                    // find the fx layer and replace it
                    if (vrcAvatarDescriptor.baseAnimationLayers[i] is { type: VRCAvatarDescriptor.AnimLayerType.FX, animatorController: not null })
                    {
                        var layer = vrcAvatarDescriptor.baseAnimationLayers[i];

                        layer.animatorController = fxLayer;
                    
                        vrcAvatarDescriptor.baseAnimationLayers[i] = layer;

                        break;
                    }
                }
            }
            
            Debug.Log($"<color=yellow>[MemoryOptimizer]</color> OnPreprocessAvatar running on {avatarGameObject.name} with {parameters.Length} parameters - loaded configuration: {memoryOptimizer.savedParameterConfigurations.Where(p => p.selected && p.willBeOptimized).ToList().Count}");

            var parametersBoolToOptimize = new List<MemoryOptimizerListData>(memoryOptimizer.savedParameterConfigurations.Count);
            var parametersIntNFloatToOptimize = new List<MemoryOptimizerListData>(memoryOptimizer.savedParameterConfigurations.Count);

            int countBoolShould = 0, countBoolIs = 0, countIntNFloatShould = 0, countIntNFloatIs = 0;
            
            foreach (var savedParameterConfiguration in memoryOptimizer.savedParameterConfigurations)
            {
                // find actual parameter
                VRCExpressionParameters.Parameter parameter = null;
                if (savedParameterConfiguration.param.name.StartsWith("VF##_"))
                {
                    parameter = parameters.FirstOrDefault(p =>
                    {
                        if (p.networkSynced)
                        {
                            // match VRCFury parameters by regex replacing VF\d+_ and replacing VF##_ and then matching name and type
                            if (p.name.StartsWith("VF") && new Regex("^VF\\d+_").Replace(p.name, string.Empty).Equals(savedParameterConfiguration.param.name.Replace("VF##_", string.Empty)) && p.valueType == savedParameterConfiguration.param.valueType)
                            {
                                return true;
                            }
                        }

                        return false;
                    });
                }
                else
                {
                    parameter = parameters.FirstOrDefault(p =>
                    {
                        if (p.networkSynced)
                        {
                            // match parameters by name and type
                            if (p.name.Equals(savedParameterConfiguration.param.name) && p.valueType == savedParameterConfiguration.param.valueType)
                            {
                                return true;
                            }
                        }

                        return false;
                    });
                }

                switch (savedParameterConfiguration.param.valueType)
                {
                    case VRCExpressionParameters.ValueType.Bool:
                        countBoolShould++;
                        break;
                    case VRCExpressionParameters.ValueType.Int:
                    case VRCExpressionParameters.ValueType.Float:
                        countIntNFloatShould++;
                        break;
                }
                
                // didn't find parameter, skip
                if (parameter is null)
                {
                    continue;
                }
                
                // add the parameter to the fx layer if it's missing
                if (fxLayer.parameters.All(p => !p.name.Equals(parameter.name)))
                {
                    var type = AnimatorControllerParameterType.Float;
                    switch (parameter.valueType)
                    {
                        case VRCExpressionParameters.ValueType.Int:
                            type = AnimatorControllerParameterType.Int;
                            break;
                        case VRCExpressionParameters.ValueType.Bool:
                            type = AnimatorControllerParameterType.Bool;
                            break;
                        default:
                        case VRCExpressionParameters.ValueType.Float:
                            // since we already assign float at the beginning, skip
                            break;
                    }
                    
                    fxLayer.AddUniqueParam(parameter.name, type);
                }
                
                if (savedParameterConfiguration.selected && savedParameterConfiguration.willBeOptimized)
                {
                    // create copy to reference the proper parameter
                    var baseCopy = savedParameterConfiguration.CopyBase(parameter);
                    
                    switch (savedParameterConfiguration.param.valueType)
                    {
                        case VRCExpressionParameters.ValueType.Bool:
                            countBoolIs++;
                            parametersBoolToOptimize.Add(baseCopy);
                            break;
                        case VRCExpressionParameters.ValueType.Int:
                        case VRCExpressionParameters.ValueType.Float:
                            countIntNFloatIs++;
                            parametersIntNFloatToOptimize.Add(baseCopy);
                            break;
                    }
                }
            }

            // ensure the bool parameters that are left still match what we can optimize
            if (countBoolIs < countBoolShould)
            {
                var newBoolCount = parametersBoolToOptimize.Count - (parametersBoolToOptimize.Count % memoryOptimizer.syncSteps);
                Debug.LogWarning($"<color=yellow>[MemoryOptimizer]</color> Bool count did not match, expected: {countBoolShould} got: {countBoolIs} - now taking: {newBoolCount}");
                parametersBoolToOptimize = parametersBoolToOptimize.Take(newBoolCount).ToList();
            }
            
            // ensure the int and float parameters that are left still match what we can optimize
            if (countIntNFloatIs < countIntNFloatShould)
            {
                var newIntNFloatCount = parametersIntNFloatToOptimize.Count - (parametersIntNFloatToOptimize.Count % memoryOptimizer.syncSteps);
                Debug.LogWarning($"<color=yellow>[MemoryOptimizer]</color> IntNFloat count did not match, expected: {countIntNFloatShould} got: {countIntNFloatIs} - now taking: {newIntNFloatCount}");
                parametersIntNFloatToOptimize = parametersIntNFloatToOptimize.Take(newIntNFloatCount).ToList();
            }

            if (parametersBoolToOptimize.Any() || parametersIntNFloatToOptimize.Any())
            {
                MemoryOptimizerMain.InstallMemOpt(vrcAvatarDescriptor, fxLayer, vrcAvatarDescriptor.expressionParameters, parametersBoolToOptimize, parametersIntNFloatToOptimize, memoryOptimizer.syncSteps, memoryOptimizer.stepDelay, memoryOptimizer.changeDetection, memoryOptimizer.wdOption, "Assets/TES/MemOpt");

                if (vrcAvatarDescriptor.expressionParameters.parameters.Length > 8192)
                {
                    Debug.LogError($"<color=yellow>[MemoryOptimizer]</color> Exceeded maximum expression parameter count of 8192.");
                    return false;
                }
                
                Debug.Log($"<color=yellow>[MemoryOptimizer]</color> OnPreprocessAvatar optimized:\n- Bools:\n{string.Join("\n", parametersBoolToOptimize.Select(p => $" > {p.param.name}"))}\n- IntNFloats:\n{string.Join("\n", parametersIntNFloatToOptimize.Select(p => $" > {p.param.name}"))}");
                
                return vrcAvatarDescriptor.expressionParameters.IsWithinBudget();
            }

            Debug.LogWarning("<color=yellow>[MemoryOptimizer]</color> System was not installed as there were no parameters to optimize.");
            
            return true;
        }
    }
}
