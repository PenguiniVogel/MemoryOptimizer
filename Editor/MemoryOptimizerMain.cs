using System;
using System.Collections.Generic;
using System.Linq;
using JeTeeS.MemoryOptimizer.Shared;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using JeTeeS.TES.HelperFunctions;
using static JeTeeS.MemoryOptimizer.Shared.MemoryOptimizerConstants;
using static JeTeeS.TES.HelperFunctions.TESHelperFunctions;

namespace JeTeeS.MemoryOptimizer
{
    internal static class MemoryOptimizerMain
    {
        internal class ParamDriversAndStates
        {
            public VRCAvatarParameterDriver paramDriver = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
            public List<AnimatorState> states = new();
        }

        private class MemoryOptimizerState
        {
            public AnimationClip oneSecBuffer;
            public AnimationClip oneFrameBuffer;
            public VRCAvatarDescriptor avatar;
            public AnimatorController FXController;
            public VRCExpressionParameters expressionParameters;
            public AnimatorStateMachine localStateMachine, remoteStateMachine;
            public List<MemoryOptimizerListData> boolsToOptimize, intsNFloatsToOptimize;
            public List<AnimatorControllerParameter> boolsDifferentials, intsNFloatsDifferentials, boolsNIntsWithCopies;
            public List<AnimatorState> localSetStates, localResetStates, remoteSetStates;
            public List<ParamDriversAndStates> localSettersParameterDrivers, remoteSettersParameterDrivers;
            public AnimatorControllerLayer syncingLayer;
        }

        public static void InstallMemOpt(VRCAvatarDescriptor avatarIn, AnimatorController fxLayer, VRCExpressionParameters expressionParameters, List<MemoryOptimizerListData> optimizeBoolList, List<MemoryOptimizerListData> optimizeIntNFloatList, int syncSteps, float stepDelay, bool generateChangeDetection, int wdOption, string mainFilePath)
        {
            var generatedAssetsFilePath = mainFilePath + "/GeneratedAssets/";
            ReadyPath(generatedAssetsFilePath);

            MemoryOptimizerState optimizerState = new()
            {
                avatar = avatarIn,
                FXController = fxLayer,
                expressionParameters = expressionParameters,
                boolsToOptimize = optimizeBoolList,
                intsNFloatsToOptimize = optimizeIntNFloatList,
                boolsNIntsWithCopies = new(),

                syncingLayer = new()
                {
                    defaultWeight = 1,
                    name = syncingLayerName,
                    stateMachine = new()
                    {
                        hideFlags = HideFlags.HideInHierarchy,
                        name = syncingLayerName,
                        anyStatePosition = new(20, 20, 0),
                        entryPosition = new(20, 50, 0)
                    }
                }
            };
            optimizerState.syncingLayer.stateMachine.AddHiddenIdentifier(syncingLayerIdentifier);

            (optimizerState.oneFrameBuffer, optimizerState.oneSecBuffer) = BufferAnims(generatedAssetsFilePath);

            fxLayer.AddUniqueParam("IsLocal", AnimatorControllerParameterType.Bool);
            fxLayer.AddUniqueParam(constantOneName, AnimatorControllerParameterType.Float, 1);
            fxLayer.AddUniqueParam(smoothingAmountParamName);

            var syncStepsBinary = (syncSteps - 1).DecimalToBinary().ToString();
            for (var i = 0; i < syncStepsBinary.Count(); i++)
            {
                AddUniqueSyncedParamToController(indexerParamName + (i + 1).ToString(), fxLayer, expressionParameters, AnimatorControllerParameterType.Bool, VRCExpressionParameters.ValueType.Bool);
            }

            for (var j = 0; j < optimizeBoolList.Count / syncSteps; j++)
            {
                AddUniqueSyncedParamToController(boolSyncerParamName + j, optimizerState.FXController, optimizerState.expressionParameters, AnimatorControllerParameterType.Bool, VRCExpressionParameters.ValueType.Bool);
            }

            for (var j = 0; j < optimizeIntNFloatList.Count / syncSteps; j++)
            {
                AddUniqueSyncedParamToController(intNFloatSyncerParamName + j, optimizerState.FXController, optimizerState.expressionParameters, AnimatorControllerParameterType.Int, VRCExpressionParameters.ValueType.Int);
            }

            CreateLocalRemoteSplit(optimizerState);

            if (generateChangeDetection)
            {
                GenerateDeltas(optimizerState, generatedAssetsFilePath);
            }

            var localEntryState = optimizerState.localStateMachine.AddState("Entry", new(0, 100, 0));
            localEntryState.hideFlags = HideFlags.HideInHierarchy;
            localEntryState.motion = optimizerState.oneFrameBuffer;

            CreateStates(optimizerState, syncSteps, stepDelay, generateChangeDetection);

            //add transition from local entry to 1st set value
            localEntryState.AddTransition(new AnimatorStateTransition { destinationState = optimizerState.localSetStates[0], exitTime = 0, hasExitTime = true, hasFixedDuration = true, duration = 0f, hideFlags = HideFlags.HideInHierarchy });

            CreateTransitions(optimizerState, syncSteps, stepDelay, generateChangeDetection);

            CreateParameterDrivers(optimizerState, syncSteps, generateChangeDetection);

            var setWD = true;
            switch (wdOption)
            {
                case 0:
                {
                    var foundWD = fxLayer.FindWDInController();
                    setWD = foundWD switch
                    {
                        -1 => true,
                        0 => false,
                        1 => true,
                        _ => setWD
                    };
                    break;
                }
                case 1:
                    setWD = false;
                    break;
                default:
                    setWD = true;
                    break;
            }

            foreach (var state in optimizerState.syncingLayer.FindAllStatesInLayer())
            {
                state.state.writeDefaultValues = setWD;
            }

            optimizerState.FXController.AddLayer(optimizerState.syncingLayer);
            optimizerState.FXController.SaveUnsavedAssetsToController();

            foreach (var param in optimizeBoolList)
            {
                param.param.networkSynced = false;
            }

            foreach (var param in optimizeIntNFloatList)
            {
                param.param.networkSynced = false;
            }
            
            EditorUtility.SetDirty(expressionParameters);

            AssetDatabase.SaveAssets();

            SetupParameterDrivers(optimizerState);
            
            // EditorApplication.Beep();
            Debug.Log("<color=yellow>[MemoryOptimizer]</color> Installation Complete");
        }

