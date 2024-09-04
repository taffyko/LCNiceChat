#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit {}
}
#endif

#if !NET7_0_OR_GREATER
namespace System.Runtime.CompilerServices {
    internal class RequiredMemberAttribute : System.Attribute { }
    internal class CompilerFeatureRequiredAttribute : System.Attribute
    {
        public CompilerFeatureRequiredAttribute(string name) { }
    }
    [System.AttributeUsage(System.AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    public sealed class SetsRequiredMembersAttribute : Attribute { }
}
#endif