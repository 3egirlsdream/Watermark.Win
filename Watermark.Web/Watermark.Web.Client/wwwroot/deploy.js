const apiBase = window.__LITOGRAPH_DEPLOY_API__
    ?? (["127.0.0.1", "localhost"].includes(window.location.hostname)
        ? "http://127.0.0.1:4396"
        : "http://thankful.top:4396");
const maximumPackageSize = 2147483647;

const platforms = {
    ".exe": { id: "windows", name: "Windows", fixedFileName: "Watermark.Win.Update.exe", client: "WatermarkV3" },
    ".apk": { id: "android", name: "Android", fixedFileName: "DaVinci Frame Master-水印相框大师.apk", client: "WatermarkAndroid" },
    ".pkg": { id: "macos", name: "macOS", fixedFileName: "Litograph.pkg", client: "WatermarkMac" },
    ".ipa": { id: "ios", name: "iOS", fixedFileName: "Litograph.ipa", client: "WatermarkIOS" }
};

export function openFilePicker(input) {
    input.value = "";
    input.click();
}

export async function validateAccess(accessToken) {
    await deployRequest("/api/Deploy/Validate", accessToken, { method: "POST" });
}

export async function loadCatalog() {
    const entries = Object.values(platforms);
    const releases = await Promise.all(entries.map(async platform => {
        try {
            const response = await fetch(`${apiBase}/api/CloudSync/GetVersion?Client=${encodeURIComponent(platform.client)}`);
            const payload = await response.json();
            return {
                platform: platform.id,
                version: payload?.data?.VERSION ?? null,
                dateTime: payload?.data?.DATETIME ?? null
            };
        } catch {
            return { platform: platform.id, version: null, dateTime: null };
        }
    }));
    return releases;
}

export async function inspectPackage(input) {
    const file = selectedFile(input);
    const extension = extensionOf(file.name);
    const platform = platforms[extension];

    if (!platform) {
        throw new Error("仅支持 Windows .exe、Android .apk、macOS .pkg 和 iOS .ipa 安装包。");
    }
    if (file.size <= 0) {
        throw new Error("所选文件为空，请重新选择安装包。");
    }
    if (file.size > maximumPackageSize) {
        throw new Error("安装包超过 2 GB，当前七牛上传策略不支持。");
    }

    const detected = await detectVersion(file, extension);
    return {
        name: file.name,
        extension,
        size: file.size,
        platform: platform.id,
        platformName: platform.name,
        fixedFileName: platform.fixedFileName,
        downloadUrl: `https://cdn.thankful.top/${encodeURIComponent(platform.fixedFileName)}`,
        version: detected.version,
        versionSource: detected.source
    };
}

export async function uploadPackage(input, accessToken, platformId, progressReceiver) {
    const file = selectedFile(input);
    const platform = Object.values(platforms).find(item => item.id === platformId);
    if (!platform || extensionOf(file.name) !== Object.keys(platforms).find(key => platforms[key].id === platformId)) {
        throw new Error("安装包格式与识别到的平台不匹配，请重新选择文件。");
    }

    const tokenPayload = await deployRequest(
        `/api/Deploy/UploadToken?platform=${encodeURIComponent(platformId)}&fileSize=${file.size}`,
        accessToken,
        { method: "POST" });
    const upload = tokenPayload.data;

    await new Promise((resolve, reject) => {
        const xhr = new XMLHttpRequest();
        const form = new FormData();
        form.append("token", upload.token);
        form.append("key", upload.key);
        form.append("file", file, upload.key);

        xhr.open("POST", upload.uploadUrl, true);
        xhr.upload.onprogress = event => {
            if (!event.lengthComputable) return;
            const progress = Math.min(99, Math.round(event.loaded / event.total * 100));
            progressReceiver.invokeMethodAsync("ReportUploadProgress", progress);
        };
        xhr.onerror = () => reject(new Error("无法连接七牛上传节点，请检查网络后重试。"));
        xhr.onabort = () => reject(new Error("上传已取消。"));
        xhr.onload = () => {
            if (xhr.status >= 200 && xhr.status < 300) {
                progressReceiver.invokeMethodAsync("ReportUploadProgress", 100);
                resolve();
                return;
            }

            let message = "七牛上传失败";
            try { message = JSON.parse(xhr.responseText)?.error ?? message; } catch { }
            reject(new Error(`${message}（HTTP ${xhr.status}）`));
        };
        xhr.send(form);
    });
}

export async function publishRelease(accessToken, platform, version, memo) {
    await deployRequest("/api/Deploy/Publish", accessToken, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ platform, version, memo })
    });
}

