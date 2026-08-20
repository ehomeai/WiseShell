# WiseShell （弹弓ssh锛?or WiseSSH

`WiseShell` 鏄竴涓熀浜?`.NET 8` 鍜?`WPF` 鐨?Windows SSH 客户端，提供终端连接、会话管理和 SFTP 鏂囦欢娴忚鑳藉姏銆?
## 功能

- SSH 终端连接
- 浼氳瘽鍒嗙粍涓庢敹钘忕鐞?- 鏀寔瀵嗙爜鍜岀閽ヤ袱绉嶈璇佹柟寮?- 基于 `WebView2 + xterm.js` 鐨勭粓绔覆鏌?- 终端主题、字体、字号和滚动缓冲设置
- SFTP 鏂囦欢娴忚銆佷笂浼犮€佷笅杞?- 主机指纹信任保存
- 使用 Windows `DPAPI` 保存凭据

## 项目结构

- `WiseShell.App`：WPF 桌面应用
- `WiseShell.Core`：核心模型、接口和本地数据存储
- `WiseShell.Transport.SshNet`锛氬熀浜?`SSH.NET` 鐨?SSH/SFTP 传输实现
- `WiseShell.Tests`锛氭祴璇曢」鐩?
## 运行环境

- Windows
- `.NET 8 SDK`
- `Microsoft Edge WebView2 Runtime`

## 寮€鍙戝懡浠?
鏋勫缓锛?
```powershell
dotnet build .\WiseShell.sln
```

杩愯搴旂敤锛?
```powershell
dotnet run --project .\WiseShell.App\WiseShell.App.csproj
```

杩愯娴嬭瘯锛?
```powershell
dotnet test .\WiseShell.Tests\WiseShell.Tests.csproj
```

## 本地数据

搴旂敤榛樿灏嗘暟鎹繚瀛樺埌锛?
```text
%APPDATA%\WiseShell
```

鍏朵腑鍖呮嫭锛?
- `sessions.json`：会话与分组信息
- `credentials.json`：加密保存的凭据
- `known-hosts.json`：已信任主机指纹
- `Logs\`锛氭棩蹇楃洰褰?
## 技术栈

- `.NET 8`
- `WPF`
- `Microsoft.Web.WebView2`
- `xterm.js`
- `SSH.NET`
