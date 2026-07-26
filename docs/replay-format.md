# 回放格式与校验

`.nrr` 是严格、逐行、便于 diff 的文本格式，没有第三方序列化依赖。

简化示例：

```text
NEBULA_RAID_REPLAY|1
BATTLE|30|12648430|20000|2000
ACTORS|2
ACTOR|1|-8000|0|120|180|18|2200|6
ACTOR|2|8000|0|120|180|18|2200|6
FRAMES|1
FRAME|0|0123456789ABCDEF|2
CMD|0|1|0|1
CMD|1|-1|0|1
END
```

`FRAME` 中保存的是执行该 tick 后的 checksum。验证器从 battle definition 重建全新 world，逐 tick 应用命令并在每帧比较；结果包含首个 mismatch tick、期望值和实际值。

校验和采用 64-bit FNV-1a，按固定小端字节加入 tick、battle metadata 与所有 actor 权威字段。它用于快速发现状态漂移，不是密码学 MAC，不能防止有能力同时修改回放内容和 checksum 的攻击者。

解析器限制 actor、frame 和 command 数量，要求命令严格升序且记录结构/字段数量完全匹配。格式版本变化应提升 header 版本，并提供显式迁移器，而不是猜测字段。

测试覆盖：

- 同输入的两个 world 连续 240 tick checksum 相同；
- 固定 demo 场景在第 88 tick 得到 golden checksum `0x9B6F5D132EEE944F`；
- serialize → parse → replay 全帧通过；
- 翻转某帧 checksum 的 1 bit，验证器定位到该 tick。

