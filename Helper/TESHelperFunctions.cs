#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Debug = UnityEngine.Debug;

namespace JeTeeS.TES.HelperFunctions
{
    internal static class TESHelperFunctions
    {
        public static string SanitizeFileName(this string fileName)
        {
            return string.Join("_", fileName.Split(System.IO.Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        }
        
        public static void MakeBackupOf(List<UnityEngine.Object> things, string saveTo)
        {
            ReadyPath(saveTo);
            foreach (var obj in things)
            {
                var i = 0;
                var splitAssetname = GetAssetName(obj).Split('.').ToList();
                var assetName = "";
                foreach (var strng in splitAssetname.Take(splitAssetname.Count - 1))
                {
                    assetName += strng;
                }

                while (System.IO.File.Exists(saveTo + assetName + $" ({i})." + splitAssetname.Last()))
                {
                    i++;
                }
                
                if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(obj), saveTo + assetName + $" ({i})." + splitAssetname.Last()))
                {
                    Debug.LogWarning($"Failed to copy '{AssetDatabase.GetAssetPath(obj)}' to path: {saveTo + assetName + $" ({i})." + splitAssetname.Last()}");
                }
            }
        }

        /// <summary>
        /// Makes a copy and returns the copy with a success status
        /// </summary>
        /// <param name="thing"></param>
        /// <param name="saveTo"></param>
        /// <param name="newThingName"></param>
        /// <returns></returns>
        public static (bool, T) MakeCopyOf<T>(UnityEngine.Object thing, string saveTo, string newThingName = null) where T : UnityEngine.Object
        {
            bool success = false;
            UnityEngine.Object copiedThing = null;
            
            ReadyPath(saveTo);
            
            string assetName = GetAssetName(thing);
            string assetFileType = GetFileType(thing);

            string copiedAssetPath = saveTo + (string.IsNullOrEmpty(newThingName) ? assetName[..^$".{assetFileType}".Length] : newThingName) + "." + GetFileType(thing);
            
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(thing), copiedAssetPath))
            {
                Debug.LogWarning($"Failed to copy '{AssetDatabase.GetAssetPath(thing)}' to path: '{copiedAssetPath}'");
            }
            else
            {
                success = true;
                copiedThing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(copiedAssetPath);
            }

