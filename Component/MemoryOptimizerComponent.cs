﻿#if UNITY_EDITOR

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using JeTeeS.MemoryOptimizer.Helper;
using JeTeeS.MemoryOptimizer.Shared;
using JeTeeS.TES.HelperFunctions;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using static JeTeeS.MemoryOptimizer.Shared.MemoryOptimizerConstants;

namespace JeTeeS.MemoryOptimizer
{
    [Serializable]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VRCAvatarDescriptor))]
    internal class MemoryOptimizerComponent : MonoBehaviour, IEditorOnly
    {
        [Serializable]
        internal class SavedParameterConfiguration : MemoryOptimizerListData
        {
            public bool isVRCFuryParameter = false;
            public string info = string.Empty; 
            
            [SerializeField] private int hashCode = -1;
            
            public SavedParameterConfiguration(VRCExpressionParameters.Parameter parameter) : base(parameter, false, false)
            {
                CalculateHashCode();
            }
            
            public SavedParameterConfiguration(string parameterName, VRCExpressionParameters.ValueType parameterType) : base(new VRCExpressionParameters.Parameter()
            {
                name = parameterName,
                valueType = parameterType,
                networkSynced = true
            }, false, false)
            {
                CalculateHashCode();
            }

            internal void CalculateHashCode()
            {
                hashCode = $"name:{this.param.name}-type:{paramTypes[(int)this.param.valueType]}".GetHashCode();
            }
            
            public override int GetHashCode()
            {
                if (hashCode == -1)
                {
                    CalculateHashCode();
                }
                
                return hashCode;
            }

            public override bool Equals(object obj)
            {
                if (obj is SavedParameterConfiguration o)
                {
                    return o.GetHashCode() == GetHashCode();
                }
                
                return base.Equals(obj);
            }

            public MemoryOptimizerListData CopyBase(VRCExpressionParameters.Parameter inherit = null)
            {
                return new MemoryOptimizerListData(inherit ?? new VRCExpressionParameters.Parameter()
                {
                    name = param.name,
                    valueType = param.valueType,
                    networkSynced = true
                }, selected, willBeOptimized);
            }
        }
        
        [Serializable]
        internal struct ComponentIssue
        {
            public string message;
            public int level;
            
            [SerializeField] private int hashCode;

            public static implicit operator ComponentIssue((string, int) data)
            {
                return new ComponentIssue()
                {
                    message = data.Item1,
                    level = data.Item2,
                    hashCode = $"{data.Item1}-level:{data.Item2}".GetHashCode()
                };
            }
            
            public override bool Equals(object obj)
            {
                if (obj is ComponentIssue o)
                {
                    return o.hashCode == hashCode;
                }
                
                return base.Equals(obj);
            }

            public override int GetHashCode()
            {
                return hashCode;
            }
        }
        
        [NonSerialized] private VRCAvatarDescriptor _vrcAvatarDescriptor;

        public bool changeDetection = false;
        public int syncSteps = 2;
        public float stepDelay = 0.2f;
        public int wdOption = 0;

        [SerializeField] internal List<ComponentIssue> componentIssues = new(0);
        [SerializeField] internal List<SavedParameterConfiguration> savedParameterConfigurations = new(0);
        
        internal void LoadParameters()
        {
            componentIssues = new(0);
            
            // get descriptor
            _vrcAvatarDescriptor ??= gameObject.GetComponent<VRCAvatarDescriptor>();
            
            var descriptorParameters = (_vrcAvatarDescriptor?.expressionParameters?.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>()).Where(p => p.networkSynced).ToList();
            
            if (MemoryOptimizerHelper.IsSystemInstalled(_vrcAvatarDescriptor))
            {
                componentIssues.Add(("MemoryOptimizer is already installed in the current FX-Layer.", 3 /* Error */));
            }
            
            // collect all descriptor parameters that are synced
            foreach (var savedParameterConfiguration in descriptorParameters.Select(p => new SavedParameterConfiguration(p) { info = "From Avatar Descriptor" }))
            {
                AddOrReplaceParameterConfiguration(savedParameterConfiguration);
            }
            
#if MemoryOptimizer_VRCFury_IsInstalled
            // since all VRCFury components are IEditorOnly, we can find them like this
            // there is no better way as VRCFury has decided to mark their classes as internal only
            List<IEditorOnly> vrcfComponents = gameObject.GetComponentsInChildren<IEditorOnly>(true)
                .Where(x => x.GetType().ToString().Contains("VRCFury"))
                .ToList();
            
            // collect all VRCFury parameters
            // this is where the real fun begins...
            if (vrcfComponents.Any())
            {
                foreach (IEditorOnly vrcfComponent in vrcfComponents)
                {
                    // for the offset
                    var vrcfGameObject = ((Component)vrcfComponent).gameObject;
                    
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
                    
                    if (contentType.Contains("UnlimitedParameters"))
                    {
                        componentIssues.Add(("VRCFury 'Parameter Compressor' (formerly 'Unlimited Parameters') was found on Avatar.\nOur system IS NOT COMPATIBLE with this.", 3 /* Error */));
                    }
                    // Toggle
                    else if (contentType.Contains("Toggle"))
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

                        SavedParameterConfiguration savedParameterConfiguration = new SavedParameterConfiguration(useGlobalParameter ? globalParameter : $"VF##_{toggleName}", isSlider ? VRCExpressionParameters.ValueType.Float : VRCExpressionParameters.ValueType.Bool)
                        {
                            isVRCFuryParameter = true,
                            info = $"From Toggle: {toggleName} on {gameObject.name}"
                        };
                        
                        AddOrReplaceParameterConfiguration(savedParameterConfiguration);
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
                                // the parameters are a field which is wrapped again
                                if (slot.GetFieldValue("parameters")?.GetFieldValue("objRef") is VRCExpressionParameters cast)
                                {
                                    extractedParametersList.Add(cast);
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
                                    
                                    var savedParameterConfiguration = new SavedParameterConfiguration((containsStar && !globalParameters.Contains($"!{parameter.name}")) || globalParameters.Contains(parameter.name) ? parameter.name : $"VF##_{parameter.name}", parameter.valueType)
                                    {
                                        isVRCFuryParameter = true,
                                        info = $"From FullController on {vrcfGameObject.name}"
                                    };
                                    
                                    AddOrReplaceParameterConfiguration(savedParameterConfiguration);
                                }
                            }
                        }
                    }
                }
            }
