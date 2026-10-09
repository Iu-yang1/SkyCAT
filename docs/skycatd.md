---
title: skycatd.exe
nav_order: 3
---

**Language:** English | [简体中文](zh-cn/skycatd.md)


# skycatd.exe

**skycatd.exe** is a command-line application based on the SkyCAT library. It connects to a
radio through a serial port and exposes CAT control over TCP.

This fork also provides three additional loopback-only services:

- a restricted Hamlib NET rigctl compatibility endpoint for WSJT-X;
- a binary IC-9700 scope-frame stream for SkyRoof;
- a restricted IC-9700 auxiliary-settings API for Remote Control Switch.

## Installation

There is no installer. [Download](download.md) the release and unzip all files to a folder.

The radio command-set files are stored in the **Rigs** subfolder. The model passed to
`--model` must match one of those command sets, or its numeric model ID.

Make sure that [.NET 9.0 Desktop Runtime](https://learn.microsoft.com/en-us/dotnet/core/install/)
is installed when using the framework-dependent build.

Windows:

```bash
winget install Microsoft.DotNet.DesktopRuntime.9
```

macOS:

```bash
brew install dotnet
```

Linux (Ubuntu):

```bash
sudo apt-get update
sudo apt-get install -y dotnet-sdk-9.0
```

More distributions are available
[here](https://learn.microsoft.com/dotnet/core/install/linux).

## Quick Start

For an IC-9700 connected as `COM9`:

```bash
skycatd.exe -m IC-9700 -r COM9
```

The IC-9700 command set already defines 115200 Baud as its default, so the command above is
equivalent to:

```bash
skycatd.exe -m IC-9700 -r COM9 -s 115200
```

With the default options, skycatd opens the following listeners:

| Service | Default address | Default port |
|---------|-----------------|-------------:|
| Main SkyCAT CAT server | `127.0.0.1` | 4532 |
| WSJT-X compatibility proxy | `127.0.0.1` | 4534 |
| IC-9700 scope stream | `127.0.0.1` | 4535 |
| IC-9700 Remote Control Switch | `127.0.0.1` | 4537 |

The main CAT server is loopback-only by default. It listens on all interfaces only when
`--allow-remote` is explicitly specified.

## Command Line Parameters

A typical command line is:

```bash
skycatd.exe -m IC-9700 -r COM9 -s 115200 -t 4532 -vvv -f
```

### `-m, --model <model>`

Required for normal server operation.

Selects the radio command set. The value may be either:

- the command-set file name without the `.json` extension, for example `IC-9700`;
- the numeric model ID printed by `skycatd.exe -l`.

Examples:

```bash
skycatd.exe -m IC-9700 -r COM9
skycatd.exe -m 3081 -r COM9
```

Both commands select the IC-9700 command set.

### `-r, --rig-file <serial-port>`

Required for normal server operation.

Specifies the serial port connected to the radio. The long option name is retained for
compatibility; the current implementation uses this value directly as the serial-port name.

Windows example:

```bash
-r COM9
```

Linux example:

```bash
-r /dev/ttyUSB0
```

### `-s, --serial-speed <baud>`

Optional.

Overrides the default Baud rate stored in the selected radio command set.

Example:

```bash
skycatd.exe -m IC-9700 -r COM9 -s 115200
```

If this option is omitted, skycatd uses the command set's `default_baud_rate`.

### `-t, --port <port>`

Optional. Default: **4532**.

Sets the TCP port of the main SkyCAT CAT server.

Example:

```bash
skycatd.exe -m IC-9700 -r COM9 -t 4600
```

By default the server listens on:

```text
127.0.0.1:<port>
```

Use `--allow-remote` to bind the main CAT server to all network interfaces.

### `--allow-remote`

Optional. Default: **off**.

Allows the main CAT server to listen on all interfaces.

Without this option:

```text
127.0.0.1:4532
```

With this option:

```bash
skycatd.exe -m IC-9700 -r COM9 --allow-remote
```

the main CAT server listens on:

```text
0.0.0.0:4532
```

This option affects only the main CAT listener. The WSJT-X proxy, scope stream,
and Switch auxiliary endpoint remain loopback-only.

> The main CAT TCP protocol does not provide TLS or user authentication. Do not expose it
> directly to the public Internet. For remote operation, use a trusted LAN, firewall rules,
> VPN, or another protected tunnel.

### `--wsjtx-port <port>`

Optional. Default: **4534**.

Sets the port of the loopback-only WSJT-X Hamlib NET rigctl compatibility proxy.

Example:

```bash
skycatd.exe -m IC-9700 -r COM9 --wsjtx-port 4540
```

The proxy always listens on `127.0.0.1`, regardless of `--allow-remote`.

### `--no-wsjtx-proxy`

Optional. Default: **off**.

Disables the WSJT-X compatibility proxy completely.

Example:

```bash
skycatd.exe -m IC-9700 -r COM9 --no-wsjtx-proxy
```

The main CAT server and scope stream continue to operate normally.

### `--scope-port <port>`

Optional. Default: **4535**.

Sets the loopback-only binary scope-stream port used by SkyRoof for native IC-9700 spectrum
frames.

Example:

```bash
skycatd.exe -m IC-9700 -r COM9 --scope-port 4605
```

The scope server always listens on `127.0.0.1`.

Each scope message on this port consists of:

1. a 4-byte little-endian unsigned frame length;
2. the raw CI-V frame of that length.

This port is binary and must not be used as a rigctl or normal CAT text endpoint.

### `--switch-port <port>`

Optional. Default: **4537**.

Sets the TCP port of the **IC-9700-only**, loopback-only Remote Control Switch
auxiliary-settings service. This is **not** a rigctl or raw CI-V port.

```bash
skycatd.exe -m IC-9700 -r COM9 --switch-port 4537
```

If another local process occupies that port, choose a different unused port
and change **both** this argument and the address in Remote Control Switch.

### `--no-switch-port`

Optional. Default: **off**.

Disables the dedicated auxiliary-settings listener. The CAT, WSJT-X and scope
services remain available (subject to their own options).

### `-v, --verbose`

Optional.

Enables verbose logging. The historical command-line form uses repeated `v` characters:

```bash
skycatd.exe -m IC-9700 -r COM9 -vvv
```

In the current implementation, specifying the verbose option enables the **Verbose** Serilog
level; the number of `v` characters does not select additional logging levels.

Without the option, the minimum log level is **Warning**.

### `-f, --file-log`

Optional. Default: **off**.

Writes the log to a file in addition to the console.

Example:

```bash
skycatd.exe -m IC-9700 -r COM9 -vvv -f
```

On Windows the file is created under the current working directory using a name similar to:

```text
Logs\skycatd_2026-09-29_080000.log
```

The full log path is printed when the server starts.

### `-l, --list`

Prints the supported radio model IDs and exits.

```bash
skycatd.exe -l
```

Example output:

```text
Rig #    Model
3081     IC-9700
...
```

Neither `--model` nor `--rig-file` is required with this option.

### `-a, --all`

Prints the capabilities of every loaded radio command set and exits.

```bash
skycatd.exe -a
```

The output is JSON-formatted capability data.

Neither `--model` nor `--rig-file` is required with this option.

### `--help`

Displays the command-line help generated by CommandLineParser and exits.

```bash
skycatd.exe --help
```

### `--version`

Displays version information and exits.

```bash
skycatd.exe --version
```

## Port Rules

All TCP ports must be in the range **1-65535**.

Active listeners must use distinct TCP ports:

- main CAT port (`--port`);
- WSJT-X proxy port (`--wsjtx-port`) unless disabled;
- scope stream port (`--scope-port`);
- IC-9700 Switch port (`--switch-port`) unless disabled.

SkyCAT validates these conflicts between its **own configured endpoints**.
It cannot reserve a port already owned by an unrelated Windows process.

For example, this is invalid:

```bash
skycatd.exe -m IC-9700 -r COM9 -t 4534 --wsjtx-port 4534
```

If a proxy is disabled, its configured port is not active. The main CAT and
scope ports always remain distinct; an enabled Switch port must also differ
from every active SkyCAT endpoint.

## Supported Radios

The current fork includes the following command sets:

| Model | Model ID | Default Baud rate |
|-------|---------:|------------------:|
| FT-817 | 1020 | 38400 |
| FT-818 | 1041 | 38400 |
| FT-847 | 1001 | 57600 |
| FT-897 | 1023 | 38400 |
| FT-991A | 1035 | 38400 |
| IC-705 | 3085 | 115200 |
| IC-705-wireless | 30850 | 19200 |
| IC-706MKIIG | 3011 | 19200 |
| IC-905 | 3090 | 115200 |
| IC-910 | 3044 | 19200 |
| IC-9100 | 3068 | 19200 |
| IC-9700 | 3081 | 115200 |
| IC-R7000 | 3040 | 1200 |
| TS-2000 | 2014 | 57600 |

Use `skycatd.exe -l` as the authoritative list for the particular build you are running.

## Common Configurations

### SkyRoof + IC-9700

For the usual local SkyRoof configuration:

```bash
skycatd.exe -m IC-9700 -r COM9
```

This provides:

```text
SkyRoof CAT:     127.0.0.1:4532
WSJT-X proxy:    127.0.0.1:4534
Scope stream:    127.0.0.1:4535
Switch auxiliary: 127.0.0.1:4537
```

If the radio uses a different COM port:

```bash
skycatd.exe -m IC-9700 -r COM12
```

### SkyRoof + WSJT-X

Run:

```bash
skycatd.exe -m IC-9700 -r COM9
```

SkyRoof connects to:

```text
127.0.0.1:4532
```

WSJT-X connects to:

```text
127.0.0.1:4534
```

Recommended WSJT-X settings are described in
[WSJT-X Compatibility Proxy](#wsjt-x-compatibility-proxy).

### Main CAT on another LAN computer

To allow another computer on the local network to connect to the main SkyCAT endpoint:

```bash
skycatd.exe -m IC-9700 -r COM9 --allow-remote
```

If Windows Firewall prompts for access, allow only the network profiles that are actually
required.

The WSJT-X, scope and Switch ports remain local to the skycatd computer.

### Custom TCP ports

```bash
skycatd.exe -m IC-9700 -r COM9 \
  --port 4600 \
  --wsjtx-port 4601 \
  --scope-port 4602 \
  --switch-port 4603
```

Use the same customized endpoints in the corresponding client programs.

### Disable WSJT-X integration

```bash
skycatd.exe -m IC-9700 -r COM9 --no-wsjtx-proxy
```

### Diagnostic logging

```bash
skycatd.exe -m IC-9700 -r COM9 -vvv -f
```

This is the recommended form when collecting logs for CAT, CI-V, PTT, or scope debugging.

## Running skycatd

Windows:

```bash
skycatd.exe <parameters>
```

Linux and macOS:

```bash
dotnet skycatd.dll <parameters>
```

Stop the server with **Ctrl-C**. skycatd stops accepting clients, releases PTT that it owns,
stops the scope stream, and closes the serial port before exiting.

If the serial port becomes unavailable while skycatd is running, the server periodically attempts
to reopen it. The TCP listeners are restarted after the serial connection becomes available again.

## Skycatd Commands

The main SkyCAT TCP server understands the following line-oriented commands:

| Action | Command |
|--------|---------|
| setup(Duplex) | `U Duplex` |
| setup(Split) | `U Split` |
| setup(Simplex) | `U Simplex` |
| read_rx_frequency | `f` |
| read_tx_frequency | `i` |
| write_rx_frequency | `F {frequency}` |
| write_tx_frequency | `I {frequency}` |
| read_rx_mode | `m` |
| read_tx_mode | `x` |
| write_rx_mode | `M {mode} 0` |
| write_tx_mode | `X {mode} 0` |
| read_ptt | `t` |
| set_ptt_on | `T 1` |
| set_ptt_off | `T 0` |
| write_ctcss_tone | `C {tone}` |
| enable_ctcss | `U TONE 1` |
| disable_ctcss | `U TONE 0` |
| enable_scope | `U SCOPE 1` |
| disable_scope | `U SCOPE 0` |
| enable_scope_data | `U SCOPE_DATA 1` |
| disable_scope_data | `U SCOPE_DATA 0` |
| IC-9700 scope sweep FAST | `U SCOPE_FAST 1` |
| IC-9700 hardware RF gain (0–255) | `U RF_GAIN 128` |
| Read IC-9700 hardware RF gain (decimal 0–255) | `U RF_GAIN_READ` |
| Read MAIN/SUB scope settings | `U SCOPE_READ` |
| Select active scope | `U SCOPE_SELECT MAIN|SUB` |
| Scope mode | `U SCOPE_MODE MAIN|SUB CENTER|FIXED|SCROLL-C|SCROLL-F` |
| Center span (Hz) | `U SCOPE_SPAN MAIN|SUB 25000` |
| Fixed edge slot | `U SCOPE_EDGE MAIN|SUB 1..4` |
| Reference level (-20..20 dB, 0.5 dB steps) | `U SCOPE_REF MAIN|SUB -12.5` |
| Sweep speed | `U SCOPE_SPEED MAIN|SUB FAST|MID|SLOW` |
| Video bandwidth | `U SCOPE_VBW MAIN|SUB NARROW|WIDE` |
| Scope during transmission | `U SCOPE_TX 0|1` |
| Center frequency convention | `U SCOPE_CENTER_TYPE FILTER|CARRIER|ABS` |
| Marker position | `U SCOPE_MARKER FILTER|CARRIER` |
| Read stored fixed edges | `U SCOPE_READ_EDGE 2 1` |
| Set stored fixed edges (Hz) | `U SCOPE_FIXED_EDGE 2 1 435000000 436000000` |


### Incremental scope readback and PTT arbitration

SkyRoof's newer frequency/spectrum controller uses `U SCOPE_READ_FIELD <KEY>`
to read one physical CI-V scope register per CAT cycle. This prevents a
16-register bulk `U SCOPE_READ` from occupying the shared CAT command
lock for many consecutive round trips.

Supported `KEY` values are `SELECT`, `MAIN.MODE`, `MAIN.SPAN`,
`MAIN.EDGE`, `MAIN.REF`, `MAIN.SPEED`, `MAIN.VBW`, the corresponding
six `SUB.*` fields, `TX`, `CENTER`, and `MARKER`. A successful reply
is precisely `KEY=value`. An unsupported/timed-out field returns its normal
`RPRT` error and does not prevent SkyRoof from applying other confirmed
fields. Legacy `U SCOPE_READ` remains available but is a blocking query.

The main CAT port and the WSJT-X compatibility proxy now share a single
exclusive PTT lease. The client that asserted PTT is responsible for
releasing it. A competing `T 1` returns `RPRT -6`; a non-owner `T 0`
cannot unkey the active owner. If the ON reply is ambiguous, the server
tries an immediate OFF and protects the lease until OFF is confirmed.
Following an interrupted/disconnected COM session, it retries the
fail-safe release before admitting another owner.

`F` and `I` frequency writes accept nonnegative 64-bit Hertz values;
the selected radio's JSON command definition still sets the maximum
CI-V frequency field width. For example IC-905's five-byte BCD field
supports 2.4 GHz but is not sufficient for every 10 GHz setting.

### SkyRoof scope-control handshake

The spectrum waveform stream (TCP 4535) and the CAT command port (default 4532)
are separate endpoints. SkyRoof's `Scope control path = SkyCAT` requires the
normal SkyCAT CAT port, with `--model IC-9700` and an active serial CI-V
connection. Merely selecting a SkyCAT waveform source does not configure the
control path.

`U SCOPE_READ` queries the radio (not cached defaults) and returns a
semicolon-separated record like:

```text
SELECT=MAIN;MAIN.MODE=CENTER;MAIN.SPAN=25000;MAIN.EDGE=1;MAIN.REF=15.0;MAIN.SPEED=FAST;MAIN.VBW=WIDE;SUB.MODE=CENTER;SUB.SPAN=25000;SUB.EDGE=1;SUB.REF=15.0;SUB.SPEED=FAST;SUB.VBW=WIDE;TX=0;CENTER=FILTER;MARKER=FILTER
```

`U SCOPE_READ_EDGE 2 1` replies with `RANGE=2;EDGE=1;LOWER=435000000;UPPER=436000000` using the radio's stored values.
Invalid controls return `RPRT -1`, rejected CI-V writes `RPRT -9`,
and a disconnected CAT transport `RPRT -6`. Scope writes require confirmed
radio FB replies; a queued command is not evidence that the IC-9700 accepted it.
Do not rely on initial readback before explicitly checking SkyCAT version and
the serial CI-V link. The radio's 27 xx command availability may vary by firmware.

**frequency** is in Hertz.

**tone** is the CTCSS transmit tone in tenths of Hz. For example:

```text
C 670
```

selects 67.0 Hz.

For compatibility with rigctld clients, the following setup aliases are also recognized:

```text
U SATMODE 1
S 1 VFOB
S 0 VFOB
```

They map to Duplex, Split and Simplex operation respectively.

## WSJT-X Compatibility Proxy

This fork includes a restricted Hamlib NET rigctl compatibility endpoint intended specifically
for WSJT-X.

The proxy implements the Hamlib initialization queries `\chk_vfo` and `\dump_state`,
frequency/mode/PTT reads, and CAT PTT.

Radio-state writes such as frequency, mode, VFO, split, SAT mode, and CTCSS are acknowledged as
successful no-ops rather than being forwarded to the radio. This prevents WSJT-X/Hamlib from
entering a Radio Fault state while keeping SkyRoof as the single owner of tuning and Doppler
control.

When a WSJT-X client that asserted CAT PTT disconnects, SkyCAT attempts to release that
client-owned PTT as a safety measure.

The WSJT-X proxy is always loopback-only.

### WSJT-X settings

In **File -> Settings -> Radio**:

- **Rig:** Hamlib NET rigctl
- **Network Server:** `127.0.0.1:4534`
- **PTT Method:** CAT
- **Mode:** None
- **Split Operation:** None

SkyRoof should continue connecting directly to the normal SkyCAT CAT port, usually:

```text
127.0.0.1:4532
```

## IC-9700 Scope Stream

The scope stream is deliberately separated from the normal line-oriented CAT protocol.

Default endpoint:

```text
127.0.0.1:4535
```

SkyCAT extracts complete IC-9700 CI-V `27 00` waveform frames from the shared serial transport
and publishes them to connected scope clients. Slow clients use a bounded queue; older spectrum
frames may be dropped so scope traffic cannot block CAT commands.

The scope stream is intended for live display rather than lossless capture.

## Notes

- The main CAT server defaults to loopback-only for safety.
- `--allow-remote` does not expose the WSJT-X, scope or Switch listeners.
- The selected `--model` determines the available CAT commands and default serial speed.
- Command availability may differ between Duplex, Split, and Simplex operating modes.
- A connected program should not assume that every radio implements every command.


## IC-9700 Remote Control Switch integration

The [Remote Control Switch fork](https://github.com/Iu-yang1/IC-9700-Remote-Control-Switch)
supports an optional **SkyCAT TCP** transport. The default endpoint is
**`127.0.0.1:4537`**. This endpoint is available **only** when SkyCAT is
started with the `IC-9700` radio model; other models do not create it.

### Using RS-BA1, SkyRoof and Remote Control Switch together

1. Start **RS-BA1 Remote Utility** and connect to the radio via its usual LAN
   session. Keep the existing RS-BA1 virtual COM port. If SkyCAT is already
   running, do not start a second copy against that port.
2. Start SkyCAT using the existing COM assigned to it, for example:
   ```powershell
   skycatd.exe -m IC-9700 -r COM9 -s 115200
   ```
   SkyCAT owns this **one** CI-V serial connection and publishes the four
   separate localhost TCP listeners: 4532 / 4534 / 4535 / **4537**.
3. Connect **SkyRoof CAT** to `127.0.0.1:4532`. Its satellite frequency,
   mode, Doppler and PTT control remains on the normal SkyCAT interface.
   SkyRoof can independently use its configured RS-BA1 passive LAN spectrum
   source or the SkyCAT binary scope stream on 4535.
4. In **Remote Control Switch → Connection settings**, choose **SkyCAT TCP**,
   enter `127.0.0.1:4537`, save the profile and then click **Connect**.
   This application does **not** open the same virtual COM port.

The connection profile is saved by **Remote Control Switch**, not by SkyCAT.
Changing a radio setting sends a command immediately; saving the TCP address
does not write anything to the radio. The client can read current radio state
after connecting, so individual control readbacks may still take time.

Both applications ultimately share the **same CI-V serial link**. The
dedicated Switch listener shares SkyCAT's `CatServer.commandLock`, so requests
are serialized rather than concurrently stealing serial replies. Different TCP
ports provide client and command **isolation**, not independent radio bandwidth.
The Switch endpoint forbids SAT-mode writes, VFO selection, frequency/mode
changes, arbitrary CI-V frames and PTT; let SkyRoof own those operations.

### Dedicated Switch protocol (TCP 4537)

**Line-oriented ASCII**. Terminate each request with a newline (`\n`).
One response line is returned per request. It is **not** the binary scope
stream or Hamlib rigctl. The initial `PING` → `PONG` handshake does not
perform radio CI-V I/O, but can still wait briefly for the shared command lock.

```text
PING
PONG
GET DATA_OFF
VALUE 05
SET DATA_OFF 05
OK
```

| Setting | Supported requests | CI-V selector | Encoding |
|---|---|---|---|
| DATA OFF input | `GET DATA_OFF`, `SET DATA_OFF 05` | `1A 05 01 15` | 1 byte, `00..05` |
| DATA ON input | `GET DATA_MOD`, `SET DATA_MOD 03` | `1A 05 01 16` | 1 byte, `00..05` |
| USB AF/IF output | `GET USB_OUTPUT`, `SET USB_OUTPUT 01` | `1A 05 01 05` | `00` AF / `01` IF |
| Speech compressor | `GET COMP`, `SET COMP 01` | `16 44` | `00` off / `01` on |
| Compressor level | `GET COMP_LEVEL`, `SET COMP_LEVEL 0128` | `14 0E` | Two-byte BCD, `0000..0255` |
| CW keying speed | `GET KEY_SPEED`, `SET KEY_SPEED 0128` | `14 0C` | Two-byte BCD, `0000..0255` |
| RF power | `GET RF_POWER`, `SET RF_POWER 0128` | `14 0A` | Two-byte BCD, `0000..0255` |
| SAT mode | `GET SAT_MODE` | `16 5A` | Read-only, `00` / `01` |

For DATA OFF/DATA MOD: `00` MIC, `01` ACC, `02` MIC+ACC,
`03` USB, `04` MIC+USB and `05` LAN. `GET` succeeds with
`VALUE <HEX>` (uppercase hexadecimal **CI-V bytes**, not a human-readable
percentage). `SET` succeeds with `OK` only after a confirmed CI-V ACK;
otherwise the endpoint returns `ERR INVALID`, `ERR DISCONNECTED`,
`ERR TIMEOUT`, `ERR RADIO` or `ERR UNSUPPORTED`.

The CI-V selectors and BCD encoding follow the IC-9700 CI-V Reference Guide
and the existing Remote Control Switch implementation. SkyCAT deliberately
does **not** expose a raw CI-V passthrough through this auxiliary endpoint.

### Troubleshooting on Windows

If SkyCAT reports **`Switch TCP listener error`** with Windows socket
error **10048** ("only one usage of each socket address"), check which
process owns the listening port:

```powershell
Get-NetTCPConnection -LocalPort 4537 -State Listen -ErrorAction SilentlyContinue |
  Select-Object LocalAddress, LocalPort, OwningProcess,
    @{Name='Process';Expression={
      (Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue).ProcessName
    }}
```

The original default was **4536**; it was moved to **4537** after a reported
local `node.exe` conflict. Current Remote Control Switch builds automatically
migrate saved **default** localhost:4536 profiles, while retaining a user-chosen
custom port. If the port is still occupied, close the known conflicting service
or configure **both** SkyCAT and the client to use a different free port:

```powershell
skycatd.exe -m IC-9700 -r COM9 --switch-port 4547
```

Then set the client address to `127.0.0.1:4547`. The other ports 4532,
4534 and 4535 stay unchanged. **Do not terminate an unfamiliar process**
without checking what it runs. If the Switch client connects but reports
`ERR RADIO` or `ERR TIMEOUT`, the listener itself is working: check the
RS-BA1 virtual COM link and IC-9700 CI-V response separately. `PING/PONG`
only validates the Switch service, **not** radio connectivity.

**Note:** no additional spectrum diagnostic UI, background spectrum
instrumentation or raw-sweep logging is required for this integration.
