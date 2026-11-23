import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { createServer } from 'node:http';
import { access, mkdir, mkdtemp, readFile } from 'node:fs/promises';
import { join } from 'node:path';
import { setTimeout as wait } from 'node:timers/promises';

// 此实验仅使用虚构凭据。调试端口不能用于真实账号。
const scratch = join(import.meta.dirname, '..', '..', '..', '.dotnet-home', 'portal-proof');
await mkdir(scratch, { recursive: true });
const profile = await mkdtemp(join(scratch, 'edge-profile-'));
const edge = process.argv[2];
await access(edge);
const submissions = [];
let pass = false;
let session = 0;
let forbiddenHits = 0;
let rejectedRequests = 0;
let scenario = '';
const account = 'fixture-student';
const password = 'test-value';
const server = createServer(async (req, res) => {
  const url = new URL(req.url, 'http://127.0.0.1');
  if (url.pathname === '/probe') {
    if (pass) return res.writeHead(200).end('expected');
    session++;
    if (scenario === '入口未知') return res.writeHead(302, { Location: 'https://unknown-prototype.invalid/portal' }).end();
    return res.writeHead(302, { Location: `/portal?s=${session}` }).end();
  }
  if (url.pathname === '/backup-probe') return res.writeHead(200).end('expected');
  if (url.pathname === '/unapproved') { forbiddenHits++; return res.writeHead(404).end(); }
  if (url.pathname === '/portal') {
    const action = scenario === '目标不匹配' ? '/unapproved' : '/sign-in';
    const count = scenario === '控件歧义' ? 2 : 1;
    const fields = '<label>学号<input autocomplete="username"></label>'.repeat(count);
    res.setHeader('Content-Type', 'text/html; charset=utf-8');
    return res.end(`<!doctype html><meta charset="utf-8"><main></main>
<script>
  const session = new URLSearchParams(location.search).get('s');
  document.querySelector('main').innerHTML = ${JSON.stringify(`<form data-submit="${action}">${fields}<label>密码<input autocomplete="current-password" type="password"></label><label>运营商<select><option value="campus">校园网</option><option value="other">其他</option></select></label><button>连接 Login</button></form>`)};
  document.querySelector('form').addEventListener('submit', async e => {
    e.preventDefault();
    const form = e.currentTarget;
    const payload = new URLSearchParams({ session, username: form.querySelector('[autocomplete=username]').value, password: form.querySelector('[type=password]').value, provider: form.querySelector('select').value });
    const response = await fetch(form.dataset.submit, {method: 'POST', body: payload});
    document.querySelector('main').textContent = await response.text();
    if (${JSON.stringify(scenario)} === '页面尝试额外上传') {
      try { await fetch('/unapproved', {method: 'POST', body: payload}); } catch {}
    }
    document.body.dataset.completed = 'yes';
  });
</script>`);
  }
  if (url.pathname !== '/sign-in' || req.method !== 'POST') return res.writeHead(404).end();
  const body = [];
  for await (const chunk of req) body.push(chunk);
  const values = new URLSearchParams(Buffer.concat(body).toString());
  submissions.push(values.get('session'));
  if (scenario === '登录无响应') { await wait(1800); return res.writeHead(503).end('unavailable'); }
  const good = values.get('username') === account && values.get('password') === password && values.get('provider') === 'campus' && values.get('session') === String(session);
  if (!good || scenario === '登录拒绝') return res.writeHead(403).end('rejected');
  if (!['声称成功但公网仍不通', '页面尝试额外上传'].includes(scenario)) pass = true;
  return res.writeHead(200).end('accepted');
});
server.listen(0, '127.0.0.1');
await once(server, 'listening');
const backend = `http://127.0.0.1:${server.address().port}`;
const origin = 'https://portal-prototype.invalid';
let browser, socket;
let browserProblem = '';
const mainTimer = setTimeout(() => {
  socket?.close();
  browser?.kill();
  server.closeAllConnections();
  server.close();
  console.error('原型达到总时限');
  process.exitCode = 1;
}, 25000);
try {
  browser = spawn(edge, [
    '--headless=new', '--no-first-run', '--disable-background-networking', '--disable-extensions',
    '--no-default-browser-check', '--remote-debugging-port=0', '--disable-quic',
    '--proxy-server=http://127.0.0.1:1', '--proxy-bypass-list=<-loopback>',
    `--user-data-dir=${profile}`, 'about:blank'
  ], { windowsHide: true, stdio: ['ignore', 'ignore', 'pipe'] });
  browser.on('error', error => { browserProblem = error.message; });
  browser.stderr.on('data', bytes => { browserProblem = `${browserProblem}${bytes}`.slice(-4000); });
  let port;
  for (let attempt = 0; attempt < 100; attempt++) {
    if (browser.exitCode !== null) break;
    try { port = Number((await readFile(join(profile, 'DevToolsActivePort'), 'utf8')).split(/\r?\n/)[0]); } catch (error) { if (error.code !== 'ENOENT') throw error; }
    if (port) break;
    await wait(100);
  }
  assert.ok(port, `无法取得隐藏浏览器调试端口：${browserProblem}`);
  const tabs = await fetch(`http://127.0.0.1:${port}/json/list`, { signal: AbortSignal.timeout(3000) }).then(response => response.json());
  const tab = tabs.find(item => item.type === 'page');
  assert.ok(tab, '缺少测试标签页');
  socket = new WebSocket(tab.webSocketDebuggerUrl);
  await Promise.race([
    once(socket, 'open'),
    wait(3000, undefined, { ref: false }).then(() => { throw Error('连接调试端口超时'); })
  ]);
  let nextId = 0;
  const pending = new Map();
  socket.addEventListener('message', event => {
    const message = JSON.parse(event.data);
    if (message.method === 'Fetch.requestPaused') {
      forward(message.params).catch(error => {
        console.error('请求转发失败', error.message);
        process.exitCode = 1;
      });
      return;
    }
    const waiter = pending.get(message.id);
    if (waiter) {
      pending.delete(message.id);
      clearTimeout(waiter.timer);
      if (message.error) waiter.reject(new Error(message.error.message));
      else waiter.resolve(message.result);
    }
  });
  function call(method, params = {}) {
    const id = ++nextId;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => { pending.delete(id); reject(new Error(`${method} 超时`)); }, 3500);
      pending.set(id, { resolve, reject, timer });
      socket.send(JSON.stringify({id, method, params}));
    });
  }
  async function forward(event) {
    const { requestId, request } = event;
    const url = new URL(request.url);
    const allowed = url.origin === origin && (
      (request.method === 'GET' && url.pathname === '/portal') ||
      (request.method === 'POST' && url.pathname === '/sign-in')
    );
    if (!allowed) {
      if (url.pathname === '/unapproved') rejectedRequests++;
      await call('Fetch.failRequest', {requestId, errorReason: 'BlockedByClient'});
      return;
    }
    const response = await fetch(`${backend}${url.pathname}${url.search}`, {
      method: request.method,
      body: request.method === 'POST' ? request.postData : undefined,
      redirect: 'manual',
      signal: AbortSignal.timeout(2200)
    });
    await call('Fetch.fulfillRequest', {
      requestId, responseCode: response.status,
      responseHeaders: [{name: 'Content-Type', value: response.headers.get('content-type') ?? 'text/plain'}],
      body: Buffer.from(await response.arrayBuffer()).toString('base64')
    });
  }
  await call('Fetch.enable', {patterns: [{urlPattern: '*'}]});
  async function evaluate(expression) {
    const response = await call('Runtime.evaluate', {expression, returnByValue: true});
    if (response.exceptionDetails) throw Error(response.exceptionDetails.text);
    return response.result.value;
  }
  async function poll(expression) {
    for (let attempt = 0; attempt < 35; attempt++) {
      if (await evaluate(expression)) return true;
      await wait(50);
    }
    return false;
  }
  async function verify(path) {
    const response = await fetch(`${backend}${path}`, {redirect: 'manual', signal: AbortSignal.timeout(2000)});
    return response.status === 200 && await response.text() === 'expected';
  }
  async function recover() {
    const discovery = await fetch(`${backend}/probe`, {redirect: 'manual', signal: AbortSignal.timeout(2000)});
    if (discovery.status === 200 && await discovery.text() === 'expected') return '校园网';
    assert.equal(discovery.status, 302);
    const entry = new URL(discovery.headers.get('location'), origin);
    if (entry.origin !== origin || entry.pathname !== '/portal')
      return await verify('/backup-probe') ? '备用网' : '失败';
    assert.equal(entry.searchParams.get('s'), String(session));
    const navigation = await call('Page.navigate', {url: entry.href});
    assert.ok(!navigation.errorText, navigation.errorText);
    assert.ok(await poll(`location.href === ${JSON.stringify(entry.href)} && !!document.querySelector('form')`), '页面未完成渲染');
    const submitted = await evaluate(`(() => {
      if (location.href !== ${JSON.stringify(entry.href)}) return false;
      const form = document.querySelector('form');
      if (new URL(form.dataset.submit, location.href).href !== ${JSON.stringify(origin + '/sign-in')}) return false;
      const user = form.querySelectorAll('input[autocomplete=username]');
      const password = form.querySelectorAll('input[type=password]');
      const choice = form.querySelector('select');
      if (user.length !== 1 || password.length !== 1 || !choice) return false;
      user[0].value = ${JSON.stringify(account)};
      password[0].value = ${JSON.stringify(password)};
      choice.value = 'campus';
      form.requestSubmit();
      return true;
    })()`);
    if (submitted) {
      const loginFinished = await poll(`document.body.dataset.completed === 'yes'`);
      if (loginFinished && await verify('/probe')) return '校园网';
    }
    return await verify('/backup-probe') ? '备用网' : '失败';
  }
  for (const [name, expected, attempts] of [
    ['首次认证', '校园网', 1], ['已经联网', '校园网', 0], ['过期后新会话', '校园网', 1],
    ['登录拒绝', '备用网', 1], ['声称成功但公网仍不通', '备用网', 1],
    ['目标不匹配', '备用网', 0], ['控件歧义', '备用网', 0], ['入口未知', '备用网', 0], ['页面尝试额外上传', '备用网', 1], ['登录无响应', '备用网', 1]
  ]) {
    const before = submissions.length;
    scenario = name;
    pass = name === '已经联网';
    assert.equal(await recover(), expected, name);
    assert.equal(submissions.length - before, attempts, name);
    console.log(`通过：${name} → ${expected}，提交 ${attempts} 次`);
  }
  assert.equal(forbiddenHits, 0, '未获准的上传到达了模拟服务器');
  assert.equal(rejectedRequests, 1, '没有观测到额外上传被拦截');
  assert.equal(new Set(submissions).size, submissions.length, '会话地址不能复用');
} finally {
  clearTimeout(mainTimer);
  socket?.close();
  browser?.kill();
  server.closeAllConnections();
  server.close();
}
