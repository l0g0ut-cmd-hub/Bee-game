#!/usr/bin/env python3
"""Сверка стабов UnityEngine (UnityStubs.cs + UnityMathStub.cs) с реальными определениями Unity 6
(npm-пакет unity-types, сгенерирован из сборок UnityEngine.*Module).

Проверяет: тип существует; каждое поле/свойство/метод/значение enum существует;
для методов — есть перегрузка с совместимым числом параметров.
Запуск: python3 check_api.py <путь к распакованному unity-types/package>
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
STUBS = [os.path.join(HERE, 'UnityStubs.cs'), os.path.join(HERE, '..', 'simtest', 'UnityMathStub.cs')]
SKIP_MEMBERS = {'Equals', 'GetHashCode', 'ToString'}
# члены-заглушки, которые есть в стабах только ради компиляции самих стабов
SKIP_TYPES = set()


def load_dts(pkg):
    text = ''
    for f in os.listdir(pkg):
        if f.startswith('UnityEngine.') and f.endswith('.d.ts'):
            text += open(os.path.join(pkg, f), encoding='utf-8').read() + '\n'
    return text


def dts_types(text):
    """namespace.Type -> тело декларации (class/enum/interface)."""
    types = {}
    ns_stack = []
    lines = text.split('\n')
    i = 0
    depth = 0
    ns_depths = []
    cur = None
    cur_depth = 0
    body = []
    for line in lines:
        m = re.match(r'\s*namespace\s+([\w.]+)\s*\{', line)
        if m and cur is None:
            ns_stack.append(m.group(1))
            ns_depths.append(depth)
            depth += 1
            continue
        if cur is None:
            m = re.match(r'\s*(?:abstract\s+)?(class|enum|interface)\s+(\w+)(?:\$\d+)?\b', line)
            if m:
                ns = '.'.join(ns_stack)
                cur = ns + '.' + m.group(2)
                cur_depth = depth
                body = [line]
                depth += line.count('{') - line.count('}')
                if line.count('{') and depth == cur_depth:
                    types.setdefault(cur, []).append('\n'.join(body))
                    cur = None
                continue
            depth += line.count('{') - line.count('}')
            while ns_depths and depth <= ns_depths[-1]:
                ns_depths.pop()
                ns_stack.pop()
        else:
            body.append(line)
            depth += line.count('{') - line.count('}')
            if depth <= cur_depth:
                types.setdefault(cur, []).append('\n'.join(body))
                cur = None
    return {k: '\n'.join(v) for k, v in types.items()}


def stub_members(path):
    """Возвращает список (namespace.Type, kind, member, nparams_or_None, line)."""
    out = []
    ns = None
    type_stack = []
    depth = 0
    type_depths = []
    for ln, line in enumerate(open(path, encoding='utf-8'), 1):
        s = line.strip()
        m = re.match(r'namespace\s+([\w.]+)', s)
        if m:
            ns = m.group(1)
        m = re.match(r'(?:\[.*\]\s*)?public\s+(?:static\s+|sealed\s+|partial\s+|readonly\s+)*(class|struct|enum)\s+(\w+)', s)
        if m:
            parent = type_stack[-1] if type_stack else None
            full = (parent + '.' if parent else ns + '.') + m.group(2)
            if m.group(1) == 'enum':
                vals = re.findall(r'(\w+)\s*(?:=\s*-?\d+)?\s*[,}]', s.split('{', 1)[1]) if '{' in s else []
                out.append((full, 'enum', None, vals, ln))
                depth += s.count('{') - s.count('}')
                continue
            out.append((full, 'type', None, None, ln))
            if '{' in s and '}' in s and s.index('}') > s.index('{'):
                # однострочный тип: public static class X { public static T M(...) ... }
                inner = s[s.index('{') + 1:s.rindex('}')]
                for mm in re.finditer(r'public\s+(?:static\s+|const\s+)*[\w.<>\[\]]+\s+(\w+)\s*(\(([^)]*)\))?', inner):
                    name, has_paren, params = mm.group(1), mm.group(2), mm.group(3)
                    n = None if not has_paren else (0 if not params.strip() else len(split_params(params)))
                    out.append((full, 'method' if has_paren else 'prop', name, n, ln))
                continue
            type_stack.append(full)
            type_depths.append(depth)
            depth += s.count('{') - s.count('}')
            continue
        if type_stack:
            # конструктор
            mc = re.match(r'public\s+(\w+)\s*\(([^)]*)\)', s)
            if mc and mc.group(1) == type_stack[-1].split('.')[-1]:
                out.append((type_stack[-1], 'ctor', mc.group(1), len(split_params(mc.group(2))) if mc.group(2).strip() else 0, ln))
            else:
                mm = re.match(r'public\s+(?:static\s+|const\s+|readonly\s+|override\s+|implicit\s+|explicit\s+)*'
                              r'(?:operator\s+\S+|[\w.<>\[\]?]+\s+(?:operator\s*\S+|(\w+)))\s*(?:<[^>]*>)?\s*(\(([^)]*)\))?', s)
                if mm and mm.group(1) and mm.group(1) not in SKIP_MEMBERS and 'operator' not in s:
                    name = mm.group(1)
                    has_paren = mm.group(2)
                    params = mm.group(3) or ''
                    n = None if not has_paren else (0 if not params.strip() else len(split_params(params)))
                    out.append((type_stack[-1], 'method' if has_paren else 'prop', name, n, ln))
                elif re.match(r'public\s+[\w.]+\s+\w+\s*(,\s*\w+)+\s*;', s):  # public float x, y;
                    for name in re.findall(r'(\w+)\s*[,;]', s.split(None, 2)[2]):
                        out.append((type_stack[-1], 'prop', name, None, ln))
        depth += s.count('{') - s.count('}')
        while type_depths and depth <= type_depths[-1]:
            type_depths.pop()
            type_stack.pop()
    return out


def split_params(p):
    parts, d, cur = [], 0, ''
    for ch in p:
        if ch in '<(': d += 1
        if ch in '>)': d -= 1
        if ch == ',' and d == 0:
            parts.append(cur); cur = ''
        else:
            cur += ch
    if cur.strip(): parts.append(cur)
    return parts


def dts_arities(body, name):
    """Все допустимые количества аргументов для метода name (учитывая необязательные $x?)."""
    res = set()
    prefix = r'(?:^|\n)\s*' if name == 'constructor' else r'public\s+(?:static\s+)?'
    for m in re.finditer(prefix + re.escape(name) + r'(?:<[^>]*>)?\s*\(([^)]*)\)', body):
        params = [p for p in split_params(m.group(1)) if p.strip()]
        req = sum(1 for p in params if '?' not in p.split(':')[0] and '...' not in p)
        for k in range(req, len(params) + 1):
            res.add(k)
    return res


def main():
    pkg = sys.argv[1]
    dts = load_dts(pkg)
    types = dts_types(dts)
    problems, checked = [], 0
    for path in STUBS:
        for full, kind, name, extra, ln in stub_members(path):
            if not full.startswith('UnityEngine'):
                continue
            # вложенные типы d.ts кладёт как Outer.Inner внутри namespace Outer
            body = types.get(full)
            if body is None:
                problems.append(f'{os.path.basename(path)}:{ln}: тип {full} не найден в Unity')
                continue
            checked += 1
            if kind == 'enum':
                for v in extra:
                    if not re.search(r'\b' + re.escape(v) + r'\s*=', body):
                        problems.append(f'{os.path.basename(path)}:{ln}: {full}.{v} нет в enum Unity')
                    checked += 1
                continue
            if kind == 'type':
                continue
            if kind == 'ctor':
                ar = dts_arities(body, 'constructor') if 'constructor' in body else set()
                if extra not in ar:
                    problems.append(f'{os.path.basename(path)}:{ln}: конструктор {full}({extra} арг.) — в Unity есть арности {sorted(ar)}')
                continue
            if kind == 'prop':
                pat = r'(?:public|protected)\s+(?:static\s+)?(?:readonly\s+)?(?:get\s+|set\s+)?' + re.escape(name) + r'\b\s*[:(]'
                b, ok = body, False
                while b is not None and not ok:
                    ok = re.search(pat, b) is not None
                    base = re.search(r'class\s+\w+\s+extends\s+([\w.]+)', b)
                    b = types.get(base.group(1)) if base else None
                if not ok:
                    problems.append(f'{os.path.basename(path)}:{ln}: {full}.{name} (свойство/поле) не найдено')
                checked += 1
                continue
            if kind == 'method':
                ar = dts_arities(body, name)
                if not ar:
                    # может быть унаследован: ищем в базовом классе
                    base = re.search(r'class\s+\w+\s+extends\s+([\w.]+)', body)
                    found = False
                    while base and not found:
                        bb = types.get(base.group(1))
                        if bb is None:
                            break
                        ar = dts_arities(bb, name)
                        found = bool(ar)
                        base = re.search(r'class\s+\w+\s+extends\s+([\w.]+)', bb)
                    if not ar:
                        problems.append(f'{os.path.basename(path)}:{ln}: метод {full}.{name} не найден')
                        continue
                if extra not in ar:
                    problems.append(f'{os.path.basename(path)}:{ln}: {full}.{name}({extra} арг.) — в Unity арности {sorted(ar)}')
                checked += 1
    print(f'проверено членов: {checked}')
    if problems:
        print('\n'.join(problems))
        sys.exit(1)
    print('OK: все используемые члены UnityEngine существуют в Unity 6 с совместимой арностью')


if __name__ == '__main__':
    main()
