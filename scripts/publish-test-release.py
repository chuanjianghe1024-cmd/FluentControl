"""Publish explicitly authorized test builds; never replace assets or stable releases."""
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import tempfile
import zipfile


def require(condition, message):
    if not condition:
        raise ValueError(message)


def api(path, data=None, method=None, optional=False):
    command = ['gh', 'api', path]
    if method:
        command += ['--method', method]
    if data is not None:
        command += ['--input', '-']
    result = subprocess.run(command, input=json.dumps(data) if data is not None else None,
                            text=True, capture_output=True, timeout=120)
    if optional and result.returncode and 'HTTP 404' in result.stderr:
        return None
    require(result.returncode == 0, 'GitHub API failed: ' + path)
    return json.loads(result.stdout) if result.stdout.strip() else None


def sha(data):
    return hashlib.sha256(data).hexdigest()


def validate_manifest(m):
    require(m['tag'] == 'v' + m['version'] + '-test' and re.fullmatch(r'0\.3\.\d+', m['version']), 'Test-only version required')
    require(re.fullmatch(r'[0-9a-f]{40}', m['sourceCommit']), 'Exact source required')
    require(m['repository'] in ('chuanjianghe1024-cmd/FluentControl', 'chuanjianghe1024-cmd/FluentControl-Sensors'), 'Unexpected repository')
    require(m['branch'] == 'codex/test-release-' + m['version'], 'Unexpected publishing branch')
    require(m['notes'] == 'docs/releases/tests/' + m['tag'] + '.md', 'Unexpected notes')
    require(m['requiredRuns'] and m['artifactRun'] in m['requiredRuns'], 'Build verification required')
    names = [a['output'] for a in m['artifacts']]
    require(names and len(names) == len(set(names)) and all(PurePosixPath(n).name == n for n in names), 'Invalid assets')


def validate_run(run, m):
    require(run['head_sha'] == m['sourceCommit'] and run['status'] == 'completed' and run['conclusion'] == 'success', 'Build not successful at exact source')
    require(run['head_repository']['full_name'] == m['repository'], 'Foreign build source')
    require(run['event'] in ('push', 'pull_request', 'workflow_dispatch'), 'Unexpected build event')


def inspect_zip(raw):
    z = zipfile.ZipFile(io.BytesIO(raw))
    require(sum(x.file_size for x in z.infolist()) <= 512 * 1024 * 1024, 'Archive too large')
    names = z.namelist()
    require(len(names) == len(set(names)), 'Duplicate archive entries')
    require(all(not PurePosixPath(n).is_absolute() and '..' not in PurePosixPath(n).parts and '\\' not in n for n in names), 'Unsafe archive path')
    return z


