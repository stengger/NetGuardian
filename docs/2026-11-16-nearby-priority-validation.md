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

构建来源：已验证提交 b8c04078050ae542b541b538ea0e8c1e946a97b8 的 git archive 独立源码，不混入工作区改动。

- 命令：source/scripts/build-installer.ps1 -Version 1.1.5 -OutputDirectory dist/releases。退出码 0，安装、守护和更新 PowerShell 脚本检查通过；独立源码核心 311/311、桌面 426/426，共 737/737 再次通过，跳过 0；完成 win-x64 自包含界面与守护发布和 Inno Setup 安装包编译。
- 安装包：NetGuardian-1.1.5-win-x64-Setup.exe，77,468,609 字节；产品版本去除 Inno 末尾空格后为 1.1.5。四个产品程序集均为 1.1.5+b8c04078050ae542b541b538ea0e8c1e946a97b8。
- SHA-256：09079b362ca3e36f5fab17d29402ba2c2dad3a8281bbc98d16a1b9881ecdcde9，校验附件一致。
- 本机构建：dotnet publish source/src/NetGuardian.App/NetGuardian.App.csproj --configuration Release --runtime win-x64 --self-contained false --no-restore --output ui-package。退出码 0；界面与其 Core 程序集版本、七个产品文件摘要通过。
- GitHub 正式版：v1.1.5，https://github.com/stengger/NetGuardian/releases/tag/v1.1.5 。标签指向 b8c04078050ae542b541b538ea0e8c1e946a97b8；匿名 latest 已确认。GitHub 两个附件均为 uploaded，大小及摘要与本地一致。软件生产更新服务匿名查询 1.1.4 成功发现 1.1.5，完整下载 77,468,609 字节，SHA-256 与构建一致；当前 1.1.5 查询无更新，验证退出码 0。结果：.dotnet-home/validation/nearby-priority/public-release-verification.json。
- 本机预检：两个安装目录当前为 1.1.4，框架依赖与新包一致；七个产品组件已校验。界面 PID 75596 来自 C:\Program Files\NetGuardian\NetGuardian.exe；后台 PID 77480 来自该目录 guardian，Healthy 心跳新鲜，两项任务启用。

本机文件替换尚待用户本次手动从托盘退出。构建和公开下载验证均已完成，最后一次检查时工作区另有 EventLogStore、PersistedEventLogReader 及其测试的日志读取改动；未纳入本次 b8c0407 独立构建、提交和发布。仅替换界面产品组件及其使用的 Core 库，不重启后台守护；部署前备份配置和现有文件，正常重新打开界面，核验两个目录摘要、实际界面启动路径、后台身份和任务启用状态。

构建首次在沙箱中恢复 NuGet 时退出 1，原因是无权读取用户 NuGet.Config；正常权限重跑通过。完整失败输出保留于 .dotnet-home/validation/nearby-priority/build-sandbox-failure.log，错误如下：

另一台电脑的真实安装与列表显示仍需用户实际验收。


```text
C:\Users\Qiu\Documents\Net\.dotnet-home\sdk\sdk\8.0.426\NuGet.targets(174,5): error : 由于未授权访问权限，因此未能读取 NuGet.Config。路径:“C:\Users\Qiu\AppData\Roaming\NuGet\NuGet.Config”。 [C:\Users\Qiu\Documents\Net\.dotnet-home\validation\nearby-priority\source\NetGuardian.sln]
C:\Users\Qiu\Documents\Net\.dotnet-home\sdk\sdk\8.0.426\NuGet.targets(174,5): error :   Access to the path 'C:\Users\Qiu\AppData\Roaming\NuGet\NuGet.Config' is denied. [C:\Users\Qiu\Documents\Net\.dotnet-home\validation\nearby-priority\source\NetGuardian.sln]
C:\Users\Qiu\Documents\Net\.dotnet-home\sdk\sdk\8.0.426\NuGet.targets(174,5): error :   由于未授权访问权限，因此未能读取 NuGet.Config。路径:“C:\Users\Qiu\AppData\Roaming\NuGet\NuGet.Config”。 [C:\Users\Qiu\Documents\Net\.dotnet-home\validation\nearby-priority\source\NetGuardian.sln]
C:\Users\Qiu\Documents\Net\.dotnet-home\sdk\sdk\8.0.426\NuGet.targets(174,5): error :   Access to the path 'C:\Users\Qiu\AppData\Roaming\NuGet\NuGet.Config' is denied. [C:\Users\Qiu\Documents\Net\.dotnet-home\validation\nearby-priority\source\NetGuardian.sln]
C:\Users\Qiu\Documents\Net\.dotnet-home\sdk\sdk\8.0.426\NuGet.targets(174,5): error :   由于未授权访问权限，因此未能读取 NuGet.Config。路径:“C:\Users\Qiu\AppData\Roaming\NuGet\NuGet.Config”。 [C:\Users\Qiu\Documents\Net\.dotnet-home\validation\nearby-priority\source\NetGuardian.sln]
C:\Users\Qiu\Documents\Net\.dotnet-home\sdk\sdk\8.0.426\NuGet.targets(174,5): error :   Access to the path 'C:\Users\Qiu\AppData\Roaming\NuGet\NuGet.Config' is denied. [C:\Users\Qiu\Documents\Net\.dotnet-home\validation\nearby-priority\source\NetGuardian.sln]
C:\Users\Qiu\Documents\Net\.dotnet-home\sdk\sdk\8.0.426\NuGet.targets(174,5): error :   由于未授权访问权限，因此未能读取 NuGet.Config。路径:“C:\Users\Qiu\AppData\Roaming\NuGet\NuGet.Config”。 [C:\Users\Qiu\Documents\Net\.dotnet-home\validation\nearby-priority\source\NetGuardian.sln]
C:\Users\Qiu\Documents\Net\.dotnet-home\sdk\sdk\8.0.426\NuGet.targets(174,5): error :   Access to the path 'C:\Users\Qiu\AppData\Roaming\NuGet\NuGet.Config' is denied. [C:\Users\Qiu\Documents\Net\.dotnet-home\validation\nearby-priority\source\NetGuardian.sln]
C:\Users\Qiu\Documents\Net\.dotnet-home\sdk\sdk\8.0.426\NuGet.targets(174,5): error :   由于未授权访问权限，因此未能读取 NuGet.Config。路径:“C:\Users\Qiu\AppData\Roaming\NuGet\NuGet.Config”。 [C:\Users\Qiu\Documents\Net\.dotnet-home\validation\nearby-priority\source\NetGuardian.sln]
C:\Users\Qiu\Documents\Net\.dotnet-home\sdk\sdk\8.0.426\NuGet.targets(174,5): error :   Access to the path 'C:\Users\Qiu\AppData\Roaming\NuGet\NuGet.Config' is denied. [C:\Users\Qiu\Documents\Net\.dotnet-home\validation\nearby-priority\source\NetGuardian.sln]
```
