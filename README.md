# Flotsam 修改器

适用于 Windows x64 版《Flotsam》的单人游戏修改器。通过独立启动器在游戏内打开中文面板，调整城镇、工人、科研和库存，也可用小地图查看周边海域并控制航行。

## 下载与使用

1. 从 [GitHub Releases](https://github.com/2683445453/Cheat-in-Foltsam/releases) 下载 `FlotsamTrainer.zip`，完整解压到一个目录。
2. 启动《Flotsam》并进入游戏主菜单，再运行解压目录中的 `FlotsamTrainer.exe`。
3. 按 **F2** 显示或隐藏面板。隐藏面板后，已开启的功能仍然生效。
4. 要停用修改器，在面板里点击「取消注入」并确认。已经写入存档的变更会保留，建议首次使用前备份存档。

## 主要功能

- 城镇：免资源建造、免材料制作、无限电力、容量调整和自动打捞。
- 工人：不死、无需吃喝、移速与经验倍率、一键治疗和属性调整。
- 科研与库存：调整科研速度和点数，按物品搜索并修改库存。
- 航行：可移动和缩放的小地图、战争迷雾开关、快捷航行和船只移速调整。

更多操作和功能限制见 [使用说明](使用说明.txt)。本项目依赖游戏的 Mono 托管程序集，游戏更新后可能需要适配。

## 从源码构建

需要 Windows x64、PowerShell 7.2 或更高版本、.NET SDK、Visual Studio C++ x64 Build Tools（含 Windows SDK），以及本地安装的 Windows 版《Flotsam》。游戏程序集从本地安装读取，Harmony 通过 NuGet 还原。

```powershell
.\build.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Flotsam'
```

将路径换成包含 `Flotsam.exe` 和 `Flotsam_Data` 的游戏目录。构建完成后，发布压缩包位于 `dist/FlotsamTrainer.zip`。

## 许可证

项目源码按 [MIT 许可证](LICENSE) 发布。随包分发的 Harmony 适用其 [独立 MIT 许可证](LICENSE.Harmony.txt)。
