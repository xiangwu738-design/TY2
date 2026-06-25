using Godot;
using System;
using System.Linq;
using Tongyuan.Core.Core;
using Tongyuan.Core.Data;
using Tongyuan.Core.Roguelike;

namespace Tongyuan.UI;

/// <summary>Roguelike 外壳屏（UI 重置阶段4）：商店/奖励/休息/事件。代码构建 Control，接 RunController。
/// 每屏完成后调 OnDone 由 Main 路由器 Advance→Route。</summary>

/// <summary>商店：金币 + 牌/遗物/移牌/恢复上限四档（规格 §4.9 价格）。</summary>
public partial class ShopScreen : Control
{
    public RunController Run { get; set; } = null!;
    public Action? OnLeave { get; set; }
    private Label _gold = null!;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var vb = new VBoxContainer();
        vb.OffsetLeft = 300; vb.OffsetTop = 180; vb.OffsetRight = 1620; vb.OffsetBottom = 900;
        vb.AddThemeConstantOverride("separation", 10);
        var title = new Label { Text = "🛒 商店" }; title.AddThemeFontSizeOverride("font_size", 28); vb.AddChild(title);
        _gold = new Label(); _gold.AddThemeFontSizeOverride("font_size", 18); vb.AddChild(_gold);
        vb.AddChild(BuyBtn("买牌 (50金)", () => Run.ShopBuyItem(RunController.ShopBuy.Card, SampleCards.All[0])));
        vb.AddChild(BuyBtn("买遗物 (150金)", () => Run.ShopBuyItem(RunController.ShopBuy.Relic)));
        vb.AddChild(BuyBtn("移牌 (30金)", () => Run.ShopBuyItem(RunController.ShopBuy.Remove)));
        vb.AddChild(BuyBtn("恢复上限 (40金)", () => Run.ShopBuyItem(RunController.ShopBuy.RestoreMax)));
        var leave = new Button { Text = "离开商店", CustomMinimumSize = new Vector2(220, 44) };
        leave.Pressed += () => OnLeave?.Invoke();
        vb.AddChild(leave);
        AddChild(vb);
        Refresh();
    }

    private Button BuyBtn(string text, Func<bool> act)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(280, 40) };
        b.Pressed += () => { act(); Refresh(); };
        return b;
    }

    private void Refresh() => _gold.Text = $"金币：{Run.State.Gold}　牌组：{Run.State.Deck.Count}";
}

/// <summary>战后奖励：三选一加牌（规格 §4.9 战后加牌）。</summary>
public partial class RewardScreen : Control
{
    public RunController Run { get; set; } = null!;
    public Action? OnPicked { get; set; }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var vb = new VBoxContainer();
        vb.OffsetLeft = 200; vb.OffsetTop = 120; vb.OffsetRight = 1720; vb.OffsetBottom = 960;
        vb.AddThemeConstantOverride("separation", 16);
        var title = new Label { Text = "🎁 战斗胜利 · 三选一加牌" }; title.AddThemeFontSizeOverride("font_size", 28); vb.AddChild(title);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 24);
        // 三选一：取 SampleCards 前 3 张作奖励池占位
        foreach (var def in SampleCards.All.Take(3))
        {
            var card = new Card { Def = def };
            var cv = new CardView();
            cv.Setup(card, null);
            var picked = def;
            cv.OnClicked += _ =>
            {
                Run.WinCombat(picked); // 加牌 + 奖励金币 + BattlesWon++
                OnPicked?.Invoke();
            };
            row.AddChild(cv);
        }
        vb.AddChild(row);
        AddChild(vb);
    }
}

/// <summary>休息：恢复血量上限（规格 §4.6/§4.9）。占位：直接推进。</summary>
public partial class RestScreen : Control
{
    public RunController Run { get; set; } = null!;
    public Action? OnDone { get; set; }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var vb = new VBoxContainer();
        vb.OffsetLeft = 400; vb.OffsetTop = 280; vb.OffsetRight = 1520; vb.OffsetBottom = 800;
        vb.AddThemeConstantOverride("separation", 14);
        var title = new Label { Text = "🏕 篝火（休息）" };
        title.AddThemeFontSizeOverride("font_size", 28);
        vb.AddChild(title);
        var l = new Label { Text = "恢复血量上限（占位：本场未持久化角色，效果待接入跑局角色）" };
        l.AddThemeFontSizeOverride("font_size", 16); vb.AddChild(l);
        var b = new Button { Text = "休息后继续", CustomMinimumSize = new Vector2(220, 44) };
        b.Pressed += () => OnDone?.Invoke();
        vb.AddChild(b);
        AddChild(vb);
    }
}

/// <summary>事件：占位。</summary>
public partial class EventScreen : Control
{
    public RunController Run { get; set; } = null!;
    public Action? OnDone { get; set; }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var ev = Run.RollEvent(Run.State.BattlesWon);
        var vb = new VBoxContainer();
        vb.OffsetLeft = 400; vb.OffsetTop = 280; vb.OffsetRight = 1520; vb.OffsetBottom = 800;
        vb.AddThemeConstantOverride("separation", 14);
        var t = new Label { Text = $"❓ 事件：{ev.Title}" }; t.AddThemeFontSizeOverride("font_size", 26); vb.AddChild(t);
        var b = new Label { Text = ev.Body }; b.AddThemeFontSizeOverride("font_size", 16); vb.AddChild(b);
        var cont = new Button { Text = "继续", CustomMinimumSize = new Vector2(220, 44) };
        cont.Pressed += () => OnDone?.Invoke();
        vb.AddChild(cont);
        AddChild(vb);
    }
}
