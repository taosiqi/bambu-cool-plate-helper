# Bambu 冷打板助手

一个原生 macOS 小工具，将本机官方 Bambu PETG Basic 在低温增粘板 SuperTack 上的首层和其他层热床温度统一设为 **40℃**，不用给多色项目逐个选择自定义耗材预设。

## 下载与使用

从 [Releases](https://github.com/taosiqi/bambu-cool-plate-helper/releases/latest) 下载 ZIP，解压后双击应用。提供 Apple Silicon 版本，最低系统目标为 macOS 13；实际仅在开发机器上完成编译与逻辑测试。

1. 保存项目并按 `⌘Q` 退出 Bambu Studio。
2. 打开助手，点击“一键设为 40℃”。
3. 重新打开 Studio，确认使用官方 Bambu PETG Basic 和低温增粘板，检查首层及其他层温度，再重新切片。

应用为本地临时签名，未经过 Apple 公证。40℃ 是用户指定值，不代表所有 PETG 和板材的推荐温度。

## 修改范围

- 机器：P2S、A1 mini。
- 喷嘴：本机已有的 0.2、0.4、0.6、0.8 mm 官方 PETG Basic 预设。
- 位置：`~/Library/Application Support/BambuStudio/system/BBL/filament/`。
- 字段：`supertack_plate_temp`、`supertack_plate_temp_initial_layer`，均设置为 `["40"]`。

不修改其他板型、PETG HF、自定义耗材或项目文件。旧 3MF 中的独立覆盖、已切片文件不会随之改变。官方配置更新可能覆盖修改，再次运行即可重新应用。尚未验证实际打印结果。

## 备份与恢复

每次实际修改前，完整备份目标文件到：

```
~/Library/Application Support/BambuCoolPlateHelper/Backups/
```

“恢复上次修改”只还原最近一次修改涉及的两个温度字段，保留其他参数。原来继承的字段恢复为继承状态。如果温度后来被其他操作修改，则停止恢复，避免覆盖新设置。重复应用且数值未变化时，不覆盖上次备份记录。

## 构建与测试

需要 macOS 和 Xcode Command Line Tools，无第三方依赖。

```sh
./build.sh
```

产物在 `dist/`。构建脚本执行临时目录自测，覆盖显式温度、继承温度、重复应用、恢复、保留无关修改、恢复冲突及 Studio 进程身份判断，不修改真实 Studio 配置。

## 支持作者

如果这个工具帮你省下了逐个修改耗材的时间，欢迎到 [我的 MakerWorld 主页](https://makerworld.com.cn/@siqijson?appSharePlatform=copy) 看看作品，求助力，感谢支持！应用窗口也提供同一个主页入口。

## v1.1.0

- 新增“恢复最初备份”：按各预设最早可用备份恢复两个温度字段，保留其他设置；原来未显式设置的字段会恢复继承状态，而不是锁定旧版父级的数值。
- 自动识别 v1.0.1 留下的历史备份，最初备份保存在 `Backups/initial/`，后续应用不会覆盖。
- 最初恢复会覆盖当前温度，包括后来手动设置或官方更新的温度字段。最近一次恢复仍保留冲突保护。
- 新增 MakerWorld 作者主页助力链接。

## v1.0.1

修复运行检测误将助手本身识别为 Bambu Studio 的问题，改用 Studio 的准确 Bundle ID 或可执行文件名判断。
