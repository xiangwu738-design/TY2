// =============================================================================
// Slack <-> Claude Code 桥（Socket Mode，无需公网 URL）
//   - Slack 消息（DM 或 @机器人）→ 调 `claude -p` headless 处理
//   - 输出实时更新到同一条 Slack 消息
//   - --resume 保持每频道会话上下文连续
//   - AUTO_CONTINUE=1：一轮跑完若未见【ALL DONE】则自动续跑，直到完成或达 AUTO_MAX
// 启动：node --env-file=.env bot.mjs
// env：SLACK_BOT_TOKEN  SLACK_APP_TOKEN  PROJECT_DIR(可选)
//      MAX_TURNS(默认60)  AUTO_CONTINUE(0/1)  AUTO_MAX(默认40)
// =============================================================================
import { App } from '@slack/bolt';
import { spawn } from 'node:child_process';

const BOT_TOKEN = process.env.SLACK_BOT_TOKEN;
const APP_TOKEN = process.env.SLACK_APP_TOKEN;
const PROJECT_DIR = process.env.PROJECT_DIR || process.cwd();
const MAX_TURNS = parseInt(process.env.MAX_TURNS || '60', 10);
const AUTO_CONTINUE = process.env.AUTO_CONTINUE === '1';
const AUTO_MAX = parseInt(process.env.AUTO_MAX || '40', 10);
const DONE_MARKER = '【ALL DONE】';

if (!BOT_TOKEN || !APP_TOKEN) {
	console.error('请在 .env 中设置 SLACK_BOT_TOKEN 与 SLACK_APP_TOKEN');
	process.exit(1);
}

const app = new App({ token: BOT_TOKEN, appToken: APP_TOKEN });
const sessions = new Map(); // channel -> session_id
let botUserId = '';

// 跑一轮 claude；返回状态 'done' | 'error' | 'continue'
function runClaude(text, channel, replyTs) {
	return new Promise((resolve) => {
		const session = sessions.get(channel);
		const args = ['-p', text, '--output-format', 'stream-json', '--verbose', '--max-turns', String(MAX_TURNS)];
		if (session) args.push('--resume', session);
		const child = spawn('claude', args, { cwd: PROJECT_DIR, env: process.env });

		let buf = '';
		let lastUpdate = 0;
		let sessionId = session;
		let errored = false;

		const flush = async (final) => {
			const now = Date.now();
			if (!final && now - lastUpdate < 2500) return;
			lastUpdate = now;
			const out = (buf || '…').slice(-3900);
			try { await app.client.chat.update({ channel, ts: replyTs, text: out }); } catch (_) {}
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
					if (typeof content === 'string') buf += content + '\n';
					else if (Array.isArray(content)) {
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
		child.stderr.on('data', () => {});
		child.on('close', async (code) => {
			await flush(true);
			if (code !== 0 && !buf) {
				errored = true;
				try { await app.client.chat.update({ channel, ts: replyTs, text: '⚠️ claude 退出码 ' + code }); } catch (_) {}
			}
			if (errored) resolve('error');
			else if (buf.includes(DONE_MARKER)) resolve('done');
			else resolve('continue');
		});
	});
}

// 自动续跑循环
async function runLoop(firstPrompt, channel) {
	let prompt = firstPrompt;
	for (let iter = 0; ; iter++) {
		const init = await app.client.chat.postMessage({
			channel,
			text: iter === 0 ? '🤖 开工…' : `🤖 自动续跑 #${iter}…`
		});
		const status = await runClaude(prompt, channel, init.ts);
		if (status === 'done') {
			await app.client.chat.postMessage({ channel, text: '✅ 全部完成（' + DONE_MARKER + '）' });
			return;
		}
		if (status === 'error') {
			await app.client.chat.postMessage({ channel, text: '⏹ 出错停止，修好后发消息可继续（自动续跑已停）' });
			return;
		}
		if (!AUTO_CONTINUE) return;
		if (iter + 1 >= AUTO_MAX) {
			await app.client.chat.postMessage({ channel, text: `⏹ 达自动续跑上限 ${AUTO_MAX}，发"继续"可再跑` });
			return;
		}
		prompt = `继续推进。若已全部完成，在回复末尾单独输出一行 ${DONE_MARKER}；否则继续下一阶段并汇报进度。`;
	}
}

async function handleText(text, channel) {
	text = text.trim();
	if (!text) return;
	await runLoop(text, channel);
}

app.message(async ({ message }) => {
	if (message.subtype || message.bot_id) return;
	if (message.channel_type !== 'im') return;
	await handleText(message.text || '', message.channel);
});

app.event('app_mention', async ({ event }) => {
	if (event.bot_id) return;
	const text = (event.text || '').replace(new RegExp('<@' + botUserId + '>', 'g'), '');
	await handleText(text, event.channel);
});

(async () => {
	await app.start();
	const auth = await app.client.auth.test({ token: BOT_TOKEN });
	botUserId = auth.user_id;
	console.log(`⚡ Slack 桥已启动 | bot=${botUserId} | project=${PROJECT_DIR} | auto=${AUTO_CONTINUE} maxIter=${AUTO_MAX} maxTurns=${MAX_TURNS}`);
})();
