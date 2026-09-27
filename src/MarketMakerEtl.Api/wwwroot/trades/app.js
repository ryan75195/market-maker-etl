const errorBox = document.getElementById('error');
const summaryBody = document.getElementById('summary-body');
const tradesList = document.getElementById('trades-list');
const tradeForm = document.getElementById('trade-form');

const fields = {
  dealSignalId: document.getElementById('field-deal-signal-id'),
  listingId: document.getElementById('field-listing-id'),
  familyId: document.getElementById('field-family-id'),
  groupKey: document.getElementById('field-group-key'),
  boughtUtc: document.getElementById('field-bought-utc'),
  buyPrice: document.getElementById('field-buy-price'),
  buyShipping: document.getElementById('field-buy-shipping'),
  buyFees: document.getElementById('field-buy-fees'),
  notes: document.getElementById('field-notes')
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

function numberOrNull(value) {
  const trimmed = value.trim();
  return trimmed.length === 0 ? null : Number(trimmed);
}

function stat(label, value) {
  const wrapper = document.createElement('div');
  wrapper.className = 'stat';

  const labelSpan = document.createElement('span');
  labelSpan.className = 'label';
  labelSpan.textContent = label;

  const valueSpan = document.createElement('span');
  valueSpan.className = 'value';
  valueSpan.textContent = value;

  wrapper.appendChild(labelSpan);
  wrapper.appendChild(valueSpan);
  return wrapper;
}

function formatMoney(value) {
  return value === null || value === undefined ? '-' : `$${Number(value).toFixed(2)}`;
}

function formatDays(value) {
  return value === null || value === undefined ? '-' : `${Number(value).toFixed(1)}d`;
}

function formatPercent(value) {
  return value === null || value === undefined ? '-' : `${Math.round(value * 100)}%`;
}

function renderSummary(summary) {
  summaryBody.replaceChildren();
  summaryBody.appendChild(stat('Sold trades', String(summary.count)));
  summaryBody.appendChild(stat('Total realised profit', formatMoney(summary.totalRealisedProfit)));
  summaryBody.appendChild(stat('Median realised profit', formatMoney(summary.medianRealisedProfit)));
  summaryBody.appendChild(stat('Median days to sell', formatDays(summary.medianDaysToSell)));

  if (summary.signalComparison) {
    summaryBody.appendChild(stat('Signals evaluated', String(summary.signalComparison.evaluatedCount)));
    summaryBody.appendChild(stat('Mean error vs prediction', formatMoney(summary.signalComparison.meanError)));
    summaryBody.appendChild(stat('Beat prediction share', formatPercent(summary.signalComparison.beatPredictionShare)));
  }
}

async function loadSummary() {
  const summary = await api('/api/trades/summary');
  renderSummary(summary);
}

function statusClass(status) {
  return `status-${status.toLowerCase()}`;
}

function appendDetail(dl, term, value) {
  const dt = document.createElement('dt');
  dt.textContent = term;
  const dd = document.createElement('dd');
  dd.textContent = value;
  dl.appendChild(dt);
  dl.appendChild(dd);
}

function buildTradeDetails(trade) {
  const dl = document.createElement('dl');
  appendDetail(dl, 'Bought', new Date(trade.boughtUtc).toLocaleString());
  appendDetail(dl, 'Buy cost', `${formatMoney(trade.buyPrice)} + ${formatMoney(trade.buyShipping)} ship + ${formatMoney(trade.buyFees)} fees`);

  if (trade.listingTitle) {
    appendDetail(dl, 'Listing', trade.listingTitle);
  }

  if (trade.dealSignalId !== null) {
    appendDetail(dl, 'Deal signal', `#${trade.dealSignalId}`);
  }

  if (trade.predictedMargin !== null) {
    appendDetail(dl, 'Predicted margin', formatMoney(trade.predictedMargin));
  }

  if (trade.soldUtc) {
    appendDetail(dl, 'Sold', new Date(trade.soldUtc).toLocaleString());
    appendDetail(dl, 'Sell proceeds', `${formatMoney(trade.sellPrice)} - ${formatMoney(trade.sellShipping)} ship - ${formatMoney(trade.sellFees)} fees`);
    appendDetail(dl, 'Realised profit', formatMoney(trade.realisedProfit));
    appendDetail(dl, 'Days to sell', formatDays(trade.daysToSell));
  }

  if (trade.notes) {
    appendDetail(dl, 'Notes', trade.notes);
  }

  return dl;
}

function buildSaleForm(trade) {
  const form = document.createElement('form');
  form.className = 'sale-form';

  const soldUtc = document.createElement('input');
  soldUtc.type = 'datetime-local';
  soldUtc.required = true;
  soldUtc.value = new Date().toISOString().slice(0, 16);

  const sellPrice = document.createElement('input');
  sellPrice.type = 'number';
  sellPrice.step = '0.01';
  sellPrice.min = '0';
  sellPrice.placeholder = 'Sell price';
  sellPrice.required = true;

  const sellShipping = document.createElement('input');
  sellShipping.type = 'number';
  sellShipping.step = '0.01';
  sellShipping.min = '0';
  sellShipping.placeholder = 'Sell shipping';

  const sellFees = document.createElement('input');
  sellFees.type = 'number';
  sellFees.step = '0.01';
  sellFees.min = '0';
  sellFees.placeholder = 'Sell fees (blank = default)';

  const status = document.createElement('select');
  for (const option of ['Sold', 'Returned', 'WrittenOff']) {
    const opt = document.createElement('option');
    opt.value = option;
    opt.textContent = option;
    status.appendChild(opt);
  }

  const submit = document.createElement('button');
  submit.type = 'submit';
  submit.textContent = 'Record sale';

  form.appendChild(soldUtc);
  form.appendChild(sellPrice);
  form.appendChild(sellShipping);
  form.appendChild(sellFees);
  form.appendChild(status);
  form.appendChild(submit);

  form.addEventListener('submit', async (event) => {
    event.preventDefault();
    clearError();
    try {
      await api(`/api/trades/${trade.id}/sale`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          soldUtc: new Date(soldUtc.value).toISOString(),
          sellPrice: Number(sellPrice.value),
          sellShipping: numberOrNull(sellShipping.value),
          sellFees: numberOrNull(sellFees.value),
          status: status.value,
          notes: null
        })
      });
      await Promise.all([loadTrades(), loadSummary()]);
    } catch (error) {
      showError(error.message);
    }
  });

  return form;
}

