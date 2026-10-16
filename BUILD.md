# 编译与部署指南

本文档说明如何在本地编译 Files 并部署运行。全部命令均在本机验证通过。

- 目标产物：**Release x64 的 MSIX 应用包（sideload、未签名，部署时自签）**
- 编译方式：MSBuild（VS 18 工具链），并行度限制为 4 以保持机器可用

---

## 1. 环境要求

| 组件 | 要求 | 本机已验证 |
|---|---|---|
| Visual Studio | VS 18（含 MSVC v145 工具集、Windows SDK） | VS 18 Community |
| .NET SDK | ≥ 10.0.102（见 `global.json`，`rollForward: latestMajor`） | 10.0.402 |
| NuGet CLI | 用于还原 C++ 工程的包 | `C:\developer\CSharp_Env\nuget\nuget` |
| 目标系统 | Windows 10 2004（10.0.19041）及以上 | — |

关键路径（下文用 `$MSBUILD` / `$NUGET` 代指）：

```
MSBuild : C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe
NuGet   : C:\developer\CSharp_Env\nuget\nuget
```

> 如果 `msbuild` 不在 PATH 中，直接使用上面的完整路径，或从 *Visual Studio Developer PowerShell* 中执行。

---

## 2. 工程结构

```
src/
├── Files.App                    WinUI 3 主应用（唯一的可执行产物）
├── Files.App.Controls           共享控件（Sidebar、Omnibar、BreadcrumbBar、ThemedIcon）
├── Files.App.Storage            存储抽象实现
├── Files.App.CsWin32            CsWin32 生成的 Win32 互操作
├── Files.App.BackgroundTasks    后台任务
├── Files.App.Server             应用服务（进程外服务器）
├── Files.Core.SourceGenerator   Roslyn 源生成器
├── Files.Core.Storage           核心存储抽象
├── Files.Shared                 共享基础代码
├── Files.App.Launcher           C++ 启动器（默认文件管理器的入口）
├── Files.App.OpenDialog         C++ COM：打开对话框接管
└── Files.App.SaveDialog         C++ COM：保存对话框接管
tests/                           UI / 交互测试（本地构建不需要）
```

### 构建产物位置

| 产物 | 路径 |
|---|---|
| MSIX 包 | `artifacts\AppxPackages\Files.App_4.3.0.0_x64_Test\Files.App_4.3.0.0_x64.msix` |
| 包依赖（WinAppSDK 运行时） | 同目录 `Dependencies\x64\Microsoft.WindowsAppRuntime.2.msix` |
| C++ 工程输出 | `artifacts\Release\{x64,Win32}\<工程名>\` |
| 常规托管输出 | `src\Files.App\bin\x64\Release\...` |

> `artifacts\` 已在 `.gitignore` 中，不会提交。

---

## 3. 编译步骤

**顺序很重要**：启动器必须先于主应用构建。它的生成后事件会把 `Files.App.Launcher.exe` 复制到
`src\Files.App\Assets\FilesOpenDialog\`，而主应用把它作为 `Content` 打包进 MSIX。

### 步骤 1 — 还原

```powershell
$env:MSBUILDDISABLENODEREUSE = 1
$MSBUILD = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe"
$NUGET   = "C:\developer\CSharp_Env\nuget\nuget"

# 解决方案级还原（含所有托管工程）
& $MSBUILD Files.slnx -t:Restore `
  -p:Configuration=Release -p:Platform=x64 -p:PublishReadyToRun=true `
  -m:4 -v:quiet -clp:ErrorsOnly

# C++ 启动器工程的包还原（不能省）
& $NUGET restore src\Files.App.Launcher\Files.App.Launcher.vcxproj -SolutionDirectory . -Verbosity quiet -NonInteractive
```

### 步骤 2 — 构建启动器（C++）

```powershell
& $MSBUILD src\Files.App.Launcher\Files.App.Launcher.vcxproj -t:Build `
  -p:Configuration=Release -p:Platform=x64 `
  -m:4 -p:CL_MPCount=4 -v:quiet -clp:ErrorsOnly
