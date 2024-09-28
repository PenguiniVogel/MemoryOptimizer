#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase.Editor.BuildPipeline;
using static JeTeeS.TES.HelperFunctions.TESHelperFunctions;

namespace JeTeeS.MemoryOptimizer
{
    public class MemoryOptimizerUploadPipeline : IVRCSDKPreprocessAvatarCallback
    {
        private readonly string[] _paramTypes = { "Int", "Float", "Bool" };
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
            var _vrcAvatarDescriptor = avatarGameObject.GetComponent<VRCAvatarDescriptor>();
            var memoryOptimizer = avatarGameObject.GetComponent<MemoryOptimizerComponent>();

            var parameters = _vrcAvatarDescriptor.expressionParameters?.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>();

            Debug.Log($"MemoryOptimizerUploadPipeline.OnPreprocessAvatar running on {avatarGameObject.name} with {parameters.Length} parameters - loaded configuration: {memoryOptimizer.savedParameterConfigurations.Where(p => p.attemptToOptimize).ToList().Count}");

            var parametersBoolToOptimize = new List<MemoryOptimizerMain.MemoryOptimizerListData>(0);
            var parametersIntNFloatToOptimize = new List<MemoryOptimizerMain.MemoryOptimizerListData>(0);

            memoryOptimizer.Refresh();
            
            foreach (var savedParameterConfiguration in memoryOptimizer.savedParameterConfigurations)
            {
                // find actual parameter
                VRCExpressionParameters.Parameter parameter = null;
                if (savedParameterConfiguration.parameterName.StartsWith("VF##_"))
                {
                    parameter = parameters.FirstOrDefault(p =>
                    {
                        if (p.networkSynced)
                        {
                            // match VRCFury parameters by regex replacing VF\d+_ and replacing VF##_ and then matching name and type
                            if (p.name.StartsWith("VF") && new Regex("^VF\\d+_").Replace(p.name, string.Empty).Equals(savedParameterConfiguration.parameterName.Replace("VF##_", string.Empty)) && _paramTypes[(int)p.valueType].Equals(savedParameterConfiguration.parameterType))
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
                            if (p.name.Equals(savedParameterConfiguration.parameterName) && _paramTypes[(int)p.valueType].Equals(savedParameterConfiguration.parameterType))
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

                var fxLayer = FindFXLayer(_vrcAvatarDescriptor);
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
                
                if (savedParameterConfiguration.attemptToOptimize && savedParameterConfiguration.willOptimize)
                {
                    Debug.Log($"MemoryOptimizerUploadPipeline.OnPreprocessAvatar {savedParameterConfiguration.parameterName} will be optimized.");
                    
                    var data = new MemoryOptimizerMain.MemoryOptimizerListData(parameter, savedParameterConfiguration.attemptToOptimize, savedParameterConfiguration.willOptimize);
                
                    switch (parameter.valueType)
                    {
                        case VRCExpressionParameters.ValueType.Bool:
                            parametersBoolToOptimize.Add(data);
                            break;
                        case VRCExpressionParameters.ValueType.Int:
                        case VRCExpressionParameters.ValueType.Float:
                            parametersIntNFloatToOptimize.Add(data);
                            break;
                    }
                }
            }
            
            MemoryOptimizerMain.InstallMemOpt(_vrcAvatarDescriptor, FindFXLayer(_vrcAvatarDescriptor), _vrcAvatarDescriptor.expressionParameters, parametersBoolToOptimize, parametersIntNFloatToOptimize, memoryOptimizer.syncSteps, 0.2f, memoryOptimizer.changeDetection, 0, "Assets/TES/MemOpt");
            
            return _vrcAvatarDescriptor.expressionParameters?.CalcTotalCost() < VRCExpressionParameters.MAX_PARAMETER_COST;
        }
    }
}

#endif
