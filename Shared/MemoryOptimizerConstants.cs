using System.Collections.Generic;

namespace JeTeeS.MemoryOptimizer.Shared
{
    internal static class MemoryOptimizerConstants
    {
        internal const string menuPath = "Tools/TES/MemoryOptimizer";
        internal const string defaultSavePath = "Packages/dev.jetees.memoryoptimizer/Temp/Save";
        internal const string prefKey = "Mem_Opt_Pref_";
        internal const string unlockSyncStepsEPKey = prefKey + "UnlockSyncSteps";
        internal const string backUpModeEPKey = prefKey + "BackUpMode";
        internal const string savePathPPKey = prefKey + "SavePath";
        internal const int maxUnsyncedParams = 8192;

        internal const string discordLink = "https://discord.gg/N7snuJhzkd";
        internal const string prefix = "MemOpt_";
        internal const string syncingLayerName = prefix + "Syncing Layer";
        internal const string syncingLayerIdentifier = prefix + "Syncer";
        internal const string mainBlendTreeIdentifier = prefix + "MainBlendTree";
        internal const string mainBlendTreeLayerName = prefix + "Main BlendTree";
        internal const string smoothingAmountParamName = prefix + "ParamSmoothing";
        internal const string smoothedVerSuffix = "_S";
        internal const string SmoothingTreeName = "SmoothingParentTree";
        internal const string DifferentialTreeName = "DifferentialParentTree";
        internal const string DifferentialSuffix = "_Delta";
        internal const string constantOneName = prefix + "ConstantOne";
        internal const string indexerParamName = prefix + "Indexer ";
        internal const string boolSyncerParamName = prefix + "BoolSyncer ";
        internal const string intNFloatSyncerParamName = prefix + "IntNFloatSyncer ";
        internal const string oneFrameBufferAnimName = prefix + "OneFrameBuffer";
        internal const string oneSecBufferAnimName = prefix + "OneSecBuffer";
        internal const float changeSensitivity = 0.05f;
        
        internal const string EditorKeyInspectComponent = "dev.jetees.memoryoptimizer_inspectcomponent";
        internal const string EditorKeyInspectParameters = "dev.jetees.memoryoptimizer_inspectparameters";
        
        internal static readonly string[] wdOptions = { "Auto-Detect", "Off", "On" };
        internal static readonly string[] backupModes = { "On", "Off", "Ask" };
        internal static readonly string[] paramTypes = { "Int", "Float", "Bool" };
        internal static readonly string[] animatorParamTypes = { "", /* 1 */ "Float", "", /* 3 */ "Int", /* 4 */ "Bool", "", "", "", "", /* 9 */ "Trigger" };
        
        // exclude certain names, like VRC Animator Parameters, we don't want to optimize those
        internal static readonly List<string> AnimatorExclusions = new()
        {
            "IsLocal",
            "Viseme",
            "Voice",
            "GestureLeft",
            "GestureRight",
            "GestureLeftWeight",
            "GestureRightWeight",
            "AngularY",
            "VelocityX",
            "VelocityY",
            "VelocityZ",
            "VelocityMagnitude",
            "Upright",
            "Grounded",
            "Seated",
            "AFK",
            "Expression1",
            "Expression2",
            "Expression3",
            "Expression4",
            "Expression5",
            "Expression6",
            "Expression7",
            "Expression8",
            "Expression9",
            "Expression10",
            "Expression11",
            "Expression12",
            "Expression13",
            "Expression14",
            "Expression15",
            "Expression16",
            "TrackingType",
            "VRMode",
            "MuteSelf",
            "InStation",
            "Earmuffs",
            "IsOnFriendsList",
            "AvatarVersion",
            "ScaleModified",
            "ScaleFactor",
            "ScaleFactorInverse",
            "EyeHeightAsMeters",
            "EyeHeightAsPercent"
        }; 
    }
}