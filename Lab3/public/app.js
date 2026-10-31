let authToken = localStorage.getItem('task_auth_token') || null;
let currentUser = null;

const alertNotification = document.getElementById('alertNotification');
const authBar = document.getElementById('authBar');
const authCard = document.getElementById('authCard');
const appWorkspace = document.getElementById('appWorkspace');
const taskFormCard = document.getElementById('taskFormCard');

const taskForm = document.getElementById('taskForm');
const taskIdInput = document.getElementById('taskId');
const titleInput = document.getElementById('title');
const descriptionInput = document.getElementById('description');
const statusSelect = document.getElementById('status');
const dueDateInput = document.getElementById('dueDate');
const taskFileInput = document.getElementById('taskFile');
const currentFileNotice = document.getElementById('currentFileNotice');
const formHeader = document.getElementById('formHeader');
const cancelButton = document.getElementById('cancelButton');
const filterStatus = document.getElementById('filterStatus');
const tasksList = document.getElementById('tasksList');
const sessionsCard = document.getElementById('sessionsCard');
const sessionsList = document.getElementById('sessionsList');

const statusLabels = {
  pending: 'Ожидает',
  in_progress: 'В работе',
  completed: 'Завершено'
};

const roleLabels = {
  admin: 'Администратор',
  editor: 'Редактор',
  viewer: 'Наблюдатель'
};

function showAlert(messages, isSuccess = false) {
  const list = Array.isArray(messages) ? messages : [messages];
  alertNotification.className = `alert ${isSuccess ? 'alert-success' : 'alert-error'}`;
  alertNotification.innerHTML = list.map(m => `<div>${m}</div>`).join('');
  alertNotification.classList.remove('hidden');
  window.scrollTo({ top: 0, behavior: 'smooth' });
}

function clearAlert() {
  alertNotification.innerHTML = '';
  alertNotification.classList.add('hidden');
}

async function apiRequest(url, options = {}) {
  const headers = options.headers || {};
  if (authToken) {
    headers['Authorization'] = `Bearer ${authToken}`;
  }
  options.headers = headers;

  const response = await fetch(url, options);

  if (response.status === 401) {
    logout(false);
    throw new Error('Сессия завершена или истек срок действия ключа. Выполните вход');
  }

  return response;
}

function switchAuthTab(tab) {
  clearAlert();
  document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
  document.querySelectorAll('.auth-form').forEach(f => f.classList.add('hidden'));

  if (tab === 'login') {
    document.getElementById('tabLogin').classList.add('active');
    document.getElementById('loginForm').classList.remove('hidden');
  } else if (tab === 'register') {
    document.getElementById('tabRegister').classList.add('active');
    document.getElementById('registerForm').classList.remove('hidden');
  } else if (tab === 'forgot') {
    document.getElementById('tabForgot').classList.add('active');
    document.getElementById('forgotForm').classList.remove('hidden');
  } else if (tab === 'reset') {
    document.getElementById('resetForm').classList.remove('hidden');
  }
}

async function handleLogin(e) {
  e.preventDefault();
  clearAlert();
  const email = document.getElementById('loginEmail').value;
  const password = document.getElementById('loginPassword').value;

  try {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password })
    });
    const data = await res.json();

    if (!res.ok) {
      if (res.status === 429) {
        throw new Error(data.message || 'Слишком много попыток входа. Попробуйте позже');
      }
      throw new Error(data.message || 'Ошибка авторизации');
    }

    authToken = data.token;
    localStorage.setItem('task_auth_token', authToken);
    currentUser = data.user;
    updateAppState();
  } catch (err) {
    showAlert(err.message);
  }
}

async function handleRegister(e) {
  e.preventDefault();
  clearAlert();
  const email = document.getElementById('regEmail').value;
  const password = document.getElementById('regPassword').value;
  const role = document.getElementById('regRole').value;

  try {
    const res = await fetch('/api/auth/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password, role })
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.message || 'Ошибка при регистрации');

    showAlert('Регистрация успешна! Теперь вы можете войти в систему', true);
    switchAuthTab('login');
  } catch (err) {
    showAlert(err.message);
  }
}

async function handleForgotPassword(e) {
  e.preventDefault();
  clearAlert();
  const email = document.getElementById('forgotEmail').value;

  try {
    const res = await fetch('/api/auth/forgot-password', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email })
    });
    const data = await res.json();
    showAlert(data.message, true);
    switchAuthTab('reset');
  } catch (err) {
    showAlert(err.message);
  }
}

