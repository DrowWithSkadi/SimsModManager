"""Собирает сайт на двух языках из site/src/template.html.

    python tools/build_site.py

Пишет site/index.html (русский) и site/en/index.html (английский).
Стили и скрипты общие: site/style.css и site/app.js.
"""
import html
import json
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parent.parent
SITE = ROOT / "site"

# Файлы в «папке Mods до программы»: имя, дата, тип, размер, иконка
MESS_FILES = [
    ("Новая папка (3)", "New folder (3)", "12.03.2026 22:14", "dir", "", "dir"),
    ("!!!_НЕ_УДАЛЯТЬ_ВАЖНОЕ", "!!!_DO_NOT_DELETE", "02.01.2026 18:40", "dir", "", "dir"),
    ("_старое_проверить", "_old_check_later", "19.11.2025 13:02", "dir", "", "dir"),
    ("hair_wavy_bob_fixed_fixed.package", None, "08.11.2026 21:35", "package", "4 812 KB", "pkg"),
    ("lashes_3D_v2_FINAL(1).package", None, "08.11.2026 21:35", "package", "733 KB", "pkg"),
    ("lashes_3D_v2_FINAL.package", None, "08.11.2026 21:34", "package", "733 KB", "pkg"),
    ("sofa_merged_OLD.package", None, "14.06.2025 10:11", "package", "21 330 KB", "pkg"),
    ("eyebrows_pack.zip", None, "07.11.2026 19:58", "zip", "18 204 KB", "zipf"),
    ("ui_tweaks_OLD.ts4script", None, "22.02.2024 16:47", "ts4script", "312 KB", "scr"),
    ("ui_tweaks.ts4script", None, "03.10.2026 12:05", "ts4script", "318 KB", "scr"),
    ("readme (2).txt", None, "07.11.2026 19:58", "txt", "2 KB", "txt"),
    ("summer_dress_v3.package", None, "05.11.2026 23:11", "package", "6 120 KB", "pkg"),
    ("Thumbs.db", None, "12.03.2026 22:14", "db", "96 KB", "sys"),
    ("kitchen_set_part2_of_5.package", None, "28.09.2026 17:30", "package", "11 404 KB", "pkg"),
    ("lastException_2611.txt", None, "26.11.2025 01:12", "txt", "4 KB", "txt"),
]
TYPE_NAMES = {
    "ru": {"dir": "Папка с файлами", "package": "Файл «PACKAGE»", "zip": "Сжатая ZIP-папка", "ts4script": "Файл «TS4SCRIPT»", "txt": "Текстовый документ", "db": "Файл «DB»"},
    "en": {"dir": "File folder", "package": "PACKAGE File", "zip": "Compressed (zipped) Folder", "ts4script": "TS4SCRIPT File", "txt": "Text Document", "db": "Data Base File"},
}


def mess_files(lang):
    rows = []
    for i, (ru, en, date, kind, size, ico) in enumerate(MESS_FILES):
        name = (en if lang == "en" and en else ru)
        sel = " sel" if i == 7 else ""
        rows.append(
            f'<div class="f{sel}"><span><i class="ico {ico}"></i>{html.escape(name)}</span>'
            f'<small>{date}</small><small>{TYPE_NAMES[lang][kind]}</small><small>{size}</small></div>')
    return "\n              ".join(rows)


def faq(items):
    out = []
    for q, a in items:
        out.append(f"<details><summary>{q}</summary>\n          <p>{a}</p></details>")
    return "\n        ".join(out)


