#!/usr/bin/env python3
"""从网关响应 JSON 中提取全部媒体（image/audio）。

用法: lib_extract.py <response.json> <输出前缀> [image|audio]

兼容网关的多种返回形状：
- chat.completions: message.images[].image_url.url 为 data URI
- chat.completions: message.content 内嵌 markdown ![](data:...;base64,...)
- images API: data[].b64_json 或 data[].url
- interactions 原生: steps[].content[] inlineData {mime_type, data}

每找到一个媒体文件打印一行路径；没有则 stderr 打印诊断信息并 exit 1。
"""
import json, base64, re, sys

src, prefix = sys.argv[1], sys.argv[2]
want = sys.argv[3] if len(sys.argv) > 3 else None
raw = open(src, encoding='utf-8', errors='replace').read()

found = []  # (mime, b64, json_path)

def walk(o, path=''):
    if isinstance(o, dict):
        m = o.get('mime_type') or o.get('mimeType')
        d = o.get('data')
        if isinstance(d, str) and isinstance(m, str) and len(d) > 200:
            found.append((m, d, path))
        for k, v in o.items():
            walk(v, path + '.' + k)
    elif isinstance(o, list):
        for i, v in enumerate(o):
            walk(v, '%s[%d]' % (path, i))
    elif isinstance(o, str):
        for m in re.finditer(r'data:([\w/+.-]+);base64,([A-Za-z0-9+/=\s]{200,})', o):
            found.append((m.group(1), m.group(2).replace('\n', ''), path))

try:
    doc = json.loads(raw)
except Exception as e:
    print('响应不是合法 JSON: %s；开头: %s' % (e, raw[:200]), file=sys.stderr)
    sys.exit(1)

walk(doc)
if want:
    found = [f for f in found if f[0].startswith(want + '/')]

EXT = {'audio/wav': 'wav', 'audio/x-wav': 'wav', 'audio/l16': 'raw',
       'audio/mpeg': 'mp3', 'audio/mp3': 'mp3', 'audio/ogg': 'ogg',
       'audio/flac': 'flac', 'image/png': 'png', 'image/jpeg': 'jpg',
       'image/webp': 'webp'}

if not found:
    msg = ''
    try:
        c = doc.get('choices', [{}])[0].get('message', {}).get('content')
        msg = str(c)[:400]
    except Exception:
        pass
    print('响应中没有媒体负载。message.content 开头: %s' % msg, file=sys.stderr)
    sys.exit(1)

for i, (mime, b64, path) in enumerate(found):
    out = '%s_%d.%s' % (prefix, i, EXT.get(mime, mime.split('/')[-1]))
    open(out, 'wb').write(base64.b64decode(b64))
    print(out)