            return (success, (T)copiedThing);
        }
        
        public static string GetFileType(UnityEngine.Object obj)
        {
            var fileName = GetAssetName(obj);
            return !string.IsNullOrEmpty(fileName) ? fileName.Split('.')[^1] : null;
        }
        
        public static string GetAssetName(string path)
        {
            return !string.IsNullOrEmpty(path) ? path.Split(@"\/".ToCharArray())[^1] : null;
        }
        
        public static string GetAssetName(UnityEngine.Object thing)
        {
            var path = AssetDatabase.GetAssetPath(thing);
            return GetAssetName(path);
        }
        
        public static int UninstallErrorDialogWithDiscordLink(string title, string mainMessage, string discordLink)
        {
            var option = EditorUtility.DisplayDialogComplex(title, mainMessage, "Continue uninstall anyways (not recommended)", "Cancel uninstall", "Join the discord");
            switch (option)
            {
                case 0:
                    return 0;
                case 1:
                    return 1;
                case 2:
                    Application.OpenURL(discordLink);
                    return 2;
                default:
                    Debug.LogError("Unrecognized option.");
                    return -1;
            }
        }
        
        public static AnimatorController FindFXLayer(VRCAvatarDescriptor descriptor)
        {
            var controller = descriptor.baseAnimationLayers.FirstOrDefault(x => x is { type: VRCAvatarDescriptor.AnimLayerType.FX, animatorController: not null }).animatorController;

            if (controller is AnimatorController fxController)
            {
                return fxController;
            }
            
            return null;
        }

        public static VRCExpressionParameters FindExpressionParams(VRCAvatarDescriptor descriptor)
        {
            return descriptor?.expressionParameters;
        }

        public static AnimatorControllerParameterType ValueTypeToParamType(this VRCExpressionParameters.ValueType valueType)
        {
            return valueType switch
            {
                VRCExpressionParameters.ValueType.Float => AnimatorControllerParameterType.Float,
                VRCExpressionParameters.ValueType.Int => AnimatorControllerParameterType.Int,
                VRCExpressionParameters.ValueType.Bool => AnimatorControllerParameterType.Bool,
                _ => AnimatorControllerParameterType.Float
            };
        }

        public static VRCExpressionParameters.ValueType ParamTypeToValueType(this AnimatorControllerParameterType paramType)
        {
            return paramType switch
            {
                AnimatorControllerParameterType.Float => VRCExpressionParameters.ValueType.Float,
                AnimatorControllerParameterType.Int => VRCExpressionParameters.ValueType.Int,
                AnimatorControllerParameterType.Bool => VRCExpressionParameters.ValueType.Bool,
                _ => VRCExpressionParameters.ValueType.Float
            };
        }

        public static string GetControllerParentFolder(AnimatorController controller)
        {
            var subPaths = controller.GetAssetPath().Split(@"\/".ToCharArray()).ToList();
            subPaths.RemoveAt(subPaths.Count - 1);
            var returnString = "";
            foreach (var subPath in subPaths)
            {
                returnString += subPath + "/";
            }
            
            return returnString;
        }

        public static Vector3 AngleRadiusToPos(float angle, float radius, Vector3 offset)
        {
            Vector3 result = new((float)Math.Sin(angle) * radius, (float)Math.Cos(angle) * radius, 0);
            result += offset;

            return result;
        }

        public static void LabelWithHelpBox(string text)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(text);
            GUILayout.EndVertical();
        }

        public static int FindLayerIndex(this AnimatorController controller, AnimatorControllerLayer layer)
        {
            for (var i = 0; i < controller.layers.Count(); i++)
            {
                if (controller.layers[i].stateMachine == layer.stateMachine)
                {
                    return i;
                }
            }

            return -1;
        }

        public static void RemoveLayer(this AnimatorController controller, AnimatorControllerLayer layer)
        {
            var i = controller.FindLayerIndex(layer);
            if (i == -1)
            {
                Debug.LogError("Layer " + layer.name + "was not found in " + controller.name);
                return;
            }

            controller.RemoveLayer(i);
        }

        public static List<ChildAnimatorState> FindAllStatesInLayer(this AnimatorControllerLayer layer)
        {
            List<ChildAnimatorState> returnList = new();

            Queue<AnimatorStateMachine> stateMachines = new();
            stateMachines.Enqueue(layer.stateMachine);
            while (stateMachines.Count > 0)
            {
                var currentStateMachine = stateMachines.Dequeue();
                foreach (var state in currentStateMachine.states)
                {
                    returnList.Add(state);
                }
                
                foreach (var stateMachine in currentStateMachine.stateMachines)
                {
                    stateMachines.Enqueue(stateMachine.stateMachine);
                }
            }

            return returnList;
        }

        public static int FindWDInLayer(this AnimatorControllerLayer layer)
        {
            Queue<ChildAnimatorState> stateQueue = new();
            foreach (var state in layer.FindAllStatesInLayer())
            {
                stateQueue.Enqueue(state);
            }

            if (stateQueue.Count == 0)
            {
                return -2;
            }
            
            var currentState = stateQueue.Dequeue();
            var firstWD = currentState.state.writeDefaultValues;
            while (stateQueue.Count > 1)
            {
                currentState = stateQueue.Dequeue();

                if (currentState.state.writeDefaultValues != firstWD && !currentState.state.name.Contains("WD On"))
                {
                    return -1;
                }
            }

            return Convert.ToInt32(firstWD);
        }

        public static int FindWDInController(this AnimatorController controller)
        {
            Queue<AnimatorControllerLayer> layerQueue = new();
            AnimatorControllerLayer currentLayer = new();
            var firstWD = -2;
            foreach (var layer in controller.layers)
            {
                layerQueue.Enqueue(layer);
            }

            while ((currentLayer.IsBlendTreeLayer() || firstWD == -2) && layerQueue.Count > 1)
            {
                if (firstWD == -1)
                {
                    return -1;
                }
                
                currentLayer = layerQueue.Dequeue();
                firstWD = currentLayer.FindWDInLayer();
            }

            while (layerQueue.Count > 1)
            {
                currentLayer = layerQueue.Dequeue();
                if (currentLayer.FindWDInLayer() != firstWD)
                {
                    return -1;
                }
            }

            return Convert.ToInt32(firstWD);
        }

        public static bool IsBlendTreeLayer(this AnimatorControllerLayer layer)
        {
            if (layer?.stateMachine is null)
            {
                return false;
            }
            
            foreach (var state in layer.stateMachine.states)
            {
                if (!state.state.name.Contains("WD On"))
                {
                    return false;
                }
            }

            return true;
        }

        public static AnimatorControllerParameter AddUniqueParam(this AnimatorController controller, string paramName, AnimatorControllerParameterType paramType = AnimatorControllerParameterType.Float, float defaultValue = 0)
        {
            foreach (var param in controller.parameters)
            {
                if (param.name == paramName)
                {
                    if (param.type != paramType)
                    {
                        Debug.LogError("Parameter " + param.name + " is of type: " + param.type.ToString() + " not " + paramType.ToString() + "!");
                    }
                    
                    return param;
                }
            }

            AnimatorControllerParameter controllerParam = new();
            if (paramType == AnimatorControllerParameterType.Float)
            {
                controllerParam = new()
                {
                    name = paramName,
                    type = paramType,
                    defaultFloat = defaultValue
                };
            }
            else if (paramType == AnimatorControllerParameterType.Int)
            {
                controllerParam = new()
                {
                    name = paramName,
                    type = paramType,
                    defaultInt = ((int)defaultValue)
                };
            }
            else if (paramType == AnimatorControllerParameterType.Bool)
            {
                controllerParam = new()
                {
                    name = paramName,
                    type = paramType,
                    defaultBool = defaultValue > 0
                };
            }
            else
            {
                Debug.LogError("Parameter " + paramName + " is not a float, int or bool??");
            }

            controller.AddParameter(controllerParam);
            
            return controller.parameters.Last(x => x.name == paramName && x.type == paramType);
        }

        public static void AddUniqueSyncedParam(this VRCExpressionParameters vrcExpressionParameters, string name, VRCExpressionParameters.ValueType valueType, bool isNetworkSynced = true, bool isSaved = true, float defaultValue = 0)
        {
            foreach (var param in vrcExpressionParameters.parameters)
            {
                if (param.name == name)
                {
                    if (param.valueType != valueType)
                    {
                        Debug.LogError("Parameter " + param.name + " is not of type: " + param.valueType.ToString() + "!");
                    }
                    
                    return;
                }
            }

            var newList = new VRCExpressionParameters.Parameter[vrcExpressionParameters.parameters.Length + 1];
            for (var i = 0; i < vrcExpressionParameters.parameters.Length; i++)
            {
                newList[i] = vrcExpressionParameters.parameters[i];
            }
            
            newList[^1] = new()
            {
                name = name,
                valueType = valueType,
                networkSynced = isNetworkSynced,
                saved = isSaved,
                defaultValue = defaultValue
            };
            
            vrcExpressionParameters.parameters = newList;
        }

        public static void AddUniqueSyncedParamToController(string name, AnimatorController controller, VRCExpressionParameters vrcExpressionParameters, AnimatorControllerParameterType paramType = AnimatorControllerParameterType.Float, VRCExpressionParameters.ValueType valueType = VRCExpressionParameters.ValueType.Float)
        {
            controller.AddUniqueParam(name, paramType);
            vrcExpressionParameters.AddUniqueSyncedParam(name, valueType);
        }

        public static AnimatorControllerLayer AddLayer(this AnimatorController controller, string name, float defaultWeight = 1)
        {
            AnimatorControllerLayer layer = new()
            {
                name = name,
                defaultWeight = defaultWeight,
                stateMachine = new() { hideFlags = HideFlags.HideInHierarchy },
            };
            
            controller.AddLayer(layer);
            
            return layer;
        }

        public static void AddHiddenIdentifier(this AnimatorStateMachine animatorStateMachine, string identifierString)
        {
            var identifier = animatorStateMachine.AddAnyStateTransition((AnimatorStateMachine)null);
            identifier.canTransitionToSelf = false;
            identifier.mute = true;
            identifier.isExit = true;
            identifier.name = identifierString;
        }

        public static List<AnimatorControllerLayer> FindHiddenIdentifier(this AnimatorController animatorController, string identifierString)
        {
            if (animatorController is null)
            {
                return null;
            }
            
            List<AnimatorControllerLayer> returnList = new();

            foreach (var layer in animatorController.layers)
            {
                foreach (var anyStateTransition in layer.stateMachine.anyStateTransitions)
                {
                    if (anyStateTransition.isExit && anyStateTransition.mute && anyStateTransition.name == identifierString)
                    {
                        returnList.Add(layer);
                    }
                }
            }

            return returnList;
        }

        public static void ReadyPath(string path)
        {
            if (!System.IO.Directory.Exists(path))
            {
                System.IO.Directory.CreateDirectory(path);
                AssetDatabase.ImportAsset(path);
            }
        }

        public static AnimationClip MakeAAP(string paramName, string saveAssetsTo, float value = 0, float animLength = 1, string assetName = "") => MakeAAP(new[] { paramName }, saveAssetsTo, value, animLength, assetName);

        public static AnimationClip MakeAAP(string[] paramNames, string saveAssetsTo, float value = 0, float animLength = 1, string assetName = "")
        {
            if (paramNames.Length == 0)
            {
                Debug.LogError("param list is empty!");
            }
            
            if (assetName == "")
            {
                assetName = paramNames[0] + "_AAP " + value;
            }
            
            var saveName = assetName.Replace('/', '_').SanitizeFileName();
            var animClip = (AnimationClip)AssetDatabase.LoadAssetAtPath(saveAssetsTo + saveName + ".anim", typeof(AnimationClip));
            if (animClip is not null)
            {
                return animClip;
            }

            ReadyPath(saveAssetsTo);

            animLength /= 60f;
            animClip = new()
            {
                name = assetName,
                wrapMode = WrapMode.Clamp,
            };
            
            foreach (var paramName in paramNames)
            {
                AnimationCurve curve = new();
                curve.AddKey(0, value);
                curve.AddKey(animLength, value);
                
                animClip.SetCurve("", typeof(Animator), paramName, curve);
            }

            AssetDatabase.CreateAsset(animClip, saveAssetsTo + saveName + ".anim");
            
            return animClip;
        }

        public static string GetAssetPath(this UnityEngine.Object item) => AssetDatabase.GetAssetPath(item);
        
        public static void SaveToAsset(this UnityEngine.Object item, UnityEngine.Object saveTo) => AssetDatabase.AddObjectToAsset(item, AssetDatabase.GetAssetPath(saveTo));

        public static void SaveUnsavedAssetsToController(this AnimatorController controller)
        {
            Queue<ChildAnimatorStateMachine> childStateMachines = new();
            List<ChildAnimatorState> states = new();
            List<AnimatorStateTransition> transitions = new();

            foreach (var layer in controller.layers)
            {
                if (GetAssetPath(layer.stateMachine) == "")
                {
                    layer.stateMachine.SaveToAsset(controller);
                }

                states.AddRange(layer.stateMachine.states);

                foreach (var tempChildStateMachine in layer.stateMachine.stateMachines)
                {
                    childStateMachines.Enqueue(tempChildStateMachine);
                    states.AddRange(tempChildStateMachine.stateMachine.states);
                }

                while (childStateMachines.Count > 0)
                {
                    var childStateMachine = childStateMachines.Dequeue();
                    foreach (var tempChildStateMachine in childStateMachine.stateMachine.stateMachines)
                    {
                        childStateMachines.Enqueue(tempChildStateMachine);
                        states.AddRange(tempChildStateMachine.stateMachine.states);
                    }

                    if (string.IsNullOrEmpty(GetAssetPath(childStateMachine.stateMachine)))
                    {
                        childStateMachine.stateMachine.SaveToAsset(controller);
                    }
                }

                transitions.AddRange(layer.stateMachine.anyStateTransitions);
            }

            foreach (var state in states)
            {
                transitions.AddRange(state.state.transitions);

                if (string.IsNullOrEmpty(GetAssetPath(state.state)))
                {
                    state.state.SaveToAsset(controller);
                }
                
                if (state.state.motion is BlendTree tree)
                {
                    SaveUnsavedBlendtreesToController(tree, controller);
                }
            }

            foreach (var transition in transitions.Where(transition => string.IsNullOrEmpty(GetAssetPath(transition))))
            {
                transition.SaveToAsset(controller);
            }
        }

        public static void SaveUnsavedBlendtreesToController(BlendTree blendTree, UnityEngine.Object saveTo)
        {
            Queue<BlendTree> blendTrees = new();
            blendTrees.Enqueue(blendTree);
            while (blendTrees.Count > 0)
            {
                var subBlendTree = blendTrees.Dequeue();
                if (string.IsNullOrEmpty(GetAssetPath(subBlendTree)))
                {
                    subBlendTree.SaveToAsset(saveTo);
                }
                
                foreach (var child in subBlendTree.children)
                {
                    if (child.motion is BlendTree tree)
                    {
                        blendTrees.Enqueue(tree);
                    }
                }
            }
        }

        public static void DeleteBlendTreeFromAsset(BlendTree blendTree)
        {
            Queue<BlendTree> blendTrees = new();
            blendTrees.Enqueue(blendTree);
            while (blendTrees.Count > 0)
            {
                var subBlendTree = blendTrees.Dequeue();
                AssetDatabase.RemoveObjectFromAsset(subBlendTree);
                foreach (var child in subBlendTree.children)
                {
                    if (child.motion is BlendTree tree)
                    {
                        blendTrees.Enqueue(tree);
                    }
                }
            }
        }

        public static AnimatorControllerParameter AddSmoothedVer(this AnimatorControllerParameter param, float minValue, float maxValue, AnimatorController controller, string smoothedParamName, string saveTo, string smoothingAmountParamName = "SmoothingAmount", string mainBlendTreeIdentifier = "MainBlendTree", string mainBlendTreeLayerName = "MainBlendTree", string smoothingParentTreeName = "SmoothingParentTree", string constantOneName = "ConstantOne")
        {
            var smoothingParentTree = GetOrGenerateChildTree(controller, smoothingParentTreeName, mainBlendTreeIdentifier, mainBlendTreeLayerName, constantOneName);
            
            var smoothingAnimMin = MakeAAP(smoothedParamName, saveTo, minValue, 1, smoothedParamName + minValue);
            var smoothingAnimMax = MakeAAP(smoothedParamName, saveTo, maxValue, 1, smoothedParamName + maxValue);
            
            controller.AddUniqueParam(smoothingAmountParamName, AnimatorControllerParameterType.Float, 0.1f);
            
            var constantOneParam = controller.AddUniqueParam(constantOneName, AnimatorControllerParameterType.Float, 1);
            var smoothedParam = controller.AddUniqueParam(smoothedParamName);

            BlendTree smoothedValue = new()
            {
                blendType = BlendTreeType.Simple1D,
                blendParameter = smoothedParamName,
                name = smoothedParamName,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy
            };

            var tempChildren = new ChildMotion[2];
            tempChildren[0].motion = smoothingAnimMin;
            tempChildren[0].timeScale = 1;
            tempChildren[0].threshold = minValue;

            tempChildren[1].motion = smoothingAnimMax;
            tempChildren[1].timeScale = 1;
            tempChildren[1].threshold = maxValue;

            smoothedValue.children = tempChildren;

            BlendTree originalValue = new()
            {
                blendType = BlendTreeType.Simple1D,
                blendParameter = param.name,
                name = param.name + "_Original",
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy
            };
            
            tempChildren = new ChildMotion[2];
            tempChildren[0].motion = smoothingAnimMin;
            tempChildren[0].timeScale = 1;
            tempChildren[0].threshold = minValue;
            tempChildren[1].motion = smoothingAnimMax;
            tempChildren[1].timeScale = 1;
            tempChildren[1].threshold = maxValue;
            originalValue.children = tempChildren;

            BlendTree smoother = new()
            {
                blendType = BlendTreeType.Simple1D,
                blendParameter = smoothingAmountParamName,
                name = param.name + " Smoothing Tree",
                hideFlags = HideFlags.HideInHierarchy
            };
            
            smoother.AddChild(smoothedValue);
            smoother.AddChild(originalValue);
            smoother.useAutomaticThresholds = false;
            smoother.children[0].threshold = minValue;
            smoother.children[1].threshold = maxValue;

            smoothingParentTree.AddChild(smoother);
            tempChildren = smoothingParentTree.children;
            tempChildren[^1].directBlendParameter = constantOneParam.name;
            smoothingParentTree.children = tempChildren;
            return smoothedParam;
        }

        public static AnimatorControllerParameter AddParamDifferential(AnimatorControllerParameter param1, AnimatorControllerParameter param2, AnimatorController controller, string saveTo, float minValue = -1, float maxValue = 1, string differentialParamName = "", string mainBlendTreeIdentifier = "MainBlendTree", string mainBlendTreeLayerName = "MainBlendTree", string differentialParentTreeName = "DifferentialParentTree", string constantOneName = "ConstantOne")
        {
            var differentialParentTree = GetOrGenerateChildTree(controller, differentialParentTreeName, mainBlendTreeIdentifier, mainBlendTreeLayerName, constantOneName);
            var constantOneParam = controller.AddUniqueParam(constantOneName, AnimatorControllerParameterType.Float, 1);
            if (differentialParamName == "")
            {
                differentialParamName = param1.name + "_Minus_" + param2.name;
                for (var i = 0; i < differentialParamName.Length; i++)
                {
                    if (differentialParamName[i] == '/' || differentialParamName[i] == '\\')
                    {
                        differentialParamName.Remove(i);
                    }
                }
            }

            var differentialParam = controller.AddUniqueParam(differentialParamName);

            if (minValue >= 0 && maxValue >= 0)
            {
                var animationClipNegative = MakeAAP(differentialParamName, saveTo, -1);
                var animationClipPositive = MakeAAP(differentialParamName, saveTo, 1);

                differentialParentTree.AddChild(animationClipPositive);
                differentialParentTree.AddChild(animationClipNegative);
                var tempChildren = differentialParentTree.children;
                tempChildren[^2].directBlendParameter = param1.name;
                tempChildren[^1].directBlendParameter = param2.name;

                differentialParentTree.children = tempChildren;
            }
            else
            {
                var animationClipMin = MakeAAP(differentialParamName, saveTo, minValue);
                var animationClipMax = MakeAAP(differentialParamName, saveTo, maxValue);
                controller.AddUniqueParam(differentialParamName);

                BlendTree param1Tree = new() { blendType = BlendTreeType.Simple1D, blendParameter = param1.name, name = param1.name + "Tree", useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
                BlendTree param2Tree = new() { blendType = BlendTreeType.Simple1D, blendParameter = param2.name, name = param2.name + "Tree", useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };

                var tempChildren = new ChildMotion[2];
                tempChildren[0].motion = animationClipMin;
                tempChildren[0].threshold = -1;
                tempChildren[0].timeScale = 1;
                tempChildren[1].motion = animationClipMax;
                tempChildren[1].threshold = 1;
                tempChildren[1].timeScale = 1;
                param1Tree.children = tempChildren;

                tempChildren = new ChildMotion[2];
                tempChildren[0].motion = animationClipMax;
                tempChildren[0].threshold = -1;
                tempChildren[0].timeScale = 1;
                tempChildren[1].motion = animationClipMin;
                tempChildren[1].threshold = 1;
                tempChildren[1].timeScale = 1;
                param2Tree.children = tempChildren;

                differentialParentTree.AddChild(param1Tree);
                differentialParentTree.AddChild(param2Tree);

                tempChildren = differentialParentTree.children;
                tempChildren[^2].directBlendParameter = constantOneParam.name;
                tempChildren[^1].directBlendParameter = constantOneParam.name;
                differentialParentTree.children = tempChildren;
            }

            return differentialParam;
        }

        public static BlendTree GetOrGenerateMainBlendTree(AnimatorController fxLayer, string mainBlendTreeIdentifier, string layerName, string constantOneName) => GetMainBlendTree(fxLayer, mainBlendTreeIdentifier) ?? GenerateMainBlendTree(fxLayer, mainBlendTreeIdentifier, layerName, constantOneName);

        private static BlendTree GetMainBlendTree(AnimatorController fxLayer, string mainBlendTreeIdentifier)
        {
            var mainBlendTrees = FindHiddenIdentifier(fxLayer, mainBlendTreeIdentifier);

            if (mainBlendTrees.Count > 0 && mainBlendTrees[0].stateMachine.states.Length > 0 && mainBlendTrees[0].stateMachine.states[0].state.motion is BlendTree tree)
            {
                return tree;
            }

            return null;
        }

        private static BlendTree GenerateMainBlendTree(AnimatorController fxLayer, string mainBlendTreeIdentifier, string layerName, string constantOneName)
        {
            fxLayer.AddUniqueParam(constantOneName, AnimatorControllerParameterType.Float, 1);

            var mainBlendTreeLayer = AddLayer(fxLayer, layerName);

            mainBlendTreeLayer.stateMachine.name = layerName;
            mainBlendTreeLayer.stateMachine.anyStatePosition = new(20, 20, 0);
            mainBlendTreeLayer.stateMachine.entryPosition = new(20, 50, 0);
            
            var state = mainBlendTreeLayer.stateMachine.AddState("MainBlendTree (WD On)", new(0, 100, 0));
            state.hideFlags = HideFlags.HideInHierarchy;
            
            BlendTree mainBlendTree = new()
            {
                hideFlags = HideFlags.HideInHierarchy,
                blendType = BlendTreeType.Direct,
                blendParameter = constantOneName,
                name = "MainBlendTree",
            };
            
            state.motion = mainBlendTree;
            state.writeDefaultValues = true;
            
            mainBlendTreeLayer.stateMachine.AddHiddenIdentifier(mainBlendTreeIdentifier);
            
            return (BlendTree)state.motion;
        }

        public static BlendTree GetOrGenerateChildTree(AnimatorController fxLayer, string name, string mainBlendTreeIdentifier, string mainBlendTreeLayerName, string constantOneName) => GetChildTree(fxLayer, name, mainBlendTreeIdentifier, mainBlendTreeLayerName, constantOneName) ?? GenerateChildTree(fxLayer, name, mainBlendTreeIdentifier, mainBlendTreeLayerName, constantOneName);

        private static BlendTree GenerateChildTree(AnimatorController controller, string name, string mainBlendTreeIdentifier, string mainBlendTreeLayerName, string constantOneName)
        {
            var mainBlendTree = GetOrGenerateMainBlendTree(controller, mainBlendTreeIdentifier, mainBlendTreeLayerName, constantOneName);

            BlendTree smoothedParentTree = new()
            {
                hideFlags = HideFlags.HideInHierarchy,
                blendType = BlendTreeType.Direct,
                blendParameter = constantOneName,
                name = name,
            };
            
            mainBlendTree.AddChild(smoothedParentTree);

            var tempChildren = mainBlendTree.children;
            tempChildren[^1].directBlendParameter = constantOneName;
            mainBlendTree.children = tempChildren;

            return (BlendTree)mainBlendTree.children.Last().motion;
        }

        private static BlendTree GetChildTree(AnimatorController controller, string name, string mainBlendTreeIdentifier, string mainBlendTreeLayerName, string constantOneName)
        {
            var mainBlendTree = GetOrGenerateMainBlendTree(controller, mainBlendTreeIdentifier, mainBlendTreeLayerName, constantOneName);

            foreach (var child in mainBlendTree.children)
            {
                if (child.motion.name == name)
                {
                    return (BlendTree)child.motion;
                }
            }

            return null;
        }

        public static int DecimalToBinary(this int i)
        {
            if (i <= 0)
            {
                return 0;
            }

            var result = "";
            for (var j = i; j > 0; j /= 2)
            {
                result = (j % 2).ToString() + result;
            }

            return int.Parse(result);
        }

        public static int GetVRCExpressionParameterCost(this VRCExpressionParameters.Parameter parameter)
        {
            return parameter.networkSynced ? 0 : parameter.valueType == VRCExpressionParameters.ValueType.Bool ? 1 : 8;
        }

        internal class TESPerformanceLogger : IDisposable
        {
            private readonly string _message;
            private readonly UnityEngine.Object _context;
            private readonly Stopwatch _w;
            
            public TESPerformanceLogger(string message = null, UnityEngine.Object context = null)
            {
                _message = string.IsNullOrEmpty(message) ? "TESPerformanceLogger finished in {0}" : message;
                
                _context = context;
                
                _w = new Stopwatch();
                _w.Start();
            }
            
            public void Dispose()
            {
                _w.Stop();
                
                var elapsed = _w.Elapsed;
                var message = string.Format(_message, $"{elapsed.TotalSeconds:#0}s-{elapsed.TotalMilliseconds:##0}ms");
                
                if (_context is not null)
                {
                    Debug.LogWarning(message, _context);
                }
                else
                {
                    Debug.LogWarning(message);
                }
            }
        }
    }
}

#endif
