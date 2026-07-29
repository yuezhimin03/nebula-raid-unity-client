# 性能实验

## 目的

微基准用于观察纯 C# `Step` 的数量级和 steady-state managed allocation，不用于推导 Unity 真机帧率。

默认场景：

- 1,024 actors，512 对敌人在独立网格区域；
- 30 Hz 逻辑配置，每 actor 每 tick 请求一次攻击；
- 整数移动、uniform-grid 查询、同时伤害结算；
- 20 tick 预热，使 JIT 和空间 bucket 完成初始化；
- command arrays 全部在计时前构建；
- 预热后执行 full GC，再记录当前线程分配和 Gen0 次数；
- measured 300 ticks。

命令：

```powershell
dotnet run --project benchmarks/NebulaRaid.Benchmarks/NebulaRaid.Benchmarks.csproj -c Release -- --entities 1024 --ticks 300
```

## 2026-07-26 本机结果

环境：Windows 10.0.19045、.NET runtime 8.0.22；CPU 字符串由系统报告为 `Intel64 Family 6 Model 69 Stepping 1, GenuineIntel`。

```text
elapsedMs=493.716
ticksPerSecond=607.6
actorStepsPerSecond=622220
measuredThreadAllocBytes=40
gen0Collections=0
attacks=327680
checksum=0x4CE7C786C41AA34A
```

这只是一次样本，不给出统计置信区间。机器负载、runtime、CPU 频率和安全软件都可能影响结果。`40 bytes` 是计时线程在该区间的 API 观测值，不代表 Unity Player 或所有功能路径零分配；基准没有订阅事件 handler，也不包含 demo bot 的命令生成。

## Unity 中应怎样测

1. Development Player 与 Release/IL2CPP Player 分开测，Editor 只用于定位。
2. 使用 Unity Profiler、ProfilerRecorder 记录 Main Thread、GC.Alloc、scripts 和 rendering。
3. 固定设备温度、电量模式、分辨率与目标帧率，记录 P50/P95/P99 frame time。
4. 把逻辑 Step、输入采样、表现事件消费、资源加载和渲染拆成 marker。
5. 用与线上相同的 actor 数、技能密度、资源包和网络条件，至少采集多轮。

CI 只跑较小的 benchmark smoke test，目的是确认入口可运行，不设置易受共享 runner 波动影响的硬性能门槛。

## 与 C++17 原生微基准的关系

`native/benchmarks/native_benchmark.cpp` 是另一条独立口径：它只测 C ABI 后面的 C++ `nebula_world_step`，使用不同场景规模，不可与本页 C# 数字直接做倍数对比。原生基准同样不代表 Unity Player FPS；P/Invoke 封送、Unity 主线程、渲染和真机温控都不在计时范围。复现命令、当前样本和边界见 [native-interop.md](native-interop.md)。