```

成功后确认 `src\Files.App\Assets\FilesOpenDialog\Files.App.Launcher.exe` 已更新。

### 步骤 3 — 构建主应用并打包 MSIX

```powershell
& $MSBUILD src\Files.App\Files.App.csproj -t:Build `
  -p:Configuration=Release -p:Platform=x64 `
  -p:AppxPackageDir="$PWD\artifacts\AppxPackages\" `
  -p:AppxBundle=Never `
  -p:GenerateAppxPackageOnBuild=true `
  -p:UapAppxPackageBuildMode=Sideload `
  -p:AppxPackageSigningEnabled=false `
  -m:4 -p:CL_MPCount=4 -v:quiet -clp:ErrorsOnly
```

产物：`artifacts\AppxPackages\Files.App_4.3.0.0_x64_Test\Files.App_4.3.0.0_x64.msix`

---

## 4. 资源限制（重要）

本机要求构建时限制并行度，避免卡死：

| 参数 | 作用 |
|---|---|
| `-m:4` | MSBuild 最多 4 个 worker 节点 |
| `-p:CL_MPCount=4` | 单个 C++ 工程内 cl.exe 并行上限 |
| `MSBUILDDISABLENODEREUSE=1` | 构建结束不驻留 MSBuild 进程，省内存 |

内存紧张时还可追加 `-p:UseSharedCompilation=false`（关闭常驻的 Roslyn 编译服务）。

---

## 5. 对话框组件（可选）

打开/保存对话框的接管依赖两个 C++ COM 工程，**默认不打进包、也不是必需的**。
只有需要重新启用对话框接管时才编译：

```powershell
# 打开对话框 ×2 平台
& $MSBUILD src\Files.App.OpenDialog\Files.App.OpenDialog.vcxproj         -p:Configuration=Release -p:Platform=x64   -m:1 -v:quiet -clp:ErrorsOnly
& $MSBUILD src\Files.App.OpenDialog\Files.App.OpenDialog.Win32.vcxproj   -p:Configuration=Release -p:Platform=Win32 -m:1 -v:quiet -clp:ErrorsOnly

# 保存对话框 ×2 平台
& $MSBUILD src\Files.App.SaveDialog\Files.App.SaveDialog.vcxproj         -p:Configuration=Release -p:Platform=x64   -m:1 -v:quiet -clp:ErrorsOnly
& $MSBUILD src\Files.App.SaveDialog\Files.App.SaveDialog.Win32.vcxproj   -p:Configuration=Release -p:Platform=Win32 -m:1 -v:quiet -clp:ErrorsOnly
```

构建后 DLL 会自动复制到 `src\Files.App\Assets\FilesOpenDialog\`，随下一次打包进 MSIX。

> ⚠️ 上游的保存对话框实现**无法接受新文件名**（它返回的是「已选中项目的路径」，
> 而不是「另存为的目标路径」），因此不能用于“另存为”。详见项目 README 的说明。

---

## 6. 部署

```powershell
powershell -ExecutionPolicy Bypass -File artifacts\build\deploy-x64.ps1
```

脚本行为：

1. 查找 `signtool.exe`（PATH / Windows Kits / NuGet `Microsoft.Windows.SDK.BuildTools`）
2. 创建或复用自签代码签名证书 `CN=Files`（`LocalMachine\My`，有效期 5 年）
3. 导出 PFX 并对 MSIX 签名（`signtool /fd SHA256 /f ... /p ...`）
4. 安装 Windows App SDK 2.x 运行时依赖
5. 安装应用；遇到 `0x80073CFB`（同版本号不同内容）自动卸载后重装
6. 启动 `Files - Dev`

常用参数：

```powershell
# 只安装不启动
... -File artifacts\build\deploy-x64.ps1 -NoLaunch
```

卸载：

```powershell
Get-AppxPackage -Name FilesDev | Remove-AppxPackage
```

> 应用使用开发通道包标识 `FilesDev` / 发布者 `CN=Files`，可与商店版 Files 共存。
> 安装自签名的 MSIX 到 `LocalMachine\TrustedPeople` 后**不需要开启开发者模式**。

### 免安装运行（注册式）

不签名、不拷贝文件，让 exe 停留在构建目录原地运行：

```powershell
powershell -ExecutionPolicy Bypass -File artifacts\build\register-portable.ps1
```

需要开发者模式。注册期间构建目录不可移动或删除；卸载用
`Get-AppxPackage -Name Files | Remove-AppxPackage`。

---

## 7. 系统集成

产物之外，Files 还能接管若干 Shell 行为（全部为**当前用户级注册，无需管理员**）：

| 脚本 | 作用 |
|---|---|
| `artifacts\build\enable-integrations.ps1` | 应用默认文件管理器重定向；清理已停用的接管项 |
| `artifacts\build\restore-defaults.ps1` | 撤销全部 Shell 接管（**只取消配置，保留组件文件**） |

```powershell
# 应用（每次重新部署包之后需要重跑，见下方注意事项）
powershell -ExecutionPolicy Bypass -File artifacts\build\enable-integrations.ps1

