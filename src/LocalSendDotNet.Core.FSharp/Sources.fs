namespace LocalSendDotNet.FSharp

open System
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks

[<RequireQualifiedAccess>]
module ContentTypes =
    let forFileName (name: string) =
        match Path.GetExtension(name).ToLowerInvariant() with
        | ".txt" -> "text/plain" | ".md" -> "text/markdown" | ".json" -> "application/json"
        | ".pdf" -> "application/pdf" | ".jpg" | ".jpeg" -> "image/jpeg" | ".png" -> "image/png"
        | ".gif" -> "image/gif" | ".webp" -> "image/webp" | ".mp3" -> "audio/mpeg" | ".wav" -> "audio/wav"
        | ".mp4" -> "video/mp4" | ".mkv" -> "video/x-matroska" | ".zip" -> "application/zip"
        | ".7z" -> "application/x-7z-compressed" | ".gz" -> "application/gzip"
        | _ -> "application/octet-stream"

[<RequireQualifiedAccess>]
module SendItems =
    let file path =
        if String.IsNullOrWhiteSpace path then invalidArg (nameof path) "A source path is required."
        let name = Path.GetFileName path
        SendItem.File { Path = path; FileName = name; ContentType = ContentTypes.forFileName name }

    let text value = SendItem.Text(value, "message.txt")

    let stream name length openRead =
        if String.IsNullOrWhiteSpace name then invalidArg (nameof name) "A filename is required."
        if length < 0L then invalidArg (nameof length) "Length cannot be negative."
        SendItem.Stream { FileName = name; Length = length; OpenRead = openRead; ContentType = ContentTypes.forFileName name }

    let fileName = function
        | SendItem.File file -> file.FileName
        | SendItem.Text(_, name) -> name
        | SendItem.Stream stream -> stream.FileName

    let contentType = function
        | SendItem.File file -> file.ContentType
        | SendItem.Text _ -> "text/plain"
        | SendItem.Stream stream -> stream.ContentType

    let length = function
        | SendItem.File file -> FileInfo(file.Path).Length
        | SendItem.Text(text, _) -> int64 (Encoding.UTF8.GetByteCount text)
        | SendItem.Stream stream -> stream.Length

    let internal openRead (ct: CancellationToken) item : Task<Stream> = task {
        ct.ThrowIfCancellationRequested()
        match item with
        | SendItem.File file ->
            return new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 512 * 1024,
                                  FileOptions.Asynchronous ||| FileOptions.SequentialScan) :> Stream
        | SendItem.Text(text, _) -> return new MemoryStream(Encoding.UTF8.GetBytes text, false) :> Stream
        | SendItem.Stream source ->
            let! stream = source.OpenRead ct
            if not stream.CanRead then
                stream.Dispose()
                invalidOp "The stream factory returned a non-readable stream."
            return stream
    }

    let enumerateDirectory recursive (directory: string) =
        let root = Path.GetFullPath directory
        let search = if recursive then SearchOption.AllDirectories else SearchOption.TopDirectoryOnly
        Directory.EnumerateFiles(root, "*", search)
        |> Seq.sortWith (fun a b -> StringComparer.OrdinalIgnoreCase.Compare(a, b))
        |> Seq.map (fun path ->
            let name = Path.GetRelativePath(root, path).Replace('\\', '/')
            SendItem.File { Path = path; FileName = name; ContentType = ContentTypes.forFileName name })
