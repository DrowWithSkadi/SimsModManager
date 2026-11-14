"""Собирает мод-помощник в AncoraHelper.ts4script.

Игра работает на Python 3.7, поэтому запускать нужно им: тогда в архив кладётся скомпилированный .pyc.
Под другой версией в архив попадает исходник .py (только для локальной проверки).

    python build.py <папка-назначения>
"""
import os
import py_compile
import sys
import tempfile
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
MODULES = ['smm_helper.py']


def main():
    target_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, 'out')
    os.makedirs(target_dir, exist_ok=True)
    target = os.path.join(target_dir, 'AncoraHelper.ts4script')
    compiled = sys.version_info[:2] == (3, 7)
    if not compiled:
        print('Внимание: Python %d.%d, а игре нужен 3.7 — кладу исходники' % sys.version_info[:2])

    with tempfile.TemporaryDirectory() as tmp, zipfile.ZipFile(target, 'w', zipfile.ZIP_STORED) as archive:
        for module in MODULES:
            source = os.path.join(HERE, module)
            if compiled:
                pyc = os.path.join(tmp, module + 'c')
                py_compile.compile(source, cfile=pyc, dfile=module, doraise=True)
                archive.write(pyc, module + 'c')
            else:
                archive.write(source, module)
    print('Готово:', target)


if __name__ == '__main__':
    main()
