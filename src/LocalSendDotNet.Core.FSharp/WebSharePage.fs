namespace LocalSendDotNet.FSharp

open LocalSendDotNet.Resources

open System
open System.IO
open System.Net
open System.Text
open System.Text.Json

[<RequireQualifiedAccess>]
module internal WebSharePage =
    let private strings culture = CoreText.WebShare(Option.toObj culture).ToJson()

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
function failure(response){return ({401:s.incorrectPin,403:s.declined,409:s.busy,408:s.timedOut,413:s.tooLarge,429:s.rateLimited})[response.status]||s.requestFailed+response.status}
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
 for(const [id,token]of Object.entries(accepted.files)){const file=files[Number(id)];if(!file)throw Error(s.requestFailed);status.textContent=s.uploading+' '+file.name;
 const result=await fetch('/api/localsend/v2/upload?'+query({sessionId:accepted.sessionId,fileId:id,token}),{method:'POST',body:file});if(!result.ok)throw Error(failure(result));count++}
 status.textContent=count?(count===1?s.sentFile:s.sentFiles).replace('{count}',count):s.noneAccepted;
 }catch(error){status.textContent=s.uploadFailed+': '+error.message}finally{pick.disabled=false;input.value=''}}
if(receive){pick.hidden=false;pick.textContent=s.pick;pick.onclick=()=>input.click();input.onchange=()=>upload(Array.from(input.files||[]))}
else downloads().catch(()=>status.textContent=s.requestFailed);
</script></body></html>"""
        html.Replace("__RECEIVE__", receive).Replace("__PIN__", pinRequired)
            .Replace("__STRINGS__", strings state.Options.UiCulture).Replace("__TITLE__", WebUtility.HtmlEncode title)