        private static void GenerateDeltas(MemoryOptimizerState optimizerState, string generatedAssetsFilePath)
        {
            var boolsToOptimize = optimizerState.boolsToOptimize;
            var intsNFloatsToOptimize = optimizerState.intsNFloatsToOptimize;

            List<AnimatorControllerParameter> boolsDifferentials = new();
            List<AnimatorControllerParameter> intsNFloatsDifferentials = new();
            
            // Add smoothed ver of every param in the list
            foreach (var param in boolsToOptimize)
            {
                var paramMatches = optimizerState.FXController.parameters.Where(x => x.name == param.param.name).ToList();
                var paramMatch = paramMatches[0];
                
                if (paramMatch.type is AnimatorControllerParameterType.Int or AnimatorControllerParameterType.Bool)
                {
                    var paramCopy = optimizerState.FXController.AddUniqueParam(prefix + paramMatch.name + "_Copy");
                    optimizerState.boolsNIntsWithCopies.Add(paramMatch);
                    
                    var smoothedParam = paramCopy.AddSmoothedVer(0, 1, optimizerState.FXController, prefix + paramCopy.name + smoothedVerSuffix, generatedAssetsFilePath, smoothingAmountParamName, mainBlendTreeIdentifier, mainBlendTreeLayerName, SmoothingTreeName, constantOneName);
                    boolsDifferentials.Add(AddParamDifferential(paramCopy, smoothedParam, optimizerState.FXController, generatedAssetsFilePath, 0, 1, prefix + paramCopy.name + DifferentialSuffix, mainBlendTreeIdentifier, mainBlendTreeLayerName, DifferentialTreeName, constantOneName));
                }
                else if (paramMatch.type == AnimatorControllerParameterType.Float)
                {
                    var smoothedParam = paramMatch.AddSmoothedVer(0, 1, optimizerState.FXController, prefix + paramMatch.name + smoothedVerSuffix, generatedAssetsFilePath, smoothingAmountParamName, mainBlendTreeIdentifier, mainBlendTreeLayerName, SmoothingTreeName, constantOneName);
                    boolsDifferentials.Add(AddParamDifferential(paramMatch, smoothedParam, optimizerState.FXController, generatedAssetsFilePath, 0, 1, prefix + paramMatch.name + DifferentialSuffix, mainBlendTreeIdentifier, mainBlendTreeLayerName, DifferentialTreeName, constantOneName));
                }
                else
                {
                    Debug.LogError("<color=yellow>[MemoryOptimizer]</color> Param " + param.param.name + "is not bool, int or float!");
                }
            }

            foreach (var param in intsNFloatsToOptimize)
            {
                var paramMatches = optimizerState.FXController.parameters.Where(x => x.name == param.param.name).ToList();
                var paramMatch = paramMatches[0];
                
                if (paramMatch.type is AnimatorControllerParameterType.Int or AnimatorControllerParameterType.Bool)
                {
                    var paramCopy = optimizerState.FXController.AddUniqueParam(prefix + paramMatch.name + "_Copy");
                    optimizerState.boolsNIntsWithCopies.Add(paramMatch);
                    
                    var smoothedParam = paramCopy.AddSmoothedVer(0, 1, optimizerState.FXController, prefix + paramCopy.name + smoothedVerSuffix, generatedAssetsFilePath, smoothingAmountParamName, mainBlendTreeIdentifier, mainBlendTreeLayerName, SmoothingTreeName, constantOneName);
                    intsNFloatsDifferentials.Add(AddParamDifferential(paramCopy, smoothedParam, optimizerState.FXController, generatedAssetsFilePath, 0, 1, prefix + paramCopy.name + DifferentialSuffix, mainBlendTreeIdentifier, mainBlendTreeLayerName, DifferentialTreeName, constantOneName));
                }
                else if (paramMatch.type == AnimatorControllerParameterType.Float)
                {
                    var smoothedParam = paramMatch.AddSmoothedVer(-1, 1, optimizerState.FXController, prefix + paramMatch.name + smoothedVerSuffix, generatedAssetsFilePath, smoothingAmountParamName, mainBlendTreeIdentifier, mainBlendTreeLayerName, SmoothingTreeName, constantOneName);
                    intsNFloatsDifferentials.Add(AddParamDifferential(paramMatch, smoothedParam, optimizerState.FXController, generatedAssetsFilePath, -1, 1, prefix + paramMatch.name + DifferentialSuffix, mainBlendTreeIdentifier, mainBlendTreeLayerName, DifferentialTreeName, constantOneName));
                }
                else
                {
                    Debug.LogError("<color=yellow>[MemoryOptimizer]</color> Param " + param.param.name + "is not bool, int or float!");
                }
            }

            optimizerState.boolsDifferentials = boolsDifferentials;
            optimizerState.intsNFloatsDifferentials = intsNFloatsDifferentials;
        }

