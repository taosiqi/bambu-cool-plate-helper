# Bambu 冷打板助手

<img src="assets/icon.png" width="128" alt="冷打板助手图标">

一键调整 Bambu Studio 所选官方 **PETG 系列耗材** 在低温增粘板 **SuperTack** 上的热床温度，不用给每种颜色单独切换耗材预设。

自动列出本机的官方 PETG 系列（例如 Basic、HF、Matte、Translucent、PETG-CF），默认只勾选 Basic。勾选后覆盖该耗材的所有机型和喷嘴预设。温度默认 **40℃**，可自行输入；首层和其他层使用相同温度。

## 下载

在 [最新版本](https://github.com/taosiqi/bambu-cool-plate-helper/releases/latest) 下载对应压缩包，解压后运行。

| 设备 | 下载包 | 系统要求 |
| --- | --- | --- |
| Mac · Apple Silicon（M 系列） | `BambuCoolPlateHelper-v1.0.0-arm64.zip` | macOS 13+ |
| Mac · Intel | `BambuCoolPlateHelper-v1.0.0-x86_64.zip` | macOS 13+ |
| Windows · x64 | `BambuCoolPlateHelper-v1.0.0-windows-x64.zip` | Windows 10/11，.NET Framework 4.8 |

无需安装 Python。Mac 应用未经过 Apple 公证，Windows 应用未做代码签名。

## 使用

1. 保存项目并退出 Bambu Studio（Mac 使用 `⌘Q`）。
2. 打开助手，勾选要修改的耗材，输入温度，点击 **应用温度**。
3. 重新打开 Studio，选择对应官方耗材和低温增粘板，检查温度并重新切片。

支持输入 **1–120℃整数**，请根据实际板材、耗材和打印机选择合适温度。

官方配置更新后可能需要重新应用。旧 3MF 的独立参数覆盖和已经切片的文件不会自动改变。未勾选的耗材、其他板型和项目文件不受影响。请只勾选打印板支持的耗材；出现在列表中不代表该耗材适合低温打印。

## 恢复默认

点击 **恢复默认**，仅还原勾选耗材首次修改前备份的温度设置，其他参数保留。默认值以首次备份为准，不是输入框中的 40℃。

首次备份不会被后续温度修改覆盖。原来继承上级预设的温度会恢复为继承状态，实际值取决于当前上级配置。

备份目录：

- Mac：`~/Library/Application Support/BambuCoolPlateHelper/Backups/`
- Windows：`%APPDATA%\BambuCoolPlateHelper\Backups\`

## 从源码构建

Mac 需要 Xcode Command Line Tools，在对应芯片的机器上运行：

```sh
./build.sh arm64   # Apple Silicon
./build.sh x86_64  # Intel
```

Windows 使用 PowerShell：

```powershell
./windows/build.ps1
```

产物位于 `dist/`。仅运行测试可用 `./verify-macos.sh` 或 `./windows/build.ps1 -TestOnly`。推送版本标签会触发三个平台的 CI 编译、自测和发布。

## 支持作者

如果这个工具帮你省下了逐个修改耗材的时间，欢迎到 [我的 MakerWorld 主页](https://makerworld.com.cn/@siqijson?appSharePlatform=copy) 看看作品，求助力，感谢支持！

[GitHub 仓库](https://github.com/taosiqi/bambu-cool-plate-helper)
