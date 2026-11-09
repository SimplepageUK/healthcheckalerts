import { LitElement, html, css, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api';
import { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth';

const API = '/umbraco/management/api/v1/healthcheckalerts';

const SEVERITIES = ['Warning', 'Error'];
const REPEAT_MODES = [
  { value: 'OnChange', name: 'Only when something new appears, with reminders' },
  { value: 'EveryRun', name: 'On every run that has a problem' },
];

export default class HealthCheckAlertsDashboard extends UmbElementMixin(LitElement) {
  static properties = {
    _settings: { state: true },
    _form: { state: true },
    _status: { state: true },
    _loading: { state: true },
    _saving: { state: true },
    _errors: { state: true },
    _notice: { state: true },
    _testing: { state: true },
    _testResult: { state: true },
    _snoozing: { state: true },
    _clearKey: { state: true },
  };

  #auth;

  constructor() {
    super();
    this._loading = true;
    this._errors = [];
    this._clearKey = false;
    this._snoozing = null;
    this.consumeContext(UMB_AUTH_CONTEXT, (auth) => {
      this.#auth = auth;
      this.#load();
    });
  }

  async #call(method, path, body) {
    const config = this.#auth.getOpenApiConfiguration();
    const token = await config.token();
    const response = await fetch(`${config.base}${API}${path}`, {
      method,
      credentials: config.credentials,
      headers: {
        Authorization: `Bearer ${token}`,
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    const text = await response.text();
    const data = text ? JSON.parse(text) : null;
    if (!response.ok) {
      const errors = data?.errors ?? (data?.detail ? [data.detail] : [`The request failed (${response.status}).`]);
      throw Object.assign(new Error(errors.join(' ')), { errors });
    }
    return data;
  }

  async #load() {
    this._loading = true;
    try {
      const [settings, status] = await Promise.all([this.#call('GET', '/settings'), this.#call('GET', '/status')]);
      this.#applySettings(settings);
      this._status = status;
      this._errors = [];
    } catch (error) {
      this._errors = error.errors ?? [error.message];
    }
    this._loading = false;
  }

  #applySettings(settings) {
    this._settings = settings;
    this._form = {
      recipientEmail: settings.recipientEmail,
      siteName: settings.siteName,
      minimumSeverity: settings.minimumSeverity,
      sendWhenHealthy: settings.sendWhenHealthy,
      subjectPrefix: settings.subjectPrefix,
      ignoreEnvironments: settings.ignoreEnvironments,
      showEnvironment: settings.showEnvironment,
      webhookUrl: settings.webhookUrl,
      webhookAuthHeader: settings.webhookAuthHeader,
      webhookAuthKey: '',
      repeatMode: settings.repeatMode,
      reminderEveryDays: settings.reminderEveryDays,
      sendResolved: settings.sendResolved,
    };
    this._clearKey = false;
  }

  #set(field, value) {
    this._form = { ...this._form, [field]: value };
    this._notice = null;
  }

  async #save() {
    this._saving = true;
    this._notice = null;
    try {
      const form = this._form;
      const settings = await this.#call('PUT', '/settings', {
        ...form,
        reminderEveryDays: Number.parseInt(form.reminderEveryDays, 10),
        webhookAuthKey: form.webhookAuthKey || null,
        clearWebhookAuthKey: this._clearKey,
      });
      this.#applySettings(settings);
      this._errors = [];
      this._notice = 'Saved. The next scheduled run uses these settings.';
      this._status = await this.#call('GET', '/status');
    } catch (error) {
      this._errors = error.errors ?? [error.message];
    }
    this._saving = false;
  }

  async #test() {
    this._testing = true;
    this._testResult = null;
    try {
      this._testResult = await this.#call('POST', '/test');
    } catch (error) {
      this._testResult = { delivered: false, messages: error.errors ?? [error.message] };
    }
    this._testing = false;
  }

  async #snooze() {
    const { check, until, reason } = this._snoozing;
    try {
      this.#applySettings(await this.#call('POST', '/snooze', { check, until, reason: reason || null }));
      this._status = await this.#call('GET', '/status');
      this._snoozing = null;
      this._errors = [];
    } catch (error) {
      this._errors = error.errors ?? [error.message];
    }
  }

  async #unsnooze(check) {
    try {
      this.#applySettings(await this.#call('DELETE', `/snooze?check=${encodeURIComponent(check)}`));
      this._status = await this.#call('GET', '/status');
      this._errors = [];
    } catch (error) {
      this._errors = error.errors ?? [error.message];
    }
  }

  #defaultSnoozeDate() {
    const date = new Date();
    date.setDate(date.getDate() + 14);
    return date.toISOString().slice(0, 10);
  }

  #fromConfig(field) {
    return this._settings.saved.includes(field) ? nothing : html`<small class="from-config">from appsettings.json or the defaults</small>`;
  }

  #formatTime(value) {
    return value ? new Date(value).toLocaleString() : 'Never';
  }

  render() {
    if (this._loading) {
      return html`<uui-loader></uui-loader>`;
    }
    if (!this._settings) {
      return html`<uui-box headline="Health Check Alerts">${this.#renderErrors()}</uui-box>`;
    }
    return html`
      ${this.#renderErrors()}
      ${this.#renderStatus()}
      ${this.#renderSettings()}
      ${this.#renderSnoozes()}
      ${this.#renderTest()}
    `;
  }

  #renderErrors() {
    if (!this._errors.length) {
      return nothing;
    }
    return html`<uui-box>
      <div class="error">
        <strong>That did not work</strong>
        <ul>${this._errors.map((message) => html`<li>${message}</li>`)}</ul>
      </div>
    </uui-box>`;
  }

  #renderStatus() {
    const status = this._status;
    return html`<uui-box headline="Status">
      ${status.environmentIgnored
        ? html`<p class="warning">Scheduled runs in the ${status.environmentName} environment never send an alert, because it is on the ignored environments list below. The test alert still works.</p>`
        : nothing}
      <p>Last scheduled run: <strong>${this.#formatTime(status.lastRunUtc)}</strong>.
        Last alert sent: <strong>${this.#formatTime(status.lastSentUtc)}</strong>.</p>
      ${status.lastRunUtc
        ? status.problems.length
          ? html`<p>Problems found at the last run:</p>
            ${status.problems.map((problem) => this.#renderProblem(problem))}`
          : html`<p>The last run found no warnings or errors.</p>`
        : html`<p>No scheduled run has been recorded yet.</p>`}
    </uui-box>`;
  }

  #renderProblem(problem) {
    const snoozing = this._snoozing?.check === problem.check;
    return html`<div class="problem">
      <div class="problem-head">
        <uui-tag color=${problem.severity === 'error' ? 'danger' : 'warning'} look="primary">${problem.severity}</uui-tag>
        <strong>${problem.check}</strong>
        ${problem.isSnoozed ? html`<uui-tag look="secondary">snoozed until ${problem.snoozeUntil}</uui-tag>` : nothing}
        ${problem.isReported ? nothing : html`<uui-tag look="outline">not in the last alert</uui-tag>`}
        <span class="spacer"></span>
        ${problem.isSnoozed
          ? html`<uui-button look="secondary" compact label="Unsnooze" @click=${() => this.#unsnooze(problem.check)}></uui-button>`
          : html`<uui-button look="secondary" compact label="Snooze" @click=${() => (this._snoozing = { check: problem.check, until: this.#defaultSnoozeDate(), reason: '' })}></uui-button>`}
      </div>
      <div class="message">${problem.message}</div>
      ${problem.readMoreUrl ? html`<a href=${problem.readMoreUrl} target="_blank" rel="noopener noreferrer">Read more</a>` : nothing}
      ${snoozing
        ? html`<div class="snooze-form">
            <label>Snooze until (this day is the last one muted)
              <uui-input type="date" .value=${this._snoozing.until} @input=${(e) => (this._snoozing = { ...this._snoozing, until: e.target.value })}></uui-input>
            </label>
            <label>Reason
              <uui-input placeholder="Optional, shown in the alert" .value=${this._snoozing.reason} @input=${(e) => (this._snoozing = { ...this._snoozing, reason: e.target.value })}></uui-input>
            </label>
            <uui-button look="primary" label="Snooze" @click=${() => this.#snooze()}></uui-button>
            <uui-button look="secondary" label="Cancel" @click=${() => (this._snoozing = null)}></uui-button>
          </div>`
        : nothing}
    </div>`;
  }

  #renderSettings() {
    const form = this._form;
    const text = (label, field, extra = {}) => html`<label class="field">${label}
      <uui-input .value=${form[field] ?? ''} @input=${(e) => this.#set(field, e.target.value)} placeholder=${extra.placeholder ?? ''}></uui-input>
      ${this.#fromConfig(this.#savedName(field))}
    </label>`;
    const toggle = (label, field, hint) => html`<div class="field">
      <uui-toggle label=${label} ?checked=${form[field]} @change=${(e) => this.#set(field, e.target.checked)}></uui-toggle>
      ${hint ? html`<small>${hint}</small>` : nothing}
      ${this.#fromConfig(this.#savedName(field))}
    </div>`;

    return html`
      <uui-box headline="Email">
        <label class="field">Recipients
          <uui-textarea .value=${form.recipientEmail} @input=${(e) => this.#set('recipientEmail', e.target.value)} placeholder="support@example.com, ops@example.com"></uui-textarea>
          <small>One or more addresses, separated by commas, semicolons, spaces or new lines. Leave empty to use only the webhook.</small>
          ${this.#fromConfig('RecipientEmail')}
        </label>
        ${text('Site name', 'siteName', { placeholder: 'Named in every subject line' })}
        <label class="field">Minimum severity
          <uui-select .options=${SEVERITIES.map((value) => ({ name: value, value, selected: form.minimumSeverity === value }))}
            @change=${(e) => this.#set('minimumSeverity', e.target.value)}></uui-select>
          <small>Error leaves warnings out of alerts.</small>
          ${this.#fromConfig('MinimumSeverity')}
        </label>
        ${text('Subject prefix', 'subjectPrefix')}
        ${text('Ignored environments', 'ignoreEnvironments')}
        <small>Scheduled runs in these environments (comma separated) never send, by email or webhook. Leave empty to send from everywhere.</small>
        ${toggle('Show the environment in the subject', 'showEnvironment')}
        ${toggle('Also email when every check passes', 'sendWhenHealthy')}
      </uui-box>

      <uui-box headline="Webhook">
        <p>Posts a JSON summary to a URL after each alert. https only (http is allowed for localhost). Leave the URL empty to turn it off.</p>
        ${text('URL', 'webhookUrl', { placeholder: 'https://example.com/hooks/health' })}
        ${text('Header name for the key', 'webhookAuthHeader')}
        <label class="field">Key
          <uui-input type="password" autocomplete="off" .value=${form.webhookAuthKey}
            placeholder=${this._settings.webhookAuthKeySet && !this._clearKey ? 'A key is set. Leave blank to keep it.' : 'Optional'}
            @input=${(e) => this.#set('webhookAuthKey', e.target.value)}></uui-input>
          <small>Sent as the value of the header above. It is stored on the server and is never shown again.</small>
        </label>
        ${this._settings.webhookAuthKeySet
          ? html`<uui-checkbox label="Remove the saved key" ?checked=${this._clearKey} @change=${(e) => (this._clearKey = e.target.checked)}></uui-checkbox>`
          : nothing}
      </uui-box>

      <uui-box headline="Repeats and reminders">
        <label class="field">Send an alert
          <uui-select .options=${REPEAT_MODES.map((mode) => ({ name: mode.name, value: mode.value, selected: form.repeatMode === mode.value }))}
            @change=${(e) => this.#set('repeatMode', e.target.value)}></uui-select>
          ${this.#fromConfig('RepeatMode')}
        </label>
        <label class="field">Remind about unchanged problems after (days)
          <uui-input type="number" min="0" max="365" .value=${String(form.reminderEveryDays)} @input=${(e) => this.#set('reminderEveryDays', e.target.value)}></uui-input>
          <small>0 means never remind. Only applies to the first option above.</small>
          ${this.#fromConfig('ReminderEveryDays')}
        </label>
        ${toggle('Send an alert when everything has cleared', 'sendResolved')}
      </uui-box>

      <div class="actions">
        <uui-button look="primary" color="positive" label="Save settings" ?disabled=${this._saving} @click=${() => this.#save()}></uui-button>
        ${this._notice ? html`<span class="notice">${this._notice}</span>` : nothing}
      </div>`;
  }

  #savedName(field) {
    return field.charAt(0).toUpperCase() + field.slice(1);
  }

  #renderSnoozes() {
    const snoozes = this._settings.snoozes;
    return html`<uui-box headline="Snoozes">
      <p>A snoozed check is left out of alerts until the date, then comes back by itself. Snooze a problem from the status list above.</p>
      ${snoozes.length
        ? snoozes.map((snooze) => html`<div class="problem-head">
            <strong>${snooze.check}</strong> until ${snooze.until}${snooze.reason ? html` (${snooze.reason})` : nothing}
            <span class="spacer"></span>
            <uui-button look="secondary" compact label="Remove" @click=${() => this.#unsnooze(snooze.check)}></uui-button>
          </div>`)
        : html`<p>Nothing is snoozed.</p>`}
    </uui-box>`;
  }

  #renderTest() {
    const result = this._testResult;
    return html`<uui-box headline="Send a test alert">
      <p>Sends a sample alert with made-up problems, to the saved recipients and webhook. Save any changes first.</p>
      <uui-button look="secondary" label="Send test alert" ?disabled=${this._testing} @click=${() => this.#test()}></uui-button>
      ${result
        ? html`<ul class=${result.delivered ? 'ok' : 'error'}>${result.messages.map((message) => html`<li>${message}</li>`)}</ul>`
        : nothing}
    </uui-box>`;
  }

  static styles = css`
    :host { display: block; padding: var(--uui-size-layout-1); }
    uui-box { margin-bottom: var(--uui-size-layout-1); }
    .field { display: block; margin-bottom: var(--uui-size-space-5); }
    .field uui-input, .field uui-textarea, .field uui-select { display: block; width: 100%; margin-top: var(--uui-size-space-2); }
    small { display: block; color: var(--uui-color-text-alt); margin-top: var(--uui-size-space-1); }
    .from-config { font-style: italic; }
    .problem { border-top: 1px solid var(--uui-color-border); padding: var(--uui-size-space-3) 0; }
    .problem-head { display: flex; align-items: center; gap: var(--uui-size-space-3); flex-wrap: wrap; padding: var(--uui-size-space-2) 0; }
    .spacer { flex: 1; }
    .message { color: var(--uui-color-text-alt); }
    .snooze-form { display: flex; gap: var(--uui-size-space-4); align-items: flex-end; flex-wrap: wrap; margin-top: var(--uui-size-space-3); }
    .actions { display: flex; align-items: center; gap: var(--uui-size-space-4); margin-bottom: var(--uui-size-layout-1); }
    .notice, .ok { color: var(--uui-color-positive-standalone); }
    .error, ul.error { color: var(--uui-color-danger-standalone); }
    .warning { color: var(--uui-color-warning-standalone); }
  `;
}

customElements.define('simplepage-health-check-alerts-dashboard', HealthCheckAlertsDashboard);