async function handleResetPassword(e) {
  e.preventDefault();
  clearAlert();
  const token = document.getElementById('resetTokenInput').value;
  const newPassword = document.getElementById('newPasswordInput').value;

  try {
    const res = await fetch('/api/auth/reset-password', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ token, newPassword })
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.message || 'Ошибка сброса пароля');

    showAlert(data.message, true);
    switchAuthTab('login');
  } catch (err) {
    showAlert(err.message);
  }
}

async function logout(callApi = true) {
  if (callApi && authToken) {
    try {
      await apiRequest('/api/auth/logout', { method: 'POST' });
    } catch {
    }
  }
  authToken = null;
  currentUser = null;
  localStorage.removeItem('task_auth_token');
  updateAppState();
}

async function checkAuthSession() {
  if (!authToken) {
    updateAppState();
    return;
  }
  try {
    const res = await apiRequest('/api/auth/me');
    if (!res.ok) throw new Error();
    const data = await res.json();
    currentUser = data.user;
    updateAppState();
  } catch {
    logout(false);
  }
}

function updateAppState() {
  if (currentUser) {
    authCard.classList.add('hidden');
    appWorkspace.classList.remove('hidden');

    const displayRole = roleLabels[currentUser.role] || currentUser.role;

    authBar.innerHTML = `
      <div class="user-badge">
        <span>${escapeHtml(currentUser.email)}</span>
        <span class="role-tag">${escapeHtml(displayRole)}</span>
      </div>
      <button class="btn btn-secondary" onclick="toggleSessionsPanel(true)">Сессии</button>
      <button class="btn btn-delete" onclick="logout()">Выход</button>
    `;

    if (currentUser.role === 'viewer') {
      taskFormCard.classList.add('hidden');
    } else {
      taskFormCard.classList.remove('hidden');
    }

    fetchTasks();
  } else {
    authCard.classList.remove('hidden');
    appWorkspace.classList.add('hidden');
    authBar.innerHTML = '';
  }
}

async function toggleSessionsPanel(show) {
  if (show) {
    sessionsCard.classList.remove('hidden');
    await loadSessions();
  } else {
    sessionsCard.classList.add('hidden');
  }
}

async function loadSessions() {
  try {
    const res = await apiRequest('/api/auth/sessions');
    if (!res.ok) throw new Error('Не удалось получить список сессий');
    const sessions = await res.json();

    sessionsList.innerHTML = sessions.map(s => `
      <div class="session-item ${s.isCurrent ? 'current' : ''}">
        <div>
          <div><strong>IP:</strong> ${escapeHtml(s.ip || 'н/д')} ${s.isCurrent ? '<span class="badge badge-completed">Текущее подключение</span>' : ''}</div>
          <div style="font-size:0.85rem; color:#6c757d;"><strong>Клиент:</strong> ${escapeHtml(s.user_agent || 'н/д')}</div>
          <div style="font-size:0.8rem; color:#6c757d;">Создана: ${new Date(s.created_at).toLocaleString()} | Истекает: ${new Date(s.expires_at).toLocaleTimeString()}</div>
        </div>
        <div>
          ${!s.isCurrent ? `<button class="btn btn-delete" onclick="revokeSession(${s.id})">Отозвать</button>` : ''}
        </div>
      </div>
    `).join('');
  } catch (err) {
    showAlert(err.message);
  }
}

async function revokeSession(id) {
  try {
    const res = await apiRequest(`/api/auth/sessions/${id}`, { method: 'DELETE' });
    if (!res.ok) throw new Error('Не удалось отозвать подключение');
    loadSessions();
  } catch (err) {
    showAlert(err.message);
  }
}

async function logoutAllOthers() {
  try {
    const res = await apiRequest('/api/auth/logout-all', { method: 'POST' });
    if (!res.ok) throw new Error('Не удалось завершить сессии');
    showAlert('Все остальные сессии успешно завершены', true);
    loadSessions();
  } catch (err) {
    showAlert(err.message);
  }
}

async function fetchTasks() {
  try {
    const status = filterStatus.value;
    const url = status === 'all' ? '/api/tasks' : `/api/tasks?status=${status}`;
    const response = await apiRequest(url);
    if (!response.ok) throw new Error('Не удалось загрузить задачи');
    const tasks = await response.json();
    renderTasks(tasks);
  } catch (err) {
    showAlert(err.message);
  }
}

