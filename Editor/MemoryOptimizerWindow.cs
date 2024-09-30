using System;
using System.Collections.Generic;
using System.Linq;
using JeTeeS.MemoryOptimizer.Shared;
using JeTeeS.TES.HelperFunctions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using static JeTeeS.MemoryOptimizer.MemoryOptimizerWindow.SqueezeScope;
using static JeTeeS.MemoryOptimizer.MemoryOptimizerWindow.SqueezeScope.SqueezeScopeType;
using static JeTeeS.TES.HelperFunctions.TESHelperFunctions;

namespace JeTeeS.MemoryOptimizer
{
    internal class MemoryOptimizerWindow : EditorWindow
    {
        private const string menuPath = "Tools/TES/MemoryOptimizer";
        private const string defaultSavePath = "Assets/TES/MemOpt";
        private const string prefKey = "Mem_Opt_Pref_";
        private const string unlockSyncStepsEPKey = prefKey + "UnlockSyncSteps";
        private const string backUpModeEPKey = prefKey + "BackUpMode";
        private const string savePathPPKey = prefKey + "SavePath";
        private const int maxUnsyncedParams = 8192;

        internal static MemoryOptimizerComponent _component;
        
        private string currentSavePath;
        private DefaultAsset savePathOverride = null;
        
        private bool unlockSyncSteps = false;
        private int backupMode = 0;
        
        internal static readonly string[] wdOptions = { "Auto-Detect", "Off", "On" };
        internal static readonly string[] backupModes = { "On", "Off", "Ask" };

        private int tab = 0;
        private Vector2 scrollPosition;
        private bool runOnce;

        private VRCAvatarDescriptor avatarDescriptor;
        private AnimatorController avatarFXLayer;
        private VRCExpressionParameters expressionParameters;

        private List<MemoryOptimizerListData> selectedBools = new();
        private List<MemoryOptimizerListData> boolsToOptimize = new();
        private List<MemoryOptimizerListData> selectedIntsNFloats = new();
        private List<MemoryOptimizerListData> intsNFloatsToOptimize = new();
        private List<MemoryOptimizerListData> paramList = new();
        
        private int installationIndexers;
        private int installationBoolSyncers;
        private int installationIntSyncers;
        private int newParamCost;
        private int maxSyncSteps = 1;
        private int syncSteps = 1;
        private float stepDelay = 0.2f;
        private int longestParamName;
        private int wdOptionSelected = 0;
        private bool changeDetectionEnabled = false;

        [MenuItem(menuPath)]
        public static void ShowWindow()
        {
            // if you open by menu path, open the normal editor
            MemoryOptimizerWindow._component = null;
            
            // Show existing window instance. If one doesn't exist, make one.
            EditorWindow window = GetWindow(typeof(MemoryOptimizerWindow), false, "Memory Optimizer", true);
            window.minSize = new Vector2(600, 900);
        }

        private void Awake()
        {
            if (_component is not null)
            {
                avatarDescriptor = _component.gameObject.GetComponent<VRCAvatarDescriptor>();
                avatarFXLayer = new AnimatorController();
                expressionParameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();

                avatarFXLayer.name = "Temporary Generated FX Layer";
                expressionParameters.name = "Temporary Generated Parameters";
                
                expressionParameters.parameters = _component.savedParameterConfigurations.Select(s =>
                {
                    avatarFXLayer.AddUniqueParam(s.param.name, s.param.valueType.ValueTypeToParamType());
                    return s.param;
                }).ToArray();
                
                if (_component.syncSteps > 4)
                {
                    unlockSyncSteps = true;
                    EditorPrefs.SetBool(unlockSyncStepsEPKey, true);
                }

                wdOptionSelected = _component.wdOption;
                maxSyncSteps = _component.syncSteps;
                syncSteps = _component.syncSteps;
                stepDelay = _component.stepDelay;
                changeDetectionEnabled = _component.changeDetection;
                savePathOverride = _component.savePathOverride;
            }
        }

