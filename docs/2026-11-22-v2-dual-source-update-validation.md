# NetGuardian v2 双源更新客户端交接与验证

2026-11-22 · 设计第 5 部分 · 本地客户端验证

## 构建和接线

设置页已通过 `MainWindow.Updates.cs` 接入。开发构建保留 v1.2 GitHub 检查、下载和缓存入口，设置页提示「当前使用 GitHub，备用源尚未启用」。正式信任信息一旦内置，工厂只创建签名客户端。来源选择、线路状态、重试按钮和历史结果使用现有更新卡，文案及附加控件位于 `Updates/SignedUpdateControls.cs`。主线程无需修改共享 XAML 或 Localizer 来接线。

维护者在 Core 的 `Updates/SignedManifest.cs` 中填写 `OfficialUpdateFeed.Trust`，随受信任客户端构建交付。所需输入为 ECDSA P-256 公钥 DER SubjectPublicKeyInfo、两源各自的 HTTPS 稳定清单地址与以 `/` 结尾的安装包根目录。主源需要 CDN 重定向时，另外内置经过确认的 HTTPS 源列表。备用源的清单、安装包和跳转均限定同源。当前正式地址、公钥留空。用户设置、环境变量、注册表和远端清单均不能替换信任根。

自动模式依次尝试主源系统线路、备用源系统线路、获准的备用源直连。发现阶段主源预算 5 秒。允许直连时，备用系统线路和直连分别预算 2 秒和 3 秒，总检查截止时间为 12 秒。下载复用发现成功的线路，每线路下载截止时间 30 分钟。Windows 管理员可设置 64 位 `HKLM\SOFTWARE\Policies\NetGuardian\Updates` 的 `ProxyOnly` 为 `DWORD 1`，仅允许系统线路。值缺省或 `DWORD 0` 允许备用源自动直连。无法读取策略或值类型异常时禁用直连。

## 分发格式

两源返回相同的 UTF-8 清单载荷字节。外层 JSON 含 `payload` 和 `signature` 两个 Base64 字符串。签名覆盖解码后的原始载荷字节，使用 ECDSA P-256、SHA-256 和 IEEE P1363 的 64 字节 `r || s` 格式。发布端可用 .NET 的 `ECDsa.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)`。私钥由维护者保管。

| 载荷字段 | 约定 |
| --- | --- |
| `schema`、`channel`、`arch` | 数字 `1`、`stable`、`win-x64` |
| `sequence`、`expiresUtc` | 递增正整数、带 `Z` 或 `+00:00` 的 UTC 到期时间 |
| `version`、`file` | 三段规范版本号，文件名为 `NetGuardian-{version}-win-x64-Setup.exe` |
| `length`、`sha256` | 1 至 1 GiB 的字节数、64 位十六进制整包摘要 |
| `notes` | 随清单签名的发布说明，可直接包含中文 |

安装包相对路径为 `v{version}/{file}`。发布时先上传两份不可变安装包，从两源回读核对长度和摘要，再切换清单。同序号必须保持完全相同的载荷字节。续期使用更高序号，同一版本仍绑定原长度和摘要。

已见序号、载荷身份、包身份、上次成功检查时间和线路写入现有受保护更新根目录的 `signed-release.json`。检查、下载完成与确认安装时复核序号及有效期。文件锁和同目录原子替换保护多个客户端之间的比较与写入。可信时间依赖本机时钟，已记录的较晚成功检查时间也参与过期判断。跨源复用分片前须核对对端签名载荷身份。包缓存绑定规范文件名、签名长度和 SHA-256，最终整包校验通过后进入待安装状态。

## 文件分工

