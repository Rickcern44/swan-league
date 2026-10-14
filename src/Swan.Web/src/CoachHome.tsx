export default function CoachHome({ name }: { name: string }) {
  return (
    <section aria-labelledby="coach-title">
      <h1 id="coach-title">Welcome, {name}</h1>
      <div className="panel">
        <h2>Your league home is on its way</h2>
        <p>
          Picks and standings are not part of the app yet. There is nothing for
          you to do here for now.
        </p>
      </div>
    </section>
  );
}
