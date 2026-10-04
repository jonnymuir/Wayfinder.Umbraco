import { execFile } from 'node:child_process';
import { promisify } from 'node:util';

const execFileAsync = promisify(execFile);

// Pure domain language, no Wayfinder vocabulary anywhere — reviewed and approved by the real
// user (kept em-dash-free to match the video's house style and the walkthrough doc's copy,
// which must stay in sync with this). This is the entire point of the demo: a service designer
// who has never heard
// of Wayfinder describes a problem statement, user needs, and constraints in their own terms;
// the MCP's own resources/skills/prompts are what teach the LLM the implementation mechanics
// (routes, gateways, showWhen, component types), not this brief. Deliberately does NOT name a
// definitionKey, an exact displayName, a style-reference blueprint, or which MCP tools/
// resources to use — a real designer wouldn't know any of that exists, and specifying it here
// would be feeding the agent an already-translated answer instead of proving the MCP does the
// translating. It also does NOT pre-avoid the two real engine bugs found during earlier
// debugging this session (boolean field validation as a summary-list sibling; showWhen
// evaluating pre-submission state) — a real designer has no way to know either exists; if the
// live agent hits one, that's the validation-feedback loop working as intended, on camera, not
// a problem to engineer around in advance.
export const DESIGNER_BRIEF = [
  'Hi. I work on licensing for the National Juggling Authority. I need help designing a new service.\n\n',
  'The problem: right now, if someone already holds a current professional juggling licence from ',
  'another recognised juggling authority and wants to work here, they have to apply for a brand ',
  "new licence from scratch, exactly the same as someone who's never juggled professionally ",
  "before. That's not fair on them, it duplicates assessment work that's already been done ",
  'properly elsewhere, and it puts off exactly the experienced jugglers we want performing here.\n\n',
  'I want a "transfer your licence" service instead. What I know about how it needs to work:\n',
  '- Only for jugglers who already hold a current licence from a juggling authority we formally ',
  "recognise. Right now that's the European Juggling Federation, Async Circle International, ",
  "and the Ring Masters Guild. Anyone else isn't eligible for transfer; they need to apply as a ",
  'new licence holder instead, which is a separate existing service.\n',
  '- We need to see their current licence certificate and some proof of who they are.\n',
  "- Before we grant anything, they need to formally declare they'll uphold our professional ",
  'standards, the same declaration a new applicant makes.\n',
  "- A caseworker always has to check the evidence and make the actual decision. This can't be ",
  'auto-approved, someone has to look at the documents.\n',
  '- Same accessibility bar as everything else we ship: WCAG double-A, in line with the GDS ',
  'service standard.\n\n',
  'Can you help me design this properly? Ask me anything you need.'
].join('');

const DESIGNER_PERSONA =
  'You are roleplaying as a service designer at the National Juggling Authority, answering ' +
  "a software team's clarifying question about a service you commissioned. Answer ONLY in " +
  'plain domain language. You are not a software engineer and know nothing about how the ' +
  'underlying system is built; never use or reference implementation terms (routes, ' +
  'gateways, showWhen, JSON, component types, field keys, or anything like that). Keep it ' +
  'brief, friendly and direct: 1 to 3 short sentences, like a real chat reply. Do not use ' +
  'dashes; use separate sentences. Do not be pedantic about the team repeating a question; ' +
  "just answer it. Stay consistent with the brief you already gave; if something wasn't " +
  'specified, make a sensible judgment call as the domain expert.';

const FALLBACK_ANSWER =
  'Good question. Use your best judgement on that one, based on how the rest of ' +
  'the service works; I trust you to make a sensible call.';

/**
 * A small, prepared set of in-character domain answers was tried first here and abandoned —
 * confirmed live it's the wrong shape entirely, not just imperfectly tuned: its topic-matching
 * regexes are coarse keyword matches, and a genuinely different question can innocently share a
 * keyword with an earlier, already-answered one (e.g. "does another system issue the new
 * certificate? Any new expiry date to show them?" re-matched an /expir/i entry meant for "has
 * their licence expired") — three separate dedup-keying schemes were tried to work around this
 * class of false positive, and all three still depended on regex matching being right in the
 * first place, which it fundamentally can't always be for open-ended real dialogue.
 *
 * Replaced with a real model call that generates the designer's answer fresh, in character, for
 * every question — no pattern matching, so there's no keyword-collision class of bug left to
 * have. Haiku, not Sonnet: this is a simple, well-specified conversational completion (answer
 * one question, in character, from a fixed brief) — a good fit, unlike the earlier abandoned
 * attempt to have Haiku drive the actual MCP-based design work itself (confirmed live to fail
 * there: it hallucinated tool calls outside its restricted --tools allowlist instead of using
 * its real MCP tools). No tools at all here (`--tools ""`) — pure text-in/text-out, matching
 * `-p`'s own non-interactive print-and-exit mode. The prompt positional argument must come
 * BEFORE `--tools` on the command line — confirmed live, `--tools` is a variadic flag
 * (`<tools...>`) that otherwise swallows the next argument as an additional (invalid) tool name
 * and leaves nothing for the prompt itself.
 */
export async function generateDesignerAnswer(questionTail: string): Promise<string> {
  const userPrompt =
    `The brief you gave earlier:\n\n${DESIGNER_BRIEF}\n\n` +
    `The team's current question (this may include some surrounding conversation context — ` +
    `answer whatever they're actually asking now):\n\n${questionTail}`;

  // execFile (async), NOT execFileSync: a `claude -p` cold start plus a model round-trip can
  // take tens of seconds, and a synchronous call blocks Node's event loop for that whole time
  // — which freezes the tmux-mirror poll and the recorded video with it. 90s timeout (the
  // earlier 30s hit ETIMEDOUT live while the main agent was also loading the API), 3 attempts.
  for (let attempt = 1; attempt <= 3; attempt++) {
    try {
      const { stdout } = await execFileAsync(
        'claude',
        ['-p', '--model', 'haiku', '--system-prompt', DESIGNER_PERSONA, userPrompt, '--tools', ''],
        { encoding: 'utf8', timeout: 90_000, maxBuffer: 10 * 1024 * 1024 }
      );
      const trimmed = stdout.trim();
      if (trimmed) return trimmed;
    } catch (err) {
      console.error(`generateDesignerAnswer attempt ${attempt} failed: ${err instanceof Error ? err.message : String(err)}`);
    }
  }
  return FALLBACK_ANSWER; // every attempt failed — a hardcoded safety net, not the primary mechanism
}
