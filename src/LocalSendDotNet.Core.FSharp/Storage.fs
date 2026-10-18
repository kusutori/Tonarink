namespace LocalSendDotNet.FSharp

open LocalSendDotNet.Resources

open System
open System.Collections.Generic
open System.IO
open System.Security.Cryptography
open System.Threading
open System.Threading.Tasks

exception StorageException of FailureCode * string

[<RequireQualifiedAccess>]
module internal Storage =
    let private unsafePath message = raise (StorageException(FailureCode.InvalidDestination, message))

    let private sanitize (value: string) =
        let invalid = Path.GetInvalidFileNameChars() |> Set.ofArray
        let sanitized =
            value.ToCharArray()
            |> Array.map (fun c -> if Char.IsControl c || Set.contains c invalid then '_' else c)
            |> fun chars -> String(chars).Trim().TrimEnd('.', ' ')
        if sanitized = "" || sanitized = "." || sanitized = ".." then unsafePath (CoreText.InvalidFilename)
        let stem = (sanitized.Split('.')[0]).ToUpperInvariant()
        let reserved =
            stem = "CON" || stem = "PRN" || stem = "AUX" || stem = "NUL"
            || (stem.Length = 4 && (stem.StartsWith "COM" || stem.StartsWith "LPT") && stem[3] >= '1' && stem[3] <= '9')
        if OperatingSystem.IsWindows() && reserved then "_" + sanitized else sanitized

    let private ensureNoLinks root (components: string array) =
        let mutable path = root
        for part in components do
            path <- Path.Combine(path, part)
            if Directory.Exists path then
                let info = DirectoryInfo path
                if not (isNull info.LinkTarget) || info.Attributes.HasFlag FileAttributes.ReparsePoint then
                    unsafePath (CoreText.PathLinked)

    let resolveUnique root (requestedName: string) (reserved: ISet<string>) =
        let fullRoot = Path.GetFullPath root
        Directory.CreateDirectory fullRoot |> ignore
        if Path.IsPathRooted requestedName || requestedName.Contains ':' then unsafePath (CoreText.UnsafePath)
        let components = requestedName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
        if components.Length = 0 || Array.exists (fun c -> c = "." || c = "..") components then unsafePath (CoreText.UnsafePath)
        let safe = Array.map sanitize components
        let parents = safe |> Array.take (safe.Length - 1)
        ensureNoLinks fullRoot parents
        let candidate = Path.Combine(Array.append [| fullRoot |] safe) |> Path.GetFullPath
        let prefix = Path.TrimEndingDirectorySeparator fullRoot + string Path.DirectorySeparatorChar
        let comparison = if OperatingSystem.IsWindows() then StringComparison.OrdinalIgnoreCase else StringComparison.Ordinal
        if not (candidate.StartsWith(prefix, comparison)) then unsafePath (CoreText.PathEscapes)
        let directory = Path.GetDirectoryName candidate |> Option.ofObj |> Option.defaultWith (fun () -> unsafePath (CoreText.ParentMissing))
        Directory.CreateDirectory directory |> ignore
        ensureNoLinks fullRoot parents
        let available path = not (File.Exists path || Directory.Exists path) && reserved.Add path
        if available candidate then candidate
        else
            let stem, extension = Path.GetFileNameWithoutExtension candidate, Path.GetExtension candidate
            Seq.initInfinite (fun i -> Path.Combine(directory, $"{stem} ({i + 1}){extension}")) |> Seq.find available

    let receive (input: Stream) destination (item: IncomingItem) verifySha256 report (ct: CancellationToken) = task {
        let temporary = destination + ".part-" + Guid.NewGuid().ToString("N")
        try
            let! count = task {
                use output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024,
                                            FileOptions.Asynchronous ||| FileOptions.SequentialScan)
                use hash = IncrementalHash.CreateHash HashAlgorithmName.SHA256
                let buffer = Array.zeroCreate<byte> (128 * 1024)
                let mutable count, finished = 0L, false
                while not finished do
                    let! read = input.ReadAsync(Memory buffer, ct)
                    if read = 0 then finished <- true
                    else
                        if int64 read > item.Size - count then
                            raise (StorageException(FailureCode.LengthMismatch, (CoreText.BodyTooLong)))
                        do! output.WriteAsync(ReadOnlyMemory(buffer, 0, read), ct)
                        if verifySha256 && item.Sha256.IsSome then hash.AppendData(buffer, 0, read)
                        count <- count + int64 read
                        report count
                if count <> item.Size then raise (StorageException(FailureCode.LengthMismatch, (CoreText.BodyTooShort)))
                if verifySha256 then
                    match item.Sha256 with
                    | None -> ()
                    | Some advertised ->
                        let actual = hash.GetHashAndReset()
                        let matches =
                            try
                                let expected = if advertised.Length = 64 then Convert.FromHexString advertised else Convert.FromBase64String advertised
                                CryptographicOperations.FixedTimeEquals(ReadOnlySpan actual, ReadOnlySpan expected)
                            with :? FormatException -> false
                        if not matches then raise (StorageException(FailureCode.ChecksumMismatch, (CoreText.ChecksumMismatch)))
                do! output.FlushAsync ct
                return count
            }
            ct.ThrowIfCancellationRequested()
            item.Modified |> Option.iter (fun date -> File.SetLastWriteTimeUtc(temporary, date.UtcDateTime))
            item.Accessed |> Option.iter (fun date -> File.SetLastAccessTimeUtc(temporary, date.UtcDateTime))
            File.Move(temporary, destination, false)
            return { Id = item.Id; FileName = item.FileName; BytesTransferred = count; SavedPath = Some destination }
        finally
            try File.Delete temporary
            with
            | :? IOException as error -> Diagnostics.Debug.WriteLine error.Message
            | :? UnauthorizedAccessException as error -> Diagnostics.Debug.WriteLine error.Message
    }

/// Reports actual stream consumption without buffering the entire source.
type internal ProgressReadStream(inner: Stream, report: int64 -> unit) =
    inherit Stream()
    let mutable count = 0L
    let consumed read =
        count <- count + int64 read
        report count
        read
    override _.CanRead = inner.CanRead
    override _.CanSeek = false
    override _.CanWrite = false
    override _.Length = inner.Length
    override _.Position with get () = count and set _ = raise (NotSupportedException())
    override _.Flush() = ()
    override _.Read(buffer, offset, length) = inner.Read(buffer, offset, length) |> consumed
    override _.ReadAsync(buffer: Memory<byte>, ct) = ValueTask<int>(task {
        let! read = inner.ReadAsync(buffer, ct)
        return consumed read
    })
    override this.ReadAsync(buffer: byte array, offset, length, ct) =
        this.ReadAsync(Memory(buffer, offset, length), ct).AsTask()
    override _.Seek(_, _) = raise (NotSupportedException())
    override _.SetLength _ = raise (NotSupportedException())
    override _.Write(_, _, _) = raise (NotSupportedException())
    // Ownership stays with the caller; StreamContent may dispose this wrapper.