function buildTradeCard(trade) {
  const card = document.createElement('div');
  card.className = 'trade-card';

  const header = document.createElement('div');
  header.className = 'trade-header';

  const title = document.createElement('span');
  title.textContent = `Trade #${trade.id}`;

  const status = document.createElement('span');
  status.className = statusClass(trade.status);
  status.textContent = trade.status;

  header.appendChild(title);
  header.appendChild(status);
  card.appendChild(header);
  card.appendChild(buildTradeDetails(trade));

  if (trade.status === 'Open') {
    card.appendChild(buildSaleForm(trade));
  }

  return card;
}

function renderTrades(trades) {
  tradesList.replaceChildren();
  if (trades.length === 0) {
    const empty = document.createElement('p');
    empty.className = 'empty';
    empty.textContent = 'No trades recorded yet.';
    tradesList.appendChild(empty);
    return;
  }

  for (const trade of trades) {
    tradesList.appendChild(buildTradeCard(trade));
  }
}

async function loadTrades() {
  const trades = await api('/api/trades');
  renderTrades(trades);
}

function prefillFromQueryParams() {
  const params = new URLSearchParams(window.location.search);
  if (params.has('dealSignalId')) {
    fields.dealSignalId.value = params.get('dealSignalId');
  }

  if (params.has('listingEntityId')) {
    fields.listingId.value = params.get('listingEntityId');
  }

  if (params.has('productFamilyId')) {
    fields.familyId.value = params.get('productFamilyId');
  }

  if (params.has('buyPrice')) {
    fields.buyPrice.value = params.get('buyPrice');
  }

  if (params.has('groupKey')) {
    fields.groupKey.value = params.get('groupKey');
  }
}

function intOrNull(value) {
  const parsed = numberOrNull(value);
  return parsed === null ? null : Math.trunc(parsed);
}

function parseGroupKey(value) {
  const trimmed = value.trim();
  if (trimmed.length === 0) {
    return null;
  }

  return JSON.parse(trimmed);
}

tradeForm.addEventListener('submit', async (event) => {
  event.preventDefault();
  clearError();
  try {
    const request = {
      dealSignalId: intOrNull(fields.dealSignalId.value),
      listingEntityId: intOrNull(fields.listingId.value),
      productFamilyId: intOrNull(fields.familyId.value),
      priceGroupKey: parseGroupKey(fields.groupKey.value),
      boughtUtc: new Date(fields.boughtUtc.value).toISOString(),
      buyPrice: Number(fields.buyPrice.value),
      buyShipping: numberOrNull(fields.buyShipping.value),
      buyFees: numberOrNull(fields.buyFees.value),
      notes: fields.notes.value.trim().length === 0 ? null : fields.notes.value.trim()
    };

    await api('/api/trades', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request)
    });

    tradeForm.reset();
    await Promise.all([loadTrades(), loadSummary()]);
  } catch (error) {
    showError(error.message);
  }
});

prefillFromQueryParams();
Promise.all([loadTrades(), loadSummary()]).catch((error) => showError(error.message));
