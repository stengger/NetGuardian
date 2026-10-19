using Xunit;

namespace NetGuardian.App.Tests;

// WindowChrome 使用进程级 WPF 属性描述器，真实窗口测试不能并发创建 HWND。
[CollectionDefinition("WPF 桌面", DisableParallelization = true)]
public sealed class DesktopTestCollection;
