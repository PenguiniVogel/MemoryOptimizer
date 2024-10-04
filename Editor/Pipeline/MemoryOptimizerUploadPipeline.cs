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
            
            var fxLayer = FindFXLayer(vrcAvatarDescriptor);
            var parameters = vrcAvatarDescriptor.expressionParameters?.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>();

            // skip if we have no fx layer, or system is installed, or we don't have parameters
            if (fxLayer is null || MemoryOptimizerHelper.IsSystemInstalled(fxLayer) || parameters.Length <= 0)
            {
                Debug.LogWarning("<color=yellow>[MemoryOptimizer]</color> System was not installed as it either: was already installed, there was no fx layer or no parameters.");
                return true;
            }

            var (copiedFxLayerSuccess, copiedFxLayer) = MakeCopyOf<AnimatorController>(fxLayer, $"Packages/dev.jetees.memoryoptimizer/Temp/Generated_{avatarGameObject.name.SanitizeFileName()}/", $"{prefix}_GeneratedFxLayer");
            var (copiedExpressionParametersSuccess, copiedExpressionParameters) = MakeCopyOf<VRCExpressionParameters>(vrcAvatarDescriptor.expressionParameters, $"Packages/dev.jetees.memoryoptimizer/Temp/Generated_{avatarGameObject.name.SanitizeFileName()}/", $"{prefix}_GeneratedExpressionParameters");

            // throw error when copy fails
            if (!copiedFxLayerSuccess || !copiedExpressionParametersSuccess)
            {
                return false;
            }

            // setup copies
            // we make the avatar reference copies to not break any original assets
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
            
            Debug.Log($"<color=yellow>[MemoryOptimizer]</color> OnPreprocessAvatar running on {avatarGameObject.name} with {parameters.Length} parameters - loaded configuration: {memoryOptimizer.savedParameterConfigurations.Where(p => p.selected && p.willBeOptimized).ToList().Count}");

            var parametersBoolToOptimize = new List<MemoryOptimizerListData>(0);
            var parametersIntNFloatToOptimize = new List<MemoryOptimizerListData>(0);
            
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

                if (parameter is null)
                {
                    continue;
                }

                // create pure copy to reference the proper parameter
                var pure = savedParameterConfiguration.Pure();
                pure.param = parameter;
                
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
                    switch (savedParameterConfiguration.param.valueType)
                    {
                        case VRCExpressionParameters.ValueType.Bool:
                            parametersBoolToOptimize.Add(pure);
                            break;
                        case VRCExpressionParameters.ValueType.Int:
                        case VRCExpressionParameters.ValueType.Float:
                            parametersIntNFloatToOptimize.Add(pure);
                            break;
                    }
                }
            }

            if (parametersBoolToOptimize.Any() || parametersIntNFloatToOptimize.Any())
            {
                MemoryOptimizerMain.InstallMemOpt(vrcAvatarDescriptor, fxLayer, vrcAvatarDescriptor.expressionParameters, parametersBoolToOptimize, parametersIntNFloatToOptimize, memoryOptimizer.syncSteps, memoryOptimizer.stepDelay, memoryOptimizer.changeDetection, memoryOptimizer.wdOption, "Assets/TES/MemOpt");
            
                Debug.Log($"<color=yellow>[MemoryOptimizer]</color> OnPreprocessAvatar optimized:\n- Bools:\n{string.Join("\n", parametersBoolToOptimize.Select(p => $" > {p.param.name}"))}\n- IntNFloats:\n{string.Join("\n", parametersIntNFloatToOptimize.Select(p => $" > {p.param.name}"))}");
                
                return vrcAvatarDescriptor.expressionParameters?.CalcTotalCost() < VRCExpressionParameters.MAX_PARAMETER_COST;
            }

            Debug.LogWarning("<color=yellow>[MemoryOptimizer]</color> System was not installed as there were no parameters to optimize.");
            
            return true;
        }
    }
}
