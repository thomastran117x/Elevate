// Run with: npm run test:scripts
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
  advisoryId,
  collectAdvisories,
  evaluateAudit,
  validateAllowlist,
} from './audit-dependencies.mjs';

const BRACES = 'GHSA-vfj7-8cjw-p6xm';

/** A report shaped like `npm audit --json`: one advisory in braces, inherited by its dependents. */
function report(...extraAdvisories) {
  const vulnerabilities = {
    braces: {
      severity: 'high',
      via: [
        {
          name: 'braces',
          severity: 'high',
          title: 'braces vulnerable to stack-exhaustion denial of service',
          url: `https://github.com/advisories/${BRACES}`,
        },
      ],
    },
    chokidar: { severity: 'high', via: ['braces'] },
    karma: { severity: 'high', via: ['braces', 'chokidar'] },
  };
  for (const advisory of extraAdvisories) {
    vulnerabilities[advisory.name] = { severity: advisory.severity, via: [advisory] };
  }
  return { vulnerabilities };
}

const allowBraces = [
  { id: BRACES, package: 'braces', reason: 'dev-only', tracking: 'https://example.test/issues/1' },
];

describe('advisoryId', () => {
  it('takes the GHSA id from an advisory URL', () => {
    assert.equal(advisoryId(`https://github.com/advisories/${BRACES}`), BRACES);
  });

  it('falls back to the whole URL when it carries no GHSA id', () => {
    assert.equal(advisoryId('https://npmjs.com/advisories/1'), 'https://npmjs.com/advisories/1');
    assert.equal(advisoryId(undefined), '');
  });
});

describe('collectAdvisories', () => {
  it('lists each advisory once, ignoring packages that only inherit it', () => {
    const advisories = collectAdvisories(report());

    assert.deepEqual(
      advisories.map((advisory) => [advisory.id, advisory.package, advisory.severity]),
      [[BRACES, 'braces', 'high']],
    );
  });

  it('copes with an empty report', () => {
    assert.deepEqual(collectAdvisories({}), []);
  });
});

describe('evaluateAudit', () => {
  it('fails on an advisory that is not allowlisted', () => {
    const { failing, allowed } = evaluateAudit(report(), []);

    assert.equal(failing.length, 1);
    assert.equal(allowed.length, 0);
  });

  it('excuses an allowlisted advisory in its own package', () => {
    const { failing, allowed, stale } = evaluateAudit(report(), allowBraces);

    assert.deepEqual(failing, []);
    assert.equal(allowed[0].id, BRACES);
    assert.deepEqual(stale, []);
  });

  it('does not excuse the same advisory id in a different package', () => {
    const { failing } = evaluateAudit(report(), [{ ...allowBraces[0], package: 'micromatch' }]);

    assert.equal(failing.length, 1);
  });

  it('still fails on any other advisory', () => {
    const lodash = {
      name: 'lodash',
      severity: 'moderate',
      title: 'Prototype pollution',
      url: 'https://github.com/advisories/GHSA-aaaa-bbbb-cccc',
    };

    const { failing } = evaluateAudit(report(lodash), allowBraces);

    assert.deepEqual(
      failing.map((advisory) => advisory.package),
      ['lodash'],
    );
  });

  it('ignores advisories below the audit level', () => {
    const lowSeverity = {
      name: 'debug',
      severity: 'low',
      title: 'Noise',
      url: 'https://github.com/advisories/GHSA-dddd-eeee-ffff',
    };

    assert.deepEqual(evaluateAudit(report(lowSeverity), allowBraces, 'moderate').failing, []);
    assert.deepEqual(evaluateAudit(report(), [], 'critical').failing, []);
  });

  it('reports an allowlist entry whose advisory is no longer reported, so it gets removed', () => {
    const { stale } = evaluateAudit({ vulnerabilities: {} }, allowBraces);

    assert.deepEqual(
      stale.map((entry) => entry.id),
      [BRACES],
    );
  });

  it('rejects an unknown audit level', () => {
    assert.throws(() => evaluateAudit(report(), [], 'severe'), /Unknown audit level/);
  });
});

describe('validateAllowlist', () => {
  it('accepts complete entries', () => {
    assert.equal(validateAllowlist(allowBraces), allowBraces);
  });

  it('requires a reason and a tracking link for every entry', () => {
    assert.throws(() => validateAllowlist([{ id: BRACES, package: 'braces' }]), /needs a "reason"/);
    assert.throws(
      () => validateAllowlist([{ id: BRACES, package: 'braces', reason: 'x', tracking: ' ' }]),
      /needs a "tracking"/,
    );
  });

  it('requires an array', () => {
    assert.throws(() => validateAllowlist({}), /must be a JSON array/);
  });
});
