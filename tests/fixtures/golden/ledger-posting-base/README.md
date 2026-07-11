# B1 base-amount fixtures — UNVERIFIED

These cases encode the approved P2-WP05 B1 posting contract, but they are **not OLD golden oracles**.

The OLD repository `Lokkeccs/Accounting` at SHA `085bedba467e3d46d3889db3bc80ea023e69756e` has FX-policy metadata helpers only. It has no posting-time `base_amount_minor` conversion or validation function that can be executed to capture expected outputs. Therefore every case is explicitly marked `UNVERIFIED` and must not be consumed as an exact expected-output fixture until the target validator exists and the fixture format is approved.

Coverage:

- same-currency base equals transaction amount;
- same-currency rate omitted or exactly `1`;
- exact foreign-currency conversion;
- positive and negative half-unit `AwayFromZero` rounding;
- one-minor-unit tolerance;
- non-positive rate rejection;
- sign mismatch rejection;
- balanced multi-currency base amounts.

The `fxRate` field is represented as a decimal string to keep fixture serialization exact and prevent consumers from parsing it as a binary floating-point number.
