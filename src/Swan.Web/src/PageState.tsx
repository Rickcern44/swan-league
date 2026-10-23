import type { ReactNode } from "react";

// One look for the moments a page has nothing ready to show: still loading, failed, or empty.
// Each page keeps its own words; this only decides how they are presented and announced.
type Props =
  | { kind: "loading"; message: string }
  | {
      kind: "error";
      title: string;
      message: string;
      onRetry?: () => void;
      retryLabel?: string;
    }
  | {
      kind: "empty";
      title: string;
      children?: ReactNode;
      // Mark as a live status when the message can change while the page is open.
      live?: boolean;
    };

export default function PageState(props: Props) {
  if (props.kind === "loading")
    return (
      <div className="page-state loading" role="status" aria-busy="true">
        <span className="page-spinner" aria-hidden="true" />
        <p>{props.message}</p>
      </div>
    );

  if (props.kind === "error")
    return (
      <div className="page-state error" role="alert">
        <p>
          <strong>{props.title}</strong> {props.message}
        </p>
        {props.onRetry && (
          <button type="button" onClick={props.onRetry}>
            {props.retryLabel ?? "Try again"}
          </button>
        )}
      </div>
    );

  return (
    <div className="page-state empty" role={props.live ? "status" : undefined}>
      <h2>{props.title}</h2>
      {props.children}
    </div>
  );
}