        private static void CreateTransitions(MemoryOptimizerState optimizerState, int syncSteps, float stepDelay, bool generateChangeDetection)
        {
            var optimizeBoolList = optimizerState.boolsToOptimize;
            var optimizeIntNFloatList = optimizerState.intsNFloatsToOptimize;
            var localSetStates = optimizerState.localSetStates;
            var remoteSetStates = optimizerState.remoteSetStates;
            var localResetStates = optimizerState.localResetStates;
            var differentialsBool = optimizerState.boolsDifferentials;
            var differentialsIntNFloat = optimizerState.intsNFloatsDifferentials;
            var remoteStateMachine = optimizerState.remoteStateMachine;

            var syncStepsBinary = (syncSteps - 1).DecimalToBinary().ToString();

            var waitForIndexer = remoteStateMachine.AddState("WaitForIndexer", new(0, 400, 0));
            waitForIndexer.hideFlags = HideFlags.HideInHierarchy;
            waitForIndexer.motion = optimizerState.oneFrameBuffer;

            for (var i = 0; i < syncSteps; i++)
            {
                var currentIndex = i.DecimalToBinary().ToString();
                while (currentIndex.Length < syncStepsBinary.Length)
                {
                    currentIndex = "0" + currentIndex;
                }

                AnimatorStateTransition toSetterTransition = new()
                {
                    destinationState = remoteSetStates[i],
                    exitTime = 0,
                    hasExitTime = true,
                    hasFixedDuration = true,
                    duration = 0f,
                    hideFlags = HideFlags.HideInHierarchy
                };


                // Make a list of transitions that go to the "wait" state
                List<AnimatorStateTransition> toWaitTransitions = new();

                // loop through each character of the binary number
                for (var j = 1; j <= currentIndex.Length; j++)
                {
                    var isZero = currentIndex[^j].ToString() == "0";
                    toSetterTransition.AddCondition(isZero ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If, 0, indexerParamName + j);
                    toWaitTransitions.Add(new()
                    {
                        destinationState = waitForIndexer,
                        exitTime = 0,
                        hasExitTime = false,
                        hasFixedDuration = true,
                        duration = 0f,
                        hideFlags = HideFlags.HideInHierarchy
                    });
                    toWaitTransitions.Last().AddCondition(isZero ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, indexerParamName + j);
                }

                if (generateChangeDetection)
                {
                    void SetupLocalResetStateTransitions(string differentialName)
                    {
                        // add transitions from value changed state to appropriate reset state
                        foreach (var state in localSetStates)
                        {
                            AnimatorStateTransition transition = new()
                            {
                                destinationState = localResetStates[i],
                                exitTime = 0,
                                hasExitTime = false,
                                hasFixedDuration = true,
                                duration = 0f,
                                hideFlags = HideFlags.HideInHierarchy
                            };
                            
                            transition.AddCondition(AnimatorConditionMode.Less, changeSensitivity * -1, differentialName);
                            state.AddTransition(transition);

                            transition = new()
                            {
                                destinationState = localResetStates[i],
                                exitTime = 0,
                                hasExitTime = false,
                                hasFixedDuration = true,
                                duration = 0f,
                                hideFlags = HideFlags.HideInHierarchy
                            };
                            
                            transition.AddCondition(AnimatorConditionMode.Greater, changeSensitivity, differentialName);
                            state.AddTransition(transition);
                        }
                    }

                    for (var j = 0; j < optimizeBoolList.Count / syncSteps; j++)
                    {
                        SetupLocalResetStateTransitions(differentialsBool[i * (optimizeBoolList.Count() / syncSteps) + j].name);
                    }

                    for (var j = 0; j < optimizeIntNFloatList.Count / syncSteps; j++)
                    {
                        SetupLocalResetStateTransitions(differentialsIntNFloat[i * (optimizeIntNFloatList.Count() / syncSteps) + j].name);
                    }
                }

                // add the transitions from remote set states to the wait state
                foreach (var transition in toWaitTransitions)
                {
                    remoteSetStates[i].AddTransition(transition);
                }

                // add transition from wait state to current set state
                waitForIndexer.AddTransition(toSetterTransition);
            }

            for (var i = 0; i < localSetStates.Count; i++)
            {
                localSetStates[i].AddTransition(new AnimatorStateTransition()
                {
                    destinationState = localSetStates[(i + 1) % localSetStates.Count],
                    exitTime = stepDelay,
                    hasExitTime = true,
                    hasFixedDuration = true,
                    duration = 0f,
                    hideFlags = HideFlags.HideInHierarchy
                });
            }
        }

