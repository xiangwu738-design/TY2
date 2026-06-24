using Godot;
using Tongyuan.Core.Core;

namespace Tongyuan.Net;

/// <summary>
/// 联机层（规格 §4.11）。C# ENetMultiplayerPeer，主机权威 + 动作同步。
/// 客户端发 action → 主机执行 → 广播 action → 各端同种子确定性重放。
/// 中途加入：主机发 seed + 历史动作，客户端重放追平。
/// 缩减人数：N 玩家 = N 角色（位 N..1）。主机=玩家之一（c0）。P2 基础实现。
/// </summary>
public partial class NetController : Node
{
    public bool IsHost { get; private set; }
    public GameState? State { get; private set; }     // 主机权威状态；客户端为本地重放状态
    public int LocalCharacterId { get; set; } = 1;     // 本端控制的角色（缩减人数：每端一角色）
    public const int DefaultPort = 24816;

    private ENetMultiplayerPeer _peer = new();

    [Signal]
    public delegate void ActionAppliedEventHandler();

    public void StartHost(int port, GameState initialState)
    {
        IsHost = true;
        State = initialState;
        _peer.CreateServer(port, maxClients: 7); // 最多 8 人（含主机），缩减人数
        Multiplayer.MultiplayerPeer = _peer;
        Multiplayer.PeerConnected += OnPeerConnected;
        GD.Print($"[Net] host up port={port} seed={initialState.Seed} chars={initialState.Characters.Count}");
    }

    public void StartClient(string host, int port, int seed)
    {
        IsHost = false;
        State = new GameState(seed); // 客户端先建空状态，等主机快照重放追平
        _peer.CreateClient(host, port);
        Multiplayer.MultiplayerPeer = _peer;
        GD.Print($"[Net] client connecting {host}:{port}");
    }

    /// <summary>本端提交动作：主机直接执行，客户端 RPC 发给主机。</summary>
    public void SubmitAction(PlayerAction action)
    {
        if (IsHost) HostExecute(action);
        else RpcId(1, MethodName.ClientToHost, ActionCodec.Serialize(action));
    }

    private void HostExecute(PlayerAction action)
    {
        State!.Apply(action);
        // 广播给所有客户端（主机已本地执行）
        Rpc(MethodName.BroadcastAction, ActionCodec.Serialize(action));
        EmitSignal(SignalName.ActionApplied);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientToHost(string serialized)
    {
        if (!IsHost) return;
        HostExecute(ActionCodec.Deserialize(serialized));
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void BroadcastAction(string serialized)
    {
        if (IsHost) return; // 主机已执行
        State!.Apply(ActionCodec.Deserialize(serialized));
        EmitSignal(SignalName.ActionApplied);
    }

    // ---- 中途加入：主机发 seed + 历史动作，客户端重放追平 ----
    private void OnPeerConnected(long id)
    {
        if (!IsHost || State is null) return;
        // 发快照：seed + 全部历史动作序列
        var sb = new System.Text.StringBuilder();
        sb.Append(State.Seed);
        foreach (var a in State.ActionHistory)
            sb.Append('#').Append(ActionCodec.Serialize(a));
        RpcId(id, MethodName.ReceiveSnapshot, sb.ToString());
        GD.Print($"[Net] sent snapshot to peer {id} ({State.ActionHistory.Count} actions)");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveSnapshot(string data)
    {
        if (IsHost) return;
        var parts = data.Split('#');
        int seed = int.Parse(parts[0]);
        State = new GameState(seed);
        for (int i = 1; i < parts.Length; i++)
        {
            if (string.IsNullOrEmpty(parts[i])) continue;
            State.Apply(ActionCodec.Deserialize(parts[i]));
        }
        GD.Print($"[Net] snapshot applied: seed={seed} actions={parts.Length - 1} pointer={State.Pointer}");
    }
}
