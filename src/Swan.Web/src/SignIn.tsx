import { FormEvent, useEffect, useRef, useState } from "react";
import { SwanMark } from "./Swan";

type View = "form" | "sent";

// The server allows one link per address per minute and answers 429 with how long is left.
const WAIT_SECONDS = 60;

async function secondsToWait(response: Response) {
  try {
    const body = await response.json();
    if (
      typeof body?.retryAfterSeconds === "number" &&
      body.retryAfterSeconds > 0
    )
      return Math.ceil(body.retryAfterSeconds);
  } catch {
    // fall back to the header
  }
  const header = Number(response.headers.get("Retry-After"));
  return Number.isFinite(header) && header > 0
    ? Math.ceil(header)
    : WAIT_SECONDS;
}

const clock = (seconds: number) =>
  `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;

// Signing in by email link. The server answers the same for every address, so nothing here says whether an email is known.
export default function SignIn({
  redeeming = false,
  linkFailed = false,
}: {
  redeeming?: boolean;
  linkFailed?: boolean;
}) {
  const [view, setView] = useState<View>("form");
  const [email, setEmail] = useState("");
  const [sentTo, setSentTo] = useState("");
  const [wait, setWait] = useState(0);
  const [limited, setLimited] = useState(false);
  const [problem, setProblem] = useState("");
  const [sending, setSending] = useState(false);
  const heading = useRef<HTMLHeadingElement>(null);
  const first = useRef(true);

  // One countdown drives both the rate-limit notice and the resend button; it runs only while there is time left.
  const counting = wait > 0;
  useEffect(() => {
    if (!counting) return;
    const timer = setInterval(
      () => setWait((left) => Math.max(0, left - 1)),
      1000,
    );
    return () => clearInterval(timer);
  }, [counting]);

  useEffect(() => {
    if (wait === 0) setLimited(false);
  }, [wait]);

  const title = redeeming
    ? "Signing you in…"
    : view === "sent"
      ? "Check your email"
      : "Sign in";

  useEffect(() => {
    document.title = `${title} · Swan League`;
    // Each new step is announced by moving focus to its heading, but not on first load.
    if (first.current) first.current = false;
    else heading.current?.focus();
  }, [title]);

  async function request(address: string) {
    setSending(true);
    setProblem("");
    try {
      const response = await fetch("/api/sign-in-links", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email: address }),
      });
      if (response.status === 429) {
        setWait(await secondsToWait(response));
        setLimited(true);
        return false;
      }
      setSentTo(address);
      setWait(WAIT_SECONDS);
      setLimited(false);
      return true;
    } catch {
      setProblem(
        "Could not reach the server. Check your connection and try again.",
      );
      return false;
    } finally {
      setSending(false);
    }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (await request(email.trim())) setView("sent");
  }

  return (
    <div className="signin">
      <section className="signin-brand" aria-label="About Swan League">
        <span className="signin-mark">
          <SwanMark />
          Swan League
        </span>
        <div className="signin-pitch">
          <p className="signin-tagline">
            Your picks, your dogs and where you stand, all season.
          </p>
          <p className="signin-headline" aria-hidden="true">
            Nine teams. Three starts a week. One dog.
          </p>
        </div>
        <div className="signin-tiers" aria-hidden="true">
          {[1, 2, 3, 4].map((tier) => (
            <span key={tier} className={`tier tier-${tier}`}>
              {tier}
            </span>
          ))}
        </div>
      </section>

      <main className="signin-sheet">
        {redeeming ? (
          <div className="signin-busy" role="status" aria-busy="true">
            <span className="signin-spinner" aria-hidden="true" />
            <h1 ref={heading} tabIndex={-1}>
              Signing you in…
            </h1>
            <p>This takes a second.</p>
          </div>
        ) : view === "sent" ? (
          <div className="signin-step">
            <span className="signin-mail" aria-hidden="true">
              <svg width="28" height="28" viewBox="0 0 24 24">
                <rect
                  x="3"
                  y="5"
                  width="18"
                  height="14"
                  rx="2"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2"
                />
                <path
                  d="M4 7l8 6 8-6"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2"
                  strokeLinejoin="round"
                />
              </svg>
            </span>
            <h1 ref={heading} tabIndex={-1}>
              Check your email
            </h1>
            <p role="status" className="signin-lede">
              If <strong>{sentTo}</strong> belongs to a coach in the league, a
              sign-in link is on its way. Open it on this device.
            </p>
            <p className="signin-note">
              Nothing after a few minutes? Check your spam folder, then send
              another.
            </p>
            {problem && (
              <p role="alert" className="signin-problem">
                {problem}
              </p>
            )}
            <button
              type="button"
              className="signin-secondary"
              disabled={wait > 0 || sending}
              onClick={() => void request(sentTo)}
            >
              {wait > 0 ? `Send another link in ${wait}s` : "Send another link"}
            </button>
            <button
              type="button"
              className="signin-link"
              onClick={() => {
                setProblem("");
                setView("form");
              }}
            >
              Use a different email
            </button>
          </div>
        ) : (
          <form
            className="signin-step"
            onSubmit={(event) => void submit(event)}
          >
            {linkFailed && !limited && !problem && (
              <p role="alert" className="signin-problem">
                <strong>That link didn't work.</strong> It may have expired or
                already been used. Enter your email for a new one.
              </p>
            )}
            <h1 ref={heading} tabIndex={-1}>
              Sign in
            </h1>
            <p className="signin-lede">
              We'll email you a one-time link. There's no password to remember.
            </p>
            <label htmlFor="email">Email</label>
            <input
              id="email"
              type="email"
              inputMode="email"
              autoComplete="email"
              placeholder="you@example.com"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              required
            />
            {limited && wait > 0 && (
              <p role="alert" className="signin-wait">
                A link was just sent to this address. You can ask for another in{" "}
                <strong>{clock(wait)}</strong>.
              </p>
            )}
            {problem && (
              <p role="alert" className="signin-problem">
                {problem}
              </p>
            )}
            <button
              type="submit"
              className="signin-primary"
              disabled={sending || (limited && wait > 0)}
            >
              Email me a sign-in link
            </button>
            <p className="signin-note">
              Only coaches the commissioner has added to the league can sign in.
            </p>
          </form>
        )}
      </main>
    </div>
  );
}
