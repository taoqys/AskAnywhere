# AskAnywhere

一个轻量、常驻后台的 Windows AI 助手。双击 **Ctrl** 即可唤起聊天窗口：

- 有选中文字 → 自动填入，可用 **↑/↓** 选择操作（解释 / 回答 / 翻译 / 润色），**回车**发送
- 无选中文字 → 打开空的聊天窗口，直接输入提问（Ctrl+Enter 发送）
- 支持任意 **OpenAI 兼容** API（OpenAI / DeepSeek / Ollama / 各类中转服务）

技术栈：C# / WPF / .NET 8，原生窗口、无 WebView2，浅色主题，空闲内存低、唤起响应快。

## 功能

- ✅ 连按 Shift 全局唤起（触发键可选 Shift / Ctrl / Alt / 禁用，间隔 100–800ms 可调，自动排除组合键误触）
- ✅ 选中文本捕获（纯 UI Automation，不碰剪贴板、不模拟按键，未选词时零副作用）
- ✅ 没有选中文字时打开空的聊天窗口，不会误填剪贴板内容
- ✅ **失焦自动隐藏**：点击其他窗口时聊天窗口自动隐藏（可在设置中关闭）
- ✅ **重开即净**：每次重新打开窗口自动清空上一次的关键词
- ✅ **键盘优先操作**：选词后 ↑/↓ 切换操作模式，回车直接发送
- ✅ OpenAI 兼容接口：Base URL / API Key / Model / Temperature 全部可配置
- ✅ 流式输出（打字机效果），发送中可随时停止
- ✅ 多轮对话上下文
- ✅ 功能列表：回答问题 / 解释 / 翻译 / 润色，每项提示词可编辑，可自由添加自定义功能
- ✅ 推理模式（DeepSeek thinking）：可开关、可选推理强度，思考内容以灰色显示
- ✅ 自动获取模型列表（点击设置中的「获取模型列表」）
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
| 连按 Shift（默认） | 唤起/隐藏聊天窗口（可在设置中改为 Ctrl / Alt / 禁用，并调整间隔） |
| ↑ / ↓ | 选中文字后切换功能（回答问题 / 解释 / 翻译 / 润色 / 自定义…） |
| 回车 | 直接发送 |
| Shift+Enter | 输入框中换行 |
| Esc | 停止生成；再次按 Esc 隐藏窗口 |
| 托盘图标 | 左键双击唤起，右键打开菜单 |

> 快捷键触发说明：两次按下之间不得有其他按键（自动排除 Shift+A、Ctrl+C 等组合操作），
> 间隔默认 300ms，可在设置中调整（100–800ms）。

### 取词行为

- 使用 **UI Automation** 读取焦点应用的选中文本，不依赖剪贴板、不模拟按键
- 未选中任何文字时返回空，窗口以空状态打开，不会误填剪贴板里的旧内容

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
- [ ] 会话历史持久化
- [ ] 多显示器窗口定位优化
- [ ] 图片/截图发送
- [ ] 自定义组合键（除双击 Ctrl 外可选）
- [ ] 会话历史持久化
- [ ] 多显示器窗口定位优化
- [ ] 图片/截图发送