        private static void CreateParameterDrivers(MemoryOptimizerState optimizerState, int syncSteps, bool generateChangeDetection)
        {
            var localSetStates = optimizerState.localSetStates;
            var localResetStates = optimizerState.localResetStates;
            var remoteSetStates = optimizerState.remoteSetStates;
            
            var optimizeBoolList = optimizerState.boolsToOptimize;
            var optimizeIntNFloatList = optimizerState.intsNFloatsToOptimize;

            List<ParamDriversAndStates> localSettersParameterDrivers = new();
            List<ParamDriversAndStates> remoteSettersParameterDrivers = new();
            
            var syncStepsBinary = (syncSteps - 1).DecimalToBinary().ToString();
            for (var i = 0; i < syncSteps; i++)
            {
                var currentIndex = i.DecimalToBinary().ToString();
                while (currentIndex.Length < syncStepsBinary.Length)
                {
                    currentIndex = "0" + currentIndex;
                }

                localSettersParameterDrivers.Add(new());
                localSettersParameterDrivers.Last().states.Add(localSetStates[i]);
                if (generateChangeDetection)
                {
                    localSettersParameterDrivers.Last().states.Add(localResetStates[i]);

                    foreach (var param in optimizerState.boolsNIntsWithCopies)
                    {
                        localSettersParameterDrivers.Last().paramDriver.parameters.Add(new()
                        {
                            name = prefix + param.name + "_Copy",
                            source = param.name,
                            type = VRC_AvatarParameterDriver.ChangeType.Copy
                        });
                    }
                }

                remoteSettersParameterDrivers.Add(new());
                remoteSettersParameterDrivers.Last().states.Add(remoteSetStates[i]);

                // loop through each character of the binary number
                for (var j = 1; j <= currentIndex.Length; j++)
                {
                    var value = currentIndex[^j].ToString() == "0" ? 0 : 1;
                    localSettersParameterDrivers.Last().paramDriver.parameters.Add(new()
                    {
                        name = indexerParamName + j,
                        value = value,
                        type = VRC_AvatarParameterDriver.ChangeType.Set
                    });
                    
                    remoteSettersParameterDrivers.Last().paramDriver.parameters.Add(new()
                    {
                        name = indexerParamName + j,
                        value = value,
                        type = VRC_AvatarParameterDriver.ChangeType.Set
                    });
                }

                for (var j = 0; j < optimizeBoolList.Count / syncSteps; j++)
                {
                    var param = optimizeBoolList.ElementAt(i * (optimizeBoolList.Count() / syncSteps) + j).param;
                    localSettersParameterDrivers.Last().paramDriver.parameters.Add(new()
                    {
                        name = boolSyncerParamName + j,
                        source = param.name,
                        type = VRC_AvatarParameterDriver.ChangeType.Copy
                    });
                    
                    remoteSettersParameterDrivers.Last().paramDriver.parameters.Add(new()
                    {
                        name = param.name,
                        source = boolSyncerParamName + j,
                        type = VRC_AvatarParameterDriver.ChangeType.Copy
                    });
                }

                for (var j = 0; j < optimizeIntNFloatList.Count / syncSteps; j++)
                {
                    var param = optimizeIntNFloatList.ElementAt(i * (optimizeIntNFloatList.Count() / syncSteps) + j).param;
                    if (param.valueType == VRCExpressionParameters.ValueType.Int)
                    {
                        localSettersParameterDrivers.Last().paramDriver.parameters.Add(new()
                        {
                            name = intNFloatSyncerParamName + j,
                            source = param.name,
                            type = VRC_AvatarParameterDriver.ChangeType.Copy
                        });
                        
                        remoteSettersParameterDrivers.Last().paramDriver.parameters.Add(
                            new()
                            {
                                name = param.name,
                                source = intNFloatSyncerParamName + j,
                                type = VRC_AvatarParameterDriver.ChangeType.Copy
                            });
                    }
                    else if (param.valueType == VRCExpressionParameters.ValueType.Float)
                    {
                        localSettersParameterDrivers.Last().paramDriver.parameters.Add(new()
                        {
                            name = intNFloatSyncerParamName + j,
                            source = param.name,
                            type = VRC_AvatarParameterDriver.ChangeType.Copy,
                            convertRange = true,
                            destMin = 0,
                            destMax = 255,
                            sourceMin = 0,
                            sourceMax = 1
                        });
                        
                        remoteSettersParameterDrivers.Last().paramDriver.parameters.Add(new()
                        {
                            name = param.name,
                            source = intNFloatSyncerParamName + j,
                            type = VRC_AvatarParameterDriver.ChangeType.Copy,
                            convertRange = true,
                            destMin = 0,
                            destMax = 1,
                            sourceMin = 0,
                            sourceMax = 255
                        });
                    }
                    else
                    {
                        Debug.LogError("<color=yellow>[MemoryOptimizer]</color> " + param.name + " is not an int or a float!");
                    }
                }
            }

            optimizerState.localSettersParameterDrivers = localSettersParameterDrivers;
            optimizerState.remoteSettersParameterDrivers = remoteSettersParameterDrivers;
        }

