import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { DevelopmentTools, RefreshAccess } from "./AccessContext";
import CoachHome from "./CoachHome";
import CommissionerDraft from "./CommissionerDraft";
import Dashboard from "./Dashboard";
import PickDesk from "./PickDesk";
import RosterDraft from "./RosterDraft";
import {
  type Access,
  commishHome,
  hasCommish,
  rememberedMode,
  rememberMode,
  resolve,
} from "./routes";
import SeasonSetup from "./SeasonSetup";
import Shell from "./Shell";
import WeekWorkspace from "./WeekWorkspace";

// setupAccess and commissionsLiveSeason come from the server, which reads them from the database on each request: they say what this person may do as a season's commissioner.
type CurrentUser = {
  id: string;
  name: string;
  role: "Admin" | "Coach";
  setupAccess?: "manage" | "create" | "none";
  commissionsLiveSeason?: boolean;
  developmentTools?: boolean;
};

export default function App() {
  const [email, setEmail] = useState("");
  const [state, setState] = useState<"sign-in" | "check-email" | "redeeming">(
    "sign-in",
  );
  const [user, setUser] = useState<CurrentUser | null>(null);
  const [path, setPath] = useState(window.location.pathname);
  const [decisions, setDecisions] = useState(0);

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

  function navigate(to: string, replace = false) {
    if (replace) window.history.replaceState({}, "", to);
    else window.history.pushState({}, "", to);
    // A query (such as a week tab) stays in the address; routing reads only the path.
    setPath(to.split("?")[0]);
  }

  const access: Access | null = user
    ? {
        isAdmin: user.role === "Admin",
        // An administrator keeps both areas; a commissioner gets Setup for the season that is theirs and the draft view for the live season they commission.
        canSetup:
          user.role === "Admin" || (user.setupAccess ?? "none") !== "none",
        canDraft: user.role === "Admin" || user.commissionsLiveSeason === true,
      }
    : null;
  const resolved = access ? resolve(path, access) : null;
  const landed = useRef(false);

  // Old addresses and pages this person may not open are replaced by where they actually lead.
  useEffect(() => {
    if (resolved && resolved.path !== path) {
      window.history.replaceState({}, "", resolved.path);
      setPath(resolved.path);
    }
  });

  // Opening the app at its home address returns a commissioner to Commish if that is where they were last.
  useEffect(() => {
    if (!access || landed.current) return;
    landed.current = true;
    if (
      window.location.pathname === "/" &&
      rememberedMode() === "commish" &&
      hasCommish(access)
    )
      navigate(commishHome(access), true);
  });

  const mode = resolved?.route.mode;
  useEffect(() => {
    if (mode && landed.current) rememberMode(mode);
  }, [mode]);

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

  if (user && access && resolved) {
    const { route } = resolved;
    return (
      <RefreshAccess.Provider value={refreshAccess}>
        <DevelopmentTools.Provider value={user.developmentTools === true}>
          <Shell
            name={user.name}
            role={user.role}
            access={access}
            route={route}
            decisions={decisions}
            onNavigate={(to) => navigate(to)}
            onSignOut={() => void signOut()}
          >
            {route.mode === "coach" ? (
              route.page === "draft" ? (
                <RosterDraft />
              ) : route.page === "picks" ? (
                <PickDesk />
              ) : (
                <CoachHome />
              )
            ) : route.page === "draft" ? (
              <CommissionerDraft />
            ) : route.page === "setup" ? (
              <SeasonSetup
                createOnly={!access.isAdmin && user.setupAccess === "create"}
              />
            ) : route.page === "week" ? (
              <WeekWorkspace
                key={route.weekId}
                weekId={route.weekId}
                onBack={() => navigate("/commish")}
              />
            ) : (
              <Dashboard
                onSeasonSetup={() => navigate("/commish/setup")}
                onOpenWeek={(id, tab) =>
                  navigate(`/commish/weeks/${id}${tab ? `?tab=${tab}` : ""}`)
                }
                onNavigate={(to) => navigate(to)}
                onDecisions={setDecisions}
                canDraft={access.canDraft}
                canSetup={access.canSetup}
              />
            )}
          </Shell>
        </DevelopmentTools.Provider>
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
