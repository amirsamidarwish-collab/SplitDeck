Local Chrome test fixture, Windows only. Requires Python, websocket-client,
and Chrome at its ordinary Program Files path. The server creates a dedicated
profile in this directory and serves only chrome-input-test.html on localhost.
Do not use a personal Chrome profile with remote debugging.

python chrome-simulation.py --serve
python chrome-simulation.py --phase baseline --report baseline.json

After normal recovery/restart/setup, an elevated bounded SplitDeck benchmark
can run concurrently with this watcher (watcher first):
python chrome-simulation.py --watch-split --report split.json --benchmark-report performance.json
SplitDeck.exe --session-benchmark performance.json

Inspect both reports. acceptedActiveSplitPass requires a real ACTIVE interval
and confirmed restoration. Synthetic Chrome input does not test OS mouse latency.
The watcher must not be accepted on incomplete journals or cleanup telemetry.
Close only the dedicated test Chrome and stop this server when finished.
