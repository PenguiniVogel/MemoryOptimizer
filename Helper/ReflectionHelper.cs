using System;
using System.Linq;

namespace JeTeeS.MemoryOptimizer.Helper
{
    internal static class ReflectionHelper
    {
        internal static object GetFieldValue(this object obj, string field)
        {
            return obj.GetType().GetField(field).GetValue(obj);
        }

        internal static Type FindTypeInAssemblies(string type)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(type)).FirstOrDefault(t => t is not null);
        }
    }
}