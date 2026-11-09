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
| step3 首页与指标展示 | 通过 | 289/289 | 184/184 | 14/14 | 通过 | 全部 0 |
| step4 无线管理与流量图例 | 通过 | 289/289 | 184/184 | 14/14 | 通过 | 全部 0 |
| step5 质量与诊断引导 | 通过 | 289/289 | 188/188 | 14/14 | 清理空白后通过 | 最终全部 0 |

保留已有 WFAC010 高 DPI 清单警告，未修改该无关配置；所有测试保留，未禁用或跳过。

第二步新增窄窗口详情用例先以缺少展开入口失败（退出码 1，`step2-red.log`）。首次实现出现 `双向绑定需要 Path 或 XPath`（退出码 1，`step2-focused.log` 完整堆栈），只读详情的 MultiBinding 改为 OneWay 后完整回归通过。完整原值、键盘可聚焦详情、复制菜单与无横向滚动均保留断言。

第三步的延迟边界、无效样本和成功率用例先出现预期失败（7 失败、5 通过，退出码 1，完整断言见 `step3-red.log`）；实现后全量通过。新增中文仅“近 7 天”“断网次数”“恢复成功率”及对应英文；原七天汇总原文保留。

第五步空状态引导用例先以缺少按钮失败（4 项，退出码 1，`step5-red.log`）；新增引导后全量通过。首次 `git diff --check` 报四处 `trailing whitespace`，输出见 `step5-diff.log`，统一清理行尾空白后复验通过（`step5-diff-final.log`，退出码 0）；未更改功能代码，不重复构建。

| step6 适配器与双协议地址 | 通过 | 289/289 | 191/191 | 14/14 | 通过 | 全部 0 |

第六步地址分行用例先有两项预期失败（退出码 1，step6-red.log），实现后完整回归通过。
