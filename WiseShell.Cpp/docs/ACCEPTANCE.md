# 实现与验收记录

验证日期：2026-09-10。Windows 使用 MSVC 14.50、Qt 6.8.3；Linux 使用 Ubuntu 22.04 WSL、GCC 11、Qt 6.2.4。两个平台均编译 Release 版本。

已生成 Windows ZIP 和 Linux tar.gz。Windows 包在仅保留系统 PATH 的环境中，以原生 Windows 窗口后端启动并正常退出；Linux 包通过 `AppRun` 在清理环境变量后启动并正常退出。打包脚本同时包含运行库、许可证和本验收文档。

## 功能与验证

| 功能 | 实现 | 已执行的验证 |
| --- | --- | --- |
| 独立项目与数据 | CMake 项目、独立目录、实例锁、原子 JSON | 会话往返、损坏配置不覆盖 |
| 会话管理 | 增删改、复制、收藏、分组、拖拽、内联重命名、搜索 | UI 测试验证重命名/移动落盘、收藏与搜索 |
| 默认设置 | 主题、字体、字号、滚动历史、按字段更新继承值 | 默认值与自定义值保留测试 |
| SSH | 密码、私钥及口令、PTY、多标签、保活、启动目录、重连 | 回环 OpenSSH 真实认证、错误密码、加密 Ed25519 私钥、窗口尺寸和退出 |
| 主机信任 | 首次询问、变化阻止自动连接、确认替换 | 信任记录变化测试、真实连接拒绝指纹 |
| 凭据 | QtKeychain、安全存储失败仅运行时缓存、取消记住时删除 | Windows 系统凭据真实写入/读取/删除；认证失败缓存失效测试 |
| 原生终端 | libvterm + Qt 绘制、UTF-8、中文 IME、ANSI/256 色、备用屏幕、选择复制、粘贴、Tab 与控制键 | 分段 UTF-8、输入法提交事件、Tab、Ctrl+C、方向键、括号粘贴、备用屏幕、滚动与缩放测试；渲染图检查 |
| SFTP | 双栏浏览、目录选择、默认目录、文件/目录上传下载、重命名、删除、新建目录 | 真实递归上传下载、中文文件名、覆盖与跳过冲突 |
| 传输队列 | 串行队列、字节进度、速度、取消、日志 | 排队取消、等待冲突时取消、上传过程中取消、临时文件清理 |
| 上传过滤 | 沿用旧过滤规则，不删除远端过滤项 | 再次上传后远端 `.git` 仍存在 |
| 递归边界 | 不跟随目录符号链接、限制递归深度、拒绝不安全文件名 | 实现中使用 lstat/isSymLink；尚未覆盖每个平台的文件系统竞态 |

`CoreTests` 有 10 个业务测试，`UiTests` 有 3 个业务测试，`IntegrationTests` 有 4 个业务测试；Qt Test 输出另计初始化和清理。Windows 三个测试套件均通过，系统凭据测试已启用；Linux核心、界面和真实 SSH/SFTP 测试通过，系统钥匙串测试未启用。

测试代码使用临时目录和专用回环测试账户。原有 WPF 数据未导入或修改。终端日志只写入收到的输出，不主动记录输入；远端回显仍可能进入日志。

## 平台边界

| 目标 | 当前证据 | 尚未验证 |
| --- | --- | --- |
| Windows 10/11 x64 | Windows 10 本机 Release 编译、单元/UI/真实 SSH 测试、Qt 渲染检查 | Windows 11 独立主机、高 DPI 多屏、不同输入法候选窗 |
| Ubuntu 24.04 x64 | Ubuntu 22.04 WSL 上 Release 编译、单元/UI/真实 SSH 测试；CI 定义使用 24.04 | 24.04 实机、Wayland 桌面输入法、Linux 系统钥匙串 |
| macOS 14+ arm64 | CMake、QtKeychain 后端和 macOS CI/打包脚本已提供 | **没有 Mac 主机，未编译运行，未生成经验证的 macOS 包** |

CI 配置已写入仓库，没有从本机推送或触发远程 CI。不能把工作流文件的存在视为 macOS 验证通过。

## 复验

构建与测试命令见 [README](../README.md)。未配置 SSH 测试环境时，CTest 会明确显示 `ssh_sftp` 跳过。

Linux/WSL 可使用 Docker Compose，或在一次性开发环境执行：

```bash
sudo bash tests/openssh/local-fixture.sh start
sudo env WISESHELL_TEST_SSH_PORT=22222 \
  WISESHELL_TEST_SSH_USER=wiseshellcpp_test \
  WISESHELL_TEST_SSH_PASSWORD=local-test-only \
  WISESHELL_TEST_SSH_KEY=/var/tmp/wiseshell-cpp-fixture/clientkey \
  ./out/release/bin/wiseshell_integration
sudo bash tests/openssh/local-fixture.sh stop
```

Windows 上设置 `WISESHELL_TEST_KEYCHAIN=1` 后运行 `wiseshell_tests.exe`，可执行真实系统凭据测试。生成截图时设置 `WISESHELL_TEST_SCREENSHOTS` 输出目录并运行 `wiseshell_ui_tests`。无窗口环境需设置 `QT_QPA_PLATFORM=offscreen`；Windows 离屏渲染需指定 `QT_QPA_FONTDIR=C:\Windows\Fonts`，这不影响正常桌面启动使用系统字体。

## 发布限制

- 便携包不包含应用安装器，不执行远程发布。Windows 使用应用目录内的 CRT DLL。
- macOS 不签名、不公证；尚无本机产物。
- Linux 需要系统显示服务和中文字体，推荐 `fonts-noto-cjk`。系统钥匙串服务不可用时只提供运行时缓存。
- SSH 功能按现有项目迁移，不包含跳板机、代理、SSH agent、端口转发和断点续传。
- 原生终端覆盖本项目所需 VT 功能，不宣称兼容所有终端扩展；图片协议和远端剪贴板写入未实现。
