# 2026-10-20 日志紧凑布局与操作区方向交接（未完成）

用户最新明确要求：所有页面的本页功能按钮统一放内容框外、页面标题下方。此前将 Wi-Fi 操作纳入框内的方向不符合预期，后续必须修正并跨页面一致，不能继续把按钮向框内统一。用户已确认中文字体正常；8034277 的质量页缩小后阅读、表格边界滚轮和取消操作均可用，但按钮位置仍需移出。

当前已安装8034277，交接记录011d62c；本轮没有更新安装文件、没有Git提交。用户原有docs/2026-10-18-validation.md改动不得修改或暂存。

未提交MainWindow.xaml、DesktopLayoutTests.cs为日志小窗口修复：原960×500中英文展开详情后LogGrid高度0DIP；将搜索/筛选/导出固定，记录、详情和跟随状态放入LogBodyScrollViewer，正文有限高度跟随ViewportHeight、MinHeight400，复用ScrollWheelChaining。针对性4/4、全量Core206/WPF142（348项）通过，退出0；完整日志分别为.dotnet-home/validation/product-ui/log-small-workarea-diagnostic.log（原2失败2通过，退出1）、log-small-workarea-green.log和log-small-workarea-all-tests.log。已查看logs-expanded-960-500-en-US.png；这是离屏真实XAML与模拟日志，且已滚到详情，不是正式业务验收或初始布局。

独立审查发现尚未修复的P2：外层ScrollWheelChaining的PreviewMouseWheel先标Handled=true，LogGrid普通XAML注册的LogReading_MouseWheel不接收已处理事件，向上阅读停止跟随会失效；开启Follow、少量记录无内部可滚空间、展开详情并滚动正文后，指向表格向上滚会转交正文却保持Follow，新记录可能打断阅读。现有布局用例先将Follow=false，且夹具删除业务事件，故不能覆盖此回归，不能仅据348项通过提交或安装。

下一步：已补边界转交后Follow从true变false的红灯用例（`log-follow-handled-red.log`，向上1失败、其余2通过），并将向上滚轮观察移入LogReadingBehavior.Attach，使用AddHandler(Mouse.PreviewMouseWheelEvent, handler, handledEventsToo:true)，移除重复XAML与旧MainWindow.Log.cs处理路径。修复后全量 Core206/WPF144、构建2条既有WFAC010且0错误，日志 `log-follow-handled-final-tests.log` 与 `log-follow-handled-build.log`；尚未提交本单元。随后优先执行用户确认的框外操作区统一，准备更新包和互斥/备份/哈希安装校验。不要用操作区方向变更来跳过日志回归。

无活动构建或测试会话。普通额度已接近耗尽，额度等待自动化netguardian已更新为ACTIVE，恢复后先核对普通额度和工作区再续接；不使用credits或重置券，不因阶段通过标完整目标完成。
