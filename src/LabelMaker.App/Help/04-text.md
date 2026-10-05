---
id: text
title: Text
category: Designing
order: 2
keywords: text, font, size, bold, italic, colour, color, highlight, align, line spacing, letter spacing, grow, shrink, wrap, edit, format, apply formatting, other pages, custom font, add font, font file, ttf, otf
---
# Text

Click `Text` on the toolbar to add a text box.

## Type and edit

- **Double-click** the text (or press `Enter` or `F2` with it selected) to type straight on the label. In there you can select single words and make them bold, italic, coloured or another size. Click outside or press `Esc` when done.
- The box under `Properties > Text Settings` edits the words too.

## Look of the text

Under `Text Settings`: font, size, **B** / *I* / underline / strikethrough, colour, highlight, alignment (left, centre, right, justify; top, middle, bottom), line spacing, letter spacing and padding.

> Note: Changing a style in the panel applies it to the whole box, also to words formatted on their own.

## Fonts of your own

Press `Add font…` beside the font list and choose a font file (`.ttf` or `.otf`): the font is added to LabelAxis, without installing it in Windows, and given to the selected text. It is then in every font list, on screen, in print and in PDF files. `File > Settings > Fonts` lists the fonts you added and removes them.

A project that uses such a font keeps a copy of it inside, so it looks the same on another PC: opening the project there adds the font.

## When the text does not fit

`When it overflows` decides what happens:

- **Grow the box** (new text boxes): the box gets taller so all the text stays inside. Dragging a side handle re-wraps the text and the height follows.
- **Shrink the text**: the letters get smaller until they fit, down to the size set in `Smallest (pt)`.
- **Let it spill out**: the box keeps its size.

## Arabic and other right-to-left text

Type Arabic (or Hebrew) and the paragraph runs right to left on its own: letters join, words read from the right, and numbers and English words stay in their own order. See [[arabic|Arabic text and fonts]].

## Same formatting on other pages

`Apply this formatting to other pages…` (at the bottom of Text Settings, or in the `Edit` menu) gives the selected box's font, size, style and colour, and if you like its alignment and spacing, to text boxes on other pages: either the box with the **same name** on each page, or **every text box** there. See [[pages|Pages]].

## Changing text with data

Put a field name in curly brackets, such as `{Product}`, and each printed label shows that column of your spreadsheet. See [[data|Data and variable labels]].
