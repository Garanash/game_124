namespace System.Runtime.CompilerServices
{
    // netstandard2.1 lacks this type, but C# 9 records and init-only setters require it.
    internal static class IsExternalInit
    {
    }
}