function renderTasks(tasks) {
  if (tasks.length === 0) {
    tasksList.innerHTML = '<p style="text-align:center; color:#6c757d; padding:20px;">Задачи не найдены</p>';
    return;
  }

  const canEdit = currentUser && ['admin', 'editor'].includes(currentUser.role);
  const canDelete = currentUser && currentUser.role === 'admin';

  tasksList.innerHTML = tasks.map(task => {
    const formattedDate = task.due_date ? task.due_date.split('T')[0] : null;
    return `
      <div class="task-item" data-id="${task.id}">
        <div class="task-info">
          <div class="task-heading">${escapeHtml(task.title)}</div>
          ${task.description ? `<div class="task-description">${escapeHtml(task.description)}</div>` : ''}
          <div class="task-meta-bar">
            <span class="badge badge-${task.status}">${statusLabels[task.status] || task.status}</span>
            ${formattedDate ? `<span>Срок: ${formattedDate}</span>` : ''}
            ${task.author_email ? `<span>Автор: ${escapeHtml(task.author_email)}</span>` : ''}
            ${task.file_name ? `
              <a class="file-link" href="/uploads/${task.file_name}" download="${task.file_original_name}">
                Файл: ${escapeHtml(task.file_original_name)}
              </a>
            ` : ''}
          </div>
        </div>
        <div class="task-actions">
          ${canEdit ? `<button class="btn btn-edit" onclick="startEditTask(${task.id})">Ред.</button>` : ''}
          ${canDelete ? `<button class="btn btn-delete" onclick="deleteTask(${task.id})">Удалить</button>` : ''}
        </div>
      </div>
    `;
  }).join('');
}

async function startEditTask(id) {
  clearAlert();
  try {
    const response = await apiRequest(`/api/tasks/${id}`);
    if (!response.ok) {
      const data = await response.json();
      throw new Error(data.message || 'Ошибка загрузки задачи');
    }
    const task = await response.json();

    taskIdInput.value = task.id;
    titleInput.value = task.title;
    descriptionInput.value = task.description || '';
    statusSelect.value = task.status;
    dueDateInput.value = task.due_date ? task.due_date.split('T')[0] : '';

    if (task.file_original_name) {
      currentFileNotice.textContent = `Прикреплен файл: ${task.file_original_name} — выберите новый для замены`;
      currentFileNotice.classList.remove('hidden');
    } else {
      currentFileNotice.classList.add('hidden');
    }

    formHeader.textContent = `Редактирование задачи #${task.id}`;
    cancelButton.classList.remove('hidden');
    window.scrollTo({ top: 0, behavior: 'smooth' });
  } catch (err) {
    showAlert(err.message);
  }
}

async function deleteTask(id) {
  clearAlert();
  if (!confirm('Вы уверены, что хотите удалить задачу?')) return;

  try {
    const response = await apiRequest(`/api/tasks/${id}`, { method: 'DELETE' });
    if (!response.ok) {
      const data = await response.json();
      throw new Error(data.message || 'Не удалось удалить задачу');
    }

    if (taskIdInput.value === String(id)) resetForm();
    fetchTasks();
  } catch (err) {
    showAlert(err.message);
  }
}

taskForm.addEventListener('submit', async (e) => {
  e.preventDefault();
  clearAlert();

  const id = taskIdInput.value;
  const isEditing = Boolean(id);

  const formData = new FormData();
  formData.append('title', titleInput.value);
  formData.append('description', descriptionInput.value);
  formData.append('status', statusSelect.value);
  formData.append('dueDate', dueDateInput.value);

  if (taskFileInput.files.length > 0) {
    formData.append('taskFile', taskFileInput.files[0]);
  }

  const url = isEditing ? `/api/tasks/${id}` : '/api/tasks';
  const method = isEditing ? 'PUT' : 'POST';

  try {
    const response = await apiRequest(url, { method, body: formData });
    const result = await response.json();

    if (!response.ok) {
      if (result.errors) {
        showAlert(result.errors);
      } else {
        showAlert(result.message || 'Произошла ошибка при сохранении');
      }
      return;
    }

    resetForm();
    fetchTasks();
  } catch (err) {
    showAlert(err.message);
  }
});

function resetForm() {
  taskIdInput.value = '';
  titleInput.value = '';
  descriptionInput.value = '';
  statusSelect.value = 'pending';
  dueDateInput.value = '';
  taskFileInput.value = '';
  currentFileNotice.textContent = '';
  currentFileNotice.classList.add('hidden');
  formHeader.textContent = 'Новая задача';
  cancelButton.classList.add('hidden');
  clearAlert();
}

cancelButton.addEventListener('click', resetForm);
filterStatus.addEventListener('change', fetchTasks);

function escapeHtml(str) {
  if (!str) return '';
  return str
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;');
}

window.addEventListener('load', () => {
  const hash = window.location.hash;
  if (hash.includes('reset-token=')) {
    const token = hash.split('reset-token=')[1];
    switchAuthTab('reset');
    document.getElementById('resetTokenInput').value = token;
  } else {
    checkAuthSession();
  }
});