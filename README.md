# AskAnywhere

一个轻量、常驻后台的 Windows AI 助手。双击 **Ctrl** 即可唤起聊天窗口：

- 有选中文字 → 自动填入并发送（可在设置中改为仅填充）
- 无选中文字 → 打开空的聊天窗口
- 支持任意 **OpenAI 兼容** API（OpenAI / DeepSeek / Ollama / 各类中转服务）

技术栈：C# / WPF / .NET 8，原生窗口、无 WebView2，空闲内存低、唤起响应快。

## 功能

- ✅ 双击 Ctrl 全局唤起（300ms 阈值可调，自动排除 Ctrl+C / Ctrl+V 等组合键误触）
- ✅ 选中文本捕获（UI Automation 优先，Ctrl+C 剪贴板兜底，自动恢复剪贴板）
- ✅ OpenAI 兼容接口：Base URL / API Key / Model / Temperature 全部可配置
- ✅ 流式输出（打字机效果），发送中可随时停止
- ✅ 多轮对话上下文
- ✅ 模式：回答问题 / 解释 / 翻译 / 润色 / 自定义 Prompt
- ✅ 系统托盘常驻（双击图标 / 右键菜单）
- ✅ 单实例运行（重复启动会唤起已有窗口）
- ✅ API Key 使用 Windows DPAPI 加密存储
- ✅ 开机自启（设置中开关）

## 使用

### 下载

1. 打开仓库的 **Actions** 页面
2. 选择最新的 **build** 工作流运行
3. 在运行详情底部下载 **AskAnywhere-win-x64** 压缩包
4. 解压后运行 `AskAnywhere.exe`（单文件，无需安装 .NET 运行时）

### 快捷键

| 操作 | 说明 |
|---|---|
| 双击 Ctrl | 唤起/隐藏聊天窗口 |
| Ctrl+Enter | 发送消息 |
| Esc | 停止生成；再次按 Esc 隐藏窗口 |
| 托盘图标 | 左键双击唤起，右键打开菜单 |

## 构建

本地构建（需要 .NET 8 SDK）：

```powershell
dotnet publish src/AskAnywhere/AskAnywhere.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -o publish
```

GitHub Actions 会在每次 push 到 `main` 时自动构建并上传产物。

## 配置

配置文件位于 `%APPDATA%\AskAnywhere\settings.json`，建议通过托盘菜单的「设置」修改。

常用 API Base URL：

| 服务 | Base URL |
|---|---|
| OpenAI | `https://api.openai.com/v1` |
| DeepSeek | `https://api.deepseek.com/v1` |
| Ollama（本地） | `http://127.0.0.1:11434/v1` |

## 路线图

- [ ] Markdown 渲染（代码高亮）
- [ ] 自定义组合键（除双击 Ctrl 外可选）
- [ ] 会话历史持久化
- [ ] 多显示器窗口定位优化
- [ ] 深色/浅色主题切换
- [ ] 图片/截图发送
