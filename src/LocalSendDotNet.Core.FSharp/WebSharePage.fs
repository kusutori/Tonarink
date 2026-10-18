namespace LocalSendDotNet.FSharp

open System
open System.IO
open System.Net
open System.Text
open System.Text.Json

[<RequireQualifiedAccess>]
module internal WebSharePage =
    let private strings culture =
        let prefix = culture |> Option.defaultValue "en" |> fun value -> (value.Split('-')[0]).ToLowerInvariant()
        // The browser page has no dependency on the host's UI resources or reflection-based serialization.
        let values =
            match prefix with
            | "zh" -> ["请输入 PIN"; "选择并上传文件"; "等待 Tonarink 接收…"; "PIN 不正确"; "PIN 尝试次数过多"; "接收方已拒绝"; "等待接收超时"; "文件过大或数量过多"; "请求失败："; "正在上传"; "已发送 {count} 个文件"; "已发送 {count} 个文件"; "没有文件被接受"; "上传失败"]
            | "ja" -> ["PIN を入力してください"; "アップロードするファイルを選択"; "Tonarink の応答を待っています…"; "PIN が違います"; "PIN の試行回数が多すぎます"; "受信側が拒否しました"; "リクエストがタイムアウトしました"; "選択したファイルが大きすぎるか、多すぎます"; "リクエストに失敗しました: "; "アップロード中"; "{count} 件のファイルを送信しました"; "{count} 件のファイルを送信しました"; "承認されたファイルはありません"; "アップロードに失敗しました"]
            | "ko" -> ["PIN을 입력하세요"; "업로드할 파일 선택"; "Tonarink 응답 대기 중…"; "PIN이 올바르지 않습니다"; "PIN 입력 횟수가 너무 많습니다"; "수신자가 거절했습니다"; "요청 시간이 초과되었습니다"; "선택한 파일이 너무 크거나 많습니다"; "요청 실패: "; "업로드 중"; "파일 {count}개를 보냈습니다"; "파일 {count}개를 보냈습니다"; "수락된 파일이 없습니다"; "업로드 실패"]
            | "fr" -> ["Saisir le PIN"; "Choisir les fichiers à envoyer"; "En attente de Tonarink…"; "PIN incorrect"; "Trop de tentatives de PIN"; "Le destinataire a refusé"; "Le délai de la demande est dépassé"; "La sélection est trop volumineuse"; "Échec de la demande : "; "Envoi en cours"; "{count} fichiers envoyés"; "{count} fichier envoyé"; "Aucun fichier n’a été accepté"; "Échec de l’envoi"]
            | "es" -> ["Introduce el PIN"; "Elegir archivos para subir"; "Esperando a Tonarink…"; "PIN incorrecto"; "Demasiados intentos de PIN"; "El destinatario rechazó la solicitud"; "Se agotó el tiempo de la solicitud"; "La selección es demasiado grande"; "Error de solicitud: "; "Subiendo"; "{count} archivos enviados"; "{count} archivo enviado"; "No se aceptó ningún archivo"; "Error de subida"]
            | _ -> ["Enter PIN"; "Choose files to upload"; "Waiting for Tonarink…"; "Incorrect PIN"; "Too many PIN attempts"; "The receiver declined"; "The request timed out"; "The selection is too large"; "Request failed: "; "Uploading"; "Sent {count} files"; "Sent {count} file"; "No files were accepted"; "Upload failed"]
        let keys = ["pinPrompt"; "pick"; "waiting"; "incorrectPin"; "tooManyPinAttempts"; "declined"; "timedOut"; "tooLarge"; "requestFailed"; "uploading"; "sentFiles"; "sentFile"; "noneAccepted"; "uploadFailed"]
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        writer.WriteStartObject()
        for key, value in List.zip keys values do writer.WriteString(key, value)
        writer.WriteEndObject()
        writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())

    let render title (state: WebShareState) =
        let receive = if state.Mode = WebShareMode.Receive then "true" else "false"
        let pinRequired = if state.Options.Pin.IsSome then "true" else "false"
        let html = """<!doctype html>
<html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>__TITLE__</title><style>
:root{color-scheme:light dark}body{font-family:system-ui,sans-serif;margin:32px auto;padding:0 24px;max-width:40rem}
button{font:inherit;padding:18px;border-radius:10px;cursor:pointer}a{display:flex;justify-content:space-between;gap:12px;padding:16px 0;color:inherit}#status{opacity:.7;min-height:2rem}
</style></head><body><h1>__TITLE__</h1><button id="pick" hidden></button><input id="files" type="file" multiple hidden><p id="status"></p><div id="downloads"></div>
<script>
const s=__STRINGS__,receive=__RECEIVE__,pinRequired=__PIN__;
const status=document.getElementById('status'),pick=document.getElementById('pick'),input=document.getElementById('files');
let pin='';
function query(params){if(pin)params.pin=pin;return new URLSearchParams(params).toString()}
function failure(response){return ({401:s.incorrectPin,403:s.declined,408:s.timedOut,413:s.tooLarge,429:s.tooManyPinAttempts})[response.status]||s.requestFailed+response.status}
function askPin(){if(pinRequired)pin=prompt(s.pinPrompt)||''}
async function downloads(){askPin();status.textContent=s.waiting;const response=await fetch('/api/localsend/v2/prepare-download?'+query({}),{method:'POST'});
 if(!response.ok){status.textContent=failure(response);return}const data=await response.json();status.textContent='';
 for(const file of data.files){const link=document.createElement('a');link.href='/api/localsend/v2/download?'+query({sessionId:data.sessionId,fileId:file.id});
 const name=document.createElement('span'),size=document.createElement('span');name.textContent=file.fileName;size.textContent=file.size+' B';link.append(name,size);document.getElementById('downloads').append(link)}}
async function upload(files){if(!files.length)return;pick.disabled=true;
 try{askPin();status.textContent=s.waiting;const entries=files.map((file,index)=>({id:String(index),fileName:file.name,size:file.size,fileType:file.type||'application/octet-stream'}));
 const response=await fetch('/api/localsend/v2/prepare-web-upload?'+query({}),{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({files:entries})});
 if(!response.ok){status.textContent=failure(response);return}if(response.status===204){status.textContent=s.noneAccepted;return}
 const accepted=await response.json();let count=0;
 for(const [id,token]of Object.entries(accepted.files)){const file=files[Number(id)];if(!file)throw Error('Unknown file');status.textContent=s.uploading+' '+file.name;
 const result=await fetch('/api/localsend/v2/upload?'+query({sessionId:accepted.sessionId,fileId:id,token}),{method:'POST',body:file});if(!result.ok)throw Error(failure(result));count++}
 status.textContent=count?(count===1?s.sentFile:s.sentFiles).replace('{count}',count):s.noneAccepted;
 }catch(error){status.textContent=s.uploadFailed+': '+error.message}finally{pick.disabled=false;input.value=''}}
if(receive){pick.hidden=false;pick.textContent=s.pick;pick.onclick=()=>input.click();input.onchange=()=>upload(Array.from(input.files||[]))}
else downloads().catch(()=>status.textContent=s.requestFailed);
</script></body></html>"""
        html.Replace("__RECEIVE__", receive).Replace("__PIN__", pinRequired)
            .Replace("__STRINGS__", strings state.Options.UiCulture).Replace("__TITLE__", WebUtility.HtmlEncode title)