        private static void CreateStates(MemoryOptimizerState optimizerState, int syncSteps, float stepDelay, bool generateChangeDetection)
        {
            var syncStepsBinary = (syncSteps - 1).DecimalToBinary().ToString();
            
            var localStateMachine = optimizerState.localStateMachine;
            var remoteStateMachine = optimizerState.remoteStateMachine;

            List<AnimatorState> localSetStates = new();
            List<AnimatorState> localResetStates = new();
            List<AnimatorState> remoteSetStates = new();
            
            for (var i = 0; i < syncSteps; i++)
            {
                // convert i to binary, so it can be used for the binary counter
                var currentIndex = i.DecimalToBinary().ToString();
                while (currentIndex.Length < syncStepsBinary.Length)
                {
                    currentIndex = "0" + currentIndex;
                }

                // add the local set and reset states
                localSetStates.Add(localStateMachine.AddState("Set Value " + (i + 1), AngleRadiusToPos(((float)i / syncSteps + 0.5f) * (float)Math.PI * 2f, 400f, new(0, 600, 0))));
                localSetStates.Last().hideFlags = HideFlags.HideInHierarchy;
                localSetStates.Last().motion = optimizerState.oneSecBuffer;

                if (generateChangeDetection)
                {
                    localResetStates.Add(localStateMachine.AddState("Reset Change Detection " + (i + 1), AngleRadiusToPos(((float)i / syncSteps + 0.5f) * (float)Math.PI * 2f + ((float)Math.PI * 0.25f), 480f, new(0, 600, 0))));
                    localResetStates.Last().hideFlags = HideFlags.HideInHierarchy;
                    localResetStates.Last().motion = optimizerState.oneSecBuffer;

                    localResetStates.Last().AddTransition(new AnimatorStateTransition()
                    {
                        destinationState = localSetStates.Last(),
                        exitTime = stepDelay / 4,
                        hasExitTime = true,
                        hasFixedDuration = true,
                        duration = 0f,
                        hideFlags = HideFlags.HideInHierarchy
                    });
                }

                // add the remote set states
                remoteSetStates.Add(remoteStateMachine.AddState("Set values for index " + (i + 1), AngleRadiusToPos(((float)i / syncSteps + 0.5f) * (float)Math.PI * 2f, 250f, new(0, 400, 0))));
                remoteSetStates.Last().hideFlags = HideFlags.HideInHierarchy;
                remoteSetStates.Last().motion = optimizerState.oneFrameBuffer;
            }

            optimizerState.localSetStates = localSetStates;
            optimizerState.localResetStates = localResetStates;
            optimizerState.remoteSetStates = remoteSetStates;
        }