        private void OnGUI()
        {
            unlockSyncSteps = EditorPrefs.GetBool(unlockSyncStepsEPKey);
            backupMode = EditorPrefs.GetInt(backUpModeEPKey);
            
            string savePathEP = PlayerPrefs.GetString(savePathPPKey);
            if (!string.IsNullOrEmpty(savePathEP) && AssetDatabase.IsValidFolder(savePathEP))
            {
                savePathOverride = (DefaultAsset)AssetDatabase.LoadAssetAtPath(savePathEP, typeof(DefaultAsset));
            }

            if (savePathOverride && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(savePathOverride)))
            {
                currentSavePath = AssetDatabase.GetAssetPath(savePathOverride);
            }
            else
            {
                currentSavePath = defaultSavePath;
            }

            tab = GUILayout.Toolbar(tab, new[] { _component is null ? "Install menu" : "Configure", "Settings menu" });
            switch (tab)
            {
                case 0:
                    using (new SqueezeScope((0, 0, Vertical, EditorStyles.helpBox)))
                    {
                        using (new SqueezeScope((0, 0, Vertical, EditorStyles.helpBox)))
                        {
                            EditorGUI.BeginDisabledGroup(_component is not null);
                            
                            using (new SqueezeScope((0, 0, Horizontal, EditorStyles.helpBox)))
                            {
                                void OnAvatarChange()
                                {
                                    if (avatarDescriptor)
                                    {
                                        avatarFXLayer = FindFXLayer(avatarDescriptor);
                                        expressionParameters = FindExpressionParams(avatarDescriptor);
                                    }
                                    else
                                    {
                                        avatarFXLayer = null;
                                        expressionParameters = null;
                                    }

                                    ResetParamSelection();
                                }
                                
                                using (new ChangeCheckScope(OnAvatarChange))
                                {
                                    avatarDescriptor = (VRCAvatarDescriptor)EditorGUILayout.ObjectField("Avatar", avatarDescriptor, typeof(VRCAvatarDescriptor), true);
                                    if (_component is null && avatarDescriptor is null)
                                    {
                                        if (GUILayout.Button("Auto-detect"))
                                        {
                                            FillAvatarFields(null, avatarFXLayer, expressionParameters);
                                        }
                                    }
                                }
                            }
                            
                            EditorGUI.EndDisabledGroup();

                            if (_component is null)
                            {
                                using (new SqueezeScope((0, 0, Horizontal, EditorStyles.helpBox)))
                                {
                                    avatarFXLayer = (AnimatorController)EditorGUILayout.ObjectField("FX Layer", avatarFXLayer, typeof(AnimatorController), true);
                                    if (_component is null && avatarFXLayer is null)
                                    {
                                        if (GUILayout.Button("Auto-Detect"))
                                        {
                                            FillAvatarFields(avatarDescriptor, null, expressionParameters);
                                        }
                                    }
                                }

                                using (new SqueezeScope((0, 0, Horizontal, EditorStyles.helpBox)))
                                {
                                    expressionParameters = (VRCExpressionParameters)EditorGUILayout.ObjectField("Parameters", expressionParameters, typeof(VRCExpressionParameters), true);
                                    if (_component is null && expressionParameters is null)
                                    {
                                        if (GUILayout.Button("Auto-Detect"))
                                        {
                                            FillAvatarFields(avatarDescriptor, avatarFXLayer, null);
                                        }
                                    }
                                }
                            }

                            if (!runOnce)
                            {
                                FillAvatarFields(null, null, null);
                                runOnce = true;
                            }

                            GUILayout.Space(5);

                            using (new SqueezeScope((0, 0, Horizontal, EditorStyles.helpBox)))
                            {
                                EditorGUILayout.LabelField("Write Defaults: ", EditorStyles.boldLabel);
                                wdOptionSelected = EditorGUILayout.Popup(wdOptionSelected, wdOptions, new GUIStyle(EditorStyles.popup) { fixedHeight = 18, stretchWidth = false });
                            }

                            using (new SqueezeScope((0, 0, Horizontal, EditorStyles.helpBox)))
                            {
                                EditorGUILayout.LabelField("Change Detection: ", EditorStyles.boldLabel);
                                if (syncSteps < 3)
                                {
                                    changeDetectionEnabled = false;
                                    GUI.enabled = false;
                                    GUI.backgroundColor = Color.red;
                                    GUILayout.Button("Off", GUILayout.Width(203));
                                    GUI.backgroundColor = Color.white;
                                    GUI.enabled = true;
                                }

                                else if (changeDetectionEnabled)
                                {
                                    GUI.backgroundColor = Color.green;
                                    if (GUILayout.Button("On", GUILayout.Width(203)))
                                    {
                                        changeDetectionEnabled = !changeDetectionEnabled;
                                    }

                                    GUI.backgroundColor = Color.white;
                                }
                                else
                                {
                                    GUI.backgroundColor = Color.red;
                                    if (GUILayout.Button("Off", GUILayout.Width(203)))
                                    {
                                        changeDetectionEnabled = !changeDetectionEnabled;
                                    }

                                    GUI.backgroundColor = Color.white;
                                }
                            }

                            GUILayout.Space(5);
                        }

                        GUILayout.Space(5);

                        if (avatarDescriptor is not null && avatarFXLayer is not null && expressionParameters is not null)
                        {
                            longestParamName = 0;
                            foreach (var x in expressionParameters.parameters)
                            {
                                longestParamName = Math.Max(longestParamName, x.name.Count());
                            }

                            using (new SqueezeScope((0, 0, Horizontal, EditorStyles.helpBox)))
                            {
                                EditorGUILayout.LabelField("Avatar Parameters: ", EditorStyles.boldLabel);

                                if (GUILayout.Button("Deselect Prefix"))
                                {
                                    EditorInputDialog.Show("", "Please enter your prefix to deselect", "", prefix =>
                                    {
                                        if (!string.IsNullOrEmpty(prefix))
                                        {
                                            foreach (MemoryOptimizerListData param in paramList.FindAll(x => x.param.name.StartsWith(prefix, true, null)))
                                            {
                                                param.selected = false;
                                            }
                                        }
                                        
                                        OnChangeUpdate();
                                    });
                                }

                                if (GUILayout.Button("Select All"))
                                {
                                    foreach (MemoryOptimizerListData param in paramList)
                                    {
                                        param.selected = true;
                                    }
                                    
                                    OnChangeUpdate();
                                }

                                if (GUILayout.Button("Clear Selected Parameters"))
                                {
                                    ResetParamSelection();
                                }
                            }

                            scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true);

                            for (int i = 0; i < expressionParameters.parameters.Length; i++)
                            {
                                using (new SqueezeScope((0, 0, Horizontal)))
                                {
                                    // make sure the param list is always the same size as avatar's expression parameters
                                    if (paramList.Count != expressionParameters.parameters.Length)
                                    {
                                        ResetParamSelection();
                                    }

                                    using (new SqueezeScope((0, 0, Horizontal)))
                                    {
                                        GUI.enabled = false;

                                        EditorGUILayout.TextArea(expressionParameters.parameters[i].name, GUILayout.MinWidth(longestParamName * 8));
                                        EditorGUILayout.Popup((int)expressionParameters.parameters[i].valueType, _paramTypes, GUILayout.Width(50));
                                        
                                        GUI.enabled = true;

                                        // System already installed
                                        if (MemoryOptimizerMain.IsSystemInstalled(avatarFXLayer))
                                        {
                                            GUI.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1);
                                            GUI.enabled = false;
                                            GUILayout.Button("System Already Installed!", GUILayout.Width(203));
                                        }
                                        // Param isn't network synced
                                        else if (!expressionParameters.parameters[i].networkSynced)
                                        {
                                            GUI.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1);
                                            GUI.enabled = false;
                                            GUILayout.Button("Param Not Synced", GUILayout.Width(203));
                                        }
                                        // Param isn't in FX layer
                                        else if (!(avatarFXLayer.parameters.Count(x => x.name == expressionParameters.parameters[i].name) > 0))
                                        {
                                            paramList[i].selected = false;
                                            GUI.backgroundColor = Color.yellow;
                                            if (GUILayout.Button("Add To FX", GUILayout.Width(100)))
                                            {
                                                avatarFXLayer.AddUniqueParam(paramList[i].param.name);
                                            }

                                            GUI.enabled = false;
                                            GUI.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1);
                                            GUILayout.Button("Param Not In FX", GUILayout.Width(100));
                                        }
                                        // Param isn't selected
                                        else if (!paramList[i].selected)
                                        {
                                            GUI.backgroundColor = Color.red;
                                            using (new ChangeCheckScope(OnChangeUpdate))
                                            {
                                                if (GUILayout.Button("Optimize", GUILayout.Width(203)))
                                                {
                                                    paramList[i].selected = !paramList[i].selected;
                                                }
                                            }
                                        }
                                        // Param won't be optimized
                                        else if (!paramList[i].willBeOptimized)
                                        {
                                            GUI.backgroundColor = Color.yellow;
                                            using (new ChangeCheckScope(OnChangeUpdate))
                                            {
                                                if (GUILayout.Button("Optimize", GUILayout.Width(203)))
                                                {
                                                    paramList[i].selected = !paramList[i].selected;
                                                }
                                            }
                                        }
                                        // Param will be optimized
                                        else
                                        {
                                            GUI.backgroundColor = Color.green;
                                            using (new ChangeCheckScope(OnChangeUpdate))
                                            {
                                                if (GUILayout.Button("Optimize", GUILayout.Width(203)))
                                                {
                                                    paramList[i].selected = !paramList[i].selected;
                                                }
                                            }
                                        }

                                        GUI.enabled = true;
                                    }
                                }

