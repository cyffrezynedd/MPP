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
const errorNotification = document.getElementById('errorNotification');

const statusLabels = {
  pending: 'Ожидает',
  in_progress: 'В работе',
  completed: 'Завершено'
};

function showError(messages) {
  const list = Array.isArray(messages) ? messages : [messages];
  errorNotification.innerHTML = list.map(m => `<div>${m}</div>`).join('');
  errorNotification.classList.remove('hidden');
  window.scrollTo({ top: 0, behavior: 'smooth' });
}

function clearError() {
  errorNotification.innerHTML = '';
  errorNotification.classList.add('hidden');
}

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
  clearError();
}

async function fetchTasks() {
  try {
    const status = filterStatus.value;
    const url = status === 'all' ? '/api/tasks' : `/api/tasks?status=${status}`;
    const response = await fetch(url);
    if (!response.ok) {
      throw new Error('Не удалось получить список задач');
    }
    const tasks = await response.json();
    renderTasks(tasks);
  } catch (err) {
    showError(err.message);
  }
}

function renderTasks(tasks) {
  if (tasks.length === 0) {
    tasksList.innerHTML = '<p style="text-align:center; color:#6c757d; padding:20px;">Задачи не найдены</p>';
    return;
  }

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
            ${task.file_name ? `
              <a class="file-link" href="/uploads/${task.file_name}" download="${task.file_original_name}">
                <svg class="icon" viewBox="0 0 24 24">
                  <path d="M21.44 11.05l-9.19 9.19a6 6 0 0 1-8.49-8.49l9.19-9.19a4 4 0 0 1 5.66 5.66l-9.2 9.19a2 2 0 0 1-2.83-2.83l8.49-8.48"></path>
                </svg>
                ${escapeHtml(task.file_original_name)}
              </a>
            ` : ''}
          </div>
        </div>
        <div class="task-actions">
          <button class="btn btn-edit" onclick="startEditTask(${task.id})">
            <svg class="icon" viewBox="0 0 24 24">
              <path d="M12 20h9"></path>
              <path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z"></path>
            </svg>
            Ред.
          </button>
          <button class="btn btn-delete" onclick="deleteTask(${task.id})">
            <svg class="icon" viewBox="0 0 24 24">
              <polyline points="3 6 5 6 21 6"></polyline>
              <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
            </svg>
            Удалить
          </button>
        </div>
      </div>
    `;
  }).join('');
}

async function startEditTask(id) {
  clearError();
  try {
    const response = await fetch(`/api/tasks/${id}`);
    if (!response.ok) {
      const data = await response.json();
      throw new Error(data.error || 'Ошибка загрузки задачи');
    }
    const task = await response.json();

    taskIdInput.value = task.id;
    titleInput.value = task.title;
    descriptionInput.value = task.description || '';
    statusSelect.value = task.status;
    dueDateInput.value = task.due_date ? task.due_date.split('T')[0] : '';

    if (task.file_original_name) {
      currentFileNotice.textContent = `Прикреплен файл: ${task.file_original_name} (выберите новый, чтобы заменить)`;
      currentFileNotice.classList.remove('hidden');
    } else {
      currentFileNotice.classList.add('hidden');
    }

    formHeader.textContent = `Редактирование задачи #${task.id}`;
    cancelButton.classList.remove('hidden');
    window.scrollTo({ top: 0, behavior: 'smooth' });
  } catch (err) {
    showError(err.message);
  }
}

async function deleteTask(id) {
  clearError();
  if (!confirm('Вы уверены, что хотите удалить задачу?')) return;

  try {
    const response = await fetch(`/api/tasks/${id}`, {
      method: 'DELETE'
    });

    if (!response.ok) {
      const data = await response.json();
      throw new Error(data.error || 'Не удалось удалить задачу');
    }

    if (taskIdInput.value === String(id)) {
      resetForm();
    }
    fetchTasks();
  } catch (err) {
    showError(err.message);
  }
}

taskForm.addEventListener('submit', async (e) => {
  e.preventDefault();
  clearError();

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
    const response = await fetch(url, {
      method,
      body: formData
    });

    const result = await response.json();

    if (!response.ok) {
      if (result.errors) {
        showError(result.errors);
      } else {
        showError(result.error || 'Произошла ошибка при сохранении');
      }
      return;
    }

    resetForm();
    fetchTasks();
  } catch (err) {
    showError(err.message);
  }
});

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

fetchTasks();