const express = require('express');
const path = require('path');
const fs = require('fs');
const crypto = require('crypto');
const multer = require('multer');
const { Pool } = require('pg');
const logger = require('./logger');

const app = express();
const PORT = process.env.PORT || 3000;

const uploadsDir = path.join(__dirname, 'uploads');
if (!fs.existsSync(uploadsDir)) {
  fs.mkdirSync(uploadsDir, { recursive: true });
}

const storage = multer.diskStorage({
  destination: (req, file, cb) => cb(null, uploadsDir),
  filename: (req, file, cb) => {
    const uniquePrefix = Date.now() + '-' + Math.round(Math.random() * 1e9);
    cb(null, uniquePrefix + '-' + file.originalname.replace(/\s+/g, '_'));
  }
});

const upload = multer({
  storage,
  limits: { fileSize: 10 * 1024 * 1024 }
});

const pool = new Pool({
  host: process.env.DB_HOST || 'localhost',
  port: process.env.DB_PORT || 5432,
  user: process.env.DB_USER || 'user',
  password: process.env.DB_PASSWORD || 'password',
  database: process.env.DB_NAME || 'taskdb'
});

function hashPassword(password, salt = crypto.randomBytes(16).toString('hex')) {
  const hash = crypto.scryptSync(password, salt, 64).toString('hex');
  return `${salt}:${hash}`;
}

function verifyPassword(password, stored) {
  const [salt, key] = stored.split(':');
  if (!salt || !key) return false;
  const derivedKey = crypto.scryptSync(password, salt, 64).toString('hex');
  return crypto.timingSafeEqual(Buffer.from(key, 'hex'), Buffer.from(derivedKey, 'hex'));
}

async function initDB() {
  let connected = false;
  while (!connected) {
    try {
      await pool.query(`
        CREATE TABLE IF NOT EXISTS users (
          id SERIAL PRIMARY KEY,
          email VARCHAR(255) UNIQUE NOT NULL,
          password_hash VARCHAR(255) NOT NULL,
          role VARCHAR(30) NOT NULL DEFAULT 'viewer',
          created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
        );
      `);

      await pool.query(`
        CREATE TABLE IF NOT EXISTS sessions (
          id SERIAL PRIMARY KEY,
          user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
          token VARCHAR(128) UNIQUE NOT NULL,
          ip VARCHAR(64),
          user_agent TEXT,
          expires_at TIMESTAMP NOT NULL,
          created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
        );
      `);

      await pool.query(`
        CREATE TABLE IF NOT EXISTS password_resets (
          id SERIAL PRIMARY KEY,
          user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
          token VARCHAR(128) UNIQUE NOT NULL,
          expires_at TIMESTAMP NOT NULL,
          used BOOLEAN DEFAULT FALSE,
          created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
        );
      `);

      await pool.query(`
        CREATE TABLE IF NOT EXISTS login_attempts (
          id SERIAL PRIMARY KEY,
          identifier VARCHAR(255) NOT NULL,
          attempt_time TIMESTAMP DEFAULT CURRENT_TIMESTAMP
        );
      `);

      await pool.query(`
        CREATE TABLE IF NOT EXISTS tasks (
          id SERIAL PRIMARY KEY,
          title VARCHAR(150) NOT NULL,
          description TEXT,
          status VARCHAR(20) NOT NULL DEFAULT 'pending',
          due_date DATE,
          file_name VARCHAR(255),
          file_original_name VARCHAR(255),
          author_id INTEGER REFERENCES users(id) ON DELETE SET NULL,
          created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
        );
      `);

      const countRes = await pool.query('SELECT COUNT(*) FROM users');
      if (parseInt(countRes.rows[0].count, 10) === 0) {
        await pool.query(`
          INSERT INTO users (email, password_hash, role) VALUES
          ('admin@tasks.local', $1, 'admin'),
          ('editor@tasks.local', $2, 'editor'),
          ('viewer@tasks.local', $3, 'viewer');
        `, [
          hashPassword('Admin123!'),
          hashPassword('Editor123!'),
          hashPassword('Viewer123!')
        ]);
        logger.info('Базовые пользователи инициализированы');
      }

      connected = true;
      logger.info('База данных успешно подключена и инициализирована');
    } catch (err) {
      logger.error('Ошибка инициализации БД, повтор через 2с', { error: err.message });
      await new Promise(resolve => setTimeout(resolve, 2000));
    }
  }
}
initDB();

