const familySelect = document.getElementById('family-select');
const questionFilter = document.getElementById('question-filter');
const remainingCount = document.getElementById('remaining-count');
const errorBox = document.getElementById('error');
const emptyBox = document.getElementById('empty');
const itemSection = document.getElementById('item');
const imagesBox = document.getElementById('images');
const titleLink = document.getElementById('title-link');
const priceBox = document.getElementById('price');
const soldBadge = document.getElementById('sold-badge');
const conditionBox = document.getElementById('condition');
const categoryPathBox = document.getElementById('category-path');
const descriptionBox = document.getElementById('description');
const instructionsBox = document.getElementById('instructions');
const optionsList = document.getElementById('options');

const state = {
  familyId: null,
  questions: null,
  queue: [],
  index: 0,
  remaining: 0,
  lastAction: null
};

async function api(path, options) {
  const response = await fetch(path, options);
  if (!response.ok) {
    const body = await response.text().catch(() => '');
    throw new Error(`${response.status} ${response.statusText}${body ? `: ${body}` : ''}`);
  }

  return response.status === 204 ? null : response.json();
}

function showError(message) {
  errorBox.textContent = message;
  errorBox.hidden = false;
}

function clearError() {
  errorBox.hidden = true;
  errorBox.textContent = '';
}

function questionsQuery() {
  return state.questions && state.questions.length > 0 ? state.questions.join(',') : '';
}

function remainingForQuestions(summary) {
  if (!state.questions) {
    return summary.total;
  }

  const wanted = new Set(state.questions);
  return summary.questions
    .filter((entry) => wanted.has(entry.question))
    .reduce((sum, entry) => sum + entry.count, 0);
}

async function loadFamilies() {
  const families = await api('/api/families');
  familySelect.innerHTML = '';
  for (const family of families) {
    const option = document.createElement('option');
    option.value = family.id;
    option.textContent = family.name;
    familySelect.appendChild(option);
  }

  if (families.length > 0) {
    state.familyId = families[0].id;
    familySelect.value = String(state.familyId);
  }
}

async function loadQueue() {
  if (state.familyId === null) {
    return;
  }

  clearError();
  const query = questionsQuery();
  const summaryUrl = `/api/families/${state.familyId}/review/summary`;
  const queueUrl = `/api/families/${state.familyId}/review${query ? `?question=${encodeURIComponent(query)}` : ''}`;

  try {
    const [summary, queue] = await Promise.all([api(summaryUrl), api(queueUrl)]);
    state.remaining = remainingForQuestions(summary);
    state.queue = queue;
    state.index = 0;
    render();
  } catch (error) {
    showError(error.message);
  }
}

function currentItem() {
  return state.queue[state.index] ?? null;
}

function renderImages(item) {
  imagesBox.innerHTML = '';
  const urls = item.imageUrls && item.imageUrls.length > 0
    ? item.imageUrls
    : (item.primaryImageUrl ? [item.primaryImageUrl] : []);

  for (const url of urls) {
    const img = document.createElement('img');
    img.src = url;
    img.alt = item.title ?? 'listing photo';
    imagesBox.appendChild(img);
  }
}

function renderOptions(item) {
  optionsList.innerHTML = '';
  const descriptionByKey = new Map(item.options.map((option) => [option.key, option.description]));
  const ordered = item.probabilities.length > 0
    ? item.probabilities
    : item.options.map((option) => ({ choice: option.key, probability: null }));

  ordered.forEach((entry, index) => {
    const li = document.createElement('li');
    if (entry.choice === item.choice) {
      li.classList.add('model-choice');
    }

    const keySpan = document.createElement('span');
    keySpan.className = 'option-key';
    keySpan.textContent = index < 9 ? `${index + 1}` : '';

    const descriptionSpan = document.createElement('span');
    descriptionSpan.textContent = descriptionByKey.get(entry.choice) ?? entry.choice;

    li.appendChild(keySpan);
    li.appendChild(descriptionSpan);

    if (entry.probability !== null) {
      const probabilitySpan = document.createElement('span');
      probabilitySpan.className = 'option-probability';
      probabilitySpan.textContent = `${Math.round(entry.probability * 100)}%`;
      li.appendChild(probabilitySpan);
    }

    optionsList.appendChild(li);
  });
}

