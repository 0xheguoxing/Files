# Files（个人定制分支）

Files 是一个面向 Windows 的现代文件管理器，基于 WinUI 3 构建。
本仓库是 [files-community/Files](https://github.com/files-community/Files) 的**个人定制分支**，
在保留上游全部核心功能的基础上，做了针对性裁剪与体验调整。

- 版本：`4.3.0.0`
- 包标识：`FilesDev`（开发通道，发布者 `CN=Files`，可与商店版共存）
- 目标系统：Windows 10 2004（10.0.19041）及以上
- 编译部署：[BUILD.md](BUILD.md)

---

## 与上游的差异

### 1. 移除 Git 集成

文件管理器不需要 Git 功能，因此整块移除：

- 动作：`GitClone` / `GitInit` / `GitPull` / `GitPush` / `GitFetch` / `GitSync`
- 服务与依赖：`LibGit2Sharp` 及其 NuGet 包引用、原生二进制
- 界面：状态栏分支显示与网络操作、文件列表的 Git 列、
  属性页与预览面板的 Git 信息、设置中的开发者工具页
- 对话框：分支管理、克隆仓库、检出、GitHub 登录

### 2. 界面调整

| 位置 | 调整 | 原因 |
|---|---|---|
| 标签栏 | 去掉顶部 10px 间隙，行高 40 → **28** | 消除标签上方空白；悬停背景在最大化与还原态都能铺满整行 |
| 地址栏 | 高度 48 → **40**，圆角 19 → **4**，路径模式改为文件夹线框图标 | 原胶囊造型偏圆，改为方框圆角更利落 |
| 面包屑 | 根项圆角 `16,2,2,16` → `4,4,4,4` | 悬停时不再出现半圆背景 |
| 侧边栏 | 紧凑宽度 56 → **48**，隐藏滚动条，选中指示条 **2px**，内边距 2/17 | 收窄留白，选中态更协调 |
| 文件列表 | 关闭复选框（含悬停显示） | 保留选中背景高亮，去掉多余控件 |

### 3. 启动优化

“双击文件夹”路径上的启动耗时从 **1.03s 降至 0.61s**（实测 3 次均值）：

- 启动器不再经 `files-dev:?cmd=<hex>` **URI 协议**中转，改为直接以参数启动别名可执行文件
- 握手探测定时器 `500ms → 100ms`：启动器原先在拉起 Files 之前会空等 500ms

R2R（ReadyToRun）复合编译已启用，无需额外配置。

### 4. Shell 集成（可选）

通过当前用户级注册表（**无需管理员**）接管部分系统行为。
脚本位于 `artifacts\build\`，详见 [BUILD.md](BUILD.md) 第 7 节。

| 集成 | 状态 | 说明 |
|---|---|---|
| 默认文件管理器 | ✅ 启用 | 双击文件夹、在新窗口打开均由 Files 接管 |
| 打开文件对话框接管 | ⛔ 停用 | 见下方“已知限制” |
| 另存为对话视接管 | ⛔ 停用 | 上游实现无法接受新文件名 |
| 右键“在 Files 中查看属性” | ⛔ 停用 | 需要新增 CLI 支持，暂不启用 |

组件文件（COM DLL 与启动器）始终保留在 `%LOCALAPPDATA%\Files\`，
停用只是移除注册表配置，随时可以重新启用。

### 5. 新增命令行能力

主应用新增 `-properties <路径>` 参数，可直接打开指定路径的属性窗口：

```powershell
files-dev.exe -properties "D:\some\folder"
```

---

## 已知限制

**属性对话框无法替换。** Windows 的属性对话框由 Shell 命名空间现场组装
（`IShellFolder::GetUIObjectOf` → `IShellPropSheetExt`），不像打开/保存对话框那样
按 CLSID 从注册表创建，因此没有可覆盖的入口。`Alt+Enter` 与右键“属性”会一直使用
系统自带对话框。唯一存在的扩展点 `shellex\PropertySheetHandlers` 只能加页，
且只接受 Win32 对话框模板，无法承载 WinUI 3 页面。

**保存对话框的上游实现不可用。** 它是「选中一个已存在项目并返回其路径」的机制：

- 没有文件名输入框，无法指定“另存为”的新名字
- 需要**关闭 Files 窗口**才会回传结果（结果在 `Window_Closed` 中写入）
- 返回值来自当前选中项，调用方会把它当成保存目标

把它注册为系统保存对话框会导致调用方直接写入错误路径，因此默认停用。

---

## 技术栈

| 层 | 技术 |
|---|---|
| 主应用 | C# / .NET 10 / WinUI 3（Windows App SDK 2.x） |
| 原生互操作 | CsWin32 源生成（`Files.App.CsWin32`）、C++/WinRT |
| 启动器 | C++（Win32，注册为 Shell 打开动词） |
| 对话框组件 | C++ / ATL COM（实现 `IFileOpenDialog` / `IFileSaveDialog`） |
| 打包 | MSIX（sideload，发布时自签名） |

## 工程结构

```
src/
├── Files.App                    主应用（唯一可执行产物）
├── Files.App.Controls           共享控件：Sidebar / Omnibar / BreadcrumbBar / ThemedIcon
├── Files.App.Storage            存储抽象实现
├── Files.App.CsWin32            CsWin32 生成的 Win32 互操作
├── Files.App.BackgroundTasks    后台任务
├── Files.App.Server             进程外应用服务
├── Files.Core.SourceGenerator   Roslyn 源生成器
├── Files.Core.Storage           核心存储抽象
├── Files.Shared                 共享基础代码
├── Files.App.Launcher           C++ 启动器
├── Files.App.OpenDialog         C++ COM：打开对话框
└── Files.App.SaveDialog         C++ COM：保存对话框
tests/                           UI 与交互测试
```

## 编译与部署

完整流程（含环境要求、资源限制、常见问题）见 **[BUILD.md](BUILD.md)**。

快速路径：

```powershell
cmd /c artifacts\build\step1-restore.bat    # 还原
cmd /c artifacts\build\step2-launcher.bat   # 构建启动器（必须早于主应用）
cmd /c artifacts\build\step3-app.bat        # 构建主应用 + 打包 MSIX

powershell -ExecutionPolicy Bypass -File artifacts\build\deploy-x64.ps1
```

> `artifacts\` 未纳入版本控制。脚本不存在时，直接使用 BUILD.md 中的 `msbuild` 命令。

## 许可

沿用上游的双许可：[LICENSE-MIT](LICENSE-MIT) 与 [LICENSE-MPL](LICENSE-MPL)。

## 上游

- 仓库：https://github.com/files-community/Files
- 官网：https://files.community
- 本分支聚焦个人使用场景，未做上游功能对齐；同步上游时请注意上文列出的定制点。