app.use((req, res, next) => {
  req.id = crypto.randomUUID();
  const startTime = Date.now();

  res.on('finish', () => {
    const durationMs = Date.now() - startTime;
    const meta = {
      requestId: req.id,
      method: req.method,
      url: req.originalUrl,
      status: res.statusCode,
      durationMs,
      ip: req.headers['x-forwarded-for'] || req.socket.remoteAddress,
      userId: req.user ? req.user.id : null
    };

    if (res.statusCode >= 500) {
      logger.error('HTTP 5xx Server Error', meta);
    } else if (res.statusCode >= 400) {
      logger.warn('HTTP 4xx Client Warning', meta);
    } else {
      logger.info('HTTP Request processed', meta);
    }
  });

  next();
});

app.use(express.json());
app.use(express.static(path.join(__dirname, 'public')));
app.use('/uploads', express.static(uploadsDir));

const MAX_ATTEMPTS = 5;
const BLOCK_WINDOW_MINUTES = 15;

async function checkBruteForce(identifier) {
  const query = `
    SELECT COUNT(*) FROM login_attempts
    WHERE identifier = $1 AND attempt_time > NOW() - INTERVAL '${BLOCK_WINDOW_MINUTES} minutes';
  `;
  const result = await pool.query(query, [identifier]);
  return parseInt(result.rows[0].count, 10) >= MAX_ATTEMPTS;
}

async function recordFailedAttempt(identifier) {
  await pool.query(
    'INSERT INTO login_attempts (identifier, attempt_time) VALUES ($1, CURRENT_TIMESTAMP)',
    [identifier]
  );
}

async function clearLoginAttempts(identifier) {
  await pool.query('DELETE FROM login_attempts WHERE identifier = $1', [identifier]);
}

async function authenticateToken(req, res, next) {
  const authHeader = req.headers['authorization'];
  const token = authHeader && authHeader.split(' ')[1];

  if (!token) {
    return res.status(401).json({
      status: 401,
      error: 'Unauthorized',
      message: 'Требуется токен авторизации'
    });
  }

  try {
    const query = `
      SELECT s.id as session_id, s.token, s.expires_at, u.id as user_id, u.email, u.role
      FROM sessions s
      JOIN users u ON s.user_id = u.id
      WHERE s.token = $1;
    `;
    const result = await pool.query(query, [token]);

    if (result.rows.length === 0) {
      return res.status(401).json({
        status: 401,
        error: 'Unauthorized',
        message: 'Недействительный токен сессии'
      });
    }

    const session = result.rows[0];
    if (new Date(session.expires_at) < new Date()) {
      await pool.query('DELETE FROM sessions WHERE id = $1', [session.session_id]);
      return res.status(401).json({
        status: 401,
        error: 'Unauthorized',
        message: 'Срок действия сессии истек. Выполните вход повторно'
      });
    }

    req.user = {
      id: session.user_id,
      email: session.email,
      role: session.role,
      sessionId: session.session_id,
      token: session.token
    };
    next();
  } catch (err) {
    logger.error('Ошибка проверки сессии', { error: err.message });
    return res.status(500).json({ status: 500, error: 'Internal Server Error', message: 'Ошибка сервера' });
  }
}

function requireRole(allowedRoles) {
  return (req, res, next) => {
    if (!req.user || !allowedRoles.includes(req.user.role)) {
      logger.security('Отказ в доступе по роли', {
        userId: req.user ? req.user.id : null,
        required: allowedRoles,
        actual: req.user ? req.user.role : null,
        url: req.originalUrl
      });
      return res.status(403).json({
        status: 403,
        error: 'Forbidden',
        message: 'Недостаточно прав для выполнения данной операции'
      });
    }
    next();
  };
}

function validateTaskPayload(data) {
  const errors = [];
  if (!data.title || typeof data.title !== 'string' || data.title.trim().length === 0) {
    errors.push('Название задачи обязательно для заполнения');
  } else if (data.title.trim().length > 150) {
    errors.push('Длина названия не должна превышать 150 символов');
  }

  const validStatuses = ['pending', 'in_progress', 'completed'];
  if (data.status && !validStatuses.includes(data.status)) {
    errors.push('Недопустимый статус. Возможные значения: pending, in_progress, completed');
  }

  if (data.dueDate && data.dueDate !== '') {
    const parsedDate = Date.parse(data.dueDate);
    if (isNaN(parsedDate)) {
      errors.push('Некорректный формат даты');
    }
  }

  return errors;
}

