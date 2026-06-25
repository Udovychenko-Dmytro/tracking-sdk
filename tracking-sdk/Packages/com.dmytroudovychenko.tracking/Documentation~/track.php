<?php
/**
 * track.php — optional live receiver example for the Dmytro Udovychenko Tracking SDK.
 *
 * Accepts an HTTP POST with a JSON body of the shape:
 *   { "events": [ {
 *       "id": "...", "type": "message|map", "ts": <unix-ms>,
 *       "sessionId": "...", "userId": "..." (or "anonymous" in privacy mode),
 *       "sdkVersion": "...", "platform": "...", "appVersion": "...",
 *       "deviceModel": "...", "osVersion": "...", "networkType": "wifi|cellular|none",
 *       "timezone": "UTC+02:00", "locale": "...", "bundleId": "...",
 *       "payload": [ { "k": "...", "t": "s|b|i|l|f|d|n", "v": "..." } ]
 *   } ] }
 *
 * Only the top-level "events" array is required; this example logs the raw body verbatim and does
 * not parse individual fields, so the SDK can add context fields without any receiver change.
 * Validates the payload, appends every attempt to track.log next to this script, and replies 200 with
 * { "ok": true, "received": <count> }.
 *
 * Chaos knob: append ?fail=<0-100> to the URL to make the endpoint return a transient 503 that often,
 * so the SDK's retry / back-off path can be exercised end-to-end against a real server. Because the
 * SDK reuses the event id on retries (at-least-once + idempotency), retried attempts show up in
 * track.log as repeated ids. Default is 0 — the endpoint always succeeds.
 *
 * A production backend would also de-duplicate by event id, authenticate the caller, and persist to a
 * real datastore. This file is only a developer diagnostic example, not the SDK backend.
 */

header('Content-Type: application/json');

if (($_SERVER['REQUEST_METHOD'] ?? 'GET') !== 'POST') {
    http_response_code(405);
    echo json_encode(['ok' => false, 'error' => 'method not allowed']);
    exit;
}

$raw  = file_get_contents('php://input');
$data = json_decode($raw, true);

if (!is_array($data) || !isset($data['events']) || !is_array($data['events'])) {
    http_response_code(400);
    echo json_encode(['ok' => false, 'error' => 'invalid payload']);
    exit;
}

$count = count($data['events']);

// Optional chaos injection.
$failRate = isset($_GET['fail']) ? max(0, min(100, (int) $_GET['fail'])) : 0;
$chaos    = $failRate > 0 && mt_rand(1, 100) <= $failRate;
$status   = $chaos ? 503 : 200;

$line = sprintf(
    "[%s] %s %d event(s) -> HTTP %d%s\n",
    gmdate('c'),
    $_SERVER['REMOTE_ADDR'] ?? 'unknown',
    $count,
    $status,
    $chaos ? ' (chaos)' : ''
);

// Best-effort append; never fail the request just because logging failed.
@file_put_contents(__DIR__ . '/track.log', $line . $raw . "\n", FILE_APPEND | LOCK_EX);

http_response_code($status);

if ($chaos) {
    echo json_encode(['ok' => false, 'error' => 'chaos: simulated transient failure']);
} else {
    echo json_encode(['ok' => true, 'received' => $count]);
}
