import { FormEvent, useEffect, useState } from "react";

type CurrentUser = { id: string; name: string; role: "Admin" | "Coach" };

export default function App() {
  const [email, setEmail] = useState("");
  const [state, setState] = useState<"sign-in" | "check-email" | "redeeming">(
    "sign-in",
  );
  const [user, setUser] = useState<CurrentUser | null>(null);

  useEffect(() => {
    async function loadSession() {
      const token = new URLSearchParams(window.location.search).get("token");
      if (window.location.pathname === "/signin-callback" && token) {
        setState("redeeming");
        const redeemed = await fetch(
          `/api/sign-in-links/redeem?token=${encodeURIComponent(token)}`,
          { credentials: "include" },
        );
        window.history.replaceState({}, "", redeemed.ok ? "/" : "/signin");
        if (!redeemed.ok) {
          setState("sign-in");
          return;
        }
      }

      const response = await fetch("/api/me", { credentials: "include" });
      if (response.ok) setUser(await response.json());
      else setState("sign-in");
    }

    void loadSession();
  }, []);

  async function requestLink(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    await fetch("/api/sign-in-links", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email }),
    });
    setState("check-email");
  }

  async function signOut() {
    await fetch("/api/sign-out", { method: "POST", credentials: "include" });
    setUser(null);
    setState("sign-in");
  }

  if (user) {
    return (
      <main>
        <h1>Swan League Tracker</h1>
        <p>
          Signed in as {user.name} · {user.role}
        </p>
        {user.role === "Admin" && <strong>Admin</strong>}
        <button onClick={() => void signOut()}>Sign out</button>
      </main>
    );
  }

  if (state === "check-email")
    return (
      <main>
        <h1>Check your email</h1>
        <p>We sent a sign-in link to {email}.</p>
      </main>
    );
  if (state === "redeeming")
    return (
      <main>
        <h1>Signing you in…</h1>
      </main>
    );

  return (
    <main>
      <h1>Swan League Tracker</h1>
      <form onSubmit={requestLink}>
        <label htmlFor="email">Email</label>
        <input
          id="email"
          type="email"
          value={email}
          onChange={(event) => setEmail(event.target.value)}
          required
        />
        <button type="submit">Send sign-in link</button>
      </form>
    </main>
  );
}