def main():
    root = Path(__file__).resolve().parents[1]
    m = json.loads((root / '.github/test-release.json').read_text())
    validate_manifest(m)
    require(os.environ.get('GITHUB_REPOSITORY') == m['repository'], 'Repository mismatch')
    require(os.environ.get('GITHUB_REF') == 'refs/heads/' + m['branch'], 'Branch mismatch')
    base = 'repos/' + m['repository']
    for run_id in m['requiredRuns']:
        validate_run(api(f'{base}/actions/runs/{run_id}'), m)
    run = api(f'{base}/actions/runs/{m["artifactRun"]}')
    if m['repository'].endswith('/FluentControl'):
        require(m['version'] == '0.3.' + str(run['run_number']), 'Installer version differs from tested build')
    stable = api(base + '/releases/latest', optional=True)
    stable_id = stable['id'] if stable else None
    ref = api(base + '/git/ref/tags/' + m['tag'], optional=True)
    if ref:
        require(ref['object']['type'] == 'commit' and ref['object']['sha'] == m['sourceCommit'], 'Existing tag differs; never move it')
    artifacts = api(f'{base}/actions/runs/{m["artifactRun"]}/artifacts?per_page=100')['artifacts']
    body = (root / m['notes']).read_text(encoding='utf-8')
    require(body.startswith('# '), 'Release notes missing')
    with tempfile.TemporaryDirectory() as temporary:
        directory = Path(temporary)
        provenance = {'format': 'fcs.test-release/1', 'tag': m['tag'], 'repository': m['repository'],
                      'sourceCommit': m['sourceCommit'], 'requiredRuns': m['requiredRuns'],
                      'companion': m['companion'], 'assets': []}
        for spec in m['artifacts']:
            found = [a for a in artifacts if a['name'] == spec['name'] and not a['expired']]
            require(len(found) == 1, 'Missing/ambiguous artifact: ' + spec['name'])
            artifact = found[0]
            archive = directory / 'download.zip'
            with archive.open('wb') as stream:
                subprocess.run(['gh', 'api', f'{base}/actions/artifacts/{artifact["id"]}/zip'], stdout=stream, check=True, timeout=300)
            raw = archive.read_bytes()
            if artifact.get('digest'):
                require(artifact['digest'] == 'sha256:' + sha(raw), 'Artifact archive digest mismatch')
            with inspect_zip(raw) as z:
                if spec.get('entry'):
                    matches = [n for n in z.namelist() if PurePosixPath(n).name == spec['entry']]
                    require(len(matches) == 1, 'Expected single artifact file')
                    content = z.read(matches[0])
                else:
                    require({'FluentControl.FcsGateway.exe', 'fcs-script-runner.py'} <= set(z.namelist()), 'Gateway payload incomplete')
                    content = raw
            if spec['output'].startswith('fcs-c3-'):
                with inspect_zip(content) as z:
                    manifest = json.loads(z.read('manifest.json'))
                    require(manifest['protocol'] == 'fcs-node/2' and manifest['hardwareTested'] is False, 'C3 scope mismatch')
                    require(set(manifest['files']) == set(z.namelist()) - {'manifest.json'}, 'C3 manifest differs')
                    for name, info in manifest['files'].items():
                        data = z.read(name)
                        require(len(data) == info['bytes'] and sha(data) == info['sha256'], 'C3 file integrity mismatch')
            (directory / spec['output']).write_bytes(content)
            provenance['assets'].append({'name': spec['output'], 'bytes': len(content), 'sha256': sha(content),
                                         'artifactId': artifact['id'], 'artifactArchiveSha256': sha(raw), 'runId': m['artifactRun']})
        archive.unlink()
        (directory / 'release-manifest.json').write_text(json.dumps(provenance, indent=2) + '\n')
        assets = sorted(directory.iterdir())
        (directory / 'SHA256SUMS.txt').write_text(''.join(sha(p.read_bytes()) + '  ' + p.name + '\n' for p in assets))
        assets = sorted(directory.iterdir())
        # All origin/content checks finish before creating the tag or draft.
        if not ref:
            api(base + '/git/refs', {'ref': 'refs/tags/' + m['tag'], 'sha': m['sourceCommit']}, 'POST')
        releases = api(base + '/releases?per_page=100')
        existing = [r for r in releases if r['tag_name'] == m['tag']]
        require(len(existing) <= 1, 'Duplicate release')
        release = existing[0] if existing else api(base + '/releases', {'tag_name': m['tag'],
            'target_commitish': m['sourceCommit'], 'name': m['title'], 'body': body,
            'draft': True, 'prerelease': True, 'make_latest': 'false'}, 'POST')
        require(release['prerelease'] and release['target_commitish'] == m['sourceCommit'] and release['body'] == body, 'Release identity differs')
        uploaded = {a['name']: a for a in release['assets']}
        require(set(uploaded) <= {p.name for p in assets}, 'Unexpected release asset')
        for path in assets:
            item = uploaded.get(path.name)
            if item:
                require(item['size'] == path.stat().st_size and item.get('digest') == 'sha256:' + sha(path.read_bytes()), 'Existing asset differs; never replace it')
            else:
                require(release['draft'], 'Published release is immutable')
                subprocess.run(['gh', 'release', 'upload', m['tag'], str(path), '--repo', m['repository']], check=True, timeout=300)
        checked = api(f'{base}/releases/{release["id"]}')
        uploaded = {a['name']: a for a in checked['assets']}
        require(set(uploaded) == {p.name for p in assets}, 'Incomplete uploaded assets')
        for path in assets:
            require(uploaded[path.name]['size'] == path.stat().st_size and uploaded[path.name].get('digest') == 'sha256:' + sha(path.read_bytes()), 'Uploaded digest mismatch')
        if checked['draft']:
            checked = api(f'{base}/releases/{release["id"]}', {'draft': False, 'prerelease': True, 'make_latest': 'false'}, 'PATCH')
        require(not checked['draft'] and checked['prerelease'], 'Publication not confirmed')
        current = api(base + '/releases/latest', optional=True)
        require((current['id'] if current else None) == stable_id, 'Stable latest changed')
        require(api(base + '/git/ref/tags/' + m['tag'])['object']['sha'] == m['sourceCommit'], 'Tag source changed')
        print('Published and verified:', checked['html_url'])


if __name__ == '__main__':
    main()
