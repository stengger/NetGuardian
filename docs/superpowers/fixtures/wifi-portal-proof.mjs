import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { createServer } from 'node:http';
import { access, mkdir, mkdtemp } from 'node:fs/promises';
import { join } from 'node:path';
import { setTimeout as wait } from 'node:timers/promises';

// 此实验在本机模拟门户，只使用固定测试数据。
const scratch = join(import.meta.dirname, '..', '..', '..', '.dotnet-home', 'portal-proof');
await mkdir(scratch, { recursive: true });
const profile = await mkdtemp(join(scratch, 'edge-pipe-profile-'));
const edge = process.argv[2];
await access(edge);
const submissions = [];
let pass = false;
let session = 0;
let forbiddenHits = 0;
let rejectedRequests = 0;
let scenario = '';
const account = 'fixture-student';
const password = 'test-value&+😃';
const userMarker = `ng-user-${randomUUID()}`;
const secretMarker = `ng-password-${randomUUID()}`;
const approvedFields = new Map([['username', [userMarker, account]], ['password', [secretMarker, password]]]);
let refusedPayloads = 0;
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
    if (${JSON.stringify(scenario)} === '凭据在页面被变换') payload.set('password', 'transformed-' + payload.get('password'));
    const json = ${JSON.stringify(scenario)} === 'JSON 表单';
    const response = await fetch(form.dataset.submit, json
      ? {method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(Object.fromEntries(payload))}
      : {method: 'POST', body: payload});
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
  const raw = Buffer.concat(body).toString();
  const values = req.headers['content-type'] === 'application/json' ? new URLSearchParams(JSON.parse(raw)) : new URLSearchParams(raw);
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
let browser;
let browserProblem = '';
const mainTimer = setTimeout(() => {
  browser?.kill();
  server.closeAllConnections();
  server.close();
  console.error('原型达到总时限');
  process.exitCode = 1;
}, 25000);
try {
  browser = spawn(edge, [
    '--headless=new', '--no-first-run', '--disable-background-networking', '--disable-extensions',
    '--no-default-browser-check', '--remote-debugging-pipe', '--disable-quic',
    '--proxy-server=http://127.0.0.1:1', '--proxy-bypass-list=<-loopback>',
    `--user-data-dir=${profile}`, 'about:blank'
  ], { windowsHide: true, stdio: ['ignore', 'ignore', 'pipe', 'pipe', 'pipe'] });
  browser.on('error', error => { browserProblem = error.message; });
  browser.stderr.on('data', bytes => { browserProblem = `${browserProblem}${bytes}`.slice(-4000); });
  let nextId = 0;
  let sessionId;
  const pending = new Map();
  let remainder = Buffer.alloc(0);
  browser.stdio[3].on('error', error => { browserProblem = error.message; });
  browser.stdio[4].on('data', chunk => {
    remainder = Buffer.concat([remainder, chunk]);
    let cut;
    while ((cut = remainder.indexOf(0)) !== -1) {
      const text = remainder.subarray(0, cut).toString('utf8');
      remainder = remainder.subarray(cut + 1);
      if (text) receive(JSON.parse(text));
    }
  });
  function receive(message) {
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
  }
  function call(method, params = {}, session = sessionId) {
    const id = ++nextId;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => { pending.delete(id); reject(new Error(`${method} 超时 ${browserProblem}`)); }, 3500);
      pending.set(id, { resolve, reject, timer });
      browser.stdio[3].write(JSON.stringify({id, method, params, sessionId: session}) + '\0');
    });
  }
  function replaceSubmittedFields(request) {
    const type = Object.entries(request.headers).find(([key]) => key.toLowerCase() === 'content-type')?.[1].split(';')[0].trim();
    if (type === 'application/x-www-form-urlencoded') {
      const fields = new URLSearchParams(request.postData);
      for (const [field, [marker, value]] of approvedFields) {
        if (fields.getAll(field).length !== 1 || fields.get(field) !== marker) return null;
        fields.set(field, value);
      }
      return { type, body: fields.toString() };
    }
    if (type !== 'application/json') return null;
    let fields;
    try { fields = JSON.parse(request.postData); } catch { return null; }
    if (!fields || Array.isArray(fields) || typeof fields !== 'object') return null;
    for (const [field, [marker, value]] of approvedFields) {
      if (!Object.hasOwn(fields, field) || fields[field] !== marker) return null;
      fields[field] = value;
    }
    return { type, body: JSON.stringify(fields) };
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
    const payload = request.method === 'POST' ? replaceSubmittedFields(request) : undefined;
    if (payload === null) {
      refusedPayloads++;
      await call('Fetch.failRequest', {requestId, errorReason: 'BlockedByClient'});
      return;
    }
    const response = await fetch(`${backend}${url.pathname}${url.search}`, {
      method: request.method,
      body: payload?.body,
      headers: payload ? { 'Content-Type': payload.type } : undefined,
      redirect: 'manual',
      signal: AbortSignal.timeout(2200)
    });
    await call('Fetch.fulfillRequest', {
      requestId, responseCode: response.status,
      responseHeaders: [{name: 'Content-Type', value: response.headers.get('content-type') ?? 'text/plain'}],
      body: Buffer.from(await response.arrayBuffer()).toString('base64')
    });
  }
  const { targetId } = await call('Target.createTarget', { url: 'about:blank' });
  ({ sessionId } = await call('Target.attachToTarget', { targetId, flatten: true }));
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
      user[0].value = ${JSON.stringify(userMarker)};
      password[0].value = ${JSON.stringify(secretMarker)};
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
    ['首次认证', '校园网', 1], ['JSON 表单', '校园网', 1], ['已经联网', '校园网', 0], ['过期后新会话', '校园网', 1],
    ['登录拒绝', '备用网', 1], ['声称成功但公网仍不通', '备用网', 1],
    ['目标不匹配', '备用网', 0], ['控件歧义', '备用网', 0], ['入口未知', '备用网', 0], ['页面尝试额外上传', '备用网', 1], ['凭据在页面被变换', '备用网', 0], ['登录无响应', '备用网', 1]
  ]) {
    const before = submissions.length;
    scenario = name;
    pass = name === '已经联网';
    assert.equal(await recover(), expected, name);
    assert.equal(submissions.length - before, attempts, name);
    console.log(`通过：${name} → ${expected}，提交 ${attempts} 次`);
  }
  assert.equal(refusedPayloads, 1, '被页面变换的凭据不应发往服务器');
  assert.equal(forbiddenHits, 0, '未获准的上传到达了模拟服务器');
  assert.equal(rejectedRequests, 1, '没有观测到额外上传被拦截');
  assert.equal(new Set(submissions).size, submissions.length, '会话地址不能复用');
} finally {
  clearTimeout(mainTimer);
  browser?.kill();
  server.closeAllConnections();
  server.close();
}
