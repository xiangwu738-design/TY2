using Godot;

namespace Tongyuan.Net;

/// <summary>
/// 联机层（规格 §4.11）。C# ENetMultiplayerPeer，主机权威 + 动作同步。
/// 客户端发 action → 主机执行 → 广播 action → 各端同种子确定性重放。
/// 中途加入：主机发 seed + 历史动作重放。P2 实现；P0 仅占位 + 扩展位声明。
/// </summary>
public partial class NetController : Node
{
    public bool IsHost { get; set; }
    public const int DefaultPort = 24816;

    public override void _Ready()
    {
        GD.Print($"[Net] P0 占位 | isHost={IsHost} port={DefaultPort}");
    }
}
