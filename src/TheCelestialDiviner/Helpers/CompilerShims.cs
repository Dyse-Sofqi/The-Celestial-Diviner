// .NET Framework 4.8 编译器特性垫片：
// net48 元数据中缺少 C# 9 init / C# 11 required 所需的编译器合成属性，
// 按官方 documented 方式手动定义（与 PolySharp 生成内容一致）。
// 命名空间必须为全局，编译器按全名匹配。
namespace System.Runtime.CompilerServices
{
    /// <summary>启用 C# 9 init 访问器（外部不可变）。</summary>
    [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal static class IsExternalInit
    {
    }

    /// <summary>标记 C# 11 required 成员。</summary>
    [global::System.AttributeUsage(global::System.AttributeTargets.All, AllowMultiple = false, Inherited = false)]
    internal sealed class RequiredMemberAttribute : global::System.Attribute
    {
    }

    /// <summary>标记编译器功能依赖（required 成员所需）。</summary>
    [global::System.AttributeUsage(global::System.AttributeTargets.All, AllowMultiple = false, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute : global::System.Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName)
        {
            FeatureName = featureName;
        }

        /// <summary>功能名称（固定 RequiredMember）。</summary>
        public string FeatureName { get; }

        /// <summary>此功能是否可选。</summary>
        public bool IsOptional => false;
    }
}
