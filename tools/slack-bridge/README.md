# Slack ↔ Claude Code 桥

让你在 Slack 里看 Claude Code 的实时输出、提新要求，不用 SSH 进云 VM。
跑在云 VM 上（和 Claude Code 同机），用 Socket Mode，**无需公网 URL**。

## 一、建 Slack App

1. 打开 https://api.slack.com/apps → **Create New App** → **From scratch**，起名（如 `同渊开发`）。
2. **Socket Mode**（Settings → Socket Mode）：开启 → 生成一个 App-Level Token，scope 选 `connections:write`。记下 `xapp-...`。
3. **OAuth & Permissions → Bot Token Scopes**，加：
   - `chat:write`
   - `app_mentions:read`
   - `im:history`
   - `channels:history`、`groups:history`、`mpim:history`（按需）
4. **Event Subscriptions**：开启 → Subscribe to bot events，加：
   - `app_mention`
   - `message.im`
5. **Install to Workspace** → 记下 Bot User OAuth Token `xoxb-...`。
6. 在要用的频道里 `/invite @同渊开发`（或直接 DM 机器人）。

## 二、在云 VM 上运行

```bash
cd /path/to/NEO/tools/slack-bridge
cp .env.example .env
# 编辑 .env 填入两个 token 和工程目录
npm install
npm start          # = node --env-file=.env bot.mjs
```

`.env` 内容：
```
SLACK_BOT_TOKEN=xoxb-...
SLACK_APP_TOKEN=xapp-...
PROJECT_DIR=/path/to/NEO
MAX_TURNS=40
```

## 三、常驻（关掉 SSH 也不断）

```bash
tmux new -d -s slack
tmux send-keys -t slack "cd /path/to/NEO/tools/slack-bridge && npm start" Enter
# 回看：tmux attach -t slack
```

或写个 systemd unit 开机自启。

## 四、用法

- **DM 机器人**：直接发消息，等它实时更新输出。
- **频道 @机器人**：`@同渊开发 把附魔改成挂在具体牌上` —— 输出会更新在机器人回复的那条消息里。
- 上下文按频道连续（用 `--resume` 维护 session）。换频道=换会话。

## 注意

- 这里的 headless `claude -p` 与你 tmux 里的交互式 `claude` 是**不同进程**，别同时对同一批文件大改，以免冲突。
- 长任务用 `MAX_TURNS` 限轮次防失控；费用按 token 计。