#endif
            if (savedParameterConfigurations.Count <= 0)
            {
                componentIssues.Add(("This avatar has no loaded parameters.", 2 /* Warning */));
            }
        }

        private void Reset()
        {
            savedParameterConfigurations = new(0);
            
            LoadParameters();
        }
        
        private void AddOrReplaceParameterConfiguration(SavedParameterConfiguration configuration)
        {
            if (savedParameterConfigurations.Contains(configuration))
            {
                // in the odd case a parameter is defined multiple times
                // we replace it with the one costing more, as mapping from a float to a bool
                // usually is handled much better than bool to a float
                var saved = savedParameterConfigurations.FirstOrDefault(p => p.Equals(configuration));
                if (saved is not null && saved.param.GetVRCExpressionParameterCost() < configuration.param.GetVRCExpressionParameterCost())
                {
                    Debug.LogWarning($"<color=yellow>[MemoryOptimizer]</color> Parameter '{saved.param.name}' from '{saved.info}' was already loaded as {paramTypes[(int)saved.param.valueType]} but has been replaced by {paramTypes[(int)configuration.param.valueType]} from '{configuration.info}'");
                    
                    saved.param = configuration.param;
                    saved.CalculateHashCode();
                }
            }
            else
            {
                savedParameterConfigurations.Add(configuration);
            }
        }

        private void AddIssue(ComponentIssue issue)
        {
            if (!componentIssues.Contains(issue))
            {
                componentIssues.Add(issue);
            }
        }
    }
}

#endif
