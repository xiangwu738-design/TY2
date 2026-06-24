// =============================================================================
// Slack <-> Claude Code 桥（Socket Mode）
//   - Slack 消息 → `claude -p` headless，输出实时更新到同一条消息
//   - --resume 保持每频道会话连续
//   - AUTO_CONTINUE=1：未见【ALL DONE】则自动续跑到完成或 AUTO_MAX
//   - 运行期间可继续发消息：排队，当前阶段结束后插队处理（不并发撞会话）
//   - 发 "stop"/"停止" 可在当前阶段后叫停自动续跑
// env：SLACK_BOT_TOKEN  SLACK_APP_TOKEN  PROJECT_DIR  MAX_TURNS(60)
//      AUTO_CONTINUE(0/1)  AUTO_MAX(40)
// =============================================================================
import pkg from '@slack/bolt';
const { App } = pkg;
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

const app = new App({ token: BOT_TOKEN, appToken: APP_TOKEN, socketMode: true });
const sessions = new Map();              // channel -> session_id
const chanState = new Map();             // channel -> { busy, queue, stopRequested }
let botUserId = '';

function getState(channel) {
	if (!chanState.has(channel)) chanState.set(channel, { busy: false, queue: [], stopRequested: false });
	return chanState.get(channel);
}

async function say(channel, text) {
	try { await app.client.chat.postMessage({ channel, text }); } catch (_) {}
}

// 跑一轮 claude；返回 'done' | 'error' | 'continue'
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
					sessionId = ev.session_id; sessions.set(channel, sessionId);
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

// 自动续跑 + 队列插队
async function runLoop(firstPrompt, channel) {
	const st = getState(channel);
	let prompt = firstPrompt;
	for (let iter = 0; ; iter++) {
		const init = await app.client.chat.postMessage({
			channel,
			text: iter === 0 ? '🤖 开工…' : `🤖 自动续跑 #${iter}…`
		});
		const status = await runClaude(prompt, channel, init.ts);

		if (status === 'done') { await say(channel, '✅ 全部完成（' + DONE_MARKER + '）'); return; }
		if (status === 'error') { await say(channel, '⏹ 出错停止，修好后发消息可继续'); return; }
		if (st.stopRequested) { st.stopRequested = false; await say(channel, '⏹ 已按你的要求停止自动续跑，发消息可继续'); return; }

		// 间隔处：先处理你运行期间排队的消息
		if (st.queue.length > 0) {
			prompt = st.queue.shift();
			await say(channel, `📥 处理你的插队消息：${prompt.slice(0, 120)}`);
			continue;
		}
		if (!AUTO_CONTINUE) return;
		if (iter + 1 >= AUTO_MAX) { await say(channel, `⏹ 达自动续跑上限 ${AUTO_MAX}，发"继续"可再跑`); return; }
		prompt = `继续推进。若已全部完成，在回复末尾单独输出一行 ${DONE_MARKER}；否则继续下一阶段并汇报。`;
	}
}

async function handleText(text, channel) {
	text = text.trim();
	if (!text) return;
	const st = getState(channel);
	const lower = text.toLowerCase();

	// 停止指令：当前阶段后叫停
	if (lower === 'stop' || lower === '停止' || lower === '停止续跑') {
		if (st.busy) { st.stopRequested = true; await say(channel, '⏹ 收到，当前阶段结束后停止自动续跑'); }
		else { await say(channel, '当前未在跑。'); }
		return;
	}

	if (st.busy) {
		st.queue.push(text);
		await say(channel, `📥 已排队（${st.queue.length}），当前阶段结束后插队处理`);
		return;
	}
	st.busy = true;
	st.stopRequested = false;
	try {
		await runLoop(text, channel);
	} finally {
		st.busy = false;
	}
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