        private static (AnimationClip oneFrameBuffer, AnimationClip oneSecBuffer) BufferAnims(string generatedAssetsFilePath)
        {
            // create and overwrite single frame buffer animation
            AnimationClip oneFrameBuffer = new() { name = oneFrameBufferAnimName, };
            AnimationCurve oneFrameBufferCurve = new();
            
            oneFrameBufferCurve.AddKey(0, 0);
            oneFrameBufferCurve.AddKey(1 / 60f, 1);
            
            oneFrameBuffer.SetCurve("", typeof(GameObject), "DO NOT CHANGE THIS ANIMATION", oneFrameBufferCurve);
            
            AssetDatabase.DeleteAsset(generatedAssetsFilePath + oneFrameBuffer.name + ".anim");
            AssetDatabase.CreateAsset(oneFrameBuffer, generatedAssetsFilePath + oneFrameBuffer.name + ".anim");

            // create and overwrite one second buffer animation
            AnimationClip oneSecBuffer = new() { name = oneSecBufferAnimName, };
            AnimationCurve oneSecBufferCurve = new();
            
            oneSecBufferCurve.AddKey(0, 0);
            oneSecBufferCurve.AddKey(1, 1);
            
            oneSecBuffer.SetCurve("", typeof(GameObject), "DO NOT CHANGE THIS ANIMATION", oneSecBufferCurve);
            
            AssetDatabase.DeleteAsset(generatedAssetsFilePath + oneSecBuffer.name + ".anim");
            AssetDatabase.CreateAsset(oneSecBuffer, generatedAssetsFilePath + oneSecBuffer.name + ".anim");
            
            return (oneFrameBuffer, oneSecBuffer);
        }

