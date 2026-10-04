// Runs `npm audit` over every dependency and fails on any advisory at or above the given level,
// except those listed in audit-allowlist.json. An allowlist entry exempts one advisory in one
// package, and only while that advisory is still reported: once it no longer appears, the entry
// itself fails the audit so it cannot quietly outlive the problem it was added for.
//
// Usage: node scripts/audit-dependencies.mjs [--audit-level=moderate]

import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const SEVERITY_RANK = { info: 0, low: 1, moderate: 2, high: 3, critical: 4 };

/** The GHSA id at the end of an advisory URL, or the URL itself when it has none. */
export function advisoryId(url) {
  const match = /GHSA(-[a-z0-9]{4}){3}/i.exec(url ?? '');
  return match ? match[0] : String(url ?? '');
}

/**
 * Every advisory in an `npm audit --json` report, once each. Packages that are only vulnerable
 * through a dependency list it by name in `via`; the advisory itself is the object form.
 */
export function collectAdvisories(report) {
  const advisories = new Map();
  for (const vulnerability of Object.values(report?.vulnerabilities ?? {})) {
    for (const via of vulnerability.via ?? []) {
      if (typeof via !== 'object' || via === null) continue;

      const id = advisoryId(via.url);
      const key = `${id}|${via.name}`;
      if (!advisories.has(key)) {
        advisories.set(key, {
          id,
          package: via.name,
          severity: via.severity,
          title: via.title,
          url: via.url,
        });
      }
    }
  }
  return [...advisories.values()];
}

/**
 * Splits a report into the advisories that fail the audit, the ones the allowlist excuses, and
 * allowlist entries that no longer match anything reported.
 */
export function evaluateAudit(report, allowlist, auditLevel = 'moderate') {
  const threshold = SEVERITY_RANK[auditLevel];
  if (threshold === undefined) throw new Error(`Unknown audit level "${auditLevel}".`);

  const advisories = collectAdvisories(report);
  const isAllowed = (advisory) =>
    allowlist.some((entry) => entry.id === advisory.id && entry.package === advisory.package);

  const atLevel = advisories.filter(
    (advisory) => (SEVERITY_RANK[advisory.severity] ?? SEVERITY_RANK.critical) >= threshold,
  );

  return {
    failing: atLevel.filter((advisory) => !isAllowed(advisory)),
    allowed: atLevel.filter(isAllowed),
    stale: allowlist.filter(
      (entry) =>
        !advisories.some(
          (advisory) => advisory.id === entry.id && advisory.package === entry.package,
        ),
    ),
  };
}

/** Rejects entries that do not say what they excuse, why, and where the real fix is tracked. */
export function validateAllowlist(allowlist) {
  if (!Array.isArray(allowlist)) throw new Error('The audit allowlist must be a JSON array.');
  for (const entry of allowlist) {
    for (const field of ['id', 'package', 'reason', 'tracking']) {
      if (typeof entry?.[field] !== 'string' || !entry[field].trim()) {
        throw new Error(`Audit allowlist entry ${JSON.stringify(entry)} needs a "${field}".`);
      }
    }
  }
  return allowlist;
}

function runNpmAudit(cwd) {
  // npm exits non-zero whenever it finds anything, so the exit code says nothing here; the
  // report does. shell: true lets Windows resolve npm.cmd.
  const result = spawnSync('npm audit --json', { cwd, encoding: 'utf8', shell: true });
  try {
    const report = JSON.parse(result.stdout);
    if (report.error) throw new Error(report.error.summary ?? JSON.stringify(report.error));
    return report;
  } catch (error) {
    throw new Error(`npm audit did not produce a report: ${error.message}\n${result.stderr}`);
  }
}

function main() {
  const frontendRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
  const levelArg = process.argv.find((arg) => arg.startsWith('--audit-level='));
  const auditLevel = levelArg ? levelArg.split('=')[1] : 'moderate';

  const allowlist = validateAllowlist(
    JSON.parse(fs.readFileSync(path.join(frontendRoot, 'audit-allowlist.json'), 'utf8')),
  );
  const { failing, allowed, stale } = evaluateAudit(
    runNpmAudit(frontendRoot),
    allowlist,
    auditLevel,
  );

  for (const advisory of allowed) {
    const entry = allowlist.find((candidate) => candidate.id === advisory.id);
    console.log(
      `Allowed ${advisory.severity} ${advisory.id} in ${advisory.package}: ${entry.reason} (${entry.tracking})`,
    );
  }

  for (const advisory of failing) {
    console.error(
      `${advisory.severity} ${advisory.id} in ${advisory.package}: ${advisory.title} - ${advisory.url}`,
    );
  }

  for (const entry of stale) {
    console.error(
      `Allowlisted ${entry.id} in ${entry.package} is no longer reported; remove it from audit-allowlist.json (${entry.tracking}).`,
    );
  }

  if (failing.length > 0 || stale.length > 0) {
    console.error(
      `Dependency audit failed: ${failing.length} advisory(ies) at ${auditLevel} or above, ${stale.length} stale allowlist entry(ies).`,
    );
    process.exit(1);
  }

  console.log(`Dependency audit passed at ${auditLevel} (${allowed.length} allowlisted).`);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main();
}
