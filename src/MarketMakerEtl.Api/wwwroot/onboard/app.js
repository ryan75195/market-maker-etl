const familySelect = document.getElementById('family-select');
const errorBox = document.getElementById('error');
const startForm = document.getElementById('onboard-form');
const startButton = document.getElementById('start-button');
const startName = document.getElementById('start-name');
const startSearchTerm = document.getElementById('start-search-term');
const startKey = document.getElementById('start-key');
const draftSection = document.getElementById('draft');
const draftHeading = document.getElementById('draft-heading');
const sampleSizeBox = document.getElementById('sample-size');
const tokenUsageBox = document.getElementById('token-usage');
const costUsdBox = document.getElementById('cost-usd');
const taxonomyJsonBox = document.getElementById('taxonomy-json');
const saveTaxonomyButton = document.getElementById('save-taxonomy-button');
const dealGroupByBox = document.getElementById('deal-group-by');
const dealMinDiscountBox = document.getElementById('deal-min-discount');
const dealMinSoldBox = document.getElementById('deal-min-sold');
const saveDealSettingsButton = document.getElementById('save-deal-settings-button');
const previewBox = document.getElementById('preview');
const feedbackBox = document.getElementById('feedback');
const regenerateButton = document.getElementById('regenerate-button');
const approveButton = document.getElementById('approve-button');
const rejectButton = document.getElementById('reject-button');

const state = {
  familyId: null
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

async function loadDraftFamilies() {
  const families = await api('/api/families');
  const drafts = families.filter((family) => family.state === 'Draft');

  familySelect.innerHTML = '<option value="">Start a new family below</option>';
  for (const family of drafts) {
    const option = document.createElement('option');
    option.value = family.id;
    option.textContent = family.name;
    familySelect.appendChild(option);
  }

  familySelect.value = state.familyId ? String(state.familyId) : '';
}

function renderChoice(choice) {
  const wrapper = document.createElement('div');
  wrapper.className = 'choice';

  const heading = document.createElement('div');
  heading.className = 'choice-heading';
  heading.textContent = `${choice.choice} (${choice.count})`;
  wrapper.appendChild(heading);

  const examples = document.createElement('ul');
  for (const example of choice.examples) {
    const li = document.createElement('li');
    li.textContent = example.title ?? example.listingId;
    examples.appendChild(li);
  }

  wrapper.appendChild(examples);
  return wrapper;
}

function renderPreview(onboarding) {
  previewBox.innerHTML = '';
  if (!onboarding.preview || onboarding.preview.length === 0) {
    previewBox.textContent = 'No preview yet.';
    return;
  }

  for (const question of onboarding.preview) {
    const section = document.createElement('div');
    section.className = 'preview-question';

    const heading = document.createElement('h4');
    heading.textContent = question.question;
    section.appendChild(heading);

    for (const choice of question.choices) {
      section.appendChild(renderChoice(choice));
    }

    previewBox.appendChild(section);
  }
}

function renderOnboarding(onboarding) {
  state.familyId = onboarding.familyId;
  draftSection.hidden = false;
  draftHeading.textContent = `${onboarding.name} (${onboarding.key})`;
  sampleSizeBox.textContent = `Sample: ${onboarding.sampleSize} listings`;
  tokenUsageBox.textContent = `Tokens: ${onboarding.promptTokens} in / ${onboarding.completionTokens} out`;
  costUsdBox.textContent = `Cost: $${onboarding.costUsd.toFixed(4)}`;
  taxonomyJsonBox.value = formatTaxonomyJson(onboarding.taxonomyJson);
  dealGroupByBox.value = onboarding.dealGroupBy ?? '';
  dealMinDiscountBox.value = onboarding.dealMinDiscount;
  dealMinSoldBox.value = onboarding.dealMinSold;
  feedbackBox.value = onboarding.lastFeedback ?? '';
  renderPreview(onboarding);
}

function formatTaxonomyJson(taxonomyJson) {
  try {
    return JSON.stringify(JSON.parse(taxonomyJson), null, 1);
  } catch {
    return taxonomyJson;
  }
}

async function loadOnboarding(familyId) {
  clearError();
  try {
    const onboarding = await api(`/api/families/${familyId}/onboarding`);
    renderOnboarding(onboarding);
  } catch (error) {
    showError(error.message);
  }
}

async function startOnboarding(event) {
  event.preventDefault();
  clearError();
  startButton.disabled = true;
  try {
    const onboarding = await api('/api/families/onboard', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        name: startName.value.trim(),
        searchTerm: startSearchTerm.value.trim(),
        key: startKey.value.trim() || null
      })
    });
    startForm.reset();
    await loadDraftFamilies();
    renderOnboarding(onboarding);
  } catch (error) {
    showError(error.message);
  } finally {
    startButton.disabled = false;
  }
}

async function saveTaxonomy() {
  clearError();
  try {
    const onboarding = await api(`/api/families/${state.familyId}/onboarding/taxonomy`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: taxonomyJsonBox.value
    });
    renderOnboarding(onboarding);
  } catch (error) {
    showError(error.message);
  }
}

async function saveDealSettings() {
  clearError();
  try {
    const family = await api(`/api/families/${state.familyId}/onboarding/deal-settings`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        dealGroupBy: dealGroupByBox.value.trim() || null,
        dealMinDiscount: dealMinDiscountBox.value ? Number(dealMinDiscountBox.value) : null,
        dealMinSold: dealMinSoldBox.value ? Number(dealMinSoldBox.value) : null
      })
    });
    dealGroupByBox.value = family.dealGroupBy ?? '';
    dealMinDiscountBox.value = family.dealMinDiscount;
    dealMinSoldBox.value = family.dealMinSold;
  } catch (error) {
    showError(error.message);
  }
}

async function regenerate() {
  clearError();
  try {
    const onboarding = await api(`/api/families/${state.familyId}/onboarding/regenerate`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ feedback: feedbackBox.value.trim() })
    });
    renderOnboarding(onboarding);
  } catch (error) {
    showError(error.message);
  }
}

async function approve() {
  clearError();
  if (!window.confirm('Approve this family and make it active?')) {
    return;
  }

  try {
    await api(`/api/families/${state.familyId}/onboarding/approve`, { method: 'POST' });
    state.familyId = null;
    draftSection.hidden = true;
    await loadDraftFamilies();
  } catch (error) {
    showError(error.message);
  }
}

async function reject() {
  clearError();
  if (!window.confirm('Reject and delete this draft family?')) {
    return;
  }

  try {
    await api(`/api/families/${state.familyId}/onboarding/reject`, { method: 'POST' });
    state.familyId = null;
    draftSection.hidden = true;
    await loadDraftFamilies();
  } catch (error) {
    showError(error.message);
  }
}

familySelect.addEventListener('change', () => {
  if (!familySelect.value) {
    state.familyId = null;
    draftSection.hidden = true;
    return;
  }

  loadOnboarding(Number(familySelect.value));
});

startForm.addEventListener('submit', startOnboarding);
saveTaxonomyButton.addEventListener('click', saveTaxonomy);
saveDealSettingsButton.addEventListener('click', saveDealSettings);
regenerateButton.addEventListener('click', regenerate);
approveButton.addEventListener('click', approve);
rejectButton.addEventListener('click', reject);

loadDraftFamilies().catch((error) => showError(error.message));
