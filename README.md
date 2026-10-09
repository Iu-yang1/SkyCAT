# SkyCAT

Please see the [SkyCAT web site](https://ve3nea.github.io/SkyCAT/index.html)

## Fork additions

This fork includes a loopback-by-default main CAT server (`127.0.0.1:4532`),
a loopback-only WSJT-X Hamlib NET rigctl compatibility proxy (`4534`), an IC-9700
binary scope stream (`4535`) and a dedicated IC-9700 Remote Control Switch
auxiliary-settings endpoint (`127.0.0.1:4537`). The Switch endpoint has
a `PING/PONG` handshake, does not open a second COM connection and prevents
PTT/VFO/frequency/mode writes.

For a combined **RS-BA1 + SkyRoof + Remote Control Switch** setup, see the
[step-by-step guide](docs/skycatd.md#ic-9700-remote-control-switch-integration)
or [中文说明](docs/zh-cn/skycatd.md#ic-9700-remote-control-switch-集成).

See the expanded [skycatd command-line guide](docs/skycatd.md) for all command-line options,
port behavior, supported radio IDs, SkyRoof examples, and WSJT-X configuration.

Documentation: [English](docs/index.md) | [简体中文](docs/zh-cn/index.md)
