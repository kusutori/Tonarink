using System.Net;

namespace LocalSendDotNet;

internal static class WebShareHtml
{
    public static string Render(string title, bool pinRequired, WebShareMode mode) => mode == WebShareMode.Receive
        ? RenderReceive(title, pinRequired)
        : RenderDownload(title, pinRequired);

    private static string RenderDownload(string title, bool pinRequired) => $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{WebUtility.HtmlEncode(title)}}</title>
        <style>
        :root { color-scheme: light dark; }
        body { font-family: system-ui, sans-serif; margin: 0 auto; padding: 24px; max-width: 40rem; }
        h1 { font-size: 1.5rem; margin: 0 0 16px; }
        a.file { display: flex; justify-content: space-between; gap: 16px; padding: 14px 0; border-bottom: 1px solid color-mix(in srgb, currentColor 16%, transparent); text-decoration: none; color: inherit; }
        .muted { opacity: .65; }
        .status { margin: 12px 0 20px; }
        </style>
        </head>
        <body>
        <h1>{{WebUtility.HtmlEncode(title)}}</h1>
        <p id="status" class="status muted"></p>
        <div id="files"></div>
        <script>
        const pinRequired = {{(pinRequired ? "true" : "false")}};
        function formatSize(bytes) {
          if (bytes < 1024) return bytes + ' B';
          if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB';
          return (bytes / 1048576).toFixed(1) + ' MB';
        }
        async function start() {
          const status = document.getElementById('status');
          const box = document.getElementById('files');
          let pin = '';
          if (pinRequired) {
            pin = prompt('PIN') || '';
          }
          status.textContent = '…';
          const response = await fetch('/api/localsend/v2/prepare-download?pin=' + encodeURIComponent(pin), { method: 'POST' });
          if (response.status === 401) { status.textContent = 'PIN'; return; }
          if (response.status === 429) { status.textContent = 'PIN'; return; }
          if (response.status === 403) { status.textContent = 'Denied'; return; }
          if (!response.ok) { status.textContent = 'Error ' + response.status; return; }
          const data = await response.json();
          status.textContent = '';
          for (const file of data.files) {
            const link = document.createElement('a');
            link.className = 'file';
            link.href = '/api/localsend/v2/download?sessionId=' + encodeURIComponent(data.sessionId)
              + '&fileId=' + encodeURIComponent(file.id)
              + (pin ? '&pin=' + encodeURIComponent(pin) : '');
            const name = document.createElement('span');
            name.textContent = file.fileName;
            const size = document.createElement('span');
            size.className = 'muted';
            size.textContent = formatSize(file.size);
            link.append(name, size);
            box.append(link);
          }
        }
        start().catch(() => { document.getElementById('status').textContent = 'Error'; });
        </script>
        </body>
        </html>
        """;

    private static string RenderReceive(string title, bool pinRequired) => $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Tonarink</title>
        <style>
        :root { color-scheme: light dark; }
        * { box-sizing: border-box; }
        body { font-family: system-ui, sans-serif; margin: 0; min-height: 100vh; display: grid; place-items: center; padding: 24px; }
        main { width: min(100%, 34rem); text-align: center; }
        h1 { font-size: 2rem; margin: 0 0 32px; }
        button { width: 100%; min-height: 9rem; border: 1px solid color-mix(in srgb, currentColor 22%, transparent); border-radius: 18px; background: color-mix(in srgb, currentColor 6%, transparent); color: inherit; font: inherit; font-size: 1.25rem; cursor: pointer; }
        button:hover { background: color-mix(in srgb, currentColor 11%, transparent); }
        button:disabled { cursor: wait; opacity: .6; }
        .icon { display: block; font-size: 2.5rem; margin-bottom: 10px; }
        .status { min-height: 1.5rem; margin-top: 20px; opacity: .7; }
        input { display: none; }
        </style>
        </head>
        <body>
        <main>
          <h1>Tonarink</h1>
          <button id="pick" type="button"><span class="icon">↑</span><span id="pickText">Upload files</span></button>
          <input id="files" type="file" multiple>
          <p id="status" class="status"></p>
        </main>
        <script>
        const pinRequired = {{(pinRequired ? "true" : "false")}};
        const chinese = (navigator.language || '').toLowerCase().startsWith('zh');
        const pick = document.getElementById('pick');
        const input = document.getElementById('files');
        const status = document.getElementById('status');
        document.getElementById('pickText').textContent = chinese ? '选择并上传文件' : 'Choose files to upload';
        pick.addEventListener('click', () => input.click());
        input.addEventListener('change', () => upload(Array.from(input.files || [])));

        function createFileId() {
          if (window.crypto && typeof window.crypto.randomUUID === 'function')
            return window.crypto.randomUUID().replace(/-/g, '');

          const bytes = new Uint8Array(16);
          if (window.crypto && typeof window.crypto.getRandomValues === 'function') {
            window.crypto.getRandomValues(bytes);
          } else {
            for (let index = 0; index < bytes.length; index++)
              bytes[index] = Math.floor(Math.random() * 256);
          }
          return Array.from(bytes, value => value.toString(16).padStart(2, '0')).join('');
        }

        async function upload(files) {
          if (!files.length) return;
          let pin = '';
          if (pinRequired) pin = prompt('PIN') || '';
          pick.disabled = true;
          status.textContent = chinese ? '等待 Tonarink 接收…' : 'Waiting for Tonarink…';
          try {
            const metadata = files.map(file => ({
              id: createFileId(),
              fileName: file.name,
              size: file.size,
              fileType: file.type || 'application/octet-stream'
            }));
            const preparedResponse = await fetch('/api/localsend/v2/prepare-web-upload?pin=' + encodeURIComponent(pin), {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ files: metadata })
            });
            if (preparedResponse.status === 401) throw new Error(chinese ? 'PIN 不正确' : 'Incorrect PIN');
            if (preparedResponse.status === 429) throw new Error(chinese ? 'PIN 尝试次数过多' : 'Too many PIN attempts');
            if (preparedResponse.status === 403) throw new Error(chinese ? '接收方已拒绝' : 'The receiver declined');
            if (preparedResponse.status === 408) throw new Error(chinese ? '等待接收超时' : 'The request timed out');
            if (preparedResponse.status === 413) throw new Error(chinese ? '文件过大或数量过多' : 'The selection is too large');
            if (!preparedResponse.ok) throw new Error((chinese ? '请求失败：' : 'Request failed: ') + preparedResponse.status);
            const prepared = await preparedResponse.json();
            let uploaded = 0;
            for (let index = 0; index < files.length; index++) {
              const token = prepared.files[metadata[index].id];
              if (!token) continue;
              status.textContent = `${chinese ? '正在上传' : 'Uploading'} ${index + 1}/${files.length}: ${files[index].name}`;
              const response = await fetch('/api/localsend/v2/upload?sessionId=' + encodeURIComponent(prepared.sessionId)
                + '&fileId=' + encodeURIComponent(metadata[index].id)
                + '&token=' + encodeURIComponent(token), {
                  method: 'POST',
                  headers: { 'Content-Type': metadata[index].fileType },
                  body: files[index]
                });
              if (!response.ok) throw new Error((chinese ? '上传失败：' : 'Upload failed: ') + response.status);
              uploaded++;
            }
            status.textContent = uploaded
              ? (chinese ? `已发送 ${uploaded} 个文件` : `Sent ${uploaded} file${uploaded === 1 ? '' : 's'}`)
              : (chinese ? '没有文件被接受' : 'No files were accepted');
          } catch (error) {
            status.textContent = error instanceof Error ? error.message : (chinese ? '上传失败' : 'Upload failed');
          } finally {
            pick.disabled = false;
            input.value = '';
          }
        }
        </script>
        </body>
        </html>
        """;
}
