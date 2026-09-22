# WiseShell C++

WiseShell 是一个面向 Windows 的 SSH/SFTP 桌面客户端，基于 ` C++20`、`Qt Widgets`、`libssh` 和 `libvterm ` 构建。它专注于常用服务器运维场景：管理 SSH 会话、打开远程终端、浏览和传输 SFTP 文件，并安全保存连接凭据。

## 构建

需要 CMake 3.24+、Ninja、Python 3、Git、C++20 编译器、Qt 6 和 OpenSSL/zlib 开发库。发布 SDK 固定为 Qt 6.8.3；兼容最低 Qt 6.2。libssh、QtKeychain、libvterm 固定到提交，见 [依赖声明](cmake/Dependencies.cmake)。首次配置需要联网下载源码，后续可使用 CMake 缓存离线构建。

### Windows x64

使用已安装 C++ 桌面开发工作负载的 Visual Studio 2022/2026，打开 **x64 Native Tools Command Prompt**。Qt 选择 `msvc2022_64`。OpenSSL 和 zlib 使用 MSVC x64 开发包，不能混用 MinGW 库。

在本项目目录执行；`QT_ROOT`、`OPENSSL_ROOT_DIR`、`ZLIB_ROOT` 应事先指向对应 SDK 安装目录：

```powershell
cmake --preset release -DCMAKE_C_COMPILER=cl -DCMAKE_CXX_COMPILER=cl "-DCMAKE_PREFIX_PATH=$env:QT_ROOT" "-DOPENSSL_ROOT_DIR=$env:OPENSSL_ROOT_DIR" "-DZLIB_ROOT=$env:ZLIB_ROOT"
cmake --build --preset release
$env:PATH = "$env:QT_ROOT\bin;$env:OPENSSL_ROOT_DIR\bin;$env:ZLIB_ROOT\bin;$env:PATH"
ctest --preset release
.\out\release\bin\WiseShellCpp.exe
```

### Ubuntu

```bash
sudo apt-get update
sudo apt-get install -y build-essential cmake ninja-build git python3 qt6-base-dev libgl1-mesa-dev libssl-dev zlib1g-dev libsecret-1-dev fonts-noto-cjk
cmake --preset release
cmake --build --preset release
ctest --preset release
./out/release/bin/WiseShellCpp
```

Ubuntu 22.04 的发行版 CMake 低于要求，先通过 Python 环境安装 `cmake==3.31.6`。发布平台基线为 Ubuntu 24.04 x64。

### macOS arm64

安装 Xcode Command Line Tools、CMake、Ninja、Python 3、Qt 6.8.3 clang_64 SDK 和 OpenSSL 3。Homebrew 的 Qt 可以用于开发构建。

```bash
brew install cmake ninja qt openssl@3
cmake --preset release -DCMAKE_PREFIX_PATH="$(brew --prefix qt)" -DOPENSSL_ROOT_DIR="$(brew --prefix openssl@3)"
cmake --build --preset release
ctest --preset release
open out/release/bin/WiseShellCpp.app
```

## 使用

新建会话，填写主机、用户名及认证方式；双击会话连接终端。右键会话可编辑、复制、收藏、重命名或删除。拖拽会话或分组可调整层级。终端设置支持 Dark、Light、Solarized、字体、字号和历史行数；全局默认设置可更新仍沿用旧默认值的字段。

终端中，Ctrl+Insert 复制选区，Shift+Insert 粘贴，Ctrl+C 发送中断。Shift+PageUp/PageDown 浏览历史，F2 重命名当前终端标签页。欢迎页和帮助界面均提供常用快捷键说明。支持 UTF-8、中文输入法、ANSI/256 色、备用屏幕和常用 VT 按键。

工具栏 SFTP 打开独立连接。左右两栏分别浏览本地与远程文件，可多选上传下载，执行目录创建、重命名、删除。传输按队列串行执行，可取消当前或排队任务。点击“设为默认目录”保存当前两栏目录。

上传过滤 `.git`、`.svn`、构建目录及旧版临时文件规则，但不会删除远端被过滤项。递归传输不跟随符号链接。上传使用临时文件后重命名，下载使用 `QSaveFile`；覆盖上传依赖服务器支持替换式重命名，服务器不支持时保留原文件并报告失败。

## 数据与凭据

| 平台 | 默认数据目录 | 凭据存储 |
| --- | --- | --- |
| Windows | `%APPDATA%\WiseShellCpp` | QtKeychain Windows 安全后端 |
| Linux | `$XDG_DATA_HOME/WiseShellCpp`，默认 `~/.local/share/WiseShellCpp` | Secret Service / KWallet |
| macOS | `~/Library/Application Support/WiseShellCpp` | Keychain |

`sessions.json` 保存会话和分组，`known-hosts.json` 保存主机信任，`Logs` 保存终端及传输日志。配置使用原子替换；同一数据目录只允许一个应用实例。配置损坏时显示错误并停止启动，不覆盖原文件。

系统凭据存储不可用时仅缓存于本次运行，不写明文。取消记住凭据会尝试删除安全存储中的记录，失败时明确显示错误。认证失败后，下次连接重新询问凭据。未知主机和变更指纹都需确认，变更指纹不会自动接受。

## 测试与打包

`ctest` 默认运行核心和终端测试；未配置测试服务器时 SSH 集成测试明确跳过。启动本地隔离的 OpenSSH 容器后运行完整集成测试：

```bash
docker compose -f tests/openssh/compose.yaml up -d --build
export WISESHELL_TEST_SSH_PORT=22222
export WISESHELL_TEST_SSH_USER=wiseshelltest
export WISESHELL_TEST_SSH_PASSWORD=local-test-only
ctest --preset release --output-on-failure
docker compose -f tests/openssh/compose.yaml down
```

测试账户仅用于此回环地址容器。不要将其部署到外部服务器。

各平台使用 `scripts/package.py` 打包本机 Release 产物。指定构建目录、Qt bin 目录和新的输出目录；Windows 还需通过 `--runtime-dir` 指定 OpenSSL/zlib DLL 目录，可重复传入，脚本从 Visual Studio 复制可再分发的 CRT DLL。示例（Qt 工具已在 PATH 中）：

```bash
python3 scripts/package.py --build out/release --qt-bin "$(qmake -query QT_INSTALL_BINS)" --output out/packages
```

打包脚本拒绝覆盖已有输出目录。生成 Windows ZIP、Linux tar.gz（通过 `AppRun` 启动）或 macOS `.app` ZIP。不提供安装器、发行签名、公证或自动更新。实际平台验证记录见 [验收清单](docs/ACCEPTANCE.md)。

## 结构

- `src/core`：配置模型、原子存储、系统凭据和主机信任。
- `src/transport`：SSH 连接、终端会话、SFTP 工作线程与取消队列。
- `src/terminal`：libvterm 屏幕模型、Qt 绘制与输入事件。
- `src/ui`：会话树、标签页、设置和文件浏览窗口。
- `tests`：Qt Test 测试及本地 OpenSSH 测试环境。

网络句柄由工作线程独占。界面通过信号接收字节流、状态、指纹请求、冲突请求和进度；线程通过可取消的决定对象等待用户输入。连接及阻塞协议调用设置 10 秒网络超时；关闭时先取消、再等待线程释放句柄。DNS 解析仍受系统解析器超时约束。
