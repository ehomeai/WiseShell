"""Generate libvterm lookup arrays without C99 array designators (MSVC)."""
import pathlib
import re
import sys

source, target = map(pathlib.Path, sys.argv[1:])
target.mkdir(parents=True, exist_ok=True)
for table in source.glob('*.tbl'):
    values = [0] * 128
    for line in table.read_text(encoding='utf-8').splitlines():
        match = re.match(r'(\d+)/(\d+)\s*=\s*(?:U\+([0-9A-Fa-f]+)|"(.)")', line)
        if match:
            high, low, code, char = match.groups()
            values[int(high) * 16 + int(low)] = int(code, 16) if code else ord(char)
        elif line.strip() and not line.lstrip().startswith('#'):
            raise ValueError(f'Unsupported encoding entry: {line}')
    contents = ('/* Generated from upstream libvterm table; MIT license. */\n'
                f'static const struct StaticTableEncoding encoding_{table.stem} = {{\n'
                '  { .decode = &decode_table },\n  { '
                + ', '.join(hex(value) for value in values) + ' }\n};\n')
    output = target / f'{table.stem}.inc'
    if not output.exists() or output.read_text(encoding='utf-8') != contents:
        output.write_text(contents, encoding='utf-8')
