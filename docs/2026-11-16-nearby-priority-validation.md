# 1.1.5 附近 Wi-Fi 恢复序号验证

日期：2026-11-16。

## 问题与变更

用户截图中，右侧附近恢复列表只有 4 条，显示全局保存配置的优先级 3、38、56、58；摘要仍统计全部 63 条。列表筛选范围与展示范围不一致。

附近恢复列表改用当前视图连续序号，显示 1、2、3、4。左侧附近网络的优先级徽标、选中网络提示和启用/总数摘要使用相同范围。拖动重排后及时刷新徽标和选中提示。禁用的附近网络仍占据其列表位置。扫描只更新视图，不改写全局配置顺序；用户上移、下移或拖动仍按既有方式调整全局保存顺序。

修改 NearbySavedWifiView 并增加 NearbyPriorityIndexConverter，使真实表格单元格从当前视图取编号；视图变动时刷新绑定，覆盖滚动行复用。同步 MainWindow 的徽标、提示和摘要，更新中英文说明。正式版本递增为 1.1.5。

## 验证

SDK：.dotnet-home/sdk/dotnet.exe；配置：Release。

- 红测试：dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj --no-restore --configuration Release --filter FullyQualifiedName~NearbyWifiPriorityDisplayTests --verbosity minimal。退出码 1，1 项失败，准确复现旧编号。
- 定向回归：同上，筛选 FullyQualifiedName~NearbyWifiPriorityDisplayTests|FullyQualifiedName~NearbySavedWifiViewTests。退出码 0，6/6 通过，跳过 0。
- 完整回归：dotnet test NetGuardian.sln --no-restore --configuration Release --verbosity minimal --logger "console;verbosity=normal" --blame-hang-timeout 90s --blame-hang-dump-type none --results-directory .dotnet-home/validation/nearby-priority/test-results。退出码 0，核心 311/311、桌面 426/426，共 737/737，跳过 0。
- 覆盖真实 WPF 表格 63 条全局配置筛选为 4 条、扫描范围缩小/恢复、上移重排、80 条配置滚动至第 56 行的容器复用、禁用位置和未保存网络无序号。
- 保留既有 WFAC010 高 DPI 配置警告；本次未修改 DPI 配置。

红测试完整错误（完整日志另存 .dotnet-home/validation/nearby-priority/red-tests.log）：

```text
VSTest 版本 17.11.1 (x64)

正在启动测试执行，请稍候...
总共 1 个测试文件与指定模式相匹配。
[xUnit.net 00:00:01.85]     NetGuardian.App.Tests.NearbyWifiPriorityDisplayTests.附近恢复列表显示连续序号且扫描和重排不会显示全局编号 [FAIL]
  失败 NetGuardian.App.Tests.NearbyWifiPriorityDisplayTests.附近恢复列表显示连续序号且扫描和重排不会显示全局编号 [1 s]
  错误消息:
   Assert.Equal() Failure: Collections differ
           ↓ (pos 0)
Expected: ["1", "2", "3", "4"]
Actual:   ["3", "38", "56", "58"]
           ↑ (pos 0)
  堆栈跟踪:
     at NetGuardian.App.Tests.NearbyWifiPriorityDisplayTests.<>c.<附近恢复列表显示连续序号且扫描和重排不会显示全局编号>b__0_0(Window window) in C:\Users\Qiu\Documents\Net\tests\NetGuardian.App.Tests\NearbyWifiPriorityDisplayTests.cs:line 25
   at NetGuardian.App.Tests.DesktopFixture.<>c__DisplayClass0_0.<Run>b__0() in C:\Users\Qiu\Documents\Net\tests\NetGuardian.App.Tests\DesktopLayoutTests.cs:line 486
--- End of stack trace from previous location ---
   at NetGuardian.App.Tests.DesktopFixture.Run(Action`1 action, ApplicationTheme theme) in C:\Users\Qiu\Documents\Net\tests\NetGuardian.App.Tests\DesktopLayoutTests.cs:line 499
   at NetGuardian.App.Tests.NearbyWifiPriorityDisplayTests.附近恢复列表显示连续序号且扫描和重排不会显示全局编号() in C:\Users\Qiu\Documents\Net\tests\NetGuardian.App.Tests\NearbyWifiPriorityDisplayTests.cs:line 15
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

失败!  - 失败:     1，通过:     0，已跳过:     0，总计:     1，持续时间: < 1 ms - NetGuardian.App.Tests.dll (net8.0)
```

完整测试日志：.dotnet-home/validation/nearby-priority/full-tests.log；定向日志：green-tests.log。中间试验的容器编号未能通过滚动复用测试，已改为当前视图绑定；首次接口订阅编译错误 CS0122 已使用 INotifyCollectionChanged 修正，最终测试覆盖该方案。

## 构建、发布与本机部署

本记录提交时，独立提交源码构建、GitHub 正式发布和本机部署尚待执行；成功后补充实际结果。仅替换界面产品组件及其使用的 Core 库，不重启后台守护。部署前按 AGENTS.md 请用户手动托盘退出，备份现有文件和配置，核对两个启动目录文件摘要，正常重新打开界面并核验实际进程路径。

另一台电脑的真实安装与列表显示仍需用户实际验收。
