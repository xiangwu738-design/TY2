// =============================================================================
// Slack <-> Claude Code 桥（Socket Mode，无需公网 URL）
//   - 你在 Slack 发消息（DM 或 @机器人）→ 调 `claude -p` headless 处理
//   - 输出实时更新到同一条 Slack 消息（tool 调用、文本都会显示）
//   - 用 --resume 保持每个频道的会话上下文连续
// 启动：node --env-file=.env bot.mjs
// 需要 env：SLACK_BOT_TOKEN(xoxb-)  SLACK_APP_TOKEN(xapp-)  PROJECT_DIR(可选)
// =============================================================================
import { App } from '@slack/bolt';
import { spawn } from 'node:child_process';

const BOT_TOKEN = process.env.SLACK_BOT_TOKEN;
const APP_TOKEN = process.env.SLACK_APP_TOKEN;
const PROJECT_DIR = process.env.PROJECT_DIR || process.cwd();
const MAX_TURNS = parseInt(process.env.MAX_TURNS || '40', 10);

if (!BOT_TOKEN || !APP_TOKEN) {
	console.error('请在 .env 中设置 SLACK_BOT_TOKEN 与 SLACK_APP_TOKEN');
	process.exit(1);
}

const app = new App({ token: BOT_TOKEN, appToken: APP_TOKEN });
const sessions = new Map(); // channel -> session_id
let botUserId = '';

async function handleText(text, channel) {
	text = text.trim();
	if (!text) return;
	const init = await app.client.chat.postMessage({ channel, text: '🤖 处理中…' });
	await runClaude(text, channel, init.ts);
}

function runClaude(text, channel, replyTs) {
	return new Promise((resolve) => {
		const session = sessions.get(channel);
		const args = ['-p', text, '--output-format', 'stream-json', '--verbose', '--max-turns', String(MAX_TURNS)];
		if (session) args.push('--resume', session);
		const child = spawn('claude', args, { cwd: PROJECT_DIR, env: process.env });

		let buf = '';
		let lastUpdate = 0;
		let sessionId = session;

		const flush = async (final) => {
			const now = Date.now();
			if (!final && now - lastUpdate < 2500) return; // 节流：非终态最多 2.5s 更新一次
			lastUpdate = now;
			const out = (buf || '…').slice(-3900); // Slack 单条上限 ~4000
			try {
				await app.client.chat.update({ channel, ts: replyTs, text: out });
			} catch (_) { /* 忽略偶发更新失败 */ }
		};

		let lineBuf = '';
		child.stdout.setEncoding('utf8');
		child.stdout.on('data', (chunk) => {
			lineBuf += chunk;
			const lines = lineBuf.split('\n');
			lineBuf = lines.pop();
			for (const line of lines) {
				if (!line.trim()) continue;
				let ev;
				try { ev = JSON.parse(line); } catch (_) { continue; }
				if (ev.type === 'system' && ev.session_id) {
					sessionId = ev.session_id;
					sessions.set(channel, sessionId);
				} else if (ev.type === 'assistant' && ev.message && ev.message.content) {
					const content = ev.message.content;
					if (typeof content === 'string') {
						buf += content + '\n';
					} else if (Array.isArray(content)) {
						for (const b of content) {
							if (b.type === 'text' && b.text) buf += b.text + '\n';
							else if (b.type === 'tool_use') buf += `\n🔧 ${b.name}\n`;
						}
					}
					flush(false);
				} else if (ev.type === 'result') {
					if (ev.result) buf = ev.result;
					if (ev.session_id) { sessionId = ev.session_id; sessions.set(channel, sessionId); }
					flush(true);
				}
			}
		});
		child.stderr.on('data', () => { /* 静默，避免噪音 */ });
		child.on('close', async (code) => {
			await flush(true);
			if (code !== 0 && !buf) {
				try { await app.client.chat.update({ channel, ts: replyTs, text: '⚠️ claude 退出码 ' + code }); } catch (_) {}
			}
			resolve();
		});
	});
}

// DM 直接响应
app.message(async ({ message, client }) => {
	if (message.subtype || message.bot_id) return;
	if (message.channel_type !== 'im') return;
	await handleText(message.text || '', message.channel);
});

// 频道里 @机器人 触发
app.event('app_mention', async ({ event, client }) => {
	if (event.bot_id) return;
	const text = (event.text || '').replace(new RegExp('<@' + botUserId + '>', 'g'), '');
	await handleText(text, event.channel);
});

(async () => {
	await app.start();
	const auth = await app.client.auth.test({ token: BOT_TOKEN });
	botUserId = auth.user_id;
	console.log('⚡ Slack 桥已启动 | bot=' + botUserId + ' | project=' + PROJECT_DIR);
})();
