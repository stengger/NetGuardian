# NetGuardian 界面优化验证记录

方案已经用户确认，实施分支 `codex/fluent-ui-polish`。完整输出和位图保存在本机忽略目录 `.dotnet-home/validation/ui-fluent/`，不提交构建产物或测试截图。

## 基线

`dotnet test NetGuardian.sln --no-restore --configuration Release --verbosity minimal` 在受限桌面执行：Core 289 通过；App 169 通过、1 失败，退出码 1。失败原文：

```text
NetGuardian.App.Tests.TrayContextMenuTests.隐藏窗口时真实弹出菜单可关闭并重复打开
Assert.False() Failure
Expected: False
Actual: True
at TrayContextMenuTests.cs:line 120
```

完整堆栈见 `baseline.log`。保留原测试，在正常桌面权限下使用同一未修改基线二进制复验：`dotnet test NetGuardian.sln --no-build --no-restore --configuration Release --verbosity minimal`，Core 289/289、App 170/170，0 跳过、退出码 0，见 `baseline-desktop.log`。前图由现有桌面测试写入 `before/`，未启动监控或执行网络操作。

## 逐步检查

每步命令：`scripts/build.ps1`（还原和 Debug 构建）、`dotnet test NetGuardian.sln --no-restore --configuration Release --verbosity minimal`、`python scripts/check-contrast.py`、`git diff --check`。SDK 为仓库 `.dotnet-home/sdk/dotnet.exe` 8.0.426；完整输出为 `<步骤>-build.log`、`<步骤>-test.log`、`<步骤>-contrast.log`、`<步骤>-diff.log`，退出码为 `<步骤>-result.json`。

| 步骤 | 构建 | Core | App | 配色 | 差异检查 | 退出码 |
| --- | --- | --- | --- | --- | --- | --- |
| step1 设计令牌和公共控件 | 通过 | 289/289 | 170/170 | 14/14 | 通过 | 全部 0 |
| step2 表格详情与自适应 | 通过 | 289/289 | 172/172 | 14/14 | 通过 | 全部 0 |

保留已有 WFAC010 高 DPI 清单警告，未修改该无关配置；所有测试保留，未禁用或跳过。

第二步新增窄窗口详情用例先以缺少展开入口失败（退出码 1，`step2-red.log`）。首次实现出现 `双向绑定需要 Path 或 XPath`（退出码 1，`step2-focused.log` 完整堆栈），只读详情的 MultiBinding 改为 OneWay 后完整回归通过。完整原值、键盘可聚焦详情、复制菜单与无横向滚动均保留断言。