        private static void SetupParameterDrivers(MemoryOptimizerState optimizerState)
        {
            var localSettersParameterDrivers = optimizerState.localSettersParameterDrivers;
            var remoteSettersParameterDrivers = optimizerState.remoteSettersParameterDrivers;
            var localSetStates = optimizerState.localSetStates;
            var localResetStates = optimizerState.localResetStates;

            foreach (var driver in localSettersParameterDrivers)
            {
                foreach (var state in driver.states)
                {
                    var temp = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                    temp.parameters = driver.paramDriver.parameters.ToList();
                }
            }

            foreach (var driver in remoteSettersParameterDrivers)
            {
                foreach (var state in driver.states)
                {
                    var temp = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                    temp.parameters = driver.paramDriver.parameters;
                }
            }

            foreach (var state in localSetStates)
            {
                var temp = (VRCAvatarParameterDriver)state.behaviours[0];
                temp.parameters.Add(new()
                {
                    name = smoothingAmountParamName,
                    type = VRC_AvatarParameterDriver.ChangeType.Set,
                    value = 0
                });
            }

            foreach (var state in localResetStates)
            {
                var temp = (VRCAvatarParameterDriver)state.behaviours[0];
                temp.parameters.Add(new()
                {
                    name = smoothingAmountParamName,
                    type = VRC_AvatarParameterDriver.ChangeType.Set,
                    value = 1
                });
            }
        }

        private static void CreateLocalRemoteSplit(MemoryOptimizerState optimizerState)
        {
            var syncingLayer = optimizerState.syncingLayer;
            var localRemoteSplitState = syncingLayer.stateMachine.AddState("Local/Remote split", position: new(0, 100, 0));
            
            localRemoteSplitState.motion = optimizerState.oneFrameBuffer;
            localRemoteSplitState.hideFlags = HideFlags.HideInHierarchy;
            syncingLayer.stateMachine.defaultState = localRemoteSplitState;

            var localStateMachine = syncingLayer.stateMachine.AddStateMachine("Local", position: new(100, 200, 0));
            localStateMachine.hideFlags = HideFlags.HideInHierarchy;

            var remoteStateMachine = syncingLayer.stateMachine.AddStateMachine("Remote", position: new(-100, 200, 0));
            remoteStateMachine.hideFlags = HideFlags.HideInHierarchy;

            localStateMachine.anyStatePosition = new(20, 20, 0);
            localStateMachine.entryPosition = new(20, 50, 0);

            remoteStateMachine.anyStatePosition = new(20, 20, 0);
            remoteStateMachine.entryPosition = new(20, 50, 0);

            var localTransition = localRemoteSplitState.AddTransition(localStateMachine);
            var remoteTransition = localRemoteSplitState.AddTransition(remoteStateMachine);

            // in some rare cases some types of FaceTracking (or other OSC based components) use a float-based IsLocal parameter
            // this will prevent the layer from working correctly as the transition binding no longer matches the type
            if (optimizerState.FXController.parameters.Any(p => p.name.Equals("IsLocal") && p.type == AnimatorControllerParameterType.Float))
            {
                localTransition.AddCondition(AnimatorConditionMode.Greater, 0.5f, "IsLocal");
                remoteTransition.AddCondition(AnimatorConditionMode.Less, 0.5f, "IsLocal");
            }
            else
            {
                localTransition.AddCondition(AnimatorConditionMode.If, 0, "IsLocal");
                remoteTransition.AddCondition(AnimatorConditionMode.IfNot, 0, "IsLocal");
            }
            
            optimizerState.localStateMachine = localStateMachine;
            optimizerState.remoteStateMachine = remoteStateMachine;
        }

