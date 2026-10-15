import { FormEvent, useCallback, useEffect, useState } from "react";
import CommissionerDraft from "./CommissionerDraft";
import { RefreshAccess } from "./AccessContext";
import PickDesk from "./PickDesk";
import RosterDraft from "./RosterDraft";
import Dashboard from "./Dashboard";
import SeasonSetup from "./SeasonSetup";
import WeekWorkspace from "./WeekWorkspace";
import Shell from "./Shell";

// setupAccess and commissionsLiveSeason come from the server, which reads them from the database on each request: they say what this person may do as a season's commissioner.
type CurrentUser = {
  id: string;
  name: string;
  role: "Admin" | "Coach";
  setupAccess?: "manage" | "create" | "none";
  commissionsLiveSeason?: boolean;
};

export default function App() {
  const [email, setEmail] = useState("");
  const [state, setState] = useState<"sign-in" | "check-email" | "redeeming">(
    "sign-in",
  );
  const [user, setUser] = useState<CurrentUser | null>(null);
  const [path, setPath] = useState(window.location.pathname);

  useEffect(() => {
    const onPop = () => setPath(window.location.pathname);
    window.addEventListener("popstate", onPop);
    return () => window.removeEventListener("popstate", onPop);
  }, []);

  // Re-reads what this person may do after an action that changes it (creating or starting a season).
  const refreshAccess = useCallback(async () => {
    try {
      const response = await fetch("/api/me", { credentials: "include" });
      if (response.ok) setUser(await response.json());
    } catch {
      // keep what is shown; the next page load reads it again
    }
  }, []);

  function navigate(to: string) {
    window.history.pushState({}, "", to);
    setPath(to);
  }

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
    const inSetup = path === "/setup" || path.startsWith("/setup/");
    const isAdmin = user.role === "Admin";
    // An administrator keeps both areas; a commissioner gets Setup for the season that is theirs and the roster view for the live season they commission.
    const canSetup = isAdmin || (user.setupAccess ?? "none") !== "none";
    const canDraft = isAdmin || user.commissionsLiveSeason === true;
    const workspaceWeek = /^\/weeks\/([0-9a-f-]{36})\/?$/i.exec(path)?.[1];
    return (
      <RefreshAccess.Provider value={refreshAccess}>
        <Shell
          name={user.name}
          role={user.role}
          canSetup={canSetup}
          canDraft={canDraft}
          path={path}
          onNavigate={navigate}
          onSignOut={() => void signOut()}
        >
          {path === "/draft" && canDraft ? (
            <CommissionerDraft />
          ) : inSetup && canSetup ? (
            <SeasonSetup
              createOnly={!isAdmin && user.setupAccess === "create"}
            />
          ) : !isAdmin ? (
            path === "/roster" ? (
              <RosterDraft />
            ) : (
              <PickDesk />
            )
          ) : workspaceWeek ? (
            <WeekWorkspace
              key={workspaceWeek}
              weekId={workspaceWeek}
              onBack={() => navigate("/")}
            />
          ) : (
            <Dashboard
              onSeasonSetup={() => navigate("/setup")}
              onOpenWeek={(id) => navigate(`/weeks/${id}`)}
            />
          )}
        </Shell>
      </RefreshAccess.Provider>
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
