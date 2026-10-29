const express = require('express');
const path = require('path');
const fs = require('fs');
const multer = require('multer');
const { Pool } = require('pg');

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

async function initDB() {
  let connected = false;
  while (!connected) {
    try {
      await pool.query(`
        CREATE TABLE IF NOT EXISTS tasks (
          id SERIAL PRIMARY KEY,
          title VARCHAR(150) NOT NULL,
          description TEXT,
          status VARCHAR(20) NOT NULL DEFAULT 'pending',
          due_date DATE,
          file_name VARCHAR(255),
          file_original_name VARCHAR(255),
          created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
        );
      `);
      connected = true;
    } catch {
      await new Promise(resolve => setTimeout(resolve, 2000));
    }
  }
}
initDB();

app.use(express.json());
app.use(express.static(path.join(__dirname, 'public')));
app.use('/uploads', express.static(uploadsDir));

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

app.get('/api/tasks', async (req, res) => {
  try {
    const { status } = req.query;
    let query = 'SELECT * FROM tasks';
    const params = [];

    if (status && ['pending', 'in_progress', 'completed'].includes(status)) {
      query += ' WHERE status = $1';
      params.push(status);
    }

    query += ' ORDER BY id DESC';
    const result = await pool.query(query, params);
    return res.status(200).json(result.rows);
  } catch {
    return res.status(500).json({ error: 'Ошибка сервера при получении списка задач' });
  }
});

app.get('/api/tasks/:id', async (req, res) => {
  const id = parseInt(req.params.id, 10);
  if (isNaN(id)) {
    return res.status(400).json({ error: 'Некорректный идентификатор задачи' });
  }

  try {
    const result = await pool.query('SELECT * FROM tasks WHERE id = $1', [id]);
    if (result.rows.length === 0) {
      return res.status(404).json({ error: 'Задача с указанным ID не найдена' });
    }
    return res.status(200).json(result.rows[0]);
  } catch {
    return res.status(500).json({ error: 'Ошибка сервера при поиске задачи' });
  }
});

app.post('/api/tasks', upload.single('taskFile'), async (req, res) => {
  const { title, description, status, dueDate } = req.body;
  const validationErrors = validateTaskPayload({ title, description, status, dueDate });

  if (validationErrors.length > 0) {
    if (req.file) {
      fs.unlinkSync(req.file.path);
    }
    return res.status(400).json({ errors: validationErrors });
  }

  const taskStatus = status || 'pending';
  const taskDueDate = dueDate && dueDate !== '' ? dueDate : null;
  const fileName = req.file ? req.file.filename : null;
  const fileOriginalName = req.file ? req.file.originalname : null;

  try {
    const result = await pool.query(
      `INSERT INTO tasks (title, description, status, due_date, file_name, file_original_name)
       VALUES ($1, $2, $3, $4, $5, $6) RETURNING *`,
      [title.trim(), description || null, taskStatus, taskDueDate, fileName, fileOriginalName]
    );
    return res.status(201).json(result.rows[0]);
  } catch {
    if (req.file) {
      fs.unlinkSync(req.file.path);
    }
    return res.status(500).json({ error: 'Ошибка базы данных при создании задачи' });
  }
});

app.put('/api/tasks/:id', upload.single('taskFile'), async (req, res) => {
  const id = parseInt(req.params.id, 10);
  if (isNaN(id)) {
    if (req.file) fs.unlinkSync(req.file.path);
    return res.status(400).json({ error: 'Некорректный идентификатор задачи' });
  }

  const { title, description, status, dueDate } = req.body;
  const validationErrors = validateTaskPayload({ title, description, status, dueDate });

  if (validationErrors.length > 0) {
    if (req.file) fs.unlinkSync(req.file.path);
    return res.status(400).json({ errors: validationErrors });
  }

  try {
    const existingResult = await pool.query('SELECT * FROM tasks WHERE id = $1', [id]);
    if (existingResult.rows.length === 0) {
      if (req.file) fs.unlinkSync(req.file.path);
      return res.status(404).json({ error: 'Редактируемая задача не найдена' });
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

    return res.status(200).json(updateResult.rows[0]);
  } catch {
    if (req.file) fs.unlinkSync(req.file.path);
    return res.status(500).json({ error: 'Ошибка сервера при обновлении данных' });
  }
});

app.delete('/api/tasks/:id', async (req, res) => {
  const id = parseInt(req.params.id, 10);
  if (isNaN(id)) {
    return res.status(400).json({ error: 'Некорректный идентификатор задачи' });
  }

  try {
    const existingResult = await pool.query('SELECT * FROM tasks WHERE id = $1', [id]);
    if (existingResult.rows.length === 0) {
      return res.status(404).json({ error: 'Удаляемая задача не существует' });
    }

    const task = existingResult.rows[0];
    if (task.file_name) {
      const filePath = path.join(uploadsDir, task.file_name);
      if (fs.existsSync(filePath)) fs.unlinkSync(filePath);
    }

    await pool.query('DELETE FROM tasks WHERE id = $1', [id]);
    return res.status(200).json({ message: 'Задача успешно удалена', id });
  } catch {
    return res.status(500).json({ error: 'Ошибка сервера при удалении записи' });
  }
});

app.listen(PORT);