// Shared Swan visual parts: the feather mark and the state chip. Everything else is plain CSS in App.css.

export function SwanMark({ className = "" }: { className?: string }) {
  // One light sweep of a feather, drawn with strokes so it takes the surrounding text colour.
  return (
    <svg
      className={`swan-mark ${className}`}
      viewBox="0 0 32 32"
      width="28"
      height="28"
      aria-hidden="true"
      focusable="false"
    >
      <path
        d="M4 24c8 2 17-1 22-12 1-2 1-4 1-6-6 1-13 5-17 12"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path
        d="M10 18c3 0 6-1 9-4M7 22c4 0 8-1 11-4"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.5"
        strokeLinecap="round"
        opacity=".7"
      />
    </svg>
  );
}

// Every state is spelled out, so colour is never the only thing that says where a week is.
const stateLabels: Record<string, string> = {
  Draft: "Draft",
  LinesApproved: "Lines approved",
  Open: "Open for picks",
  Locked: "Locked",
  Final: "Final",
};

export const kindLabels: Record<string, string> = {
  Regular: "Regular season",
  Playoff: "Playoffs",
  SuperBowl: "Super Bowl",
};

export const stageLabelsInOrder = [
  "Draft",
  "LinesApproved",
  "Open",
  "Locked",
  "Final",
].map((state) => ({ state, label: stateLabels[state] }));

export function stateLabel(state: string) {
  return stateLabels[state] ?? state;
}

export function StateChip({ state }: { state: string }) {
  return (
    <span className={`chip chip-${state.toLowerCase()}`}>
      <span className="chip-dot" aria-hidden="true" />
      {stateLabel(state)}
    </span>
  );
}