# 全部还原
powershell -ExecutionPolicy Bypass -File artifacts\build\restore-defaults.ps1
```

脚本位置在 `artifacts\build\`（未纳入版本控制）。若克隆后目录为空，可直接照抄本文档中的
`msbuild` 命令，并参考脚本逻辑自行写入注册表。

---

## 8. 一键脚本（本地便捷封装）

`artifacts\build\` 下有三个批处理脚本，封装了第 3 节的命令，并把日志写到同目录：

```bat
cmd /c artifacts\build\step1-restore.bat    :: 还原 + C++ 包还原
cmd /c artifacts\build\step2-launcher.bat   :: 构建启动器
cmd /c artifacts\build\step3-app.bat        :: 构建主应用 + 打包 MSIX
```

---

## 9. 常见问题

### `-p:AppxPackageDir` 加了引号后打包参数全部失效

`.bat` 中用 `-p:AppxPackageDir="D:\path\"` 时，结尾的 `\"` 会让 `cmd` 认为引号未闭合，
`^` 续行随之失效，后续参数（`GenerateAppxPackageOnBuild` 等）全部丢失——表现为“构建成功但没有 MSIX”。
**路径不含空格时不要加引号。**

### `NETSDK1004: 找不到资产文件 project.assets.json`

`obj\` 被清理过。先执行第 3 节的**步骤 1 还原**再构建。

### `0x80073CFB`：已安装具有相同标识但内容不同的程序包

同一版本号（4.3.0.0）反复部署必然遇到。`deploy-x64.ps1` 已自动处理
（卸载旧包 → 重装）；手动处理：

```powershell
Get-AppxPackage -Name FilesDev | Remove-AppxPackage
```

### 重新部署后“默认文件管理器”变回资源管理器

部署会卸载并重装包，期间启动器检测不到 `files-dev` 别名，会触发自身的
“Files 已卸载”分支，把注册表还原成 Explorer。**重装后重跑**
`enable-integrations.ps1` 即可恢复。

### 不要直接构建整个解决方案

`Files.slnx` 包含测试工程，会引入不必要的依赖与耗时。只构建
`Files.App.Launcher.vcxproj`（先）与 `Files.App.csproj`（后）即可。

### 构建警告

Release 构建约有 80–90 条警告，全部是仓库既有的过时 API 提示
（`NavigationBarSuggestionItem`、`MenuFlyoutHelper` 等），可忽略。

---

## 10. 验证清单

构建后逐项确认：

```powershell
# 1. 无编译错误（0 error）
# 2. MSIX 存在且时间戳为本次构建
Get-Item artifacts\AppxPackages\Files.App_4.3.0.0_x64_Test\Files.App_4.3.0.0_x64.msix | Select-Object LastWriteTime, Length

# 3. 包内包含启动器与（可选的）对话框组件
Add-Type -AssemblyName System.IO.Compression.FileSystem
$a = [System.IO.Compression.ZipFile]::OpenRead("$PWD\artifacts\AppxPackages\Files.App_4.3.0.0_x64_Test\Files.App_4.3.0.0_x64.msix")
$a.Entries | Where-Object { $_.FullName -like '*FilesOpenDialog*' } | Select-Object FullName
$a.Dispose()

# 4. 安装成功
Get-AppxPackage -Name FilesDev | Select-Object Version, InstallLocation
```