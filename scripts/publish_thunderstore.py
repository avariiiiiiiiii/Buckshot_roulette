"""Publish a prepared zip using Thunderstore's official multipart upload API.

Set THUNDERSTORE_TOKEN in the process environment; credentials are never written.
"""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import urllib.error
import urllib.request

BASE = 'https://thunderstore.io/api/experimental/'


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('package', type=Path)
    parser.add_argument('--team', default='Avariiiprime')
    args = parser.parse_args()
    token = os.environ.get('THUNDERSTORE_TOKEN')
    if not token:
        raise SystemExit('THUNDERSTORE_TOKEN is required')

    def api(path, payload):
        request = urllib.request.Request(BASE + path, data=json.dumps(payload).encode(), headers={
            'Authorization': 'Bearer ' + token,
            'Content-Type': 'application/json',
            'User-Agent': 'BuckshotRoulette-Release/0.1.3',
        })
        try:
            with urllib.request.urlopen(request, timeout=120) as response:
                return json.load(response)
        except urllib.error.HTTPError as error:
            body = error.read().decode(errors='replace').replace(token, '[redacted]')
            raise RuntimeError(f'Thunderstore {path}: HTTP {error.code}: {body[:2000]}') from None

    content = args.package.read_bytes()
    upload = api('usermedia/initiate-upload/', {
        'filename': args.package.name, 'file_size_bytes': len(content),
    })
    uuid = upload['user_media']['uuid']
    parts = []
    for part in upload['upload_urls']:
        chunk = content[part['offset']:part['offset'] + part['length']]
        request = urllib.request.Request(part['url'], data=chunk, method='PUT', headers={
            'Content-MD5': base64.b64encode(hashlib.md5(chunk).digest()).decode(),
            'Content-Length': str(len(chunk)),
        })
        with urllib.request.urlopen(request, timeout=120) as response:
            etag = response.headers.get('ETag')
            if not etag:
                raise RuntimeError('Upload part returned no ETag')
            parts.append({'ETag': etag, 'PartNumber': part['part_number']})
        print(f'Uploaded part {part["part_number"]}', flush=True)
    api(f'usermedia/{uuid}/finish-upload/', {'parts': parts})
    result = api('submission/submit/', {
        'author_name': args.team, 'categories': [], 'communities': ['repo'],
        'community_categories': {'repo': []}, 'has_nsfw_content': False, 'upload_uuid': uuid,
    })
    version = result.get('package_version', {})
    print(json.dumps({key: version.get(key) for key in ['full_name', 'version_number', 'download_url']}, indent=2))
    if not version.get('download_url'):
        raise RuntimeError('Submission did not return a published package version')


if __name__ == '__main__':
    main()
