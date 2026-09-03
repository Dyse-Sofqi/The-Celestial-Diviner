using System.Collections.Generic;
using System.Collections.Specialized;

namespace TheCelestialDiviner.Helpers;

/// <summary>
/// .NET Framework 4.8 兼容垫片：补齐 netcoreapp 独有、net48 缺失的少量 API。
/// 语义与 .NET 8 对应实现保持一致。调用点统一用 <c>Compat.Clamp</c> 等写法。
/// </summary>
public static class Compat
{
    /// <summary>Math.Clamp 的 net48 等价实现（.NET Framework 无 System.Math.Clamp）。</summary>
    public static T Clamp<T>(T value, T min, T max) where T : IComparable<T>
    {
        if (min.CompareTo(max) > 0) throw new ArgumentException("min 大于 max");
        if (value.CompareTo(min) < 0) return min;
        if (value.CompareTo(max) > 0) return max;
        return value;
    }

    /// <summary>
    /// System.HashCode.Combine 的 net48 等价实现（net48 无 System.HashCode）。
    /// FNV-1a 变体混合，均匀性对 InputSource 哈希用途足够。
    /// </summary>
    public static int CombineHashCodes(params int[] codes)
    {
        unchecked
        {
            uint hash = 2166136261u;
            foreach (var code in codes)
            {
                hash ^= (uint)code;
                hash *= 16777619u;
            }
            return (int)hash;
        }
    }
}

/// <summary>net48 缺失的集合扩展方法（.NET Core 内置于 CollectionExtensions 等）。</summary>
public static class CollectionShims
{
    /// <summary>Dictionary.GetValueOrDefault 等价实现。</summary>
    public static TValue? GetValueOrDefault<TKey, TValue>(
        this Dictionary<TKey, TValue> dictionary, TKey key) where TKey : notnull
        => dictionary.TryGetValue(key, out var value) ? value : default;

    /// <summary>KeyValuePair.Deconstruct 等价实现（支持 foreach 解构）。</summary>
    public static void Deconstruct<TKey, TValue>(
        this KeyValuePair<TKey, TValue> kvp, out TKey key, out TValue value)
    {
        key = kvp.Key;
        value = kvp.Value;
    }
}