RU = {
    "lang": "ru", "root": "", "og": "og.jpg", "pcPath": "", "ruHref": "./", "enHref": "en/", "isRu": "true", "isEn": "false",
    "meta.title": "Sims Mod Manager — клиент для The Sims 4",
    "meta.description": "Бесплатный клиент для The Sims 4: ставит моды из архивов, находит виновника вылета и откатывает сохранения прямо из игры.",
    "nav.label": "Разделы", "nav.ingame": "В игре", "nav.features": "Отличия", "nav.tune": "Настройки", "nav.faq": "Вопросы", "nav.download": "Скачать",
    "hero.h1a": "Клиент для Sims 4,", "hero.h1b": "который бережёт твою игру",
    "hero.lead": "Ставит моды из любых архивов, находит виновника вылета и откатывает сохранение прямо в игре. А всё остальное настраивается под тебя.",
    "hero.secondary": "Посмотреть в игре",
    "hero.meta": "Бесплатно. Открытый код, без аккаунта и рекламы.",
    "hero.alt": "Окно Sims Mod Manager: библиотека модов с карточками, фильтрами по типам и зоной для перетаскивания архивов",
    "hero.toast": "Установлено 3 мода из «летние-платья.zip»",
    "dl.main": "Скачать для Windows", "dl.mainNote": "Установщик, около 70 МБ",
    "dl.alt": "Без установки", "dl.altNote": "Архив .zip — распакуй и запускай",
    "zip.label": "Пример архива с модами: перетащи его в окно программы или нажми", "zip.hint": "Брось меня в окно", "zip.name": "летние-платья.zip",
    "buddy.pet": "Погладь меня", "buddy.wait": "Жду тебя!",
    "mess.h2": "Твоя папка Mods, но без хаоса",
    "mess.p": "Сотни файлов с непонятными именами, дубли, старые версии и архивы, которые забыли распаковать. Потяни ползунок и сравни.",
    "mess.slider": "Сравнение: папка Mods в проводнике и библиотека в программе",
    "mess.afterAlt": "Та же папка Mods в Sims Mod Manager: карточки с превью, типы и переключатели",
    "mess.path": "Документы › Electronic Arts › The Sims 4 › Mods",
    "mess.colName": "Имя", "mess.colDate": "Дата изменения", "mess.colType": "Тип", "mess.colSize": "Размер",
    "mess.before": "Проводник", "mess.after": "Sims Mod Manager",
    "ingame.h2": "Точки сохранения прямо в игре",
    "ingame.p": "Нажми F9 — слева появится панель. Сделай точку перед свадьбой, переездом или экспериментом с читами и возвращайся к ней, когда захочешь.",
    "ingame.alt": "The Sims 4 с открытой слева панелью Sims Mod Manager: список точек сохранения с кнопками «Откатить»",
    "ingame.dialog": "Вернуться к точке «Перед откатом · 13.11 00:15»? Текущая игра уже сохранена отдельной точкой. Игра выйдет в главное меню — там нажми «Продолжить».",
    "ingame.back": "Вернуться к точке", "ingame.stay": "Остаться",
    "tl.label": "Как работает откат", "tl.rewind": "Откат к точке",
    "tl.when0": "Пятница, 18:40", "tl.h0": "Сделал точку", "tl.p0": "Игра сохраняется сама, копия ложится отдельно и не засоряет список сохранений.",
    "tl.when1": "Суббота, 21:10", "tl.h1": "Играешь дальше", "tl.p1": "Свадьба, переезд, читы — что угодно. Важные точки можно закрепить.",
    "tl.when2": "Сейчас", "tl.h2": "Не понравилось — назад", "tl.p2": "Подтверди в окне игры и нажми «Продолжить». Текущая игра тоже сохранится.",
    "feat.h2": "Чем он отличается от обычного менеджера модов",
    "feat.p": "Обычный менеджер просто раскладывает файлы. Sims Mod Manager ещё и работает внутри игры, следит за модами и бережёт сохранения.",
    "feat.h0": "Работает внутри игры", "feat.p0": "Оверлей по клавише: точки сохранения и откат, не сворачивая игру. Встроенный мод-помощник сам сохраняет игру и показывает окна прямо в Sims.",
    "feat.h1": "Ставит из любых архивов", "feat.p1": "Zip, rar или 7z — программа распакует, разложит файлы по нужным папкам и покажет, что появилось. Одежда, причёски, объекты и скрипты — каждый на своей вкладке.",
    "feat.h2i": "Кнопка «Играть» со страховкой", "feat.p2": "Перед каждым запуском копирует сохранения. В два клика запускает игру без модов или без скриптов — чтобы быстро понять, кто виноват в вылете.",
    "feat.h3": "Находит, что сломалось", "feat.p3": "Читает отчёты игры после вылета, называет виновника и предлагает выключить его. Моды и скрипты включаются одним переключателем — без правки Options.ini.",
    "tune.h2": "Настраивается под тебя",
    "tune.p": "Клавиша, пауза, правила хранения точек, тема и даже Коробок — всё меняется в настройках, без правки файлов. Попробуй прямо здесь.",
    "tune.overlay": "Оверлей в игре", "tune.general": "Программа",
    "tune.key": "Клавиша", "tune.keyNote": "Нажми и выбери любую", "tune.pause": "Пауза в игре", "tune.pauseNote": "Пока открыта панель",
    "tune.stream": "Скрыть на стриме", "tune.streamNote": "OBS и запись экрана его не увидят",
    "tune.keep": "Хранить точки", "tune.keepNote": "Закреплённые — всегда", "tune.forever": "всегда",
    "tune.theme": "Оформление", "tune.themeNote": "Стекло — на Windows 11", "tune.dark": "Тёмная", "tune.light": "Светлая", "tune.glass": "Стекло",
    "tune.buddy": "Коробок", "tune.buddyNote": "Выключи — и он спрячется",
    "tune.note": "Это лишь часть настроек — в программе их больше.",
    "faq.h2": "Частые вопросы",
    "faq.items": faq([
        ("Windows пишет «Windows защитила ваш компьютер». Это нормально?", "Да. Так Windows встречает любую новую программу без платной цифровой подписи. Нажми «Подробнее», затем «Выполнить в любом случае». Исходный код открыт на GitHub — проверить его может кто угодно."),
        ("Программа удаляет мои моды?", "Нет, если ты сам не попросишь. Исправления только выключают моды — переносят их в папку «Mods (выключенные)». Включить обратно можно одним нажатием."),
        ("Оверлей не сломает игру?", "Нет. Это отдельное окно поверх игры: оно не встраивается в неё и не трогает её файлы. Нужен режим экрана «На весь экран в окне» или «В окне»."),
        ("Работает с EA app и Steam?", "Да. Программа находит игру сама и запускает её через нужный лаунчер. Если не нашла — один раз спросит, где лежит игра."),
        ("Нужен интернет или аккаунт?", "Нет. Всё работает на твоём компьютере, без регистрации. Интернет нужен только для обновлений программы."),
        ("Сколько места занимают точки сохранения?", "Одна точка весит примерно как само сохранение — обычно несколько мегабайт. Лимит по количеству, возрасту или размеру задаёшь сам в настройках."),
    ]),
    "end.h2": "Забирай — это бесплатно",
    "end.p": "Ставится за минуту и обновляется сама. Коробок уже ждёт и первым скажет, если какой-то мод начнёт шалить.",
    "pc.p": "Sims Mod Manager работает на компьютере с Windows. Скопируй ссылку и открой её на компьютере.", "pc.label": "Ссылка на сайт", "pc.copy": "Скопировать",
    "footer.note": "Фанатский проект. Не связан с Electronic Arts и Maxis. The Sims — торговая марка Electronic Arts.",
    "footer.code": "Исходный код на GitHub",
    "js": {
        "lang": "ru", "pet": "Погладь меня", "installed": "Распаковал! Уже в игре", "listen": "Нажми клавишу…",
        "forever": "всегда", "days": "{n} дн.", "copied": "Скопировано",
        "release": "Версия {v} · обновлена {when}", "today": "сегодня", "yesterday": "вчера", "daysAgo": "{n} дн. назад",
        "downloads": " · скачали {n} раз",
    },
}

