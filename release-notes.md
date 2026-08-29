# AskAnywhere v0.4.1

轻量、常驻后台的 Windows AI 助手：连按 Shift 唤起聊天窗口，自动捕获选中文字，支持任意 OpenAI 兼容 API（DeepSeek / OpenAI / Ollama 等）。

## v0.4.1 更新

- **修复流式回答跨线程异常**：后台流式任务不再直接读取 UI 线程上的「推理」开关，而是在 UI 线程先取值，避免出现「调用线程无法访问此对象」错误

## v0.4.0 更新

- **知乎接入**：新增「知乎」供应商（知乎直答 Zhida），模型含 `zhida-fast-1p5` / `zhida-thinking-1p5` / `zhida-agent`，可作普通聊天模型使用
- **知乎信源**：联网搜索新增「知乎」选项，回答前检索知乎站内内容作为参考资料，回答下方照常展示「📎 信源」链接
- **浏览器核验**：信源下方新增「🔍 用浏览器搜索核验」按钮，一键用浏览器打开核心问题（知乎站则跳知乎搜索，其余跳 Google）
- **搜索信源手动选择**：聊天窗口底部新增搜索信源下拉（Tavily / Google / 知乎 / 自定义），单次对话生效
- **搜索信源补全**：设置中补齐此前代码已支持但未暴露的 Google（Serper.dev）信源
- 知乎 Access Secret 使用 Windows DPAPI 加密存储

## v0.3.0 更新

- **联网搜索（Tavily）**：回答前可自动联网搜索，把实时搜索结果作为参考资料注入，让模型基于最新信息回答
  - 设置 → 联网搜索：默认开关、Tavily API Key（https://tavily.com 注册，有免费额度）、自定义搜索接口
  - 聊天窗口底部新增「联网」临时开关，单次对话生效
  - 状态栏显示搜索进度（正在搜索… / 已找到 N 条结果）
  - 搜索可随时取消；搜索失败或没有结果时仍会正常回答
  - 搜索结果只用于本次回答，不写入会话历史

## 功能

- 连按 Shift / Ctrl / Alt 全局唤起（可配置、可禁用，间隔可调）
- 选中文字自动填入（纯 UI Automation，不碰剪贴板）
- ↑/↓ 随时切换功能（回答问题 / 解释 / 翻译 / 润色 / 自定义，提示词可编辑）
- Enter 发送、Shift+Enter 换行
- 联网搜索（Tavily，可选）与推理模式（DeepSeek thinking）都支持全局/单次开关
- 输入框下方模型选择 + 一键获取模型列表
- Markdown 渲染：标题、列表、表格、引用、行内代码与代码块语法高亮
- 流式输出、多轮上下文、可停止生成
- 每次打开自动保存会话到历史记录，全新对话开始
- 历史记录查看（顶栏 / 托盘入口）
- 浅色现代化界面（HandyControl）、ClearType 锐利文字、窗口原生阴影
- API Key DPAPI 加密存储、开机自启、失焦自动隐藏

## 运行要求

- Windows 10/11 x64
- 需安装 **.NET 8 Desktop Runtime (x64)**：https://dotnet.microsoft.com/download/dotnet/8.0

## 使用

1. 解压 zip，运行 `AskAnywhere.exe`
2. 托盘右键 → 设置，填入 Base URL / API Key（DeepSeek：`https://api.deepseek.com/v1`）
3. （可选）设置 → 联网搜索，填入 Tavily API Key
4. 选中文字 → 连按 Shift → 选择功能 → 回车发送

## 许可证

MIT License
