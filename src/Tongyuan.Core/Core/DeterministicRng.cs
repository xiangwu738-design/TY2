namespace Tongyuan.Core.Core;

/// <summary>
/// 确定性伪随机（规格 §2：除开局带种子洗牌外，结算零 RNG）。
/// 仅用于抽牌堆洗牌；联机需同步种子。用可显式复现的线性同余，避免依赖系统 RNG。
/// </summary>
public sealed class DeterministicRng
{
    private ulong _state;
    public DeterministicRng(int seed)
    {
        // 避免全 0 种子
        _state = seed == 0 ? 0x9E3779B97F4A7C15UL : (ulong)seed;
    }

    private DeterministicRng(ulong state) { _state = state; }

    public int Next(int maxExclusive)
    {
        // xshift128/splitmix64 风格的廉价确定性步进
        _state += 0x9E3779B97F4A7C15UL;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (int)(z % (ulong)maxExclusive);
    }

    /// <summary>Fisher-Yates 洗牌（确定性，给定同一种子结果一致）。</summary>
    public void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>克隆当前 RNG 状态（预演需复制已推进的随机状态，保证确定性）。</summary>
    public DeterministicRng Clone() => new DeterministicRng(_state);
}