app.post('/api/auth/register', async (req, res) => {
  const { email, password, role } = req.body;

  if (!email || !password || typeof email !== 'string' || typeof password !== 'string') {
    return res.status(400).json({ status: 400, error: 'Bad Request', message: 'Email и пароль обязательны' });
  }

  const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
  if (!emailRegex.test(email)) {
    return res.status(400).json({ status: 400, error: 'Bad Request', message: 'Некорректный адрес электронной почты' });
  }

  if (password.length < 6) {
    return res.status(400).json({ status: 400, error: 'Bad Request', message: 'Длина пароля должна быть не менее 6 символов' });
  }

  const assignedRole = ['admin', 'editor', 'viewer'].includes(role) ? role : 'viewer';

  try {
    const existing = await pool.query('SELECT id FROM users WHERE email = $1', [email.toLowerCase().trim()]);
    if (existing.rows.length > 0) {
      return res.status(400).json({ status: 400, error: 'Bad Request', message: 'Пользователь с таким email уже зарегистрирован' });
    }

    const hash = hashPassword(password);
    const result = await pool.query(
      'INSERT INTO users (email, password_hash, role) VALUES ($1, $2, $3) RETURNING id, email, role, created_at',
      [email.toLowerCase().trim(), hash, assignedRole]
    );

    logger.security('Зарегистрирован новый пользователь', { userId: result.rows[0].id, email: result.rows[0].email, role: assignedRole });
    return res.status(201).json(result.rows[0]);
  } catch (err) {
    logger.error('Ошибка при регистрации', { error: err.message });
    return res.status(500).json({ status: 500, error: 'Internal Server Error', message: 'Ошибка сервера при регистрации' });
  }
});

app.post('/api/auth/login', async (req, res) => {
  const { email, password } = req.body;
  const clientIp = req.headers['x-forwarded-for'] || req.socket.remoteAddress;

  if (!email || !password) {
    return res.status(400).json({ status: 400, error: 'Bad Request', message: 'Укажите email и пароль' });
  }

  const identifier = `${clientIp}_${email.toLowerCase().trim()}`;

  try {
    const isBlocked = await checkBruteForce(identifier);
    if (isBlocked) {
      logger.security('Блокировка попытки входа: превышен лимит попыток', { ip: clientIp, email });
      res.setHeader('Retry-After', BLOCK_WINDOW_MINUTES * 60);
      return res.status(429).json({
        status: 429,
        error: 'Too Many Requests',
        message: `Слишком много неудачных попыток входа. Доступ заблокирован на ${BLOCK_WINDOW_MINUTES} минут`
      });
    }

    const userRes = await pool.query('SELECT * FROM users WHERE email = $1', [email.toLowerCase().trim()]);
    if (userRes.rows.length === 0 || !verifyPassword(password, userRes.rows[0].password_hash)) {
      await recordFailedAttempt(identifier);
      logger.security('Неудачная попытка входа', { ip: clientIp, email });
      return res.status(401).json({
        status: 401,
        error: 'Unauthorized',
        message: 'Неверный адрес электронной почты или пароль'
      });
    }

    const user = userRes.rows[0];
    await clearLoginAttempts(identifier);

    const token = crypto.randomBytes(32).toString('hex');
    const expiresAt = new Date(Date.now() + 2 * 60 * 60 * 1000);
    const userAgent = req.headers['user-agent'] || 'Unknown';

    await pool.query(
      `INSERT INTO sessions (user_id, token, ip, user_agent, expires_at)
       VALUES ($1, $2, $3, $4, $5)`,
      [user.id, token, clientIp, userAgent, expiresAt]
    );

    logger.security('Успешный вход пользователя', { userId: user.id, email: user.email, role: user.role, ip: clientIp });

    return res.status(200).json({
      token,
      expiresAt: expiresAt.toISOString(),
      user: {
        id: user.id,
        email: user.email,
        role: user.role
      }
    });
  } catch (err) {
    logger.error('Ошибка при входе', { error: err.message });
    return res.status(500).json({ status: 500, error: 'Internal Server Error', message: 'Ошибка сервера' });
  }
});

app.get('/api/auth/me', authenticateToken, (req, res) => {
  return res.status(200).json({ user: req.user });
});

