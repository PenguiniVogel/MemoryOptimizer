#if UNITY_EDITOR

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using JeTeeS.MemoryOptimizer.Helper;
using JeTeeS.MemoryOptimizer.Shared;
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
    internal class MemoryOptimizerComponent : MonoBehaviour, IEditorOnly, ISerializationCallbackReceiver
    {
        [Serializable]
        internal class ParameterConfig : MemoryOptimizerListData
        {
            public string info = string.Empty;
            
            /**
             * We set this flag for parameters that have lost their connection, but may re-gain it
             */
            public bool isOrphanParameter = false;
            
            [SerializeField] private int hashCode = -1;
            
            public ParameterConfig(VRCExpressionParameters.Parameter parameter) : base(parameter, false, false)
            {
                CalculateHashCode();
            }
            
            public ParameterConfig(string parameterName, VRCExpressionParameters.ValueType parameterType) : base(new VRCExpressionParameters.Parameter()
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
                if (obj is ParameterConfig o)
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

        [SerializeField] internal List<ComponentIssue> componentIssues = new(16);
        [SerializeField] internal List<ParameterConfig> parameterConfigs = new(1024);

        [NonSerialized] private Dictionary<int, ParameterConfig> lookupParameterConfigs = new(1024);
        
        public void OnBeforeSerialize()
        { }

        public void OnAfterDeserialize()
        {
            foreach (var parameterConfig in parameterConfigs)
            {
                lookupParameterConfigs.Add(parameterConfig.GetHashCode(), parameterConfig);
            }
        }
        
        internal void LoadParameters()
        {
            componentIssues = new(0);

            foreach (var parameterConfig in parameterConfigs)
            {
                parameterConfig.isOrphanParameter = true;
            }
            
            // get descriptor
            _vrcAvatarDescriptor ??= gameObject.GetComponent<VRCAvatarDescriptor>();
            
            var descriptorParameters = (_vrcAvatarDescriptor?.expressionParameters?.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>()).Where(p => p.networkSynced).ToList();
            
            if (MemoryOptimizerHelper.IsSystemInstalled(_vrcAvatarDescriptor))
            {
                componentIssues.Add(("MemoryOptimizer is already installed in the current FX-Layer.", 3 /* Error */));
            }
            
            // collect all descriptor parameters that are synced
            foreach (var savedParameterConfiguration in descriptorParameters.Select(p => new ParameterConfig(p) { info = "From Avatar Descriptor" }))
            {
                AddParameterConfig(savedParameterConfiguration);
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
                    // get type name
                    var vrcfType = vrcfComponent.GetType().ToString();
                    
                    // get containing object
                    var vrcfGameObject = ((Component)vrcfComponent).gameObject;

                    // VRCFuryHapticSocket components are handled differently
                    if (vrcfType.Contains("VRCFuryHapticSocket"))
                    {
                        var hapticName = string.Empty;
                        if (vrcfComponent.GetFieldValue("name") is string _name && !string.IsNullOrEmpty(_name))
                        {
                            hapticName = _name;
                        }

                        // VRCFuryHapticSocket generate a parameter called _stealth
                        AddParameterConfig(new ParameterConfig("VF##_stealth", VRCExpressionParameters.ValueType.Bool));
                        
                        var hapticAddMenuItem = vrcfComponent.GetFieldValue("addMenuItem") is true;

                        // ignore _Unknown or no menu item
                        if (string.IsNullOrEmpty(hapticName) || !hapticAddMenuItem)
                        {
                            continue;
                        }

                        AddParameterConfig(new ParameterConfig($"VF##_{hapticName}", VRCExpressionParameters.ValueType.Bool));
                        
                        // we can go to the next component
                        continue;
                    }
                    // VRCFuryHapticPlug don't generate a parameter because ???
                    else if (vrcfType.Contains("VRCFuryHapticPlug"))
                    {
                        continue;    
                    }
                    
                    // the normal components are handled via VRCFury.content
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

                        ParameterConfig parameterConfig = new ParameterConfig(useGlobalParameter ? globalParameter : $"VF##_{toggleName}", isSlider ? VRCExpressionParameters.ValueType.Float : VRCExpressionParameters.ValueType.Bool)
                        {
                            info = $"From Toggle: {toggleName} on {gameObject.name}"
                        };
                        
                        AddParameterConfig(parameterConfig);
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
                        var isGoGo = false;
                        
                        // get the parameter list
                        if (contentValue.GetFieldValue("prms") is IEnumerable _parametersList)
                        {
                            var generatedConfigs = new Dictionary<int, ParameterConfig>(1024);
                            // remap to actual expression parameters
                            foreach (var slot in _parametersList)
                            {
                                // the parameters are a field which is wrapped again
                                if (slot.GetFieldValue("parameters")?.GetFieldValue("objRef") is VRCExpressionParameters cast)
                                {
                                    // add parameters
                                    foreach (var parameter in cast.parameters)
                                    {
                                        // so we can fix them later
                                        if (parameter.name.Equals("Go/Locomotion"))
                                        {
                                            isGoGo = true;
                                        }
                                        
                                        // ignore un-synced
                                        if (!parameter.networkSynced)
                                        {
                                            continue;
                                        }
                                    
                                        var parameterConfig = new ParameterConfig((containsStar && !globalParameters.Contains($"!{parameter.name}")) || globalParameters.Contains(parameter.name) ? parameter.name : $"VF##_{parameter.name}", parameter.valueType)
                                        {
                                            info = $"From FullController on {vrcfGameObject.name}"
                                        };

                                        // avoid duplicates
                                        generatedConfigs.TryAdd(parameterConfig.GetHashCode(), parameterConfig);
                                    }
                                }
                            }

                            // add to component
                            foreach (var parameterConfig in generatedConfigs.Select(generatedConfig => generatedConfig.Value))
                            {
                                // since GoGo has an issue with their global parameters using 'a *' in most versions, we remap them ourselves correctly
                                if (isGoGo)
                                {
                                    var fixedName = parameterConfig.param.name["VF##_".Length..];
                                    if (!fixedName.StartsWith("Go/"))
                                    {
                                        fixedName = "Go/" + fixedName;
                                    }
                                    
                                    parameterConfig.param = new VRCExpressionParameters.Parameter()
                                    {
                                        name = fixedName,
                                        valueType = parameterConfig.param.valueType,
                                        networkSynced = parameterConfig.param.networkSynced
                                    };
                                    
                                    parameterConfig.CalculateHashCode();
                                }
                                
                                AddParameterConfig(parameterConfig);
                            }
                        }
                    }
                }
            }
#endif
            if (parameterConfigs.Count <= 0)
            {
                componentIssues.Add(("This avatar has no loaded parameters.", 2 /* Warning */));
            }
        }

        internal void ClearOrphans()
        {
            foreach (var parameterConfig in parameterConfigs.ToArray())
            {
                if (parameterConfig.isOrphanParameter)
                {
                    parameterConfigs.Remove(parameterConfig);
                    lookupParameterConfigs.Remove(parameterConfig.GetHashCode());
                }
            }
        }
        
        private void Reset()
        {
            parameterConfigs = new(1024);
            lookupParameterConfigs = new Dictionary<int, ParameterConfig>(1024);
            
            LoadParameters();
        }
        
        private bool AddParameterConfig(ParameterConfig config)
        {
            if (AnimatorExclusions.Contains(config.param.name))
            {
                return false;
            }

            if (lookupParameterConfigs.TryGetValue(config.GetHashCode(), out var stored))
            {
                stored.isOrphanParameter = false;
            }
            else
            {
                if (lookupParameterConfigs.TryAdd(config.GetHashCode(), config))
                {
                    parameterConfigs.Add(config);
                }
            }
            
            return true;
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
