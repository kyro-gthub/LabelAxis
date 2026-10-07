---
id: files
title: 项目、保存和设置
category: Reference
order: 2
keywords: save, open, file, labelproj, autosave, recovery, backup, settings, theme, dark, portable, usb, recent, language, arabic, kurdish, sorani, persian, farsi, french, spanish, dutch, russian, chinese, hindi, update, new version, upgrade, themes, frost, aurora, midnight, graphite, ember, ocean, sakura, sage, glass, custom theme, own colours, make your own theme, 保存, 打开, 文件, 自动保存, 恢复, 备份, 设置, 主题, 深色, 便携版, 语言, 中文, 更新, 新版本, 自定义主题
---
# 项目、保存和设置

## 项目

项目（.labelproj 文件）包含标签纸、所有页面、所用的图片以及数据连接。可以同时打开多个项目，每个项目在标签上方都有自己的标签页。`文件 › 最近打开` 列出最近的项目。

## 永不丢失工作

- LabelAxis 每隔几分钟会保存一份 **恢复快照**。如果电脑或程序意外停止，下次启动时会提示恢复您的工作。
- 保存时会把上一个版本作为备份保留在文件旁边。

## 设置

`文件 › 设置…`：语言、外观（八种主题，有浅色也有深色；主窗口右上角的主题按钮也可以切换）、网格大小和吸附距离、自动保存、欢迎窗口、重新打开上一个项目、更新，以及删除页面或数据前的确认。

## 语言

`文件 › 设置… › 语言` 可将 LabelAxis 显示为英语、阿拉伯语、中库尔德语（索拉尼语）、波斯语、法语、西班牙语、荷兰语、俄语、简体中文或印地语。阿拉伯语、库尔德语和波斯语的窗口从右到左排列；PDF、QR Code 和 Wi-Fi 等技术名称保持英文。新语言在 LabelAxis 重新启动后生效，程序会提示您重新启动。首次启动时，如果 Windows 的语言是其中之一，LabelAxis 就会使用它。

## 更新

LabelAxis 大约每天检查一次新版本，每当您选择 `帮助 › 检查更新…` 时也会检查。有新版本时，标签上方会出现一个栏，提供 `立即更新`、`新增功能`、`稍后` 和 `跳过此版本`。**立即更新** 会下载新版本，检查它与发布的文件完全一致，关闭 LabelAxis（并询问是否保存您的工作），安装后重新启动 LabelAxis。您的设置、模板和校准都会保留。

在 `文件 › 设置… › 更新` 中，可以关闭每日检查，或输入 **更新地址**：发布 LabelAxis 更新的网址（https://，例如 GitHub 发布文件夹）或共享文件夹。内置了地址的版本会在此框留空时使用该地址。

## 便携版

便携版可以在任意文件夹或 U 盘中运行。只要 LabelAxis.exe 旁边有 **portable.txt** 文件，设置、您的自定义模板和校准就会保存在旁边的 **Data** 文件夹中，而不是保存在 Windows 中。

便携版不会自行安装更新：有新版本时，请将其解压覆盖到该文件夹（Data 文件夹会保留）。

## 自定义主题

`文件 › 设置… › 制作自定义主题…`（或右上角的主题按钮）可以用您自己的颜色制作主题：选择一个起始主题、一个主色、两种光晕颜色，如需要再选一个背景，以及光晕的强度。LabelAxis 会随您的选择实时变化，因此您能立即看到效果；文字颜色会自动选择，确保始终清晰可读。您的主题会与其他主题一起列出；`删除我的主题` 可将其删除。