app.post('/api/auth/logout', authenticateToken, async (req, res) => {
  try {
    await pool.query('DELETE FROM sessions WHERE id = $1', [req.user.sessionId]);
    logger.info('Сессия завершена', { userId: req.user.id, sessionId: req.user.sessionId });
    return res.status(200).json({ message: 'Сессия успешно завершена' });
  } catch (err) {
    logger.error('Ошибка завершения сессии', { error: err.message });
    return res.status(500).json({ status: 500, error: 'Internal Server Error', message: 'Ошибка сервера' });
  }
});

app.get('/api/auth/sessions', authenticateToken, async (req, res) => {
  try {
    const result = await pool.query(
      `SELECT id, ip, user_agent, expires_at, created_at
       FROM sessions
       WHERE user_id = $1 AND expires_at > NOW()
       ORDER BY created_at DESC`,
      [req.user.id]
    );

    const sessions = result.rows.map(s => ({
      ...s,
      isCurrent: s.id === req.user.sessionId
    }));

    return res.status(200).json(sessions);
  } catch (err) {
    logger.error('Ошибка получения списка сессий', { error: err.message });
    return res.status(500).json({ status: 500, error: 'Internal Server Error', message: 'Ошибка сервера' });
  }
});

app.delete('/api/auth/sessions/:id', authenticateToken, async (req, res) => {
  const sessionId = parseInt(req.params.id, 10);
  if (isNaN(sessionId)) {
    return res.status(400).json({ status: 400, error: 'Bad Request', message: 'Некорректный ID сессии' });
  }

  try {
    const result = await pool.query(
      'DELETE FROM sessions WHERE id = $1 AND user_id = $2 RETURNING id',
      [sessionId, req.user.id]
    );

    if (result.rows.length === 0) {
      return res.status(404).json({ status: 404, error: 'Not Found', message: 'Сессия не найдена' });
    }

    logger.security('Сессия отозвана пользователем', { userId: req.user.id, revokedSessionId: sessionId });
    return res.status(200).json({ message: 'Сессия успешно отозвана' });
  } catch (err) {
    logger.error('Ошибка отзыва сессии', { error: err.message });
    return res.status(500).json({ status: 500, error: 'Internal Server Error', message: 'Ошибка сервера' });
  }
});

app.post('/api/auth/logout-all', authenticateToken, async (req, res) => {
  try {
    await pool.query(
      'DELETE FROM sessions WHERE user_id = $1 AND id != $2',
      [req.user.id, req.user.sessionId]
    );
    logger.security('Отозваны все остальные сессии', { userId: req.user.id });
    return res.status(200).json({ message: 'Все остальные сессии успешно завершены' });
  } catch (err) {
    logger.error('Ошибка отзыва остальных сессий', { error: err.message });
    return res.status(500).json({ status: 500, error: 'Internal Server Error', message: 'Ошибка сервера' });
  }
});

app.post('/api/auth/forgot-password', async (req, res) => {
  const { email } = req.body;
  if (!email) {
    return res.status(400).json({ status: 400, error: 'Bad Request', message: 'Email обязателен' });
  }

  try {
    const userRes = await pool.query('SELECT id, email FROM users WHERE email = $1', [email.toLowerCase().trim()]);
    if (userRes.rows.length > 0) {
      const user = userRes.rows[0];
      const resetToken = crypto.randomBytes(32).toString('hex');
      const expiresAt = new Date(Date.now() + 15 * 60 * 1000);

      await pool.query(
        'INSERT INTO password_resets (user_id, token, expires_at) VALUES ($1, $2, $3)',
        [user.id, resetToken, expiresAt]
      );

      logger.security('Токен восстановления доступа сформирован', {
        to: user.email,
        resetToken,
        resetLink: `http://localhost:${PORT}/#reset-token=${resetToken}`
      });
    }

    return res.status(200).json({
      message: 'Если указанный адрес зарегистрирован, на него была отправлена инструкция по сбросу пароля'
    });
  } catch (err) {
    logger.error('Ошибка запроса восстановления доступа', { error: err.message });
    return res.status(500).json({ status: 500, error: 'Internal Server Error', message: 'Ошибка сервера' });
  }
});

