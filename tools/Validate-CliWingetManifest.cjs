// Validate against Microsoft's versioned schemas without installing App Installer on CI.
// Dependencies are installed into an isolated directory by the workflow, not the repository.
const fs = require('node:fs/promises');
const path = require('node:path');

async function main() {
    const [manifestDirectory, dependencyDirectory] = process.argv.slice(2);
    if (!manifestDirectory || !dependencyDirectory) {
        throw new Error('Usage: node Validate-CliWingetManifest.cjs MANIFEST_DIRECTORY DEPENDENCY_DIRECTORY');
    }
    const requireDependency = name => require(path.resolve(dependencyDirectory, 'node_modules', name));
    const Ajv = requireDependency('ajv');
    const addFormats = requireDependency('ajv-formats');
    const YAML = requireDependency('yaml');
    const ajv = new Ajv({ allErrors: true, strict: false });
    addFormats(ajv);
    ajv.addFormat('long', { type: 'number', validate: Number.isInteger });
    const files = (await fs.readdir(manifestDirectory)).filter(name => name.endsWith('.yaml'));
    if (files.length !== 3) throw new Error('Expected version, installer and defaultLocale manifests');
    const manifests = [];
    for (const file of files) {
        const manifest = YAML.parse(await fs.readFile(path.join(manifestDirectory, file), 'utf8'), { uniqueKeys: true });
        if (!['version', 'installer', 'defaultLocale'].includes(manifest.ManifestType) || manifest.ManifestVersion !== '1.12.0') {
            throw new Error(`Unexpected manifest type or schema version: ${file}`);
        }
        const url = `https://raw.githubusercontent.com/microsoft/winget-cli/master/schemas/JSON/manifests/v1.12.0/manifest.${manifest.ManifestType}.1.12.0.json`;
        const response = await fetch(url);
        if (!response.ok) throw new Error(`Could not fetch official schema: ${response.status} ${url}`);
        const validate = ajv.compile(await response.json());
        if (!validate(manifest)) throw new Error(`${file}: ${ajv.errorsText(validate.errors, { separator: '\n' })}`);
        manifests.push(manifest);
        console.log(`Validated ${file}`);
    }
    const version = manifests.find(item => item.ManifestType === 'version');
    if (!version || new Set(manifests.map(item => item.ManifestType)).size !== 3 ||
        manifests.some(item => item.PackageIdentifier !== version.PackageIdentifier || item.PackageVersion !== version.PackageVersion)) {
        throw new Error('Manifest types, package identifiers or versions do not agree');
    }
    const installer = manifests.find(item => item.ManifestType === 'installer');
    if (installer.NestedInstallerType !== 'portable' || installer.NestedInstallerFiles?.[0]?.PortableCommandAlias !== 'tonarink-cli') {
        throw new Error('The portable installer must register the tonarink-cli command');
    }
}

main().catch(error => {
    console.error(error.message);
    process.exitCode = 1;
});
