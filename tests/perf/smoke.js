// k6 load test against a booted Wayfinder.Umbraco.ReferenceApp.
//   k6 run -e BASE_URL=http://127.0.0.1:8080 tests/perf/smoke.js            # smoke (default, ~70s)
//   k6 run -e BASE_URL=... -e PROFILE=stress tests/perf/smoke.js            # ramp to find the limit
// Read paths only: citizens browse the anonymous journey pages; caseworkers sign in through the
// reference app's own demo login and load their worklists. Stage submission is deliberately not
// load-tested here: the reference app maps every anonymous visitor to one shared citizen, so only
// the first submission of a fresh database reaches a form. A host with real per-citizen identity
// is where a write-path load test belongs.
import http from 'k6/http';
import { check, group, sleep } from 'k6';

const BASE = __ENV.BASE_URL || 'http://127.0.0.1:8080';
const STRESS = __ENV.PROFILE === 'stress';

const ramp = (peak) => [
  { duration: '20s', target: peak }, { duration: '40s', target: peak }, { duration: '10s', target: 0 },
];

export const options = {
  scenarios: {
    citizens: { executor: 'ramping-vus', exec: 'citizen', startVUs: 0, stages: ramp(STRESS ? 100 : 20) },
    caseworkers: { executor: 'constant-vus', exec: 'caseworker', vus: STRESS ? 25 : 5, duration: '70s' },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    'http_req_duration{scenario:citizens}': ['p(95)<800'],
    'http_req_duration{scenario:caseworkers}': ['p(95)<1500'],
    checks: ['rate>0.99'],
  },
};

const ok = (res, name) => check(res, { [`${name} is 200`]: (r) => r.status === 200 });

export function citizen() {
  group('citizen browse', () => {
    for (const path of ['/', '/apply', '/apply-to-coach']) {
      ok(http.get(`${BASE}${path}`, { tags: { name: path } }), path);
      sleep(1);
    }
  });
}

let signedIn = false;
export function caseworker() {
  if (!signedIn) {
    http.post(`${BASE}/demo/login`, { email: 'casey@example.test' }, { tags: { name: 'login' } });
    signedIn = true;
  }
  group('caseworker worklist', () => {
    for (const path of ['/caseworker-queue', '/coaching-register-queue']) {
      ok(http.get(`${BASE}${path}`, { tags: { name: path } }), path);
      sleep(1);
    }
  });
}
