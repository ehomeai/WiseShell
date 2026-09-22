# 发布源码到 GitHub

仓库地址：https://github.com/ehomeai/WiseShell.git 。以下命令在 Windows PowerShell 中执行，需要已安装 Git，并使用有仓库写入权限的 GitHub 账号完成认证。每一步成功后再执行下一步。

当前项目已经配置 `origin`，并将 `main` 推送到远程。后续更新直接使用下面的流程。这里的发布指源码推送；构建与安装包生成见 [项目文档](README.md)。

## 后续更新

先进入仓库，确认当前分支为 `main`，查看本次修改：

```powershell
Set-Location D:\AI-Code\WiseShell
git status --short --branch
git remote -v
git diff --stat
```

暂存修改并查看将要提交的内容。`git add -A` 会包含新增、修改和删除文件；提交说明应按实际改动调整。

```powershell
git add -A
git diff --cached --stat
git diff --cached
git commit -m "Update WiseShell source and documentation"
git push origin main
```

如果没有改动，跳过 `git commit`；若有尚未推送的提交，仍可执行 `git push origin main`。

## 首次发布到空仓库

仅用于尚未初始化 Git 的项目目录，且 GitHub 上已创建空仓库。当前项目无需重复初始化。

```powershell
Set-Location D:\AI-Code\WiseShell
git init -b main
git remote add origin https://github.com/ehomeai/WiseShell.git
git add -A
git diff --cached --stat
git diff --cached
git commit -m "Initial commit"
git push -u origin main
```

如果本地已有 Git 历史，保留现有仓库，跳过 `git init`。通过 `git branch --show-current` 确认分支；需要将当前分支改名为 `main` 且本地没有其他 `main` 分支时，执行 `git branch -m main`。

如果已经存在 `origin`，先执行 `git remote -v` 检查；只有地址不正确时才修改：

```powershell
git remote set-url origin https://github.com/ehomeai/WiseShell.git
```

## 校验推送结果

```powershell
git status --short --branch
git rev-parse HEAD
git ls-remote origin refs/heads/main
```

后两个命令输出的提交哈希应一致。工作区干净且分支同步时，状态显示 `## main...origin/main`，没有文件变更或 ahead/behind 提示。

## 远程已有新提交时

如果推送提示 `non-fast-forward` 或 `fetch first`，先确保本地修改已经提交，再同步远程历史：

```powershell
git pull --rebase origin main
git push origin main
```

若 rebase 出现冲突，先解决冲突，再执行 `git add -A` 和 `git rebase --continue`；完成后重新推送。需要取消此次 rebase 时执行 `git rebase --abort`。

## 本项目的上传范围

项目根目录为 `D:\AI-Code\WiseShell`，源码位于根目录下的 `src/`，旧 WPF 版本保留在 Git 历史中。根目录的 `.gitignore` 排除了 `~bak/`、`out/`、`build/`、`deps/` 及本地环境配置等内容。

可检查主要忽略规则是否生效：

```powershell
git check-ignore '~bak/WiseShell.WPF/README.md' out deps
```

这些规则仅影响未跟踪文件，已经提交过的文件不会因加入忽略规则而自动移出 Git。