                                GUI.backgroundColor = Color.white;
                            }

                            GUILayout.EndScrollView();

                            using (new SqueezeScope((0, 0, EditorH)))
                            {
                                LabelWithHelpBox($"Selected Bools: {selectedBools.Count}");
                                LabelWithHelpBox($"Selected Ints and Floats: {selectedIntsNFloats.Count}");
                            }

                            using (new SqueezeScope((0, 0, EditorH)))
                            {
                                LabelWithHelpBox($"Original Param Cost: {expressionParameters.CalcTotalCost()}");
                                LabelWithHelpBox($"New Param Cost: {newParamCost}");
                                LabelWithHelpBox($"Amount You Will Save:  {expressionParameters.CalcTotalCost() - newParamCost}");
                                LabelWithHelpBox($"Total Sync Time:  {syncSteps * stepDelay}s");
                            }
                            
                            using (new SqueezeScope((0, 0, EditorH, EditorStyles.helpBox)))
                            {
                                if (MemoryOptimizerMain.IsSystemInstalled(avatarFXLayer))
                                {
                                    GUI.backgroundColor = Color.black;
                                    GUILayout.Label("System Already Installed!", EditorStyles.boldLabel);
                                    GUI.enabled = false;
                                    EditorGUILayout.IntSlider(syncSteps, 0, 0);
                                }
                                else if (maxSyncSteps < 2)
                                {
                                    GUI.backgroundColor = Color.red;
                                    GUILayout.Label("Too Few Parameters Selected!", EditorStyles.boldLabel);
                                    GUI.enabled = false;
                                    EditorGUILayout.IntSlider(syncSteps, 0, 0);
                                }
                                else
                                {
                                    GUILayout.Label("Syncing Steps", GUILayout.MaxWidth(100));
                                    using (new ChangeCheckScope(OnChangeUpdate))
                                    {
                                        syncSteps = EditorGUILayout.IntSlider(syncSteps, 2, unlockSyncSteps ? maxSyncSteps : Math.Min(maxSyncSteps, 4));
                                    }
                                }

                                GUI.backgroundColor = Color.white;
                            }

