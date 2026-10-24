---
id: export
title: 导出和检查任务
category: Exporting
order: 1
keywords: export, pdf, png, svg, picture, file, preflight, check, publisher, import, 导出, 图片, 文件, 打印前检查, 检查, 导入, publisher margins, label sheet, publisher 边距, 标签纸
---
# 导出和检查任务

## 保存为文件

- `打印 › 导出 › 纸张导出为 PDF…` 会用与打印相同的设置（纸张、起始位置、记录）将任务保存为 PDF。可以发给印刷店，也可以留作样稿。
- `当前标签导出为 PNG…` 将屏幕上的标签保存为图片；`当前标签导出为 SVG…` 保存为设计软件可以打开的图形。

## 打印前检查

`打印 › 打印前检查…` 会在昂贵的任务之前查找问题：缺少图片或字体、条码数据错误或对标签来说太大、某条记录的字段为空、条码重复，以及标签超出打印机可打印区域。

## 导入 Microsoft Publisher 文件

`文件 › 打开 Publisher 文件（所有页面）…` 将 Publisher（.pub）文件转换为项目；`从 Publisher 导入文字和图片…` 将其内容导入当前打开的项目。

Publisher 标签文件通常每页一个标签，每张纸打印多页。LabelAxis 会把这些页面放到真实的标签纸上：Publisher 所指定的标签纸，或标签尺寸相同的内置标签纸，并使用 Publisher 自身的边距和间距，使标签打印在与 Publisher 打印时相同的位置。状态栏会显示所选的标签纸；如果您使用的是其他标签纸，请在 **标签管理器** 中更改。此前从 Publisher 导入的项目（每个小页面上只有一个标签）在打开时会提示使用其标签纸；**取消** 会保持每页一个标签，并且不会再就该文件询问。
