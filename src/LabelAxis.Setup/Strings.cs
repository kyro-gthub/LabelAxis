using System;
using System.Collections.Generic;
using System.Globalization;

namespace LabelAxis.Setup;

/// <summary>
/// Setup's words in the app's ten languages. Each entry lists them in the order of
/// <see cref="Languages"/>: English, Arabic, Kurdish (Sorani), Spanish, Persian, French, Hindi,
/// Dutch, Russian, Chinese (Simplified).
/// </summary>
internal static class Strings
{
    public static readonly string[] Languages = { "en", "ar", "ckb", "es", "fa", "fr", "hi", "nl", "ru", "zh-Hans" };

    public static readonly string[] LanguageNames = { "English", "العربية", "کوردی", "Español", "فارسی", "Français", "हिन्दी", "Nederlands", "Русский", "简体中文" };

    /// <summary>The version the Finish page's "New in" list is about; for another version it is not shown.</summary>
    public const string HighlightsVersion = "1.1.2";

    private static int _index;

    public static string Language => Languages[_index];

    public static bool RightToLeft => Language is "ar" or "fa" or "ckb";

    /// <summary>The name Inno Setup gives the language (its messages only show if something goes wrong).</summary>
    public static string InnoLanguage => Language switch
    {
        "ar" => "arabic",
        "fr" => "french",
        "es" => "spanish",
        "nl" => "dutch",
        "ru" => "russian",
        _ => "english",
    };

    public static void Use(string language) => _index = Math.Max(0, Array.IndexOf(Languages, language));

    /// <summary>Windows' own language when Setup has it, otherwise English.</summary>
    public static string FromWindows()
    {
        CultureInfo ui = CultureInfo.CurrentUICulture;
        string name = ui.Name;
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return "zh-Hans";
        }

        if (name.StartsWith("ku", StringComparison.OrdinalIgnoreCase) || name.StartsWith("ckb", StringComparison.OrdinalIgnoreCase))
        {
            return "ckb";
        }

