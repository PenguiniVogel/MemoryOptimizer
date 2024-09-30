#if UNITY_EDITOR

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using JeTeeS.MemoryOptimizer.Helper;
using JeTeeS.MemoryOptimizer.Shared;
using JeTeeS.TES.HelperFunctions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

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

            public SavedParameterConfiguration(VRCExpressionParameters.Parameter parameter) : base(parameter, false, false)
            {
            }
            
            public SavedParameterConfiguration(string parameterName, VRCExpressionParameters.ValueType parameterType) : base(new VRCExpressionParameters.Parameter()
            {
                name = parameterName,
                valueType = parameterType,
                networkSynced = true
            }, false, false)
            {
            }

            public override int GetHashCode()
            {
                return $"{this.param.name}{TESHelperFunctions._paramTypes[(int)this.param.valueType]}".GetHashCode();
            }

            public override bool Equals(object obj)
            {
                if (obj is null)
                {
                    return false;
                }

                if (obj is SavedParameterConfiguration typed)
                {
                    return typed.param.name.Equals(this.param.name) && typed.param.valueType == this.param.valueType;
                }
                
                return base.Equals(obj);
            }

            public MemoryOptimizerListData Pure()
            {
                return new MemoryOptimizerListData(new VRCExpressionParameters.Parameter()
                {
                    name = this.param.name,
                    valueType = this.param.valueType,
                    networkSynced = true
                }, this.selected, this.willBeOptimized);
            }
            
            public static bool operator ==(SavedParameterConfiguration a, object b)
            {
                return a?.Equals(b) ?? false;
            }

            public static bool operator !=(SavedParameterConfiguration a, object b)
            {
                return !(a == b);
            }
        }
        
        [NonSerialized] private VRCAvatarDescriptor _vrcAvatarDescriptor;

        public bool changeDetection = false;
        public int syncSteps = 2;
        public float stepDelay = 0.2f;
        public int wdOption = 0;
        [SerializeReference] internal DefaultAsset savePathOverride = null;
        
        [NonSerialized] internal int longestParameterName = 0;

        [SerializeField] internal List<SavedParameterConfiguration> savedParameterConfigurations = new(0);
        
        internal void Load()
        {
            // get descriptor
            _vrcAvatarDescriptor = gameObject.GetComponent<VRCAvatarDescriptor>();
            var descriptorParameters = (_vrcAvatarDescriptor?.expressionParameters?.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>()).Where(p => p.networkSynced).ToList();
            
            // collect all descriptor parameters that are synced
            foreach (var savedParameterConfiguration in descriptorParameters.Select(p => new SavedParameterConfiguration(p)
                     {
                         info = "From Avatar Descriptor"
                     }))
            {
                AddOrReplaceParameterConfiguration(savedParameterConfiguration);
            }
            
#if MemoryOptimizer_VRCFury_IsInstalled
            // since all VRCFury components are IEditorOnly, we can find them like this
            // there is no better way as VRCFury has decided to mark their classes as internal only
            List<IEditorOnly> vrcfComponents = (gameObject.GetComponents<IEditorOnly>())
                .Concat(gameObject.GetComponentsInChildren<IEditorOnly>(true))
                .Where(x => x.GetType().ToString().Contains("VRCFury"))
                .ToList();
            
            // collect all VRCFury parameters
            // this is where the real fun begins...
            if (vrcfComponents.Any())
            {
                foreach (IEditorOnly vrcfComponent in vrcfComponents)
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

                        SavedParameterConfiguration savedParameterConfiguration = new SavedParameterConfiguration(useGlobalParameter ? globalParameter : $"VF##_{toggleName}", isSlider ? VRCExpressionParameters.ValueType.Float : VRCExpressionParameters.ValueType.Bool)
                        {
                            isVRCFuryParameter = true,
                            info = isEditorOnly ? "(EditorOnly) " : string.Empty + $"From Toggle: {toggleName} on {gameObject.name}"
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
                                    
                                    SavedParameterConfiguration savedParameterConfiguration = new SavedParameterConfiguration((containsStar && !globalParameters.Contains($"!{parameter.name}")) || globalParameters.Contains(parameter.name) ? parameter.name : $"VF##_{parameter.name}", parameter.valueType)
                                    {
                                        isVRCFuryParameter = true,
                                        info = isEditorOnly ? "(EditorOnly) " : string.Empty + $"From FullController on {vrcfGameObject.name}"
                                    };
                                    
                                    AddOrReplaceParameterConfiguration(savedParameterConfiguration);
                                }
                            }
                        }
                    }
                }
            }
#endif
        }

        private void Awake()
        {
            Load();
        }

        private void Reset()
        {
            savedParameterConfigurations = new(0);
        }
        
        private void AddOrReplaceParameterConfiguration(SavedParameterConfiguration configuration)
        {
            if (savedParameterConfigurations.Contains(configuration))
            {
                // in the odd case a parameter is defined multiple times
                // we replace it with the one costing more, as mapping from a float to a bool
                // usually is handled much better than bool to a float
                var saved = savedParameterConfigurations.FirstOrDefault(p => p == configuration);
                if (saved is not null && saved.param.GetParameterCost() < configuration.param.GetParameterCost())
                {
                    saved.param = configuration.param;
                }
            }
            else
            {
                longestParameterName = Math.Max(longestParameterName, configuration.param.name.Count());
                savedParameterConfigurations.Add(configuration);
            }
        }
    }
}

#endif