- Core `Updates/UpdateClient.cs` 决定构建采用的协议，`SignedUpdateService.cs` 管理检查、下载及换源，`SignedManifest.cs` 验证签名和字段，`UpdateRouteHandler.cs` 检查每次跳转与直连授权，`UpdateSequenceStore.cs` 持久化序号。
- Core `Updates/GitHubUpdateService.cs` 复用现有四路分片、受保护缓存和单连接下载。`UpdateRelease.cs` 支持直接使用签名清单中的整包摘要。
- App `MainWindow.Updates.cs`、`Updates/UpdateSession.cs`、`UpdateStorage.cs`、`SignedUpdateControls.cs`、`UpdateProxyPolicy.cs` 连接实际设置入口、安装前复核、ACL、线路显示和机器策略。
- `SignedDualSourceTests.cs`、`UpdateLoopbackServer.cs` 与 `SignedUpdateLayoutTests.cs` 覆盖受控 TLS 双源、构建协议选择和 WPF 更新状态。

## 验证记录

在基线 `f30a2685a71a1580c8d51bab394f17251775447d` 的独立副本上覆盖本任务源文件。更新模块、窗口入口及新增测试共 21 个文件已逐一对比工作区 SHA-256。按要求使用 `require_escalated` 运行本机回环 TLS 和 WPF 测试。回环服务使用临时证书，客户端仅对本次测试信任该证书。

```powershell
$sdk = 'E:\Environment\dotnet-sdk-8.0\dotnet.exe'
$solution = '.dotnet-home\v2-signed-qa\NetGuardian.sln'
& $sdk build $solution --no-restore --nologo
& $sdk test $solution --no-build --no-restore --filter 'FullyQualifiedName~Update|FullyQualifiedName~SignedDualSourceTests' --logger 'console;verbosity=normal' --nologo
```

| 项目 | 结果 |
| --- | --- |
| 解决方案构建 | 退出码 0，0 个错误，2 条 WFAC010 高 DPI 清单警告 |
| Core 更新回归及本地双源集成 | 61 项通过 |
| App 更新会话、安装交接及 WPF 状态 | 25 项通过 |
| 合计 | 86 项通过，0 失败，0 跳过，测试退出码 0 |

最终逐用例日志在 `.dotnet-home/v2-dual-source-final.log`，SHA-256 为 `3DC1B8903C2B4948376F27BFB732652FDCAA8D219EB4365033429CC43384F489`。

- 开发构建用例记录到一次旧版 GitHub 请求。签名错误、过期及全部线路离线三个场景中，旧协议客户端构造次数和请求数均为零。
- 本地 TLS 服务验证了系统代理失效、企业禁止直连、跨源断点、无 Range 时回到整包、备用源暂未同步、跨域跳转、同名换包、过期缓存及跨实例序号回退。8 MiB 以上的安装包在主源完成三个区段，备用源只补首段剩余字节，整包摘要通过。
- 早期沙箱回环运行日志为 `.dotnet-home/v2-signed-tls.log`，退出码 1。Windows TLS 测试私钥的 Schannel 错误及堆栈在 `.dotnet-home/v2-signed-tls-unsandboxed.log`，退出码 1。测试服务改用本机可引用的临时证书私钥后复验通过。早期 WPF 夹具未执行呈现逻辑的断言及堆栈在 `.dotnet-home/v2-both-updates.log`，退出码 1，最终夹具调用与设置页相同的呈现方法。

正式基础设施就绪后，A4 与 A7 仍需在实验室无代理网络验证公开双源可达、首包独立摘要、旧版实际安装、重启和守护任务交接。此次使用本地测试载荷，范围截止于验证客户端与既有安装入口之间的交接。提交、发布和部署由主线程统一处理。

### 离线生成签名清单

使用 PowerShell 7.4 或更新版本。向 `scripts/new-signed-update-manifest.ps1` 提供实际安装包、递增的发行序号、到期时间、中文发行说明、输出路径，以及从维护者证书或密钥提供程序取得的 `ECDsa` 私钥句柄。脚本按安装包实际字节计算大小和摘要，并检查文件名与产品版本。将同一份输出清单分发到两处。

```powershell
& ./scripts/new-signed-update-manifest.ps1 -Installer $installer -Sequence $sequence -ExpiresUtc $expires -Notes $notes -SigningKey $releaseKey -Output $newManifest
```

执行 `./tests/Test-SignedUpdateManifest.ps1` 可用临时密钥验证脚本与 Release 构建的客户端验签实现兼容。测试还核对整包摘要以及已有输出文件的防覆盖行为。