using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using JeTeeS.MemoryOptimizer.Shared;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase.Editor.BuildPipeline;
using static JeTeeS.TES.HelperFunctions.TESHelperFunctions;

namespace JeTeeS.MemoryOptimizer.Pipeline
{
    internal class MemoryOptimizerUploadPipeline : IVRCSDKPreprocessAvatarCallback
    {
        private readonly string[] _animatorParamTypes =
        {
            "",
            // 1
            "Float",
            "",
            // 3
            "Int",
            // 4
            "Bool",
            "",
            "",
            "",
            "",
            // 9
            "Trigger"
        };
        
        // VRCFury runs at -10000
        // VRChat strips components at -1024
        // So we preprocess in between but not too close to the stripping of components 
        public int callbackOrder => -2048;
        
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            var vrcAvatarDescriptor = avatarGameObject.GetComponent<VRCAvatarDescriptor>();
            var memoryOptimizer = avatarGameObject.GetComponent<MemoryOptimizerComponent>();

            var parameters = vrcAvatarDescriptor.expressionParameters?.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>();

            Debug.Log($"<color=yellow>[MemoryOptimizer]</color> OnPreprocessAvatar running on {avatarGameObject.name} with {parameters.Length} parameters - loaded configuration: {memoryOptimizer.savedParameterConfigurations.Where(p => p.selected && p.willBeOptimized).ToList().Count}");

            var parametersBoolToOptimize = new List<MemoryOptimizerListData>(0);
            var parametersIntNFloatToOptimize = new List<MemoryOptimizerListData>(0);
            
            var fxLayer = FindFXLayer(vrcAvatarDescriptor);
            
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
                        case VRCExpressionParameters.ValueType.Float:
                            break;
                        case VRCExpressionParameters.ValueType.Bool:
                            type = AnimatorControllerParameterType.Bool;
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

            Debug.Log("<color=yellow>[MemoryOptimizer]</color> System was not installed as there were no parameters to optimize.");
            return true;
        }
    }
}
