# Basic Usage — sample

Importable via **Package Manager → Dmytro Udovychenko Tracking SDK → Samples → Basic Usage → Import**.

After importing, just press **Play**: the sample auto-spawns a Canvas UI — no scene wiring required.
It exercises the whole public API:

- **Initialization buttons** — local simulated delivery, plus a developer live test receiver (stub) in clean and chaos modes.
- **SendMessage** — valid non-blocking fire-and-forget event, plus an invalid empty-message case.
- **SendMapAsync** — valid, mixed-warning, empty, and invalid-entry maps whose `Task<bool>` resolves when
  the batch is actually delivered or rejected.
- **Flush now** — forces delivery of everything buffered.
- **Disable tracking / Purge** — privacy opt-out (GDPR-style consent withdrawal + data delete).
- **Privacy mode** — anonymous mode (`userId` → `"anonymous"`, context kept).
- **Live metrics** — enqueued / sent / dropped / retried / given-up / dead-lettered counters.

The sample calls `Tracker.Init("demo-user-001")` in `Awake` (default endpoint, simulated transport — no
live server needed). Use the two HTTP buttons (clean / chaos) to reinitialize against a built-in
developer **live test receiver (stub)** — a validate-log-`200` endpoint, not a real backend.