EN = {
    "lang": "en", "root": "../", "og": "og-en.jpg", "pcPath": "en/", "ruHref": "../", "enHref": "./", "isRu": "false", "isEn": "true",
    "meta.title": "Sims Mod Manager — a client for The Sims 4",
    "meta.description": "A free client for The Sims 4: installs mods from archives, finds the mod that crashed your game and rolls back saves right from inside the game.",
    "nav.label": "Sections", "nav.ingame": "In game", "nav.features": "Why it's different", "nav.tune": "Settings", "nav.faq": "FAQ", "nav.download": "Download",
    "hero.h1a": "The Sims 4 client", "hero.h1b": "that keeps your game safe",
    "hero.lead": "Installs mods from any archive, finds what crashed your game and rolls back your save right inside the game. Everything else is yours to tune.",
    "hero.secondary": "See it in game",
    "hero.meta": "Free. Open source, no account, no ads.",
    "hero.alt": "Sims Mod Manager window: a mod library with cards, type filters and a drop zone for archives",
    "hero.toast": "Installed 3 mods from “summer-dresses.zip”",
    "dl.main": "Download for Windows", "dl.mainNote": "Installer, about 70 MB",
    "dl.alt": "Portable", "dl.altNote": ".zip — unpack and run",
    "zip.label": "Sample mod archive: drag it into the app window or press it", "zip.hint": "Drop me in the window", "zip.name": "summer-dresses.zip",
    "buddy.pet": "Pet me", "buddy.wait": "Waiting for you!",
    "mess.h2": "Your Mods folder, minus the mess",
    "mess.p": "Hundreds of cryptic file names, duplicates, old versions and archives nobody unpacked. Drag the slider to compare.",
    "mess.slider": "Comparison: the Mods folder in File Explorer and the library in the app",
    "mess.afterAlt": "The same Mods folder in Sims Mod Manager: cards with previews, types and toggles",
    "mess.path": "Documents › Electronic Arts › The Sims 4 › Mods",
    "mess.colName": "Name", "mess.colDate": "Date modified", "mess.colType": "Type", "mess.colSize": "Size",
    "mess.before": "File Explorer", "mess.after": "Sims Mod Manager",
    "ingame.h2": "Save points right in the game",
    "ingame.p": "Press F9 and a panel slides in from the left. Make a save point before a wedding, a move or a cheat experiment — and come back to it whenever you like.",
    "ingame.alt": "The Sims 4 with the Sims Mod Manager panel open on the left: a list of save points with Roll back buttons",
    "ingame.dialog": "Go back to the save point? Your current game is already saved as a separate point. The game will exit to the main menu — press Continue there.",
    "ingame.back": "Go back", "ingame.stay": "Stay",
    "tl.label": "How rolling back works", "tl.rewind": "Back to the save point",
    "tl.when0": "Friday, 6:40 PM", "tl.h0": "Make a save point", "tl.p0": "The game saves itself, and the copy is kept separately so your save list stays clean.",
    "tl.when1": "Saturday, 9:10 PM", "tl.h1": "Keep playing", "tl.p1": "A wedding, a move, cheats — anything. Pin the points that matter.",
    "tl.when2": "Now", "tl.h2": "Didn't like it? Go back", "tl.p2": "Confirm in the game's own dialog and press Continue. Your current game is saved too.",
    "feat.h2": "How it differs from a regular mod manager",
    "feat.p": "A regular manager just sorts files. Sims Mod Manager also works inside the game, watches your mods and protects your saves.",
    "feat.h0": "Works inside the game", "feat.p0": "An overlay on a hotkey: save points and rollback without leaving the game. A built-in helper mod saves the game and shows dialogs right in The Sims.",
    "feat.h1": "Installs from any archive", "feat.p1": "Zip, rar or 7z — it unpacks, puts every file where it belongs and shows what's new. Clothes, hair, objects and scripts each get their own tab.",
    "feat.h2i": "A Play button with a safety net", "feat.p2": "Backs up your saves before every launch. Two clicks to start the game without mods or without scripts — to find the culprit fast.",
    "feat.h3": "Finds what broke", "feat.p3": "Reads the game's crash reports, names the culprit and offers to turn it off. Mods and scripts switch on with one toggle — no editing Options.ini.",
    "tune.h2": "Tuned to you",
    "tune.p": "Hotkey, pause, save point rules, theme — even Boxy. Everything lives in the settings, no file editing. Try it right here.",
    "tune.overlay": "In-game overlay", "tune.general": "App",
    "tune.key": "Hotkey", "tune.keyNote": "Click and pick any key", "tune.pause": "Pause the game", "tune.pauseNote": "While the panel is open",
    "tune.stream": "Hide on stream", "tune.streamNote": "OBS and screen recording won't see it",
    "tune.keep": "Keep save points", "tune.keepNote": "Pinned ones — forever", "tune.forever": "forever",
    "tune.theme": "Theme", "tune.themeNote": "Glass needs Windows 11", "tune.dark": "Dark", "tune.light": "Light", "tune.glass": "Glass",
    "tune.buddy": "Boxy", "tune.buddyNote": "Turn him off and he hides",
    "tune.note": "That's only some of the settings — the app has more.",
    "faq.h2": "FAQ",
    "faq.items": faq([
        ("Windows says “Windows protected your PC”. Is that okay?", "Yes. Windows shows this for any new app without a paid code-signing certificate. Click “More info”, then “Run anyway”. The source code is open on GitHub for anyone to check."),
        ("Will it delete my mods?", "Not unless you ask. Fixes only turn mods off by moving them to a “Mods (disabled)” folder. One click turns them back on."),
        ("Can the overlay break the game?", "No. It's a separate window on top of the game: it doesn't hook into the game or touch its files. Use “Windowed Fullscreen” or “Windowed” display mode."),
        ("Does it work with the EA app and Steam?", "Yes. It finds the game on its own and launches it through the right launcher. If it can't, it asks once where the game is."),
        ("Do I need internet or an account?", "No. Everything runs on your computer with no sign-up. Internet is only used for app updates."),
        ("How much space do save points take?", "One point is about the size of the save itself — usually a few megabytes. You set limits by count, age or size in the settings."),
    ]),
    "end.h2": "Grab it — it's free",
    "end.p": "Installs in a minute and updates itself. Boxy is already waiting and will be the first to tell you if a mod starts misbehaving.",
    "pc.p": "Sims Mod Manager runs on Windows PCs. Copy the link and open it on your computer.", "pc.label": "Site link", "pc.copy": "Copy",
    "footer.note": "Fan project. Not affiliated with Electronic Arts or Maxis. The Sims is a trademark of Electronic Arts.",
    "footer.code": "Source code on GitHub",
    "js": {
        "lang": "en", "pet": "Pet me", "installed": "Unpacked! It's in the game", "listen": "Press a key…",
        "forever": "forever", "days": "{n} days", "copied": "Copied",
        "release": "Version {v} · updated {when}", "today": "today", "yesterday": "yesterday", "daysAgo": "{n} days ago",
        "downloads": " · downloaded {n} times",
    },
}

RAW = {"faq.items", "mess.files"}


def render(strings, out):
    template = (SITE / "src" / "template.html").read_text(encoding="utf-8")
    strings = dict(strings)
    strings["mess.files"] = mess_files(strings["lang"])

    def sub(m):
        key = m.group(1)
        if key == "js":
            return json.dumps(strings["js"], ensure_ascii=False).replace("</", "<\\/")
        if key not in strings:
            raise KeyError(f"нет строки «{key}» для {strings['lang']}")
        value = strings[key]
        return value if key in RAW else html.escape(value, quote=True)

    page = re.sub(r"\{\{([\w.]+)\}\}", sub, template)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(page, encoding="utf-8")
    print("✓", out.relative_to(ROOT))


if __name__ == "__main__":
    missing = set(RU) ^ set(EN)
    if missing:
        raise SystemExit(f"строки есть только в одном языке: {sorted(missing)}")
    render(RU, SITE / "index.html")
    render(EN, SITE / "en" / "index.html")
