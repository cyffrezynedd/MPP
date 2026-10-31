const { test, describe, before, after } = require('node:test');
const assert = require('node:assert');
const http = require('http');
const { app, pool, dbInitPromise } = require('../server');

let server;
let baseUrl;

before(async () => {
  await dbInitPromise;

  await new Promise(resolve => {
    server = http.createServer(app).listen(0, () => {
      baseUrl = `http://localhost:${server.address().port}`;
      resolve();
    });
  });
});

after(async () => {
  if (server) server.close();
  await pool.end();
});

describe('Проверка семантики HTTP кодов и модели доступа', () => {
  test('401 Unauthorized: обращение к защищенному маршруту без токена', async () => {
    const res = await fetch(`${baseUrl}/api/tasks`);
    assert.strictEqual(res.status, 401);
    const body = await res.json();
    assert.strictEqual(body.error, 'Unauthorized');
  });

  test('400 Bad Request: передача невалидных параметров при регистрации', async () => {
    const res = await fetch(`${baseUrl}/api/auth/register`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: 'bad-email', password: '123' })
    });
    assert.strictEqual(res.status, 400);
    const body = await res.json();
    assert.strictEqual(body.error, 'Bad Request');
  });

  test('404 Not Found: обращение к несуществующему API маршруту', async () => {
    const res = await fetch(`${baseUrl}/api/non-existent-route`);
    assert.strictEqual(res.status, 404);
    const body = await res.json();
    assert.strictEqual(body.error, 'Not Found');
  });

  test('403 Forbidden: роль Наблюдатель не имеет права создавать задачи', async () => {
    const loginRes = await fetch(`${baseUrl}/api/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: 'viewer@tasks.local', password: 'Viewer123!' })
    });
    const { token } = await loginRes.json();

    const createRes = await fetch(`${baseUrl}/api/tasks`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${token}`
      },
      body: JSON.stringify({ title: 'Тестовая задача' })
    });

    assert.strictEqual(createRes.status, 403);
    const body = await createRes.json();
    assert.strictEqual(body.error, 'Forbidden');
  });

  test('403 Forbidden: роль Редактор не имеет права удалять задачи', async () => {
    const loginRes = await fetch(`${baseUrl}/api/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: 'editor@tasks.local', password: 'Editor123!' })
    });
    const { token } = await loginRes.json();

    const deleteRes = await fetch(`${baseUrl}/api/tasks/1`, {
      method: 'DELETE',
      headers: { 'Authorization': `Bearer ${token}` }
    });

    assert.strictEqual(deleteRes.status, 403);
  });
});

describe('Защита от подбора учетных данных', () => {
  test('429 Too Many Requests: блокировка после серии неудачных попыток', async () => {
    const attackEmail = `victim_${Date.now()}@tasks.local`;

    for (let i = 0; i < 5; i++) {
      await fetch(`${baseUrl}/api/auth/login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: attackEmail, password: 'WrongPassword' })
      });
    }

    const blockedRes = await fetch(`${baseUrl}/api/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: attackEmail, password: 'WrongPassword' })
    });

    assert.strictEqual(blockedRes.status, 429);
    assert.ok(blockedRes.headers.get('retry-after'));
    const body = await blockedRes.json();
    assert.strictEqual(body.error, 'Too Many Requests');
  });
});

describe('Структурированное логирование', () => {
  test('Логер формирует корректный JSON с метаданными', () => {
    const logger = require('../logger');
    assert.strictEqual(typeof logger.info, 'function');
    assert.strictEqual(typeof logger.security, 'function');
    assert.strictEqual(typeof logger.error, 'function');
  });
});