app.post('/api/auth/reset-password', async (req, res) => {
  const { token, newPassword } = req.body;

  if (!token || !newPassword || newPassword.length < 6) {
    return res.status(400).json({
      status: 400,
      error: 'Bad Request',
      message: 'Токен и новый пароль обязательны'
    });
  }

  try {
    const resetRes = await pool.query(
      `SELECT id, user_id, expires_at, used
       FROM password_resets
       WHERE token = $1 AND used = FALSE;`,
      [token]
    );

    if (resetRes.rows.length === 0) {
      return res.status(400).json({ status: 400, error: 'Bad Request', message: 'Недействительный или уже использованный токен сброса' });
    }

    const reset = resetRes.rows[0];
    if (new Date(reset.expires_at) < new Date()) {
      return res.status(400).json({ status: 400, error: 'Bad Request', message: 'Срок действия токена сброса истек' });
    }

    const newHash = hashPassword(newPassword);
    await pool.query('UPDATE users SET password_hash = $1 WHERE id = $2', [newHash, reset.user_id]);
    await pool.query('UPDATE password_resets SET used = TRUE WHERE id = $1', [reset.id]);
    await pool.query('DELETE FROM sessions WHERE user_id = $1', [reset.user_id]);

    logger.security('Пароль успешно сброшен, все сессии завершены', { userId: reset.user_id });
    return res.status(200).json({ message: 'Пароль успешно обновлен. Войдите в систему с новым паролем' });
  } catch (err) {
    logger.error('Ошибка сброса пароля', { error: err.message });
    return res.status(500).json({ status: 500, error: 'Internal Server Error', message: 'Ошибка сервера' });
  }
});

app.get('/api/tasks', authenticateToken, requireRole(['viewer', 'editor', 'admin']), async (req, res) => {
  try {
    const { status } = req.query;
    let query = `
      SELECT t.*, u.email as author_email
      FROM tasks t
      LEFT JOIN users u ON t.author_id = u.id
    `;
    const params = [];

    if (status && ['pending', 'in_progress', 'completed'].includes(status)) {
      query += ' WHERE t.status = $1';
      params.push(status);
    }

    query += ' ORDER BY t.id DESC';
    const result = await pool.query(query, params);
    return res.status(200).json(result.rows);
  } catch (err) {
    logger.error('Ошибка получения списка задач', { error: err.message });
    return res.status(500).json({ status: 500, error: 'Internal Server Error', message: 'Ошибка сервера при получении списка задач' });
  }
});

app.get('/api/tasks/:id', authenticateToken, requireRole(['viewer', 'editor', 'admin']), async (req, res) => {
  const id = parseInt(req.params.id, 10);
  if (isNaN(id)) {
    return res.status(400).json({ status: 400, error: 'Bad Request', message: 'Некорректный идентификатор задачи' });
  }

  try {
    const result = await pool.query('SELECT * FROM tasks WHERE id = $1', [id]);
    if (result.rows.length === 0) {
      return res.status(404).json({ status: 404, error: 'Not Found', message: 'Задача с указанным ID не найдена' });
    }
    return res.status(200).json(result.rows[0]);
  } catch (err) {
    logger.error('Ошибка поиска задачи', { error: err.message, taskId: id });
    return res.status(500).json({ status: 500, error: 'Internal Server Error', message: 'Ошибка сервера при поиске задачи' });
  }
});

app.post('/api/tasks', authenticateToken, requireRole(['editor', 'admin']), upload.single('taskFile'), async (req, res) => {
  const { title, description, status, dueDate } = req.body;
  const validationErrors = validateTaskPayload({ title, description, status, dueDate });

  if (validationErrors.length > 0) {
    if (req.file) fs.unlinkSync(req.file.path);
    return res.status(400).json({ status: 400, error: 'Bad Request', errors: validationErrors });
  }

  const taskStatus = status || 'pending';
  const taskDueDate = dueDate && dueDate !== '' ? dueDate : null;
  const fileName = req.file ? req.file.filename : null;
  const fileOriginalName = req.file ? req.file.originalname : null;

  try {
    const result = await pool.query(
      `INSERT INTO tasks (title, description, status, due_date, file_name, file_original_name, author_id)
       VALUES ($1, $2, $3, $4, $5, $6, $7) RETURNING *`,
      [title.trim(), description || null, taskStatus, taskDueDate, fileName, fileOriginalName, req.user.id]
    );

    logger.info('Задача создана', { taskId: result.rows[0].id, userId: req.user.id });
    return res.status(201).json(result.rows[0]);
  } catch (err) {
    if (req.file) fs.unlinkSync(req.file.path);
    logger.error('Ошибка создания задачи', { error: err.message });
    return res.status(500).json({ status: 500, error: 'Internal Server Error', message: 'Ошибка создания задачи' });
  }
});

