---
title: TX-5DR 兼容说明
parent: 中文文档
---

# TX-5DR 与 SkyRoof 内嵌 SkyCAT

以下端口不是可互换的通用 Hamlib NET rigctl：

- 4532：SkyCAT 主 CAT 命令服务，不是完整的通用 NET rigctl。
- 4534：WSJT-X 受限代理。频率、模式、VFO、Split、CTCSS/DCS 写入会返回成功，但**不执行硬件更改**；可读取频率/模式/PTT，并使用共享租约控制 CAT PTT。
- 4535：IC-9700 二进制频谱数据。
- 4537：Remote Control Switch 辅助设置，不控制 PTT/VFO。
- 4538：独立 CW Keyer 服务，不是 rigctl。

SkyRoof 承载内嵌 SkyCAT 时，应独占 CI-V 串口并负责卫星多普勒、频率和模式。TX-5DR 的 Hamlib 独立进程与 #97 的 TONE/CSQL 修复，不会改变 4534 的写入屏蔽。不能将 RPRT 0 作为硬件成功调频的证据，也不能假定 4532 兼容全部 Hamlib 协议。

可使用 [SkyRoof 只读诊断脚本](https://github.com/Iu-yang1/SkyRoof-Plus/blob/master/scripts/Probe-Tx5drSkycat.ps1) 核实 4534 的代理身份、真实频率和模式；它不会发射或写入频率。若需让 TX-5DR 自行控制电台，必须先关闭 SkyRoof 对同一电台的 CAT 控制，选用经验证的完整连接；禁止两个进程同时打开相同 COM 口。

参见 [skycatd 命令文档](skycatd.md) 和 [SkyRoof 中文指南](https://github.com/Iu-yang1/SkyRoof-Plus/blob/master/docs/zh-cn/fork-guide/tx5dr-skycat.md)。
