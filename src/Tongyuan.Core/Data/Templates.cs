namespace Tongyuan.Core.Data;

using Tongyuan.Core.Core;

/// <summary>
/// 角色模板（规格 §4.10）。4 定位：输出/布防/控制/治疗。专属卡池/整备牌/颜色用模板占位，§7 用户后填。
/// </summary>
public enum RoleArchetype
{
    Damage,   // 输出
    Defense,  // 布防
    Control,  // 控制
    Support,  // 治疗
}

public sealed class CharacterTemplate
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public RoleArchetype Archetype { get; init; }
    public int Color { get; init; }
    public int BaseHp { get; init; } = 30;
    public PrepCardTemplate PrepTemplate { get; init; } = new();
    public List<CardDef> CardPool { get; } = new(); // 专属卡池占位
}

/// <summary>敌人编排占位（规格 §7：默认 2 种普通敌 + 1 Boss）。</summary>
public sealed class EnemyEncounter
{
    public string Id { get; init; } = string.Empty;
    public List<Enemy> Enemies { get; } = new();
    public bool IsBoss { get; init; }
}
