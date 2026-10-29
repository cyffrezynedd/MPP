const express = require('express');
const path = require('path');
const fs = require('fs');
const multer = require('multer');

const app = express();
const PORT = 3000;

const uploadsDir = path.join(__dirname, 'uploads');
if (!fs.existsSync(uploadsDir)) {
  fs.mkdirSync(uploadsDir);
}

const storage = multer.diskStorage({
  destination: (req, file, cb) => {
    cb(null, uploadsDir);
  },
  filename: (req, file, cb) => {
    const safeName = Date.now() + '-' + file.originalname.replace(/\s+/g, '_');
    cb(null, safeName);
  }
});
const upload = multer({ storage: storage });

app.set('view engine', 'ejs');
app.set('views', path.join(__dirname, 'views'));

app.use(express.urlencoded({ extended: true }));

app.use('/uploads', express.static(uploadsDir));

let tasks = [
  {
    id: 1,
    title: 'выполенине лаб 1',
    dueDate: '2026-28-10',
    status: 'pending',
    file: null
  }
];

app.get('/', (req, res) => {
  const currentFilter = req.query.status || 'all';

  let filteredTasks = tasks;
  if (currentFilter !== 'all') {
    filteredTasks = tasks.filter(task => task.status === currentFilter);
  }

  res.render('index', {
    tasks: filteredTasks,
    currentFilter: currentFilter
  });
});

app.post('/tasks/create', upload.single('taskFile'), (req, res) => {
  const { title, dueDate } = req.body;

  if (title && title.trim() !== '') {
    const newTask = {
      id: Date.now(),
      title: title.trim(),
      dueDate: dueDate || null,
      status: 'pending',
      file: req.file ? {
        originalName: req.file.originalname,
        fileName: req.file.filename
      } : null
    };

    tasks.push(newTask);
  }

  res.redirect('/');
});

app.post('/tasks/:id/toggle', (req, res) => {
  const taskId = Number(req.params.id);
  const task = tasks.find(t => t.id === taskId);

  if (task) {
    task.status = task.status === 'pending' ? 'completed' : 'pending';
  }

  res.redirect('/');
});

app.post('/tasks/:id/delete', (req, res) => {
  const taskId = Number(req.params.id);
  const taskIndex = tasks.findIndex(t => t.id === taskId);

  if (taskIndex !== -1) {
    const task = tasks[taskIndex];
    if (task.file) {
      const filePath = path.join(uploadsDir, task.file.fileName);
      if (fs.existsSync(filePath)) {
        fs.unlinkSync(filePath);
      }
    }
    tasks.splice(taskIndex, 1);
  }

  res.redirect('/');
});

app.listen(PORT, () => {
  console.log(`server is up: http://localhost:${PORT}`);
});