                            GUI.enabled = true;
                        }

                        if (syncSteps > maxSyncSteps)
                        {
                            syncSteps = maxSyncSteps;
                        }

                        if (_component is null)
                        {
                            if (MemoryOptimizerMain.IsSystemInstalled(avatarFXLayer))
                            {
                                if (GUILayout.Button("Uninstall"))
                                {
                                    MemoryOptimizerMain.UninstallMemOpt(avatarDescriptor, avatarFXLayer, expressionParameters);
                                }
                            }
                            else
                            {
                                GUI.enabled = false;
                                GUILayout.Button("Uninstall");
                                GUI.enabled = true;
                            }
                        }

                        if (_component is not null)
                        {
                            if (expressionParameters.parameters.Length + (installationBoolSyncers + installationIntSyncers + installationIndexers) >= maxUnsyncedParams)
                            {
                                GUI.enabled = false;
                                GUI.backgroundColor = Color.red;
                                GUILayout.Button($"Generated params will exceed {maxUnsyncedParams}!");
                            } 
                            else if (GUILayout.Button("Save"))
                            {
                                // write our configuration back
                                _component.wdOption = wdOptionSelected;
                                _component.syncSteps = syncSteps;
                                _component.stepDelay = stepDelay;
                                _component.changeDetection = changeDetectionEnabled;
                                _component.savePathOverride = savePathOverride;
                                
                                foreach (var data in paramList)
                                {
                                    var match = _component.savedParameterConfigurations.FirstOrDefault(p => p.param.name.Equals(data.param.name) && p.param.valueType == data.param.valueType);

                                    if (match is not null)
                                    {
                                        match.selected = data.selected;
                                        match.willBeOptimized = data.willBeOptimized;
                                    }
                                }

                                // clear reference
                                _component = null;
                                
                                // close the window
                                Close();
                            }
                        }
                        else if (MemoryOptimizerMain.IsSystemInstalled(avatarFXLayer))
                        {
                            GUI.enabled = false;
                            GUI.backgroundColor = Color.black;
                            GUILayout.Button("System Already Installed! Please Uninstall Before Reinstalling.");
                        }
                        else if (syncSteps < 2)
                        {
                            GUI.enabled = false;
                            GUI.backgroundColor = Color.red;
                            GUILayout.Button("Select More Parameters!");
                        }
                        else if (!avatarDescriptor)
                        {
                            GUI.enabled = false;
                            GUI.backgroundColor = Color.red;
                            GUILayout.Button("No Avatar Selected!");
                        }
                        else if (!avatarFXLayer)
                        {
                            GUI.enabled = false;
                            GUI.backgroundColor = Color.red;
                            GUILayout.Button("No FX Layer Selected!");
                        }
                        else if (expressionParameters.parameters.Length + (installationBoolSyncers + installationIntSyncers + installationIndexers) >= maxUnsyncedParams)
                        {
                            GUI.enabled = false;
                            GUI.backgroundColor = Color.red;
                            GUILayout.Button($"Generated params will exceed {maxUnsyncedParams}!");
                        }
                        else
                        {
                            if (GUILayout.Button("Install"))
                            {
                                backupMode = EditorPrefs.GetInt(backUpModeEPKey);
                                if (backupMode == 0)
                                {
                                    MakeBackupOf(new List<UnityEngine.Object> { avatarFXLayer, expressionParameters }, currentSavePath + "/Backup/");
                                }
                                else if (backupMode == 2)
                                {
                                    if (EditorUtility.DisplayDialog("", "Do you want to make a backup of your controller and parameters?", "Yes", "No"))
                                    {
                                        MakeBackupOf(new List<UnityEngine.Object> { avatarFXLayer, expressionParameters }, currentSavePath + "/Backup/");
                                    }
                                }

                                MemoryOptimizerMain.InstallMemOpt(avatarDescriptor, avatarFXLayer, expressionParameters, boolsToOptimize, intsNFloatsToOptimize, syncSteps, stepDelay, changeDetectionEnabled, wdOptionSelected, currentSavePath);
                            }
                        }
                    }

                    break;
                case 1:
                    using (new SqueezeScope((0, 0, Vertical, EditorStyles.helpBox)))
                    {
                        // Backup Mode
                        using (new SqueezeScope((0, 0, Horizontal, EditorStyles.helpBox)))
                        {
                            EditorGUILayout.LabelField("Backup Mode: ", EditorStyles.boldLabel);
                            EditorPrefs.SetInt(backUpModeEPKey, EditorGUILayout.Popup(backupMode, backupModes, new GUIStyle(EditorStyles.popup) { fixedHeight = 18, stretchWidth = false }));
                        }

                        GUILayout.Space(5);

                        // Unlock sync steps button
                        GUI.backgroundColor = unlockSyncSteps ? Color.green : Color.red;

                        if (GUILayout.Button("Unlock sync steps"))
                        {
                            EditorPrefs.SetBool(unlockSyncStepsEPKey, !unlockSyncSteps);
                        }

                        GUI.backgroundColor = Color.white;

                        GUILayout.Space(5);

                        // save path
                        using (new SqueezeScope((0, 0, Vertical, EditorStyles.helpBox)))
                        {
                            using (new SqueezeScope((0, 0, Horizontal)))
                            {
                                using (new ChangeCheckScope(SavePathChange))
                                {
                                    EditorGUILayout.LabelField("Select folder to save generated assets to: ");
                                    savePathOverride = (DefaultAsset)EditorGUILayout.ObjectField("", savePathOverride, typeof(DefaultAsset), false);
                                }

                                void SavePathChange()
                                {
                                    PlayerPrefs.SetString(savePathPPKey, AssetDatabase.GetAssetPath(savePathOverride));
                                }
                            }

                            if (savePathOverride && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(savePathOverride)))
                            {
                                EditorGUILayout.HelpBox($"Valid folder! Now saving to: {currentSavePath}", MessageType.Info, true);
                            }
                            else
                            {
                                EditorGUILayout.HelpBox($"Not valid! Now saving to: {currentSavePath}", MessageType.Info, true);
                            }
                        }

                        GUILayout.Space(5);

                        // Step delay
                        using (new SqueezeScope((0, 0, Vertical, EditorStyles.helpBox)))
                        {
                            EditorGUILayout.HelpBox("It is recommended not editing this value!", MessageType.Error, true);
                            using (new SqueezeScope((0, 0, Horizontal)))
                            {
                                GUILayout.Label("Step delay", GUILayout.MaxWidth(100));
                                using (new ChangeCheckScope(OnChangeUpdate))
                                {
                                    stepDelay = EditorGUILayout.FloatField(stepDelay);
                                }
                            }

                            if (GUILayout.Button("Reset value"))
                            {
                                stepDelay = 0.2f;
                            }
                        }
                    }

                    break;
            }

            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
        }

        internal void OnChangeUpdate()
        {
            foreach (MemoryOptimizerListData param in paramList)
            {
                param.willBeOptimized = false;
                if (avatarFXLayer && (!param.param.networkSynced || !(avatarFXLayer.parameters.Count(x => x.name == param.param.name) > 0)))
                {
                    param.selected = false;
                }
            }

            selectedBools = new List<MemoryOptimizerListData>();
            selectedIntsNFloats = new List<MemoryOptimizerListData>();
            
            foreach (var param in paramList)
            {
                if (param.selected)
                {
                    if (param.param.valueType == VRCExpressionParameters.ValueType.Bool)
                    {
                        selectedBools.Add(param);
                    }
                    else
                    {
                        selectedIntsNFloats.Add(param);
                    }
                }
            }

            maxSyncSteps = Math.Max(Math.Max(selectedBools.Count(), selectedIntsNFloats.Count()), 1);
            if (maxSyncSteps == 1)
            {
                installationIndexers = 0;
                installationBoolSyncers = 0;
                installationIntSyncers = 0;
                newParamCost = expressionParameters?.CalcTotalCost() ?? 0;
                
                return;
            }

            if (syncSteps < 2)
            {
                syncSteps = 2;
            }

            boolsToOptimize = selectedBools.Take(selectedBools.Count - (selectedBools.Count % syncSteps)).ToList();
            intsNFloatsToOptimize = selectedIntsNFloats.Take(selectedIntsNFloats.Count - (selectedIntsNFloats.Count % syncSteps)).ToList();

            foreach (MemoryOptimizerListData param in boolsToOptimize)
            {
                param.willBeOptimized = true;
            }
            
            foreach (MemoryOptimizerListData param in intsNFloatsToOptimize)
            {
                param.willBeOptimized = true;
            }

            installationIndexers = (syncSteps - 1).DecimalToBinary().ToString().Count();
            installationBoolSyncers = boolsToOptimize.Count / syncSteps;
            installationIntSyncers = intsNFloatsToOptimize.Count / syncSteps;

            newParamCost = expressionParameters.CalcTotalCost() + installationIndexers + installationBoolSyncers + (installationIntSyncers * 8) - (boolsToOptimize.Count + (intsNFloatsToOptimize.Count * 8));
        }

        internal void ResetParamSelection()
        {
            paramList = new List<MemoryOptimizerListData>();

            if (_component is not null)
            {
                foreach (var value in _component.savedParameterConfigurations)
                {
                    paramList.Add(value.Pure());
                }
            }
            else
            {
                if (expressionParameters is not null && expressionParameters.parameters.Length > 0)
                {
                    foreach (VRCExpressionParameters.Parameter param in expressionParameters.parameters)
                    {
                        paramList.Add(new MemoryOptimizerListData(param, false, false));
                    }
                }
                
                maxSyncSteps = 1;
                syncSteps = 1;
            }
            
            OnChangeUpdate();
        }

        internal void FillAvatarFields(VRCAvatarDescriptor descriptor, AnimatorController controller, VRCExpressionParameters parameters)
        {
            if (descriptor is null)
            {
                avatarDescriptor = FindObjectOfType<VRCAvatarDescriptor>();
            }
            else
            {
                if (controller is null)
                {
                    avatarFXLayer = FindFXLayer(avatarDescriptor);
                }
                
                if (parameters is null)
                {
                    expressionParameters = FindExpressionParams(avatarDescriptor);
                }
            }

            OnChangeUpdate();
        }

        internal class ChangeCheckScope : IDisposable
        {
            public Action callBack;

            public ChangeCheckScope(Action callBack)
            {
                this.callBack = callBack;
                
                EditorGUI.BeginChangeCheck();
            }

            public void Dispose()
            {
                if (EditorGUI.EndChangeCheck())
                {
                    callBack();
                }
            }
        }

        internal class SqueezeScope : IDisposable
        {
            private readonly SqueezeSettings[] settings;

            internal enum SqueezeScopeType
            {
                Horizontal,
                Vertical,
                EditorH,
                EditorV
            }

            internal SqueezeScope(SqueezeSettings input) : this(new[] { input })
            {
            }

            internal SqueezeScope(params SqueezeSettings[] input)
            {
                settings = input;
                foreach (var squeezeSettings in input)
                {
                    switch (squeezeSettings.type)
                    {
                        case Horizontal:
                            GUILayout.BeginHorizontal(squeezeSettings.style);
                            break;
                        case Vertical:
                            GUILayout.BeginVertical(squeezeSettings.style);
                            break;
                        case EditorH:
                            EditorGUILayout.BeginHorizontal(squeezeSettings.style);
                            break;
                        case EditorV:
                            EditorGUILayout.BeginVertical(squeezeSettings.style);
                            break;
                    }

                    GUILayout.Space(squeezeSettings.width1);
                }
            }

            public void Dispose()
            {
                foreach (var squeezeSettings in settings.Reverse())
                {
                    GUILayout.Space(squeezeSettings.width2);
                    switch (squeezeSettings.type)
                    {
                        case Horizontal:
                            GUILayout.EndHorizontal();
                            break;
                        case Vertical:
                            GUILayout.EndVertical();
                            break;
                        case EditorH:
                            EditorGUILayout.EndHorizontal();
                            break;
                        case EditorV:
                            EditorGUILayout.EndVertical();
                            break;
                    }
                }
            }
        }

        internal struct SqueezeSettings
        {
            public int width1;
            public int width2;
            public SqueezeScopeType type;
            public GUIStyle style;

            public static implicit operator SqueezeSettings((int, int) val)
            {
                return new SqueezeSettings { width1 = val.Item1, width2 = val.Item2, type = Horizontal, style = GUIStyle.none };
            }

            public static implicit operator SqueezeSettings((int, int, /* For some reason Unity can't resolve SqueezeScopeType unless SqueezeScope is specified */ SqueezeScope.SqueezeScopeType) val)
            {
                return new SqueezeSettings { width1 = val.Item1, width2 = val.Item2, type = val.Item3, style = GUIStyle.none };
            }

            public static implicit operator SqueezeSettings((int, int, /* For some reason Unity can't resolve SqueezeScopeType unless SqueezeScope is specified */ SqueezeScope.SqueezeScopeType, GUIStyle) val)
            {
                return new SqueezeSettings { width1 = val.Item1, width2 = val.Item2, type = val.Item3, style = val.Item4 };
            }
        }

        // https://forum.unity.com/threads/is-there-a-way-to-input-text-using-a-unity-editor-utility.473743/#post-7191802
        // https://forum.unity.com/threads/is-there-a-way-to-input-text-using-a-unity-editor-utility.473743/#post-7229248
        // Thanks to JelleJurre for help
        internal class EditorInputDialog : EditorWindow
        {
            string description, inputText;
            string okButton, cancelButton;
            bool initializedPosition = false;
            Action onOKButton;

            bool shouldClose = false;
            Vector2 maxScreenPos;

            void OnGUI()
            {
                // Check if Esc/Return have been pressed
                var e = Event.current;
                if (e.type == EventType.KeyDown)
                {
                    switch (e.keyCode)
                    {
                        // Escape pressed
                        case KeyCode.Escape:
                            shouldClose = true;
                            e.Use();
                            break;
                        // Enter pressed
                        case KeyCode.Return:
                        case KeyCode.KeypadEnter:
                            onOKButton?.Invoke();
                            shouldClose = true;
                            e.Use();
                            break;
                    }
                }

                if (shouldClose)
                {
                    // Close this dialog
                    Close();
                }

                // Draw our control
                var rect = EditorGUILayout.BeginVertical();

                EditorGUILayout.Space(12);
                EditorGUILayout.LabelField(description);

                EditorGUILayout.Space(8);
                GUI.SetNextControlName("inText");
                inputText = EditorGUILayout.TextField("", inputText);
                // Focus text field
                GUI.FocusControl("inText");
                EditorGUILayout.Space(12);

                // Draw OK / Cancel buttons
                var r = EditorGUILayout.GetControlRect();
                r.width /= 2;
                if (GUI.Button(r, okButton))
                {
                    onOKButton?.Invoke();
                    shouldClose = true;
                }

                r.x += r.width;
                if (GUI.Button(r, cancelButton))
                {
                    // Cancel - delete inputText
                    inputText = null;
                    shouldClose = true;
                }

                EditorGUILayout.Space(8);
                EditorGUILayout.EndVertical();

                // Force change size of the window
                if (rect.width != 0 && minSize != rect.size)
                {
                    minSize = maxSize = rect.size;
                }

                // Set dialog position next to mouse position
                if (!initializedPosition && e.type == EventType.Layout)
                {
                    initializedPosition = true;

                    // Move window to a new position. Make sure we're inside visible window
                    var mousePos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
                    mousePos.x += 32;
                    if (mousePos.x + position.width > maxScreenPos.x) mousePos.x -= position.width + 64; // Display on left side of mouse
                    if (mousePos.y + position.height > maxScreenPos.y) mousePos.y = maxScreenPos.y - position.height;

                    position = new Rect(mousePos.x, mousePos.y, position.width, position.height);

                    // Focus current window
                    Focus();
                }
            }

            /// <summary>
            /// Returns text player entered, or null if player cancelled the dialog.
            /// </summary>
            /// <param name="title"></param>
            /// <param name="description"></param>
            /// <param name="inputText"></param>
            /// <param name="okButton"></param>
            /// <param name="cancelButton"></param>
            /// <returns></returns>
            //public static string Show(string title, string description, string inputText, string okButton = "OK", string cancelButton = "Cancel")
            public static void Show(string title, string description, string inputText, Action<string> callBack, string okButton = "OK", string cancelButton = "Cancel")
            {
                // Make sure our popup is always inside parent window, and never offscreen
                // So get caller's window size
                var maxPos = GUIUtility.GUIToScreenPoint(new Vector2(Screen.width, Screen.height));

                if (EditorWindow.HasOpenInstances<EditorInputDialog>())
                {
                    return;
                }

                var window = CreateInstance<EditorInputDialog>();
                window.maxScreenPos = maxPos;
                window.titleContent = new GUIContent(title);
                window.description = description;
                window.inputText = inputText;
                window.okButton = okButton;
                window.cancelButton = cancelButton;
                window.onOKButton += () => callBack(window.inputText);
                window.ShowPopup();
            }
        }
    }
}