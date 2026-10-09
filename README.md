# SkyCAT

Documentation sites:

- **[SkyCAT fork — GitHub Pages](https://iu-yang1.github.io/SkyCAT/)**
- **[SkyCAT fork — 简体中文](https://iu-yang1.github.io/SkyCAT/zh-cn/)**
- **[Pages publishing history (Actions)](https://github.com/Iu-yang1/SkyCAT/actions)** — GitHub's native `pages build and deployment`.
- **[Verified documentation deployment](https://github.com/Iu-yang1/SkyCAT/actions/workflows/pages-deployment-visibility.yml)** — `production` environment with published site URL.
- [Upstream VE3NEA SkyCAT documentation](https://ve3nea.github.io/SkyCAT/index.html)

The fork uses GitHub Pages' native **pages build and deployment** workflow.
The native Pages workflow is the only publisher. An additional lightweight
verification workflow records a `production` environment and public URL
after a successful native publication. It does **not** rebuild or republish the
site. The deployment is also tracked under the `production` environment;
use the Actions links above if GitHub's Deployments dashboard is unavailable.

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
