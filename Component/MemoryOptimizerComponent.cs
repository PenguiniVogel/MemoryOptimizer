#if UNITY_EDITOR

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using JeTeeS.TES.HelperFunctions;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace JeTeeS.MemoryOptimizer
{
    [Serializable]
    [RequireComponent(typeof(VRCAvatarDescriptor))]
    public class MemoryOptimizerComponent : MonoBehaviour, IEditorOnly
    {
        [Serializable]
        public class SavedParameterConfiguration
        {
            public string parameterName = string.Empty;
            [ReadOnlyPropertyHelper.ReadOnly] public string parameterType = string.Empty;
            public bool attemptToOptimize = true;
            [ReadOnlyPropertyHelper.ReadOnly] public bool willOptimize = false;
            [ReadOnlyPropertyHelper.ReadOnly] public bool isVRCFuryParameter = false;
            [ReadOnlyPropertyHelper.ReadOnly] public string info = string.Empty;

            public override bool Equals(object obj)
            {
                if (obj is null)
                {
                    return false;
                }

                if (obj is SavedParameterConfiguration typed)
                {
                    return typed.parameterName.Equals(parameterName) && typed.parameterType.Equals(parameterType);
                }
                
                return base.Equals(obj);
            }

            public override string ToString()
            {
                return $"{parameterName} ({parameterType}) - {(attemptToOptimize ? "Will try to Optimize" : "Won't Optimize")}";
            }
        } 
        
        [NonSerialized] private readonly string[] _paramTypes = { "Int", "Float", "Bool" };
        [NonSerialized] private VRCAvatarDescriptor _vrcAvatarDescriptor;

        public bool changeDetection = false;
        public int syncSteps = 2;
        public List<SavedParameterConfiguration> savedParameterConfigurations = new List<SavedParameterConfiguration>(0);
        public List<string> exclusions = new List<string>(0);
        public int totalParameterCost = 0;
        public int optimizedParameterCost = 0;
        
        internal void Load()
        {
            // get descriptor
            _vrcAvatarDescriptor = gameObject.GetComponent<VRCAvatarDescriptor>();
            
            // collect all descriptor parameters that are synced
            foreach (var savedParameterConfiguration in (_vrcAvatarDescriptor?.expressionParameters?.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>()).Where(p => p.networkSynced).Select(p => new SavedParameterConfiguration()
                     {
                         parameterName = p.name,
                         parameterType = _paramTypes[(int)p.valueType],
                         attemptToOptimize = true,
                         info = "From Avatar Descriptor"
                     }))
            {
                if (!savedParameterConfigurations.Contains(savedParameterConfiguration))
                {
                    savedParameterConfigurations.Add(savedParameterConfiguration);
                }
            }
            
#if VRCFury_Installed
            // since all VRCFury components are IEditorOnly, we can find them like this
            // there is no better way as VRCFury has decided to mark their classes as internal only
            IEnumerable<IEditorOnly> _vrcfComponents = (gameObject.GetComponents<IEditorOnly>())
                .Concat(gameObject.GetComponentsInChildren<IEditorOnly>(true))
                .Where(x => x.GetType().ToString().Contains("VRCFury"));
            
            // collect all VRCFury parameters
            // this is where the real fun begins...
            if (_vrcfComponents.Any())
            {
                foreach (IEditorOnly vrcfComponent in _vrcfComponents)
                {
                    // some systems use EditorOnly components to be optimized away
                    var vrcfGameObject = ((Component)vrcfComponent).gameObject;
                    var isEditorOnly = vrcfGameObject.tag.Equals("EditorOnly");
                    
                    // extract the content field from VRCFury, this is where the actual "component" data can be found
                    object contentValue = null;
                    try
                    {
                        contentValue = vrcfComponent.GetFieldValue("content");
                    }
                    catch
                    {
                        continue;
                    }

                    // not all components have content
                    if (string.IsNullOrEmpty(contentValue?.GetType()?.ToString()))
                    {
                        continue;
                    }
                    
                    // from here on out it depends on what we have
                    var contentType = contentValue.GetType().ToString();
                    
                    // Notes:
                    // VRCFury generates parameters with the following format: VF\d+_{name}
                    // since we have no access to the number unless we build the avatar, we just display VF##_{name} and map it correctly later
                    
                    // Toggle
                    if (contentType.Contains("Toggle"))
                    {
                        // Toggles are rather simple, they can be in any of these configurations:
                        // 1. being a normal toggle (auto-generated bool parameter)
                        // 2. using a global parameter (defined bool parameter)
                        // 3. being a slider (auto-generated float parameter)
                        // 4. being a slider using a global parameter (defined float parameter)

                        var toggleName = "VF##_";
                        if (contentValue.GetFieldValue("name") is string _toggleName && !string.IsNullOrEmpty(_toggleName))
                        {
                            toggleName = _toggleName;
                        }
                        
                        var isSlider = contentValue.GetFieldValue("slider") is true;
                        
                        var useGlobalParameter = contentValue.GetFieldValue("useGlobalParam") is true;
                        var globalParameter = string.Empty;
                        if (useGlobalParameter && contentValue.GetFieldValue("globalParam") is string _globalParameter && !string.IsNullOrEmpty(_globalParameter))
                        {
                            globalParameter = _globalParameter;
                        }
                        else
                        {
                            useGlobalParameter = false;
                        }

                        // if the toggle is a non described empty one and there isn't a global parameter
                        // that is being targeted, we skip this as we cannot correctly map it back
                        if (toggleName.Equals("VF##_") && !useGlobalParameter)
                        {
                            continue;
                        }

                        SavedParameterConfiguration savedParameterConfiguration = new SavedParameterConfiguration()
                        {
                            parameterName = useGlobalParameter ? globalParameter : $"VF##_{toggleName}",
                            parameterType = isSlider ? "Float" : "Bool",
                            attemptToOptimize = true,
                            isVRCFuryParameter = true,
                            info = isEditorOnly ? "(EditorOnly) " : string.Empty + $"From Toggle: {toggleName} on {gameObject.name}"
                        };
                        
                        if (!savedParameterConfigurations.Contains(savedParameterConfiguration))
                        {
                            savedParameterConfigurations.Add(savedParameterConfiguration);
                        }
                    }
                    // Full Controller
                    else if (contentType.Contains("FullController"))
                    {
                        // get global parameters
                        List<string> globalParameters = new List<string>(0);
                        if (contentValue.GetFieldValue("globalParams") is List<string> _globalParameters)
                        {
                            globalParameters = _globalParameters;
                        }

                        var containsStar = globalParameters.Contains("*");
                        
                        // get the parameter list
                        if (contentValue.GetFieldValue("prms") is IEnumerable _parametersList)
                        {
                            // remap to actual expression parameters
                            var extractedParametersList = new List<VRCExpressionParameters>();
                            foreach (object slot in _parametersList)
                            {
                                VRCExpressionParameters extracted = null;
                                // the parameters are a field which is wrapped again
                                if (slot.GetFieldValue("parameters")?.GetFieldValue("objRef") is VRCExpressionParameters cast)
                                {
                                    extracted = cast;
                                }

                                if (extracted is not null)
                                {
                                    extractedParametersList.Add(extracted);
                                }
                            }

                            // flatten parameters down
                            foreach (VRCExpressionParameters vrcExpressionParameters in extractedParametersList)
                            {
                                foreach (VRCExpressionParameters.Parameter parameter in vrcExpressionParameters.parameters)
                                {
                                    // ignore un-synced
                                    if (!parameter.networkSynced)
                                    {
                                        continue;
                                    }
                                    
                                    SavedParameterConfiguration savedParameterConfiguration = new SavedParameterConfiguration()
                                    {
                                        parameterName = (containsStar && !globalParameters.Contains($"!{parameter.name}")) || globalParameters.Contains(parameter.name) ? parameter.name : $"VF##_{parameter.name}",
                                        parameterType = _paramTypes[(int)parameter.valueType],
                                        attemptToOptimize = true,
                                        isVRCFuryParameter = true,
                                        info = isEditorOnly ? "(EditorOnly) " : string.Empty + $"From FullController on {vrcfGameObject.name}"
                                    };
                                    
                                    if (!savedParameterConfigurations.Contains(savedParameterConfiguration))
                                    {
                                        savedParameterConfigurations.Add(savedParameterConfiguration);
                                    }
                                }
                            }
                        }
                    }
                }
            }
#endif
        }

        internal void Reset()
        {
            savedParameterConfigurations = new List<SavedParameterConfiguration>(0);
            Load();
            Refresh();
        }

        internal void Refresh()
        {
            totalParameterCost = 0;
            optimizedParameterCost = 0;
            
            var parametersBool = new List<SavedParameterConfiguration>(savedParameterConfigurations.Count);
            var parametersIntNFloat = new List<SavedParameterConfiguration>(savedParameterConfigurations.Count);
            
            foreach (var savedParameterConfiguration in savedParameterConfigurations)
            {
                savedParameterConfiguration.attemptToOptimize = exclusions.All(e => !savedParameterConfiguration.parameterName.Replace("VF##_", string.Empty).StartsWith(e));
                savedParameterConfiguration.willOptimize = false;

                if (savedParameterConfiguration.attemptToOptimize)
                {
                    switch (savedParameterConfiguration.parameterType)
                    {
                        case "Bool":
                            parametersBool.Add(savedParameterConfiguration);
                            break;
                        case "Int":
                        case "Float":
                            parametersIntNFloat.Add(savedParameterConfiguration);
                            break;
                    }
                }
            }
            
            var parametersBoolToOptimize = parametersBool.Take(parametersBool.Count - (parametersBool.Count % syncSteps)).ToList();
            var parametersIntNFloatToOptimize = parametersIntNFloat.Take(parametersIntNFloat.Count - (parametersIntNFloat.Count % syncSteps)).ToList();

            foreach (var savedParameterConfiguration in parametersBoolToOptimize)
            {
                savedParameterConfiguration.willOptimize = true;
            }

            foreach (var savedParameterConfiguration in parametersIntNFloatToOptimize)
            {
                savedParameterConfiguration.willOptimize = true;
            }

            foreach (var savedParameterConfiguration in savedParameterConfigurations)
            {
                totalParameterCost += savedParameterConfiguration.parameterType.Equals("Bool") ? 1 : 8;
                if (!savedParameterConfiguration.willOptimize)
                {
                    optimizedParameterCost += savedParameterConfiguration.parameterType.Equals("Bool") ? 1 : 8;
                }
            }
            
            var installationIndexers = (syncSteps - 1).DecimalToBinary().ToString().Count();
            var installationBoolSyncers = parametersBoolToOptimize.Count / syncSteps;
            var installationIntNFloatSyncers = parametersIntNFloatToOptimize.Count / syncSteps;

            optimizedParameterCost += installationIndexers + installationBoolSyncers + (installationIntNFloatSyncers * 8);
        }
    }
}

#endif
