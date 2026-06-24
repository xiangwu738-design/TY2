namespace Tongyuan.Core.Core;

/// <summary>
/// PlayerAction 编解码（联机传输用）。Godot RPC 用字符串载体，避免 Variant/自定义类型序列化复杂度。
/// 同种子 + 同动作序列 → 各端确定性重放一致（规格 §4.11）。
/// </summary>
public static class ActionCodec
{
    public static string Serialize(PlayerAction a)
    {
        // 格式：charId|type|cardGuid|targetChar|targetEnemy|targetCardGuid
        return string.Join('|',
            a.CharacterId,
            (int)a.Type,
            a.CardInstanceId?.ToString() ?? "",
            a.TargetCharacterId?.ToString() ?? "",
            a.TargetEnemyId?.ToString() ?? "",
            a.TargetCardInstanceId?.ToString() ?? "");
    }

    public static PlayerAction Deserialize(string s)
    {
        var p = s.Split('|');
        return new PlayerAction(
            int.Parse(p[0]),
            (ActionType)int.Parse(p[1]),
            GuidOrNull(p[2]),
            IntOrNull(p[3]),
            IntOrNull(p[4]),
            GuidOrNull(p[5]));
    }

    private static Guid? GuidOrNull(string s) => string.IsNullOrEmpty(s) ? null : Guid.Parse(s);
    private static int? IntOrNull(string s) => string.IsNullOrEmpty(s) ? null : int.Parse(s);
}