async function deployRequest(path, accessToken, options = {}) {
    const headers = new Headers(options.headers ?? {});
    headers.set("X-Deploy-Token", accessToken);
    const response = await fetch(`${apiBase}${path}`, { ...options, headers });

    let payload;
    try {
        payload = await response.json();
    } catch {
        throw new Error(`发布 API 返回了无法识别的响应（HTTP ${response.status}）。`);
    }

    if (!response.ok || payload?.success !== true) {
        throw new Error(payload?.message?.content ?? `发布 API 请求失败（HTTP ${response.status}）。`);
    }
    return payload;
}

function selectedFile(input) {
    const file = input?.files?.[0];
    if (!file) throw new Error("请先选择一个安装包。");
    return file;
}

function extensionOf(name) {
    const index = name.lastIndexOf(".");
    return index < 0 ? "" : name.slice(index).toLowerCase();
}

async function detectVersion(file, extension) {
    try {
        let embedded = null;
        if (extension === ".apk") embedded = await versionFromApk(file);
        if (extension === ".ipa") embedded = await versionFromIpa(file);
        if (extension === ".pkg") embedded = await versionFromPkg(file);
        if (extension === ".exe") embedded = await versionFromExe(file);
        const normalized = normalizeVersion(embedded);
        if (normalized) return { version: normalized, source: "从应用信息自动读取" };
    } catch (error) {
        console.debug("Embedded version detection failed", error);
    }

    const fromName = normalizeVersion(file.name);
    if (fromName) return { version: fromName, source: "从文件名自动读取" };
    return { version: null, source: "未读取到，请手动填写" };
}

function normalizeVersion(value) {
    if (!value) return null;
    const match = String(value).match(/(?:^|[^\d])(\d{1,5}\.\d{1,5}(?:\.\d{1,5}){0,2})(?!\d)/);
    return match?.[1] ?? null;
}

async function versionFromApk(file) {
    const manifest = await readZipEntry(file, name => name === "AndroidManifest.xml");
    if (!manifest) return null;
    return parseAndroidManifest(manifest)?.versionName ?? null;
}

function parseAndroidManifest(bytes) {
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    let strings = [];
    let offset = 8;
    while (offset + 8 <= bytes.byteLength) {
        const type = view.getUint16(offset, true);
        const headerSize = view.getUint16(offset + 2, true);
        const chunkSize = view.getUint32(offset + 4, true);
        if (chunkSize < 8 || offset + chunkSize > bytes.byteLength) break;

        if (type === 0x0001) {
            strings = parseStringPool(view, offset, headerSize);
        } else if (type === 0x0102 && strings.length && headerSize >= 16) {
            const elementName = poolString(strings, view.getUint32(offset + 20, true));
            if (elementName === "manifest") {
                const attributeStart = view.getUint16(offset + 24, true);
                const attributeSize = view.getUint16(offset + 26, true);
                const attributeCount = view.getUint16(offset + 28, true);
                const result = {};
                let attributeOffset = offset + headerSize + attributeStart;
                for (let index = 0; index < attributeCount; index++, attributeOffset += attributeSize) {
                    if (attributeOffset + 20 > offset + chunkSize) break;
                    const name = poolString(strings, view.getUint32(attributeOffset + 4, true));
                    const rawIndex = view.getUint32(attributeOffset + 8, true);
                    const dataType = view.getUint8(attributeOffset + 15);
                    const data = view.getUint32(attributeOffset + 16, true);
                    const rawValue = rawIndex !== 0xffffffff
                        ? poolString(strings, rawIndex)
                        : dataType === 0x03 ? poolString(strings, data) : String(data);
                    if (name === "versionName") result.versionName = rawValue;
                    if (name === "versionCode") result.versionCode = rawValue;
                }
                return result;
            }
        }
        offset += chunkSize;
    }
    return null;
}

function parseStringPool(view, offset, headerSize) {
    const stringCount = view.getUint32(offset + 8, true);
    const flags = view.getUint32(offset + 16, true);
    const stringsStart = view.getUint32(offset + 20, true);
    const utf8 = (flags & 0x100) !== 0;
    const result = [];
    for (let index = 0; index < stringCount; index++) {
        const stringOffset = view.getUint32(offset + headerSize + index * 4, true);
        let cursor = offset + stringsStart + stringOffset;
        if (utf8) {
            [, cursor] = readUtf8Length(view, cursor);
            let byteLength;
            [byteLength, cursor] = readUtf8Length(view, cursor);
            result.push(new TextDecoder("utf-8").decode(new Uint8Array(view.buffer, view.byteOffset + cursor, byteLength)));
        } else {
            let characterLength;
            [characterLength, cursor] = readUtf16Length(view, cursor);
            result.push(new TextDecoder("utf-16le").decode(new Uint8Array(view.buffer, view.byteOffset + cursor, characterLength * 2)));
        }
    }
    return result;
}

