namespace JeTeeS.MemoryOptimizer.Helper
{
    internal static class ReflectionHelper
    {
        internal static object GetFieldValue(this object obj, string field)
        {
            return obj.GetType().GetField(field).GetValue(obj);
        }
    }
}