function render() {
  remainingCount.textContent = `Remaining: ${state.remaining}`;
  const item = currentItem();

  if (!item) {
    itemSection.hidden = true;
    emptyBox.hidden = false;
    return;
  }

  emptyBox.hidden = true;
  itemSection.hidden = false;

  renderImages(item);
  titleLink.textContent = item.title ?? '(untitled)';
  titleLink.href = item.url ?? '#';
  priceBox.textContent = item.price !== null ? `$${item.price}` : '';
  soldBadge.hidden = !item.isSold;
  conditionBox.textContent = item.condition ?? '';
  categoryPathBox.textContent = item.categoryPath ?? '';
  descriptionBox.textContent = item.description ?? '';
  instructionsBox.textContent = item.instructions;
  renderOptions(item);
}

function advance() {
  state.queue.splice(state.index, 1);
  if (state.index >= state.queue.length) {
    state.index = 0;
  }

  render();

  if (state.queue.length === 0) {
    loadQueue();
  }
}

async function setChoice(choice) {
  const item = currentItem();
  if (!item) {
    return;
  }

  clearError();
  try {
    await api(`/api/listings/${item.listingId}/classification/${item.question}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ choice })
    });
    state.lastAction = { listingId: item.listingId, question: item.question, previousChoice: item.choice };
    state.remaining = Math.max(0, state.remaining - 1);
    advance();
  } catch (error) {
    showError(error.message);
  }
}

async function confirmChoice() {
  const item = currentItem();
  if (!item) {
    return;
  }

  clearError();
  try {
    await api(`/api/listings/${item.listingId}/classification/${item.question}/confirm`, { method: 'POST' });
    state.lastAction = { listingId: item.listingId, question: item.question, previousChoice: item.choice };
    state.remaining = Math.max(0, state.remaining - 1);
    advance();
  } catch (error) {
    showError(error.message);
  }
}

function skip() {
  clearError();
  state.index = state.queue.length > 0 ? (state.index + 1) % state.queue.length : 0;
  render();
}

async function undo() {
  if (!state.lastAction) {
    showError('Nothing to undo.');
    return;
  }

  clearError();
  const { listingId, question, previousChoice } = state.lastAction;
  try {
    await api(`/api/listings/${listingId}/classification/${question}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ choice: previousChoice })
    });
    state.lastAction = null;
  } catch (error) {
    showError(error.message);
  }
}

function isTypingTarget(target) {
  return target.tagName === 'INPUT' || target.tagName === 'SELECT' || target.tagName === 'TEXTAREA';
}

function onKeyDown(event) {
  if (isTypingTarget(event.target)) {
    return;
  }

  const item = currentItem();
  if (event.key >= '1' && event.key <= '9') {
    if (!item) {
      return;
    }

    const ordered = item.probabilities.length > 0 ? item.probabilities : item.options.map((o) => ({ choice: o.key }));
    const chosen = ordered[Number(event.key) - 1];
    if (chosen) {
      setChoice(chosen.choice);
    }

    return;
  }

  if (event.key === 'Enter') {
    confirmChoice();
    return;
  }

  if (event.key === 's' || event.key === 'S') {
    skip();
    return;
  }

  if (event.key === 'u' || event.key === 'U') {
    undo();
  }
}

familySelect.addEventListener('change', () => {
  state.familyId = Number(familySelect.value);
  loadQueue();
});

questionFilter.addEventListener('change', () => {
  const raw = questionFilter.value.trim();
  state.questions = raw.length > 0 ? raw.split(',').map((q) => q.trim()).filter((q) => q.length > 0) : null;
  loadQueue();
});

document.addEventListener('keydown', onKeyDown);

loadFamilies()
  .then(loadQueue)
  .catch((error) => showError(error.message));
