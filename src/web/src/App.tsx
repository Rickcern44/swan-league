import { useEffect, useState } from "react";

export default function App() {
  const [message, setMessage] = useState("Loading API…");

  useEffect(() => {
    async function loadPing() {
      try {
        const response = await fetch("/api/ping");

        if (!response.ok) {
          throw new Error(`API request failed with ${response.status}`);
        }

        const payload: { message: string } = await response.json();
        setMessage(payload.message);
      } catch {
        setMessage("Unable to reach the API");
      }
    }

    void loadPing();
  }, []);

  return (
    <main>
      <h1>Swan League Tracker</h1>
      <p>API response: {message}</p>
    </main>
  );
}