        string two = ui.TwoLetterISOLanguageName;
        return Array.IndexOf(Languages, two) >= 0 ? two : "en";
    }

    public static string T(string key) => Table.TryGetValue(key, out string[]? all) ? all[_index] : key;

    public static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);

    private static readonly Dictionary<string, string[]> Table = new()
    {
        ["StepWelcome"] = new[] { "Welcome", "مرحبًا", "بەخێربێیت", "Bienvenida", "خوش آمدید", "Bienvenue", "स्वागत", "Welkom", "Добро пожаловать", "欢迎" },
        ["StepOptions"] = new[] { "Options", "الخيارات", "هەڵبژاردنەکان", "Opciones", "گزینه‌ها", "Options", "विकल्प", "Opties", "Параметры", "选项" },
        ["StepInstalling"] = new[] { "Installing", "التثبيت", "دامەزراندن", "Instalación", "نصب", "Installation", "इंस्टॉल", "Installeren", "Установка", "安装" },
        ["StepFinish"] = new[] { "Finish", "الانتهاء", "تەواو", "Fin", "پایان", "Terminé", "समाप्त", "Klaar", "Готово", "完成" },

        ["SetupVersion"] = new[] { "Setup  ·  version {0}", "الإعداد  ·  الإصدار {0}", "دامەزرێنەر  ·  وەشانی {0}", "Instalador  ·  versión {0}", "نصب  ·  نسخهٔ {0}", "Installation  ·  version {0}", "सेटअप  ·  संस्करण {0}", "Setup  ·  versie {0}", "Установка  ·  версия {0}", "安装程序  ·  版本 {0}" },
        ["OpenSource"] = new[] { "Free and open source  ·  MIT licence", "مجاني ومفتوح المصدر  ·  رخصة MIT", "بەخۆڕایی و سەرچاوە-کراوە  ·  مۆڵەتی MIT", "Gratuito y de código abierto  ·  licencia MIT", "رایگان و متن‌باز  ·  مجوز MIT", "Gratuit et open source  ·  licence MIT", "मुफ़्त और ओपन सोर्स  ·  MIT लाइसेंस", "Gratis en open source  ·  MIT-licentie", "Бесплатно, с открытым кодом  ·  лицензия MIT", "免费开源  ·  MIT 许可证" },

        ["WelcomeOver"] = new[] { "WELCOME", "مرحبًا", "بەخێربێیت", "BIENVENIDA", "خوش آمدید", "BIENVENUE", "स्वागत है", "WELKOM", "ДОБРО ПОЖАЛОВАТЬ", "欢迎" },
        ["WelcomeTitle"] = new[] { "Install LabelAxis", "تثبيت LabelAxis", "دامەزراندنی LabelAxis", "Instalar LabelAxis", "نصب LabelAxis", "Installer LabelAxis", "LabelAxis इंस्टॉल करें", "LabelAxis installeren", "Установка LabelAxis", "安装 LabelAxis" },
        ["UpdateTitle"] = new[] { "Update LabelAxis", "تحديث LabelAxis", "نوێکردنەوەی LabelAxis", "Actualizar LabelAxis", "به‌روزرسانی LabelAxis", "Mettre à jour LabelAxis", "LabelAxis अपडेट करें", "LabelAxis bijwerken", "Обновление LabelAxis", "更新 LabelAxis" },
        ["WelcomeBody"] = new[]
        {
            "Design labels and stickers, then print them on label sheets, thermal printers or to PDF.",
            "صمّم الملصقات والاستيكرات، ثم اطبعها على أوراق الملصقات أو الطابعات الحرارية أو بصيغة PDF.",
            "لەیبڵ و ستیکەر دیزاین بکە، پاشان لەسەر پەڕەی لەیبڵ، چاپکەری گەرمی یان وەک PDF چاپیان بکە.",
            "Diseñe etiquetas y pegatinas y luego imprímalas en hojas de etiquetas, impresoras térmicas o en PDF.",
            "برچسب و استیکر طراحی کنید، سپس آن‌ها را روی برگه‌های برچسب، چاپگرهای حرارتی یا به‌صورت PDF چاپ کنید.",
            "Créez des étiquettes et des autocollants, puis imprimez-les sur des planches, des imprimantes thermiques ou en PDF.",
            "लेबल और स्टिकर डिज़ाइन करें, फिर उन्हें लेबल शीट, थर्मल प्रिंटर या PDF में प्रिंट करें।",
            "Ontwerp etiketten en stickers en druk ze af op etiketvellen, thermische printers of als PDF.",
            "Создавайте этикетки и наклейки и печатайте их на листах этикеток, термопринтерах или в PDF.",
            "设计标签和贴纸，然后打印到标签纸、热敏打印机或 PDF。",
        },
        ["UpdateBody"] = new[]
        {
            "LabelAxis {0} is on this PC. This updates it to {1}; your settings, sheets and designs stay.",
            "يوجد LabelAxis {0} على هذا الحاسوب. سيُحدَّث إلى {1}، وتبقى إعداداتك وأوراقك وتصاميمك كما هي.",
            "LabelAxis {0} لەسەر ئەم کۆمپیوتەرەیە. ئەمە دەیکاتە {1}؛ ڕێکخستن، پەڕە و دیزاینەکانت دەمێننەوە.",
            "LabelAxis {0} está en este PC. Se actualizará a {1}; su configuración, hojas y diseños se conservan.",
            "LabelAxis {0} روی این رایانه است. به {1} به‌روز می‌شود؛ تنظیمات، برگه‌ها و طرح‌هایتان می‌مانند.",
            "LabelAxis {0} est sur ce PC. Il passe à la version {1} ; vos réglages, planches et modèles sont conservés.",
            "इस पीसी पर LabelAxis {0} है। यह इसे {1} में अपडेट करता है; आपकी सेटिंग्स, शीट और डिज़ाइन बने रहते हैं।",
            "LabelAxis {0} staat op deze pc. Dit werkt het bij naar {1}; uw instellingen, vellen en ontwerpen blijven.",
            "На этом ПК установлен LabelAxis {0}. Он будет обновлён до {1}; настройки, листы и макеты сохранятся.",
            "此电脑上已有 LabelAxis {0}。将更新到 {1}；您的设置、标签纸和设计都会保留。",
        },
        ["ReinstallTitle"] = new[] { "Reinstall LabelAxis", "إعادة تثبيت LabelAxis", "دووبارە دامەزراندنەوەی LabelAxis", "Reinstalar LabelAxis", "نصب دوبارهٔ LabelAxis", "Réinstaller LabelAxis", "LabelAxis फिर से इंस्टॉल करें", "LabelAxis opnieuw installeren", "Переустановка LabelAxis", "重新安装 LabelAxis" },
        ["ButtonReinstall"] = new[] { "Reinstall", "إعادة التثبيت", "دووبارە دامەزراندنەوە", "Reinstalar", "نصب دوباره", "Réinstaller", "फिर से इंस्टॉल करें", "Opnieuw installeren", "Переустановить", "重新安装" },
        ["ReinstallBody"] = new[]
        {
            "LabelAxis {0} is already on this PC. Installing it again puts back any missing or damaged files; your settings, sheets and designs stay.",
            "LabelAxis {0} موجود بالفعل على هذا الحاسوب. إعادة تثبيته تعيد أي ملفات مفقودة أو تالفة، وتبقى إعداداتك وأوراقك وتصاميمك كما هي.",
            "LabelAxis {0} پێشتر لەسەر ئەم کۆمپیوتەرەیە. دووبارە دامەزراندنەوەی هەر فایلێکی ونبوو یان تێکچوو دەگەڕێنێتەوە؛ ڕێکخستن، پەڕە و دیزاینەکانت دەمێننەوە.",
            "LabelAxis {0} ya está en este PC. Instalarlo de nuevo repone los archivos que falten o estén dañados; su configuración, hojas y diseños se conservan.",
            "LabelAxis {0} از قبل روی این رایانه است. نصب دوباره هر فایل گم‌شده یا آسیب‌دیده را برمی‌گرداند؛ تنظیمات، برگه‌ها و طرح‌هایتان می‌مانند.",
            "LabelAxis {0} est déjà sur ce PC. Le réinstaller remet les fichiers manquants ou abîmés ; vos réglages, planches et modèles sont conservés.",
            "इस पीसी पर LabelAxis {0} पहले से है। फिर से इंस्टॉल करने पर गायब या ख़राब फ़ाइलें वापस आ जाती हैं; आपकी सेटिंग्स, शीट और डिज़ाइन बने रहते हैं।",
            "LabelAxis {0} staat al op deze pc. Opnieuw installeren zet ontbrekende of beschadigde bestanden terug; uw instellingen, vellen en ontwerpen blijven.",
            "LabelAxis {0} уже установлен на этом ПК. Повторная установка вернёт недостающие или повреждённые файлы; настройки, листы и макеты сохранятся.",
            "此电脑上已有 LabelAxis {0}。重新安装会恢复缺失或损坏的文件；您的设置、标签纸和设计都会保留。",
        },
        ["NewerBody"] = new[]
        {
            "A newer LabelAxis ({0}) is on this PC. Installing {1} puts this older version in its place; your settings, sheets and designs stay.",
            "يوجد LabelAxis أحدث ({0}) على هذا الحاسوب. تثبيت {1} يضع هذا الإصدار الأقدم مكانه، وتبقى إعداداتك وأوراقك وتصاميمك كما هي.",
            "LabelAxisـێکی نوێتر ({0}) لەسەر ئەم کۆمپیوتەرەیە. دامەزراندنی {1} ئەم وەشانە کۆنترە لە جێگەی دادەنێت؛ ڕێکخستن، پەڕە و دیزاینەکانت دەمێننەوە.",
            "Hay un LabelAxis más reciente ({0}) en este PC. Instalar {1} pone esta versión anterior en su lugar; su configuración, hojas y diseños se conservan.",
            "LabelAxis تازه‌تری ({0}) روی این رایانه است. نصب {1} این نسخهٔ قدیمی‌تر را جای آن می‌گذارد؛ تنظیمات، برگه‌ها و طرح‌هایتان می‌مانند.",
            "Un LabelAxis plus récent ({0}) est sur ce PC. Installer la {1} met cette version plus ancienne à sa place ; vos réglages, planches et modèles sont conservés.",
            "इस पीसी पर नया LabelAxis ({0}) है। {1} इंस्टॉल करने पर यह पुराना संस्करण उसकी जगह आ जाता है; आपकी सेटिंग्स, शीट और डिज़ाइन बने रहते हैं।",
            "Er staat een nieuwere LabelAxis ({0}) op deze pc. Installeren van {1} zet deze oudere versie op zijn plaats; uw instellingen, vellen en ontwerpen blijven.",
            "На этом ПК установлена более новая версия LabelAxis ({0}). Установка {1} заменит её этой, более старой; настройки, листы и макеты сохранятся.",
            "此电脑上有更新的 LabelAxis（{0}）。安装 {1} 会用这个旧版本替换它；您的设置、标签纸和设计都会保留。",
        },
        ["Feature1Title"] =new[] { "3,700 label sheets", "3,700 ورقة ملصقات", "3,700 پەڕەی لەیبڵ", "3.700 hojas de etiquetas", "3,700 برگهٔ برچسب", "3 700 planches d'étiquettes", "3,700 लेबल शीट", "3.700 etiketvellen", "3 700 листов этикеток", "3,700 种标签纸" },
        ["Feature1Text"] = new[]
        {
            "Avery, HERMA, Formtec, APLI and nine more makers", "Avery وHERMA وFormtec وAPLI وتسعة مصنّعين آخرين", "Avery، HERMA، Formtec، APLI و نۆ بەرهەمهێنەری تر",
            "Avery, HERMA, Formtec, APLI y nueve fabricantes más", "Avery، HERMA، Formtec، APLI و نُه سازندهٔ دیگر", "Avery, HERMA, Formtec, APLI et neuf autres fabricants",
            "Avery, HERMA, Formtec, APLI और नौ अन्य निर्माता", "Avery, HERMA, Formtec, APLI en negen andere fabrikanten", "Avery, HERMA, Formtec, APLI и ещё девять производителей", "Avery、HERMA、Formtec、APLI 等 13 家制造商",
        },
        ["Feature2Title"] = new[] { "Barcodes that scan", "باركودات تُمسح ضوئيًا", "باڕکۆدی سکانکراو", "Códigos de barras que se escanean", "بارکدهایی که اسکن می‌شوند", "Des codes-barres qui se scannent", "स्कैन होने वाले बारकोड", "Barcodes die scannen", "Штрихкоды, которые сканируются", "可扫描的条码" },
        ["Feature2Text"] = new[]
        {
            "Code 128, EAN, QR and GS1, with a print check", "Code 128 وEAN وQR وGS1، مع فحص للطباعة", "Code 128، EAN، QR و GS1، لەگەڵ پشکنینی چاپ",
            "Code 128, EAN, QR y GS1, con comprobación de impresión", "Code 128، EAN، QR و GS1، همراه با بررسی چاپ", "Code 128, EAN, QR et GS1, avec contrôle d'impression",
            "Code 128, EAN, QR और GS1, प्रिंट जाँच के साथ", "Code 128, EAN, QR en GS1, met afdrukcontrole", "Code 128, EAN, QR и GS1, с проверкой печати", "Code 128、EAN、QR 和 GS1，并可检查打印",
        },
        ["Feature3Title"] = new[] { "Print from your data", "اطبع من بياناتك", "لە داتاکانتەوە چاپ بکە", "Imprima desde sus datos", "از داده‌هایتان چاپ کنید", "Imprimez depuis vos données", "अपने डेटा से प्रिंट करें", "Afdrukken uit uw gegevens", "Печать из ваших данных", "从数据打印" },
        ["Feature3Text"] = new[]
        {
            "Excel, CSV and databases, one label per record", "Excel وCSV وقواعد البيانات، ملصق لكل سجل", "Excel، CSV و بنکەدراوە، لەیبڵێک بۆ هەر تۆمارێک",
            "Excel, CSV y bases de datos, una etiqueta por registro", "Excel، CSV و پایگاه داده، یک برچسب برای هر رکورد", "Excel, CSV et bases de données, une étiquette par enregistrement",
            "Excel, CSV और डेटाबेस, हर रिकॉर्ड का एक लेबल", "Excel, CSV en databases, één etiket per record", "Excel, CSV и базы данных, по этикетке на запись", "Excel、CSV 和数据库，每条记录一个标签",
        },
        ["Accept"] = new[] { "I accept the {0}", "أوافق على {0}", "{0} پەسەند دەکەم", "Acepto la {0}", "{0} را می‌پذیرم", "J'accepte la {0}", "मैं {0} स्वीकार करता/करती हूँ", "Ik ga akkoord met de {0}", "Я принимаю {0}", "我接受 {0}" },
        ["Licence"] = new[] { "MIT licence", "رخصة MIT", "مۆڵەتی MIT", "licencia MIT", "مجوز MIT", "licence MIT", "MIT लाइसेंस", "MIT-licentie", "лицензию MIT", "MIT 许可证" },
        ["FooterJustMe"] = new[]
        {
            "For you only  ·  {0} MB  ·  no administrator rights", "لك وحدك  ·  {0} ميغابايت  ·  دون صلاحيات المسؤول", "تەنها بۆ تۆ  ·  {0} MB  ·  بێ مافی بەڕێوەبەر",
            "Solo para usted  ·  {0} MB  ·  sin permisos de administrador", "فقط برای شما  ·  {0} مگابایت  ·  بدون دسترسی مدیر", "Pour vous seul  ·  {0} Mo  ·  sans droits d'administrateur",
            "केवल आपके लिए  ·  {0} MB  ·  व्यवस्थापक अधिकार नहीं", "Alleen voor u  ·  {0} MB  ·  geen beheerdersrechten", "Только для вас  ·  {0} МБ  ·  без прав администратора", "仅限您  ·  {0} MB  ·  无需管理员权限",
        },
        ["FooterEveryone"] = new[]
        {
            "For everyone on this PC  ·  {0} MB", "لكل مستخدمي هذا الحاسوب  ·  {0} ميغابايت", "بۆ هەموو کەسێک لەم کۆمپیوتەرە  ·  {0} MB", "Para todos en este PC  ·  {0} MB", "برای همهٔ کاربران این رایانه  ·  {0} مگابایت",
            "Pour tous les utilisateurs de ce PC  ·  {0} Mo", "इस पीसी के सभी लोगों के लिए  ·  {0} MB", "Voor iedereen op deze pc  ·  {0} MB", "Для всех на этом ПК  ·  {0} МБ", "此电脑上的所有人  ·  {0} MB",
        },

        ["ButtonOptions"] = new[] { "Options", "الخيارات", "هەڵبژاردنەکان", "Opciones", "گزینه‌ها", "Options", "विकल्प", "Opties", "Параметры", "选项" },
        ["ButtonInstall"] = new[] { "Install", "تثبيت", "دامەزراندن", "Instalar", "نصب", "Installer", "इंस्टॉल करें", "Installeren", "Установить", "安装" },
        ["ButtonUpdate"] = new[] { "Update", "تحديث", "نوێکردنەوە", "Actualizar", "به‌روزرسانی", "Mettre à jour", "अपडेट करें", "Bijwerken", "Обновить", "更新" },
        ["ButtonBack"] = new[] { "Back", "رجوع", "گەڕانەوە", "Atrás", "بازگشت", "Retour", "वापस", "Terug", "Назад", "返回" },
        ["ButtonCancel"] = new[] { "Cancel", "إلغاء", "پاشگەزبوونەوە", "Cancelar", "لغو", "Annuler", "रद्द करें", "Annuleren", "Отмена", "取消" },
        ["ButtonFinish"] = new[] { "Finish", "إنهاء", "تەواو", "Finalizar", "پایان", "Terminer", "समाप्त", "Voltooien", "Готово", "完成" },
        ["ButtonClose"] = new[] { "Close", "إغلاق", "داخستن", "Cerrar", "بستن", "Fermer", "बंद करें", "Sluiten", "Закрыть", "关闭" },
        ["ButtonTryAgain"] = new[] { "Try again", "أعد المحاولة", "دووبارە هەوڵ بدەرەوە", "Reintentar", "دوباره امتحان کنید", "Réessayer", "फिर कोशिश करें", "Opnieuw proberen", "Повторить", "重试" },
        ["ButtonBrowse"] = new[] { "Browse…", "استعراض…", "گەڕان…", "Examinar…", "مرور…", "Parcourir…", "ब्राउज़ करें…", "Bladeren…", "Обзор…", "浏览…" },
        ["Minimise"] = new[] { "Minimise", "تصغير", "بچووککردنەوە", "Minimizar", "کوچک کردن", "Réduire", "छोटा करें", "Minimaliseren", "Свернуть", "最小化" },

        ["OptionsOver"] = new[] { "OPTIONS", "الخيارات", "هەڵبژاردنەکان", "OPCIONES", "گزینه‌ها", "OPTIONS", "विकल्प", "OPTIES", "ПАРАМЕТРЫ", "选项" },
        ["OptionsTitle"] = new[] { "Choose how to install", "اختر طريقة التثبيت", "شێوازی دامەزراندن هەڵبژێرە", "Elija cómo instalar", "شیوهٔ نصب را انتخاب کنید", "Choisissez comment installer", "चुनें कि कैसे इंस्टॉल करना है", "Kies hoe u installeert", "Выберите способ установки", "选择安装方式" },
        ["InstallFor"] = new[] { "INSTALL FOR", "التثبيت لـ", "دامەزراندن بۆ", "INSTALAR PARA", "نصب برای", "INSTALLER POUR", "किसके लिए इंस्टॉल करें", "INSTALLEREN VOOR", "УСТАНОВИТЬ ДЛЯ", "安装给" },
        ["JustMe"] = new[] { "Just me", "أنا فقط", "تەنها من", "Solo para mí", "فقط من", "Moi uniquement", "केवल मैं", "Alleen ik", "Только для меня", "仅我" },
        ["JustMeText"] = new[] { "No administrator rights needed", "لا يحتاج إلى صلاحيات المسؤول", "پێویستی بە مافی بەڕێوەبەر نییە", "No necesita permisos de administrador", "به دسترسی مدیر نیاز ندارد", "Aucun droit d'administrateur requis", "व्यवस्थापक अधिकार की ज़रूरत नहीं", "Geen beheerdersrechten nodig", "Права администратора не нужны", "无需管理员权限" },
        ["Everyone"] = new[] { "Everyone on this PC", "كل مستخدمي هذا الحاسوب", "هەموو کەسێک لەم کۆمپیوتەرە", "Todos en este PC", "همهٔ کاربران این رایانه", "Tous les utilisateurs", "इस पीसी पर सभी", "Iedereen op deze pc", "Все пользователи ПК", "此电脑上的所有人" },
        ["EveryoneText"] = new[] { "Asks for administrator rights", "يطلب صلاحيات المسؤول", "مافی بەڕێوەبەر داوا دەکات", "Pide permisos de administrador", "دسترسی مدیر را می‌خواهد", "Demande les droits d'administrateur", "व्यवस्थापक अधिकार माँगता है", "Vraagt om beheerdersrechten", "Запросит права администратора", "需要管理员权限" },
        ["Folder"] = new[] { "FOLDER", "المجلد", "بوخچە", "CARPETA", "پوشه", "DOSSIER", "फ़ोल्डर", "MAP", "ПАПКА", "文件夹" },
        ["ChooseFolder"] = new[] { "Choose where to install LabelAxis", "اختر مكان تثبيت LabelAxis", "شوێنی دامەزراندنی LabelAxis هەڵبژێرە", "Elija dónde instalar LabelAxis", "محل نصب LabelAxis را انتخاب کنید", "Choisissez où installer LabelAxis", "चुनें कि LabelAxis कहाँ इंस्टॉल हो", "Kies waar LabelAxis komt", "Выберите, куда установить LabelAxis", "选择 LabelAxis 的安装位置" },
        ["Associate"] = new[] { "Open .labelproj projects with LabelAxis", "فتح مشاريع ‎.labelproj‎ بواسطة LabelAxis", "پڕۆژەکانی ‎.labelproj‎ بە LabelAxis بکەرەوە", "Abrir proyectos .labelproj con LabelAxis", "پروژه‌های ‎.labelproj‎ با LabelAxis باز شوند", "Ouvrir les projets .labelproj avec LabelAxis", ".labelproj प्रोजेक्ट LabelAxis से खोलें", ".labelproj-projecten openen met LabelAxis", "Открывать проекты .labelproj в LabelAxis", "用 LabelAxis 打开 .labelproj 项目" },
        ["AssociateText"] = new[] { "Double-click a project to open it", "انقر نقرًا مزدوجًا على المشروع لفتحه", "دوو کرتە لە پڕۆژەیەک بکە بۆ کردنەوەی", "Haga doble clic en un proyecto para abrirlo", "برای باز کردن پروژه روی آن دوبار کلیک کنید", "Double-cliquez sur un projet pour l'ouvrir", "खोलने के लिए प्रोजेक्ट पर डबल-क्लिक करें", "Dubbelklik op een project om het te openen", "Двойной щелчок открывает проект", "双击项目即可打开" },
        ["Desktop"] = new[] { "Desktop shortcut", "اختصار على سطح المكتب", "قەدبڕ لەسەر ڕووی مێز", "Acceso directo en el escritorio", "میان‌بر روی دسکتاپ", "Raccourci sur le Bureau", "डेस्कटॉप शॉर्टकट", "Snelkoppeling op het bureaublad", "Ярлык на рабочем столе", "桌面快捷方式" },
        ["DesktopText"] = new[] { "LabelAxis is in the Start menu either way", "يكون LabelAxis في قائمة ابدأ في كل الأحوال", "LabelAxis لە هەر حاڵەتێکدا لە لیستی Start دایە", "LabelAxis está en el menú Inicio de todos modos", "LabelAxis در هر حال در منوی Start هست", "LabelAxis est dans le menu Démarrer dans tous les cas", "LabelAxis हर हाल में Start मेनू में रहता है", "LabelAxis staat hoe dan ook in het Startmenu", "LabelAxis в любом случае будет в меню «Пуск»", "无论如何，LabelAxis 都在开始菜单中" },
        ["UpdatingHere"] = new[]
        {
            "LabelAxis is already installed here, so it is updated in the same place.", "LabelAxis مثبّت هنا بالفعل، لذا يُحدَّث في المكان نفسه.", "LabelAxis پێشتر لێرە دامەزراوە، بۆیە لە هەمان شوێن نوێ دەکرێتەوە.",
            "LabelAxis ya está instalado aquí, así que se actualiza en el mismo lugar.", "LabelAxis از قبل اینجا نصب است، پس در همان جا به‌روز می‌شود.", "LabelAxis est déjà installé ici ; il est donc mis à jour au même endroit.",
            "LabelAxis यहाँ पहले से इंस्टॉल है, इसलिए उसी जगह अपडेट होगा।", "LabelAxis staat hier al, dus het wordt op dezelfde plek bijgewerkt.", "LabelAxis уже установлен здесь, поэтому обновится на том же месте.", "LabelAxis 已安装在此处，因此将在原位置更新。",
        },
        ["SizeSmall"] = new[]
        {
            "{0} MB, plus .NET 10 (about 60 MB) if this PC does not have it yet", "{0} ميغابايت، إضافةً إلى ‎.NET 10‎ (نحو 60 ميغابايت) إن لم يكن على الحاسوب", "{0} MB، لەگەڵ ‎.NET 10‎ (نزیکەی 60 MB) ئەگەر لەسەر کۆمپیوتەرەکە نەبێت",
            "{0} MB, más .NET 10 (unos 60 MB) si este PC aún no lo tiene", "{0} مگابایت، به‌اضافهٔ ‎.NET 10‎ (حدود 60 مگابایت) اگر روی رایانه نباشد", "{0} Mo, plus .NET 10 (environ 60 Mo) si ce PC ne l'a pas encore",
            "{0} MB, और .NET 10 (लगभग 60 MB) यदि इस पीसी पर नहीं है", "{0} MB, plus .NET 10 (ongeveer 60 MB) als deze pc het nog niet heeft", "{0} МБ и .NET 10 (около 60 МБ), если его ещё нет на ПК", "{0} MB，如果此电脑还没有 .NET 10，另需约 60 MB",
        },
        ["SizeOffline"] = new[] { "{0} MB, everything included", "{0} ميغابايت، كل ما يلزم مضمَّن", "{0} MB، هەموو شتێک لەخۆدەگرێت", "{0} MB, todo incluido", "{0} مگابایت، همه چیز همراه است", "{0} Mo, tout compris", "{0} MB, सब कुछ शामिल", "{0} MB, alles inbegrepen", "{0} МБ, всё включено", "{0} MB，已包含全部内容" },

        ["InstallingOver"] = new[] { "INSTALLING", "جارٍ التثبيت", "دامەزراندن", "INSTALANDO", "در حال نصب", "INSTALLATION", "इंस्टॉल हो रहा है", "INSTALLEREN", "УСТАНОВКА", "正在安装" },
        ["InstallingTitle"] = new[] { "Installing LabelAxis…", "جارٍ تثبيت LabelAxis…", "LabelAxis دادەمەزرێت…", "Instalando LabelAxis…", "در حال نصب LabelAxis…", "Installation de LabelAxis…", "LabelAxis इंस्टॉल हो रहा है…", "LabelAxis wordt geïnstalleerd…", "Установка LabelAxis…", "正在安装 LabelAxis…" },
        ["TaskCheck"] = new[] { "Checked this PC", "تم فحص هذا الحاسوب", "ئەم کۆمپیوتەرە پشکنرا", "PC comprobado", "این رایانه بررسی شد", "PC vérifié", "इस पीसी की जाँच हुई", "Deze pc gecontroleerd", "ПК проверен", "已检查此电脑" },
        ["TaskRuntime"] = new[] { "Get .NET 10 from Microsoft", "جلب ‎.NET 10‎ من Microsoft", "وەرگرتنی ‎.NET 10‎ لە Microsoft", "Obtener .NET 10 de Microsoft", "دریافت ‎.NET 10‎ از Microsoft", "Obtenir .NET 10 auprès de Microsoft", "Microsoft से .NET 10 लेना", ".NET 10 ophalen bij Microsoft", "Получение .NET 10 от Microsoft", "从 Microsoft 获取 .NET 10" },
        ["TaskRuntimeNote"] = new[] { "Only needed once on this PC", "يلزم مرة واحدة فقط على هذا الحاسوب", "تەنها یەک جار لەم کۆمپیوتەرە پێویستە", "Solo hace falta una vez en este PC", "فقط یک بار روی این رایانه لازم است", "Une seule fois sur ce PC", "इस पीसी पर बस एक बार ज़रूरी", "Maar één keer nodig op deze pc", "Нужно только один раз на этом ПК", "此电脑只需一次" },
        ["TaskCopy"] = new[] { "Copy LabelAxis", "نسخ LabelAxis", "کۆپیکردنی LabelAxis", "Copiar LabelAxis", "کپی LabelAxis", "Copier LabelAxis", "LabelAxis कॉपी करना", "LabelAxis kopiëren", "Копирование LabelAxis", "复制 LabelAxis" },
        ["TaskFinish"] = new[] { "Add the shortcut and file type", "إضافة الاختصار ونوع الملف", "زیادکردنی قەدبڕ و جۆری فایل", "Añadir el acceso directo y el tipo de archivo", "افزودن میان‌بر و نوع فایل", "Ajouter le raccourci et le type de fichier", "शॉर्टकट और फ़ाइल प्रकार जोड़ना", "Snelkoppeling en bestandstype toevoegen", "Ярлык и тип файла", "添加快捷方式和文件类型" },
        ["Preparing"] = new[] { "Getting ready…", "جارٍ التحضير…", "ئامادەکاری…", "Preparando…", "در حال آماده‌سازی…", "Préparation…", "तैयारी हो रही है…", "Voorbereiden…", "Подготовка…", "正在准备…" },
        ["Downloading"] = new[] { "Downloading .NET 10  ·  {0} of {1} MB", "تنزيل ‎.NET 10‎  ·  {0} من {1} ميغابايت", "داگرتنی ‎.NET 10‎  ·  {0} لە {1} MB", "Descargando .NET 10  ·  {0} de {1} MB", "دانلود ‎.NET 10‎  ·  {0} از {1} مگابایت", "Téléchargement de .NET 10  ·  {0} sur {1} Mo", ".NET 10 डाउनलोड  ·  {1} में से {0} MB", ".NET 10 downloaden  ·  {0} van {1} MB", "Загрузка .NET 10  ·  {0} из {1} МБ", "正在下载 .NET 10  ·  {0} / {1} MB" },
        ["InstallingRuntime"] = new[] { "Installing .NET 10…", "جارٍ تثبيت ‎.NET 10‎…", "‎.NET 10‎ دادەمەزرێت…", "Instalando .NET 10…", "در حال نصب ‎.NET 10‎…", "Installation de .NET 10…", ".NET 10 इंस्टॉल हो रहा है…", ".NET 10 installeren…", "Установка .NET 10…", "正在安装 .NET 10…" },
        ["CopyingFile"] = new[] { "Copying files  ·  {0}", "نسخ الملفات  ·  {0}", "کۆپیکردنی فایلەکان  ·  {0}", "Copiando archivos  ·  {0}", "کپی فایل‌ها  ·  {0}", "Copie des fichiers  ·  {0}", "फ़ाइलें कॉपी हो रही हैं  ·  {0}", "Bestanden kopiëren  ·  {0}", "Копирование файлов  ·  {0}", "正在复制文件  ·  {0}" },
        ["Finishing"] = new[] { "Adding the shortcut and file type…", "إضافة الاختصار ونوع الملف…", "قەدبڕ و جۆری فایل زیاد دەکرێن…", "Añadiendo el acceso directo y el tipo de archivo…", "در حال افزودن میان‌بر و نوع فایل…", "Ajout du raccourci et du type de fichier…", "शॉर्टकट और फ़ाइल प्रकार जोड़े जा रहे हैं…", "Snelkoppeling en bestandstype toevoegen…", "Добавление ярлыка и типа файла…", "正在添加快捷方式和文件类型…" },
        ["TipTitle"] = new[] { "Your settings stay.", "إعداداتك باقية.", "ڕێکخستنەکانت دەمێننەوە.", "Su configuración se conserva.", "تنظیمات شما می‌ماند.", "Vos réglages restent.", "आपकी सेटिंग्स बनी रहती हैं।", "Uw instellingen blijven.", "Настройки сохраняются.", "您的设置会保留。" },
        ["TipText"] = new[]
        {
            "Label sheets, designs and printer calibration are kept when you update.", "تُحفظ أوراق الملصقات والتصاميم ومعايرة الطابعة عند التحديث.", "پەڕەی لەیبڵ، دیزاین و ڕێکخستنی چاپکەر لە کاتی نوێکردنەوەدا دەمێننەوە.",
            "Las hojas de etiquetas, los diseños y la calibración de la impresora se conservan al actualizar.", "برگه‌های برچسب، طرح‌ها و کالیبراسیون چاپگر هنگام به‌روزرسانی می‌مانند.", "Les planches, les modèles et l'étalonnage de l'imprimante sont conservés lors des mises à jour.",
            "अपडेट करने पर लेबल शीट, डिज़ाइन और प्रिंटर कैलिब्रेशन बने रहते हैं।", "Etiketvellen, ontwerpen en printerkalibratie blijven bij het bijwerken.", "Листы этикеток, макеты и калибровка принтера сохраняются при обновлении.", "更新时会保留标签纸、设计和打印机校准。",
        },

        ["ReadyTitle"] = new[] { "LabelAxis is ready", "LabelAxis جاهز", "LabelAxis ئامادەیە", "LabelAxis está listo", "LabelAxis آماده است", "LabelAxis est prêt", "LabelAxis तैयार है", "LabelAxis is klaar", "LabelAxis готов", "LabelAxis 已就绪" },
        ["ReadyBody"] = new[]
        {
            "Find it in the Start menu. New versions arrive on their own.", "ستجده في قائمة ابدأ. والإصدارات الجديدة تصل تلقائيًا.", "لە لیستی Start دەیدۆزیتەوە. وەشانە نوێیەکان خۆیان دێن.",
            "Lo encontrará en el menú Inicio. Las versiones nuevas llegan solas.", "آن را در منوی Start پیدا می‌کنید. نسخه‌های تازه خودکار می‌رسند.", "Il est dans le menu Démarrer. Les nouvelles versions arrivent d'elles-mêmes.",
            "यह Start मेनू में मिलेगा। नए संस्करण अपने आप आते हैं।", "U vindt het in het Startmenu. Nieuwe versies komen vanzelf.", "Он в меню «Пуск». Новые версии приходят сами.", "可在开始菜单中找到它。新版本会自动推送。",
        },
        ["NewIn"] = new[] { "NEW IN {0}", "جديد في {0}", "نوێ لە {0}", "NOVEDADES DE {0}", "تازه‌های {0}", "NOUVEAUTÉS DE LA {0}", "{0} में नया", "NIEUW IN {0}", "НОВОЕ В {0}", "{0} 新功能" },
        ["Highlight1"] = new[] { "About 3,700 label sheets from 13 makers", "نحو 3,700 ورقة ملصقات من 13 مصنّعًا", "نزیکەی 3,700 پەڕەی لەیبڵ لە 13 بەرهەمهێنەر", "Unas 3.700 hojas de etiquetas de 13 fabricantes", "حدود 3,700 برگهٔ برچسب از 13 سازنده", "Environ 3 700 planches de 13 fabricants", "13 निर्माताओं की लगभग 3,700 लेबल शीट", "Ongeveer 3.700 etiketvellen van 13 fabrikanten", "Около 3 700 листов этикеток 13 производителей", "来自 13 家制造商的约 3,700 种标签纸" },
        ["Highlight2"] = new[] { "A page for each record, and numbered copies", "صفحة لكل سجل، ونُسخ مرقّمة", "پەڕەیەک بۆ هەر تۆمارێک، و کۆپی ژمارەدار", "Una página por registro y copias numeradas", "یک صفحه برای هر رکورد، و نسخه‌های شماره‌دار", "Une page par enregistrement, et des copies numérotées", "हर रिकॉर्ड का एक पेज, और क्रमांकित प्रतियाँ", "Een pagina per record, en genummerde kopieën", "Страница на каждую запись и нумерованные копии", "每条记录一页，以及编号副本" },
        ["Highlight3"] = new[] { "Undo history, curves and a barcode scanner check", "سجل التراجع والمنحنيات وفحص بماسح الباركود", "مێژووی گەڕانەوە، چەماوە و پشکنین بە سکانەری باڕکۆد", "Historial de deshacer, curvas y comprobación con lector", "تاریخچهٔ واگرد، منحنی‌ها و بررسی با اسکنر بارکد", "Historique d'annulation, courbes et contrôle au lecteur", "अनडू इतिहास, कर्व और बारकोड स्कैनर जाँच", "Ongedaan-maken-geschiedenis, curven en scannercontrole", "История отмены, кривые и проверка сканером", "撤销历史、曲线和扫码器检查" },
        ["OpenNow"] = new[] { "Open LabelAxis now", "افتح LabelAxis الآن", "ئێستا LabelAxis بکەرەوە", "Abrir LabelAxis ahora", "اکنون LabelAxis را باز کن", "Ouvrir LabelAxis maintenant", "अभी LabelAxis खोलें", "LabelAxis nu openen", "Открыть LabelAxis сейчас", "立即打开 LabelAxis" },

        ["AppOpen"] = new[]
        {
            "LabelAxis is open. Setup closes it, so save your work first.", "LabelAxis مفتوح. سيغلقه الإعداد، فاحفظ عملك أولًا.", "LabelAxis کراوەیە. دامەزرێنەر دایدەخات، بۆیە سەرەتا کارەکەت پاشەکەوت بکە.",
            "LabelAxis está abierto. El instalador lo cerrará: guarde su trabajo antes.", "LabelAxis باز است. نصب آن را می‌بندد، پس اول کارتان را ذخیره کنید.", "LabelAxis est ouvert. L'installation va le fermer : enregistrez d'abord votre travail.",
            "LabelAxis खुला है। सेटअप इसे बंद करेगा, इसलिए पहले अपना काम सहेजें।", "LabelAxis is open. Setup sluit het, dus sla eerst uw werk op.", "LabelAxis открыт. Установка закроет его — сначала сохраните работу.", "LabelAxis 正在运行。安装程序会将其关闭，请先保存您的工作。",
        },
        ["FailedTitle"] = new[] { "Setup could not finish", "تعذّر إكمال الإعداد", "دامەزراندن تەواو نەبوو", "No se pudo completar la instalación", "نصب کامل نشد", "L'installation n'a pas abouti", "सेटअप पूरा नहीं हो सका", "Setup kon niet worden voltooid", "Не удалось завершить установку", "安装未能完成" },
        ["ErrorAdmin"] = new[]
        {
            "Installing for everyone needs administrator rights, and Windows did not give them. Choose “Just me”, or try again.",
            "التثبيت لكل المستخدمين يحتاج إلى صلاحيات المسؤول، ولم يمنحها Windows. اختر «أنا فقط» أو أعد المحاولة.",
            "دامەزراندن بۆ هەمووان مافی بەڕێوەبەری پێویستە و Windows نەیدا. «تەنها من» هەڵبژێرە، یان دووبارە هەوڵ بدەرەوە.",
            "Instalar para todos necesita permisos de administrador y Windows no los concedió. Elija «Solo para mí» o vuelva a intentarlo.",
            "نصب برای همه به دسترسی مدیر نیاز دارد و Windows آن را نداد. «فقط من» را انتخاب کنید یا دوباره امتحان کنید.",
            "Installer pour tous demande les droits d'administrateur, que Windows n'a pas accordés. Choisissez « Moi uniquement » ou réessayez.",
            "सबके लिए इंस्टॉल करने को व्यवस्थापक अधिकार चाहिए, जो Windows ने नहीं दिए। “केवल मैं” चुनें या फिर कोशिश करें।",
            "Installeren voor iedereen vraagt beheerdersrechten, en Windows gaf ze niet. Kies ‘Alleen ik’ of probeer het opnieuw.",
            "Для установки для всех нужны права администратора, а Windows их не дала. Выберите «Только для меня» или повторите.",
            "为所有人安装需要管理员权限，但 Windows 未授予。请选择“仅我”，或重试。",
        },
        ["ErrorExit"] = new[]
        {
            "The installer stopped (error {0}). Try again; if it happens again, use the offline installer.", "توقّف المثبّت (الخطأ {0}). أعد المحاولة؛ وإن تكرر ذلك فاستخدم المثبّت دون اتصال.", "دامەزرێنەر وەستا (هەڵەی {0}). دووبارە هەوڵ بدەرەوە؛ ئەگەر دووبارە بووەوە، دامەزرێنەری بێ ئینتەرنێت بەکاربهێنە.",
            "El instalador se detuvo (error {0}). Vuelva a intentarlo; si se repite, use el instalador sin conexión.", "نصب‌کننده متوقف شد (خطای {0}). دوباره امتحان کنید؛ اگر تکرار شد، از نصب‌کنندهٔ آفلاین استفاده کنید.", "Le programme d'installation s'est arrêté (erreur {0}). Réessayez ; si cela se reproduit, utilisez l'installateur hors ligne.",
            "इंस्टॉलर रुक गया (त्रुटि {0})। फिर कोशिश करें; दोबारा हो तो ऑफ़लाइन इंस्टॉलर इस्तेमाल करें।", "Het installatieprogramma stopte (fout {0}). Probeer opnieuw; gebeurt het weer, gebruik dan het offline installatieprogramma.", "Установщик остановился (ошибка {0}). Повторите; если снова — используйте автономный установщик.", "安装程序已停止（错误 {0}）。请重试；如果再次出现，请使用离线安装程序。",
        },
        ["ErrorSpace"] = new[]
        {
            "There is not enough free space on {0}: LabelAxis needs {1} MB.", "لا توجد مساحة كافية على {0}: يحتاج LabelAxis إلى {1} ميغابايت.", "بۆشایی پێویست لەسەر {0} نییە: LabelAxis پێویستی بە {1} MB ە.",
            "No hay espacio libre suficiente en {0}: LabelAxis necesita {1} MB.", "فضای خالی کافی روی {0} نیست: LabelAxis به {1} مگابایت نیاز دارد.", "Pas assez d'espace libre sur {0} : LabelAxis a besoin de {1} Mo.",
            "{0} पर पर्याप्त खाली जगह नहीं है: LabelAxis को {1} MB चाहिए।", "Niet genoeg vrije ruimte op {0}: LabelAxis heeft {1} MB nodig.", "Недостаточно места на {0}: LabelAxis нужно {1} МБ.", "{0} 上的可用空间不足：LabelAxis 需要 {1} MB。",
        },
        ["ErrorDownload"] = new[]
        {
            "Microsoft .NET 10 could not be downloaded. Check the internet connection and try again, or use the offline installer.", "تعذّر تنزيل Microsoft .NET 10. تحقّق من الاتصال بالإنترنت وأعد المحاولة، أو استخدم المثبّت دون اتصال.", "Microsoft .NET 10 دانەگیرا. پەیوەندی ئینتەرنێت بپشکنە و دووبارە هەوڵ بدەرەوە، یان دامەزرێنەری بێ ئینتەرنێت بەکاربهێنە.",
            "No se pudo descargar Microsoft .NET 10. Compruebe la conexión a Internet y vuelva a intentarlo, o use el instalador sin conexión.", "Microsoft .NET 10 دانلود نشد. اتصال اینترنت را بررسی و دوباره امتحان کنید، یا از نصب‌کنندهٔ آفلاین استفاده کنید.", "Microsoft .NET 10 n'a pas pu être téléchargé. Vérifiez la connexion Internet et réessayez, ou utilisez l'installateur hors ligne.",
            "Microsoft .NET 10 डाउनलोड नहीं हो सका। इंटरनेट कनेक्शन जाँचें और फिर कोशिश करें, या ऑफ़लाइन इंस्टॉलर इस्तेमाल करें।", "Microsoft .NET 10 kon niet worden gedownload. Controleer de internetverbinding en probeer opnieuw, of gebruik het offline installatieprogramma.", "Не удалось загрузить Microsoft .NET 10. Проверьте подключение к Интернету и повторите или используйте автономный установщик.", "无法下载 Microsoft .NET 10。请检查网络连接后重试，或使用离线安装程序。",
        },
        ["ErrorRuntime"] = new[]
        {
            "Microsoft .NET 10 could not be installed (error {0}). Try again, or use the offline installer.", "تعذّر تثبيت Microsoft .NET 10 (الخطأ {0}). أعد المحاولة، أو استخدم المثبّت دون اتصال.", "Microsoft .NET 10 دانەمەزرا (هەڵەی {0}). دووبارە هەوڵ بدەرەوە، یان دامەزرێنەری بێ ئینتەرنێت بەکاربهێنە.",
            "No se pudo instalar Microsoft .NET 10 (error {0}). Vuelva a intentarlo o use el instalador sin conexión.", "Microsoft .NET 10 نصب نشد (خطای {0}). دوباره امتحان کنید، یا از نصب‌کنندهٔ آفلاین استفاده کنید.", "Microsoft .NET 10 n'a pas pu être installé (erreur {0}). Réessayez, ou utilisez l'installateur hors ligne.",
            "Microsoft .NET 10 इंस्टॉल नहीं हो सका (त्रुटि {0})। फिर कोशिश करें, या ऑफ़लाइन इंस्टॉलर इस्तेमाल करें।", "Microsoft .NET 10 kon niet worden geïnstalleerd (fout {0}). Probeer opnieuw, of gebruik het offline installatieprogramma.", "Не удалось установить Microsoft .NET 10 (ошибка {0}). Повторите или используйте автономный установщик.", "无法安装 Microsoft .NET 10（错误 {0}）。请重试，或使用离线安装程序。",
        },
        ["ErrorMissing"] = new[]
        {
            "This Setup is incomplete: it has no installer inside. Download it again.", "هذا الإعداد غير مكتمل: لا يحتوي على المثبّت. نزّله مرة أخرى.", "ئەم دامەزرێنەرە ناتەواوە: دامەزرێنەری تێدا نییە. دووبارە دایبگرە.",
            "Este instalador está incompleto: no lleva el programa de instalación. Descárguelo de nuevo.", "این فایل نصب ناقص است: نصب‌کننده درونش نیست. دوباره دانلودش کنید.", "Ce programme d'installation est incomplet. Téléchargez-le à nouveau.",
            "यह सेटअप अधूरा है: इसमें इंस्टॉलर नहीं है। इसे फिर से डाउनलोड करें।", "Deze Setup is onvolledig: er zit geen installatieprogramma in. Download hem opnieuw.", "Этот установщик неполный. Скачайте его снова.", "此安装程序不完整，请重新下载。",
        },
        ["StopQuestion"] = new[]
        {
            "Stop Setup? LabelAxis is not installed yet.", "إيقاف الإعداد؟ لم يُثبَّت LabelAxis بعد.", "دامەزراندن ڕابگیرێت؟ LabelAxis هێشتا دانەمەزراوە.", "¿Detener la instalación? LabelAxis aún no está instalado.", "نصب متوقف شود؟ LabelAxis هنوز نصب نشده است.",
            "Arrêter l'installation ? LabelAxis n'est pas encore installé.", "सेटअप रोकें? LabelAxis अभी इंस्टॉल नहीं हुआ है।", "Setup stoppen? LabelAxis is nog niet geïnstalleerd.", "Остановить установку? LabelAxis ещё не установлен.", "要停止安装吗？LabelAxis 尚未安装。",
        },
    };
}
