using System.Runtime.CompilerServices;

// Expose internal pipeline types (EventQueue, EventDispatcher, QueuedEvent) to the test assembly so
// the concurrency core can be white-box tested deterministically.
[assembly: InternalsVisibleTo("DmytroUdovychenko.Tracking.Tests")]
