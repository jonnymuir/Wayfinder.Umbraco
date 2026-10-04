import type { Locator, Page } from '@playwright/test';
import { humanClick, humanType } from 'wayfinder-demo-recording-kit';
import { licenceCertificatePath, proofOfIdentityPath } from './demo-config.js';

// The agent's stage order, labels and field keys aren't fixed ahead of time, so the walk is
// generic: on each stage fill whatever inputs it finds with contextually plausible values (chosen
// from the field's own label, so the recording never shows "JL-123456" in every box), then submit.

async function labelHintFor(page: Page, field: Locator): Promise<string> {
  const id = await field.getAttribute('id').catch(() => null);
  const name = (await field.getAttribute('name').catch(() => null)) ?? '';
  let label = '';
  if (id) label = await page.locator(`label[for="${id}"]`).first().innerText().catch(() => '');
  if (!label) {
    label = await field
      .evaluate(el => {
        const grp = el.closest('.govuk-form-group, .govuk-fieldset, fieldset');
        return grp?.querySelector('label, legend')?.textContent?.trim() ?? '';
      })
      .catch(() => '');
  }
  return `${label} ${name}`.toLowerCase();
}

type ValueRule = { matches: (hint: string, type: string) => boolean; value: string | ((hint: string) => string) };

/** First matching rule wins, so order matters. */
const VALUE_RULES: ValueRule[] = [
  { matches: (hint, type) => type === 'email' || /e-?mail/.test(hint), value: 'alex.applicant@example.com' },
  { matches: (hint, type) => type === 'tel' || /phone|telephone|mobile/.test(hint), value: '07700 900123' },
  { matches: (_hint, type) => type === 'number', value: '1' },
  { matches: hint => /full name|your name|applicant'?s? name|name of applicant/.test(hint), value: 'Alex Applicant' },
  { matches: hint => /first name|forename|given name/.test(hint), value: 'Alex' },
  { matches: hint => /last name|surname|family name/.test(hint), value: 'Applicant' },
  // Registration/licence number BEFORE the "licensing body" name check: "your registration
  // number with your current licensing body" contains "licensing body" but wants a number.
  {
    matches: hint => /registration number|licence number|license number|membership number|licence reference|reference number/.test(hint),
    value: hint => (/\bnew\b|issued by us|national juggling|record the|record this|on our register/.test(hint) ? 'NJA-2026-0417' : 'EJF-2021-44718')
  },
  {
    matches: hint =>
      /(name of|which) .{0,30}(licensing body|licensing authority|issuing authority|awarding body|federation|guild)|issuing authority|current licensing body/.test(hint),
    value: 'European Juggling Federation'
  },
  {
    matches: hint => /reason|why|explain|\bnote\b|\bdetails?\b|describe|comments?|more information|what.*(provide|need)|evidence requested|caseworker note/.test(hint),
    value: 'The certificate matches the register and the licence is current, so this can proceed.'
  },
  { matches: hint => /registration/.test(hint), value: 'NJA-2026-0417' },
  { matches: hint => /postcode|post code/.test(hint), value: 'SW1A 1AA' },
  { matches: hint => /address|street/.test(hint), value: '10 Rehearsal Lane, London' }
];

function plausibleValue(hint: string, type: string): string {
  const rule = VALUE_RULES.find(candidate => candidate.matches(hint, type));
  if (!rule) return 'Alex Applicant';
  return typeof rule.value === 'function' ? rule.value(hint) : rule.value;
}

const eachIndex = async (locator: Locator, visit: (item: Locator) => Promise<void>) => {
  for (let i = 0, n = await locator.count(); i < n; i++) await visit(locator.nth(i));
};

/** Files: the licence certificate vs proof of identity, decided by each input's own label, and two genuinely different files so it doesn't read as one upload used twice. */
async function attachFiles(page: Page, scope: Locator): Promise<void> {
  await eachIndex(scope.locator('input[type="file"]'), async input => {
    const hint = await labelHintFor(page, input);
    await input.setInputFiles(/identit|passport|driving licence|photo id|proof of who/.test(hint) ? proofOfIdentityPath : licenceCertificatePath);
  });
}

/** Checkboxes: tick anything not already ticked (eligibility, the declaration). */
async function tickCheckboxes(page: Page, scope: Locator): Promise<void> {
  await eachIndex(scope.locator('input[type="checkbox"]'), async box => {
    if (!(await box.isChecked())) await humanClick(page, box);
  });
}

/** Radios: one per group, avoiding a negative/opt-out option so the happy path stays eligible. */
async function chooseRadios(page: Page, scope: Locator): Promise<void> {
  const radios = scope.locator('input[type="radio"]');
  const names = new Set<string>();
  await eachIndex(radios, async radio => {
    const name = await radio.getAttribute('name');
    if (name) names.add(name);
  });

  for (const name of names) {
    const group = scope.locator(`input[type="radio"][name="${name}"]`);
    if ((await group.locator(':checked').count()) > 0) continue;
    let chosen = group.first();
    for (let i = 0, n = await group.count(); i < n; i++) {
      const option = group.nth(i);
      const id = await option.getAttribute('id');
      const labelText = id ? await page.locator(`label[for="${id}"]`).innerText().catch(() => '') : '';
      if (!/none of these|none of the above|not sure|don'?t know|not listed/i.test(labelText)) {
        chosen = option;
        break;
      }
    }
    await humanClick(page, chosen);
  }
}

/** <select> dropdowns (e.g. "Which identity document?"): the first real option. */
async function chooseSelectOptions(scope: Locator): Promise<void> {
  await eachIndex(scope.locator('select'), async select => {
    if (await select.inputValue().catch(() => '')) return;
    const values: string[] = await select.locator('option').evaluateAll(options => options.map(o => (o as HTMLOptionElement).value).filter(v => v !== ''));
    if (values.length) await select.selectOption(values[0]);
  });
}

/** GOV.UK day/month/year triple inputs: a future date. A past "licence expiry" reads as an expired licence and blocks the happy path, and no date in this domain needs to be past. */
async function fillDateTriples(page: Page, scope: Locator): Promise<void> {
  const parts: Array<[string, string]> = [
    ['-day', '1'],
    ['-month', '6'],
    ['-year', '2030']
  ];
  for (const [suffix, value] of parts) {
    await eachIndex(scope.locator(`input[name$="${suffix}"]`), async field => {
      if ((await field.inputValue().catch(() => '')) === '') await humanType(page, field, value);
    });
  }
}

/** Text / email / tel / number / textarea: a plausible value picked from the field's label. */
async function fillTextInputs(page: Page, scope: Locator): Promise<void> {
  const selectors = ['input[type="text"]', 'input:not([type])', 'textarea', 'input[type="email"]', 'input[type="tel"]', 'input[type="number"]'];
  for (const selector of selectors) {
    await eachIndex(scope.locator(selector), async field => {
      if ((await field.inputValue().catch(() => '')) !== '') return;
      const type = (await field.getAttribute('type')) ?? 'text';
      await humanType(page, field, plausibleValue(await labelHintFor(page, field), type));
    });
  }
}

/** Native <input type="date">: .fill() the ISO value (keystroke typing misparses it). */
async function fillNativeDates(page: Page, scope: Locator): Promise<void> {
  await eachIndex(scope.locator('input[type="date"]'), async field => {
    if ((await field.inputValue().catch(() => '')) === '') {
      await humanClick(page, field);
      await field.fill('2030-06-01');
    }
  });
}

/** Fills every empty input inside `scope` with something plausible, whatever the stage asks for. */
export async function fillVisibleFields(page: Page, scope: Locator): Promise<void> {
  await attachFiles(page, scope);
  await tickCheckboxes(page, scope);
  await chooseRadios(page, scope);
  await chooseSelectOptions(scope);
  await fillDateTriples(page, scope);
  await fillTextInputs(page, scope);
  await fillNativeDates(page, scope);
}
