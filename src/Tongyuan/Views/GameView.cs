using Godot;
using System.Linq;
using Tongyuan.Core.Core;
using Tongyuan.Core.Layout;

namespace Tongyuan.Views;

/// <summary>
/// 战场表现层（规格 §3/§4.8）。每进程一个，按 <see cref="BattleLayout"/> 渲染
/// 战场（角色左/敌人右）/时间轴/手牌——占位色块，确定性无重叠布局（P5 自验）。
/// 订阅 Core 事件流播放动画/更新 UI。
/// </summary>
public partial class GameView : Control
{
    public GameState? State { get; set; }
    private BattleLayout? _layout;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Rebuild();
    }

    public void Bind(GameState state)
    {
        State = state;
        Rebuild();
    }

    private void Rebuild()
    {
        if (State is null) return;
        int nChars = State.AliveCharacters.Count();
        int nEnemies = State.Enemies.Count;
        int tlLen = State.Timeline.Length;
        int hand = State.Characters.FirstOrDefault()?.Hand.Count ?? 0;
        _layout = BattleLayout.Compute(nChars, nEnemies, tlLen, hand, State.Pointer);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_layout is null) return;
        // 角色立绘（左，按专属颜色）
        int i = 0;
        foreach (var c in State?.AliveCharacters ?? Enumerable.Empty<Character>())
        {
            var r = _layout.CharacterPortraits[i];
            DrawRect(ToRect(r), ColorOf(c.Color), filled: true);
            DrawRect(ToRect(r), new Color(1, 1, 1, 0.6f), filled: false);
            i++;
        }
        // 敌人立绘（右）
        i = 0;
        foreach (var e in State?.Enemies ?? Enumerable.Empty<Enemy>())
        {
            var r = _layout.EnemyPortraits[i];
            DrawRect(ToRect(r), new Color(0.8f, 0.3f, 0.3f), filled: true);
            DrawRect(ToRect(r), new Color(1, 1, 1, 0.6f), filled: false);
            i++;
        }
        // 时间轴
        foreach (var cell in _layout.TimelineCells)
        {
            DrawRect(ToRect(cell), new Color(0.2f, 0.2f, 0.25f), filled: true);
            DrawRect(ToRect(cell), new Color(0.5f, 0.5f, 0.55f), filled: false);
        }
        DrawRect(ToRect(_layout.PointerMarker), new Color(1f, 0.85f, 0.2f), filled: true);
        // 手牌
        i = 0;
        foreach (var card in State?.Characters.FirstOrDefault()?.Hand ?? Enumerable.Empty<Card>())
        {
            var r = _layout.HandCards[i];
            DrawRect(ToRect(r), ColorOf(unchecked((int)0xFF8A8A8A)), filled: true);
            DrawRect(ToRect(r), new Color(1, 1, 1, 0.6f), filled: false);
            i++;
        }
    }

    private static Rect2 ToRect(RectF r) => new(r.X, r.Y, r.W, r.H);

    private static Color ColorOf(int argb)
    {
        float a = ((argb >> 24) & 0xFF) / 255f;
        float r = ((argb >> 16) & 0xFF) / 255f;
        float g = ((argb >> 8) & 0xFF) / 255f;
        float b = (argb & 0xFF) / 255f;
        return new Color(r, g, b, a);
    }
}
