---
title: TX-5DR interoperability
parent: SkyCAT
---

# TX-5DR with SkyRoof embedded SkyCAT

SkyCAT exposes several different services. They must not be treated as interchangeable Hamlib NET rigctl servers:

- 4532: main SkyCAT CAT commands, not a complete generic NET rigctl API.
- 4534: intentionally restricted WSJT-X NET rigctl proxy. Frequency/mode/split/VFO/CTCSS/DCS writes report success yet DO NOT change the radio. Frequency/mode/PTT reads and CAT PTT use the shared PTT lease.
- 4535: IC-9700 binary scope stream.
- 4537: Remote Control Switch auxiliary-only endpoint.
- 4538: leased CW keyer. It is not NET rigctl.

SkyRoof is the single frequency, mode and Doppler owner while hosting SkyCAT and its CI-V serial device. TX-5DR's independent Hamlib worker process and native TONE/CSQL improvements do not change the 4534 proxy's write restrictions. Do not configure independent TX-5DR CAT tuning against 4534: an RPRT 0 write response is NOT hardware confirmation. A generic Hamlib connection to 4532 is likewise not currently certified.

Use the [read-only SkyRoof identity probe](https://github.com/Iu-yang1/SkyRoof-Plus/blob/master/scripts/Probe-Tx5drSkycat.ps1) to verify that 4534 is the expected proxy and that actual receive frequency/mode can be read. It never sends TX/PTT, frequency or tone writes. When independently controlling the rig from TX-5DR, first disable SkyRoof CAT ownership and use a separately validated full rig control connection. Do not open the same COM port from two processes.

Refer to [skycatd command reference](skycatd.md) and [SkyRoof coexistence guide](https://github.com/Iu-yang1/SkyRoof-Plus/blob/master/docs/fork-guide/tx5dr-skycat.md).