function readUtf8Length(view, offset) {
    const first = view.getUint8(offset++);
    if ((first & 0x80) === 0) return [first, offset];
    return [((first & 0x7f) << 8) | view.getUint8(offset++), offset];
}

function readUtf16Length(view, offset) {
    const first = view.getUint16(offset, true);
    offset += 2;
    if ((first & 0x8000) === 0) return [first, offset];
    return [((first & 0x7fff) << 16) | view.getUint16(offset, true), offset + 2];
}

function poolString(strings, index) {
    return index === 0xffffffff ? null : strings[index] ?? null;
}

async function versionFromIpa(file) {
    const plist = await readZipEntry(file, name => /^Payload\/[^/]+\.app\/Info\.plist$/i.test(name));
    if (!plist) return null;
    if (startsWithAscii(plist, "bplist")) {
        const root = parseBinaryPlist(plist);
        return root?.CFBundleShortVersionString ?? root?.CFBundleVersion ?? null;
    }
    const xml = new TextDecoder("utf-8").decode(plist);
    return plistString(xml, "CFBundleShortVersionString") ?? plistString(xml, "CFBundleVersion");
}

function plistString(xml, key) {
    const escaped = key.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    const match = xml.match(new RegExp(`<key>\\s*${escaped}\\s*</key>\\s*<string>([^<]+)</string>`, "i"));
    return match?.[1]?.trim() ?? null;
}

function parseBinaryPlist(bytes) {
    if (bytes.length < 40) return null;
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    const trailer = bytes.length - 32;
    const offsetSize = view.getUint8(trailer + 6);
    const referenceSize = view.getUint8(trailer + 7);
    const objectCount = Number(readUnsigned(view, trailer + 8, 8));
    const topObject = Number(readUnsigned(view, trailer + 16, 8));
    const offsetTable = Number(readUnsigned(view, trailer + 24, 8));
    if (!offsetSize || !referenceSize || objectCount > 100000 || offsetTable >= bytes.length) return null;

    const offsets = new Array(objectCount);
    for (let i = 0; i < objectCount; i++) offsets[i] = Number(readUnsigned(view, offsetTable + i * offsetSize, offsetSize));
    const cache = new Map();

    function lengthAt(position, info) {
        if (info < 0x0f) return [info, position];
        const marker = view.getUint8(position++);
        if ((marker >> 4) !== 0x1) throw new Error("Invalid binary plist length");
        const size = 1 << (marker & 0x0f);
        return [Number(readUnsigned(view, position, size)), position + size];
    }

    function readObject(index) {
        if (cache.has(index)) return cache.get(index);
        let position = offsets[index];
        const marker = view.getUint8(position++);
        const type = marker >> 4;
        const info = marker & 0x0f;
        let value = null;

        if (type === 0x0) value = info === 0x9 ? true : info === 0x8 ? false : null;
        else if (type === 0x1) value = Number(readUnsigned(view, position, 1 << info));
        else if (type === 0x5 || type === 0x6) {
            let length;
            [length, position] = lengthAt(position, info);
            const byteLength = type === 0x6 ? length * 2 : length;
            value = new TextDecoder(type === 0x6 ? "utf-16be" : "ascii")
                .decode(new Uint8Array(view.buffer, view.byteOffset + position, byteLength));
        } else if (type === 0xd) {
            let count;
            [count, position] = lengthAt(position, info);
            const result = {};
            for (let i = 0; i < count; i++) {
                const keyIndex = Number(readUnsigned(view, position + i * referenceSize, referenceSize));
                const valueIndex = Number(readUnsigned(view, position + (count + i) * referenceSize, referenceSize));
                result[String(readObject(keyIndex))] = readObject(valueIndex);
            }
            value = result;
        }
        cache.set(index, value);
        return value;
    }

    return readObject(topObject);
}

function readUnsigned(view, offset, length) {
    let value = 0n;
    for (let i = 0; i < length; i++) value = (value << 8n) | BigInt(view.getUint8(offset + i));
    return value;
}

