using System;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace JeTeeS.MemoryOptimizer.Shared
{
    [Serializable]
    internal class MemoryOptimizerListData
    {
        public VRCExpressionParameters.Parameter param;
        public bool selected = false;
        public bool willBeOptimized = false;

        public MemoryOptimizerListData(VRCExpressionParameters.Parameter parameter, bool isSelected, bool willOptimize)
        {
            param = parameter;
            selected = isSelected;
            willBeOptimized = willOptimize;
        }
    }
}