app.put('/api/tasks/:id', authenticateToken, requireRole(['editor', 'admin']), upload.single('taskFile'), async (req, res) => {
  const id = parseInt(req.params.id, 10);
  if (isNaN(id)) {
    if (req.file) fs.unlinkSync(req.file.path);
    return res.status(400).json({ status: 400, error: 'Bad Request', message: 'Некорректный идентификатор задачи' });
  }

  const { title, description, status, dueDate } = req.body;
  const validationErrors = validateTaskPayload({ title, description, status, dueDate });

  if (validationErrors.length > 0) {
    if (req.file) fs.unlinkSync(req.file.path);
    return res.status(400).json({ status: 400, error: 'Bad Request', errors: validationErrors });
  }

  try {
    const existingResult = await pool.query('SELECT * FROM tasks WHERE id = $1', [id]);
    if (existingResult.rows.length === 0) {
      if (req.file) fs.unlinkSync(req.file.path);
      return res.status(404).json({ status: 404, error: 'Not Found', message: 'Редактируемая задача не найдена' });
    }

    const currentTask = existingResult.rows[0];
    let newFileName = currentTask.file_name;
    let newOriginalName = currentTask.file_original_name;

    if (req.file) {
      if (currentTask.file_name) {
        const oldPath = path.join(uploadsDir, currentTask.file_name);
        if (fs.existsSync(oldPath)) fs.unlinkSync(oldPath);
      }
      newFileName = req.file.filename;
      newOriginalName = req.file.originalname;
    }

    const taskStatus = status || currentTask.status;
    const taskDueDate = dueDate !== undefined && dueDate !== '' ? dueDate : null;

    const updateResult = await pool.query(
      `UPDATE tasks
       SET title = $1, description = $2, status = $3, due_date = $4, file_name = $5, file_original_name = $6
       WHERE id = $7 RETURNING *`,
      [title.trim(), description || null, taskStatus, taskDueDate, newFileName, newOriginalName, id]
    );

    logger.info('Задача обновлена', { taskId: id, userId: req.user.id });
    return res.status(200).json(updateResult.rows[0]);
  } catch (err) {
    if (req.file) fs.unlinkSync(req.file.path);
    logger.error('Ошибка обновления задачи', { error: err.message, taskId: id });
    return res.status(500).json({ status: 500, error: 'Internal Server Error', message: 'Ошибка сервера при обновлении данных' });
  }
});

app.delete('/api/tasks/:id', authenticateToken, requireRole(['admin']), async (req, res) => {
  const id = parseInt(req.params.id, 10);
  if (isNaN(id)) {
    return res.status(400).json({ status: 400, error: 'Bad Request', message: 'Некорректный идентификатор задачи' });
  }

  try {
    const existingResult = await pool.query('SELECT * FROM tasks WHERE id = $1', [id]);
    if (existingResult.rows.length === 0) {
      return res.status(404).json({ status: 404, error: 'Not Found', message: 'Удаляемая задача не существует' });
    }

    const task = existingResult.rows[0];
    if (task.file_name) {
      const filePath = path.join(uploadsDir, task.file_name);
      if (fs.existsSync(filePath)) fs.unlinkSync(filePath);
    }

    await pool.query('DELETE FROM tasks WHERE id = $1', [id]);
    logger.info('Задача удалена', { taskId: id, userId: req.user.id });
    return res.status(200).json({ message: 'Задача успешно удалена', id });
  } catch (err) {
    logger.error('Ошибка удаления задачи', { error: err.message, taskId: id });
    return res.status(500).json({ status: 500, error: 'Internal Server Error', message: 'Ошибка сервера при удалении записи' });
  }
});

app.use('/api', (req, res) => {
  return res.status(404).json({ status: 404, error: 'Not Found', message: 'Запрашиваемый API-маршрут не существует' });
});

app.use((err, req, res, next) => {
  logger.error('Неперехваченное исключение', { error: err.message, stack: err.stack });
  res.status(500).json({ status: 500, error: 'Internal Server Error', message: 'Внутренняя ошибка сервиса' });
});

if (process.env.NODE_ENV !== 'test') {
  app.listen(PORT, () => {
    logger.info(`Сервер запущен на порту ${PORT}`);
  });
}

module.exports = { app, pool, hashPassword, verifyPassword };