async function versionFromPkg(file) {
    if (file.size < 28) return null;
    const headerBytes = new Uint8Array(await file.slice(0, 28).arrayBuffer());
    if (!startsWithAscii(headerBytes, "xar!")) return null;
    const header = new DataView(headerBytes.buffer);
    const headerSize = header.getUint16(4, false);
    const compressedLength = Number(header.getBigUint64(8, false));
    if (compressedLength <= 0 || compressedLength > 64 * 1024 * 1024) return null;

    const compressedToc = file.slice(headerSize, headerSize + compressedLength);
    const tocBytes = await decompressBlob(compressedToc, "deflate");
    const toc = new DOMParser().parseFromString(new TextDecoder().decode(tocBytes), "application/xml");
    if (toc.querySelector("parsererror")) return null;

    const heapStart = headerSize + compressedLength;
    const entries = [...toc.querySelectorAll("file")];
    for (const preferredName of ["PackageInfo", "Distribution"]) {
        const entry = entries.find(node => directChildText(node, "name") === preferredName);
        if (!entry) continue;
        const data = [...entry.children].find(node => node.tagName === "data");
        if (!data) continue;
        const offset = Number(directChildText(data, "offset"));
        const length = Number(directChildText(data, "length"));
        if (!Number.isFinite(offset) || !Number.isFinite(length) || length <= 0 || length > 8 * 1024 * 1024) continue;
        let content = new Uint8Array(await file.slice(heapStart + offset, heapStart + offset + length).arrayBuffer());
        const encoding = [...data.children].find(node => node.tagName === "encoding")?.getAttribute("style") ?? "";
        if (encoding.includes("gzip")) content = await decompressBlob(new Blob([content]), "deflate");
        const text = new TextDecoder().decode(content);
        const match = text.match(/(?:pkg-info|pkg-ref)[^>]*\bversion=["']([^"']+)["']/i);
        if (match) return match[1];
    }
    return null;
}

function directChildText(node, name) {
    return [...node.children].find(child => child.tagName === name)?.textContent?.trim() ?? null;
}

async function versionFromExe(file) {
    const chunks = [file.slice(0, Math.min(file.size, 12 * 1024 * 1024))];
    if (file.size > 12 * 1024 * 1024) chunks.push(file.slice(Math.max(0, file.size - 2 * 1024 * 1024)));
    for (const chunk of chunks) {
        const text = new TextDecoder("utf-16le").decode(await chunk.arrayBuffer());
        for (const key of ["ProductVersion", "FileVersion"]) {
            const index = text.indexOf(key);
            if (index < 0) continue;
            const version = normalizeVersion(text.slice(index + key.length, index + key.length + 300));
            if (version) return version;
        }
    }
    return null;
}

async function readZipEntry(file, predicate) {
    if (file.size < 22) return null;
    const tailSize = Math.min(file.size, 65557);
    const tail = new Uint8Array(await file.slice(file.size - tailSize).arrayBuffer());
    const tailView = new DataView(tail.buffer, tail.byteOffset, tail.byteLength);
    let endOffset = -1;
    for (let offset = tail.length - 22; offset >= 0; offset--) {
        if (tailView.getUint32(offset, true) === 0x06054b50) { endOffset = offset; break; }
    }
    if (endOffset < 0) return null;

    const entryCount = tailView.getUint16(endOffset + 10, true);
    const centralSize = tailView.getUint32(endOffset + 12, true);
    const centralOffset = tailView.getUint32(endOffset + 16, true);
    if (centralSize > 64 * 1024 * 1024) return null;
    const central = new Uint8Array(await file.slice(centralOffset, centralOffset + centralSize).arrayBuffer());
    const view = new DataView(central.buffer, central.byteOffset, central.byteLength);
    let offset = 0;
    for (let index = 0; index < entryCount && offset + 46 <= central.length; index++) {
        if (view.getUint32(offset, true) !== 0x02014b50) break;
        const method = view.getUint16(offset + 10, true);
        const compressedSize = view.getUint32(offset + 20, true);
        const fileNameLength = view.getUint16(offset + 28, true);
        const extraLength = view.getUint16(offset + 30, true);
        const commentLength = view.getUint16(offset + 32, true);
        const localOffset = view.getUint32(offset + 42, true);
        const name = new TextDecoder().decode(central.subarray(offset + 46, offset + 46 + fileNameLength));
        if (predicate(name)) {
            const localHeader = new Uint8Array(await file.slice(localOffset, localOffset + 30).arrayBuffer());
            const localView = new DataView(localHeader.buffer);
            if (localView.getUint32(0, true) !== 0x04034b50) return null;
            const localNameLength = localView.getUint16(26, true);
            const localExtraLength = localView.getUint16(28, true);
            const dataOffset = localOffset + 30 + localNameLength + localExtraLength;
            const compressed = file.slice(dataOffset, dataOffset + compressedSize);
            if (method === 0) return new Uint8Array(await compressed.arrayBuffer());
            if (method === 8) return decompressBlob(compressed, "deflate-raw");
            return null;
        }
        offset += 46 + fileNameLength + extraLength + commentLength;
    }
    return null;
}

async function decompressBlob(blob, format) {
    if (typeof DecompressionStream === "undefined") throw new Error("当前浏览器不支持安装包元数据解压。");
    const stream = blob.stream().pipeThrough(new DecompressionStream(format));
    return new Uint8Array(await new Response(stream).arrayBuffer());
}

function startsWithAscii(bytes, value) {
    if (bytes.length < value.length) return false;
    for (let i = 0; i < value.length; i++) if (bytes[i] !== value.charCodeAt(i)) return false;
    return true;
}