        public static void UninstallMemOpt(VRCAvatarDescriptor avatar, AnimatorController fxLayer, VRCExpressionParameters expressionParameters)
        {
            List<VRCExpressionParameters.Parameter> generatedExpressionParams = new();
            List<VRCExpressionParameters.Parameter> optimizedParams = new();
            List<AnimatorControllerParameter> generatedAnimatorParams = new();
            
            foreach (var controllerParam in fxLayer.parameters)
            {
                if (controllerParam.name.Contains(prefix))
                {
                    generatedAnimatorParams.Add(controllerParam);
                }
            }

            var mainBlendTreeLayers = fxLayer.FindHiddenIdentifier(mainBlendTreeIdentifier);
            var syncingLayers = fxLayer.FindHiddenIdentifier(syncingLayerIdentifier);

            if (mainBlendTreeLayers.Count > 1)
            {
                if (UninstallErrorDialogWithDiscordLink($"Too many MemOptBlendtrees found", $"Too many MemOptBlendtrees found! {mainBlendTreeLayers.Count} found. \nPlease join the discord for support. \nKeep in mind there are backups made by default by the script!", discordLink) != 0)
                {
                    return;
                }
            }

            if (syncingLayers.Count != 1)
            {
                var s = (mainBlendTreeLayers.Count > 1) ? "many" : "few";
                if (UninstallErrorDialogWithDiscordLink($"Too {s} syncing layers found", $"Too {s} syncing layers found! {syncingLayers.Count} found. \nPlease join the discord for support. \nKeep in mind there are backups made by default by the script!", discordLink) != 0)
                {
                    return;
                }
            }
            else
            {
                var states = syncingLayers[0].FindAllStatesInLayer();
                var setStates = states.Where(x => x.state.name.Contains("Set Value ")).ToList();
                foreach (var state in setStates)
                {
                    var avatarParameterDriver = (VRCAvatarParameterDriver)state.state.behaviours[0];
                    var avatarParameterDriverParameters = avatarParameterDriver.parameters;
                    foreach (var param in avatarParameterDriverParameters.Where(param => !string.IsNullOrEmpty(param.source)))
                    {
                        optimizedParams.AddRange(expressionParameters.parameters.Where(x => x.name == param.source));
                    }
                }
            }

            foreach (var item in expressionParameters.parameters.Where(x => x.name.Contains(prefix)))
            {
                generatedExpressionParams.Add(item);
            }
            
            if (generatedExpressionParams.Count <= 0)
            {
                if (UninstallErrorDialogWithDiscordLink("Too few generated expressions found", $"Too few generated expressions found! {generatedExpressionParams.Count} found. \nPlease join the discord for support. \nKeep in mind there are backups made by default by the script!", discordLink) != 0)
                {
                    return;
                }
            }

            if (generatedAnimatorParams.Count <= 0)
            {
                if (UninstallErrorDialogWithDiscordLink("Too few generated animator parameters found!", $"Too few generated animator parameters found! {generatedAnimatorParams.Count} found. \nPlease join the discord for support. \nKeep in mind there are backups made by default by the script!", discordLink) != 0)
                {
                    return;
                }
            }

            if (optimizedParams.Count < 2)
            {
                if (UninstallErrorDialogWithDiscordLink("Too few optimized parameters found!", $"Too few generated animator parameters found! {optimizedParams.Count} found. \nPlease join the discord for support. \nKeep in mind there are backups made by default by the script!", discordLink) != 0)
                {
                    return;
                }
            }

            foreach (var mainBlendTreeLayer in mainBlendTreeLayers)
            {
                // Debug.Log("<color=yellow>[MemoryOptimizer]</color> Animator layer " + mainBlendTreeLayer.name + " of index " + fxLayer.FindLayerIndex(mainBlendTreeLayer) + " is being deleted");
                DeleteBlendTreeFromAsset((BlendTree)mainBlendTreeLayer.stateMachine.states[0].state.motion);
                fxLayer.RemoveLayer(mainBlendTreeLayer);
            }

            foreach (var syncingLayer in syncingLayers)
            {
                // Debug.Log("<color=yellow>[MemoryOptimizer]</color> Animator layer " + syncingLayer.name + " of index " + fxLayer.FindLayerIndex(syncingLayer) + " is being deleted");
                fxLayer.RemoveLayer(syncingLayer);
            }

            foreach (var param in generatedExpressionParams)
            {
                // Debug.Log("<color=yellow>[MemoryOptimizer]</color> Expression param " + param.name + "  of type: " + param.valueType + " is being deleted");
                expressionParameters.parameters = expressionParameters.parameters.Where(x => x != param).ToArray();
            }

            foreach (var param in generatedAnimatorParams)
            {
                // Debug.Log("<color=yellow>[MemoryOptimizer]</color> Controller param " + param.name + "  of type: " + param.type + " is being deleted");
                fxLayer.RemoveParameter(param);
            }

            foreach (var param in optimizedParams)
            {
                // Debug.Log("<color=yellow>[MemoryOptimizer]</color> Optimized param " + param.name + "  of type: " + param.valueType + " setting to sync");
                param.networkSynced = true;
            }

            EditorUtility.SetDirty(expressionParameters);
            AssetDatabase.SaveAssets();
            
            EditorApplication.Beep();
            Debug.Log("<color=yellow>[MemoryOptimizer]</color> Uninstall Complete");
        }
    }
}
