---
id: data
title: 数据和可变标签
category: Data
order: 1
keywords: data, csv, excel, spreadsheet, database, sql, mysql, postgresql, access, odbc, sqlite, field, merge, variable, records, sequence, serial, counter, date, time, expression, function, duplicate, 数据, 电子表格, 数据库, 字段, 合并, 记录, 序列, 序列号, 计数器, 函数, 重复, make pages, page for each record, pages from records, 生成页面, 每条记录一个页面
---
# 数据和可变标签

为电子表格或数据库的每一行打印一张不同的标签：姓名、价格、条码、序列号。

## 连接数据

- `数据 › 导入 CSV / Excel…` 读取 .csv 或 .xlsx 文件。
- `数据 › 连接到数据库…` 可从 SQL Server、MySQL / MariaDB、PostgreSQL、SQLite、Microsoft Access 或任意 ODBC 源读取。`编辑连接…` 可修改连接；`F5` 刷新记录。
- `数据 › 记录…`（`Ctrl+T`）显示各行；可在那里编辑、选择要打印的行，或只打印所选的行。

## 在标签上放置字段

在任意文字或条码中，用花括号写出列名：`{Product}`、`{Price}`、`{Barcode}`。`数据 › 添加数据字段…` 会为您插入。屏幕上的标签显示当前记录；状态栏中的箭头可逐条切换。

## 现成字段

| 字段 | 显示 |
|---|---|
| `{TODAY}` / `{NOW}` | 今天的日期 / 日期和时间 |
| `{RECORDNUMBER}` / `{RECORDCOUNT}` | 本条记录的编号 / 记录总数 |
| `{PAGENUMBER}` / `{LABELPOSITION}` | 纸张编号 / 标签在纸张上的位置 |

## 计算

花括号内还可以使用函数，例如 `{UPPER(Product)}`、`{ROUND(Price * 1.15, 2)}`、`{IF(Stock > 0, "有货", "售罄")}`、`{DATEFORMAT(ADDDAYS(TODAY(), 30), "dd/MM/yyyy")}`。其他函数：LOWER、TRIM、LEFT、RIGHT、MID、CONCAT、REPLACE、PADLEFT、FORMAT、MIN、MAX、ABS、YEAR、MONTH、DAY、ADDMONTHS、IFEMPTY、CONTAINS、STARTSWITH。

## 序列号

`数据 › 管理序列…` 可设置计数器：起始值、结束值、步长以及前缀或后缀。在花括号中用其名称即可使用；每打印一张标签就加一。

## 为每条记录生成一个页面

除了为每条记录打印一个标签，还可以把记录变成页面，以便手动修改其中一些，或与其他页面一起打印：

1. 把列放到标签上（`记录…` › **将字段添加到标签**，或在文本框中输入 `{Product}`）。
2. 选择 `数据` › **生成页面…** 为所有记录生成页面；或打开 `记录…`，选中所需的记录（`Ctrl`+单击、`Shift`+单击），然后单击 **生成页面**。如果没有选中任何记录，则使用筛选器显示的所有记录。

每个新页面都是屏幕上标签的副本，其文字、条码和表格单元格中写入了该记录的值，并以记录第一列的值命名。日期、计数器和其他占位符保持不变，因此打印这些页面时仍然有效。随后打印会设为打印这些页面，每张纸打印多个。这只是一步：`Ctrl+Z` 会移除所有新页面。每次最多生成 2,000 个页面。

## 打印之前

`数据 › 重复检查…` 会找出会重复打印相同值的记录，例如两张标签使用同一个条码。要每条记录打印一张标签，请在“打印”窗口中选择 **每条数据记录一张标签**；请参阅 [[printing|打印]]。
