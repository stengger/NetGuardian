# 公网 IP 缺失坐标验证

- `PublicIpInfoService.cs`：DTO 与结果经纬度使用 nullable；仅提供地址的回退返回 null，不把缺失数据写成 0,0。可选注入 HttpClient 用于隔离测试；默认仍使用原共享客户端，现有 DI 与 10 分钟缓存行为保留。
- `PublicIpPresentation.cs`、`MainWindow.Diagnostics.cs`、`Localizer.cs`：缺失、不完整、非有限值或越界坐标显示本地化未知；真实零坐标有效，标签和显示统一按纬度、经度顺序。
- `PublicIpInfoTests.cs`：假 HTTP 处理器覆盖仅地址回退、主提供方缺失字段和真实零坐标，不访问真实服务。`PublicIpPresentationTests.cs`：缺失、NaN、越界、零坐标和顺序。

沿用项目 SDK 与工作区 TEMP；公共参数 `--no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`。

| 命令 | 结果 | 退出码 |
|---|---|---|
| `dotnet test NetGuardian.sln` 加公共参数 | Core 206/206、WPF 72/72，无跳过 | 0 |
| `dotnet build NetGuardian.sln` 加公共参数 | 0 警告、0 错误 | 0 |

完整日志位于忽略目录 `.dotnet-home/validation/product-ui/ip-coordinates-tests.log`、`ip-coordinates-build.log`。测试初次因原服务不支持注入客户端报 CS1729，退出 1，完整输出保留 `red-ip-coordinates.log`。独立只读复核未发现新增明确 P1/P2。

没有执行真实公网查询；第三方坐标精度与真实性不在本地校验能力范围内。全应用实际运行与部署尚未验收，不能据此宣称完整产品目标完成。
