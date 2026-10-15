// The league runs on Eastern Time, so every draft deadline is shown that way whatever the browser's zone is.
const deadlineFormat = new Intl.DateTimeFormat("en-US", {
  timeZone: "America/New_York",
  weekday: "short",
  month: "short",
  day: "numeric",
  hour: "numeric",
  minute: "2-digit",
});

export const formatDeadline = (instant: string) =>
  `${deadlineFormat.format(new Date(instant))} ET`;

// "2d 3h", "3h 12m", "12m" or "less than a minute", from the server's clock so the browser's own clock never decides anything.
export function timeLeft(deadline: string, serverTime: string) {
  const ms = new Date(deadline).getTime() - new Date(serverTime).getTime();
  if (ms <= 0) return "none";
  const minutes = Math.floor(ms / 60_000);
  if (minutes < 1) return "less than a minute";
  const days = Math.floor(minutes / 1440);
  const hours = Math.floor((minutes % 1440) / 60);
  const rest = minutes % 60;
  if (days > 0) return `${days}d ${hours}h`;
  if (hours > 0) return `${hours}h ${rest}m`;
  return `${rest}m`;
}

// In-app navigation without a page load; App listens for popstate.
export function goTo(path: string) {
  window.history.pushState({}, "", path);
  window.dispatchEvent(new PopStateEvent("popstate"));
}
