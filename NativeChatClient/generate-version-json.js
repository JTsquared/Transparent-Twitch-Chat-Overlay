const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const pkg = require('./package.json');

const outDir = path.resolve(__dirname, 'dist');
if (!fs.existsSync(outDir)) fs.mkdirSync(outDir, { recursive: true });

fs.writeFileSync(
    path.join(outDir, 'version.json'),
    JSON.stringify({ version: pkg.version }, null, 2)
);

const manifestPath = path.join(outDir, 'nativechat-manifest.json');

function collectFiles(dir) {
    const files = [];
    for (const item of fs.readdirSync(dir, { withFileTypes: true })) {
        const fullPath = path.join(dir, item.name);
        if (item.isDirectory()) {
            files.push(...collectFiles(fullPath));
            continue;
        }

        if (fullPath === manifestPath) continue;

        const bytes = fs.readFileSync(fullPath);
        const relativePath = path
            .relative(outDir, fullPath)
            .split(path.sep)
            .join('/');

        files.push({
            path: relativePath,
            size: bytes.length,
            sha256: crypto.createHash('sha256').update(bytes).digest('hex')
        });
    }

    return files;
}

const manifest = {
    id: 'native-chat',
    name: 'NativeChat',
    version: pkg.version,
    protocolVersion: 1,
    minimumAppVersion: '1.1.0',
    generatedAtUtc: new Date().toISOString(),
    entryPoints: {
        settings: 'index.html',
        overlay: 'v2/index.html'
    },
    files: collectFiles(outDir).sort((a, b) => a.path.localeCompare(b.path))
};

fs.writeFileSync(manifestPath, JSON.stringify(manifest, null, 2));

console.log(`Generated dist/version.json: ${pkg.version}`);
console.log(`Generated dist/nativechat-manifest.json: ${pkg.version}`);
