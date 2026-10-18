namespace LocalSendDotNet.FSharp

open LocalSendDotNet.Resources

open System
open System.Net
open System.Net.Sockets
open System.Threading

type ConfigurationError = { Field: string; Message: string }

type NodeOptions =
    { Alias: string
      DataDirectory: string
      DownloadDirectory: string
      DeviceModel: string option
      DeviceType: DeviceType
      Port: int
      MulticastAddress: IPAddress
      NetworkWhitelist: string list option
      NetworkBlacklist: string list option
      EnableHttps: bool
      ReceivePin: string option
      MaxConcurrentTransfers: int
      MaxConcurrentFileUploads: int
      DiscoveryTimeout: TimeSpan
      RequestTimeout: TimeSpan
      UploadTimeout: TimeSpan
      CancelRequestTimeout: TimeSpan
      IncomingDecisionTimeout: TimeSpan
      IncomingTransferTimeout: TimeSpan
      PinLockoutDuration: TimeSpan
      AnnouncementInterval: TimeSpan
      DeviceExpiration: TimeSpan
      MaxIncomingItemsPerTransfer: int
      MaxIncomingTransferBytes: int64
      MaxPrepareRequestBytes: int64 }

/// Only validated options cross into the transport/lifecycle implementation.
type ValidatedOptions = private ValidatedOptions of NodeOptions

[<RequireQualifiedAccess>]
module NodeOptions =
    let defaults alias dataDirectory downloadDirectory =
        { Alias = alias; DataDirectory = dataDirectory; DownloadDirectory = downloadDirectory
          DeviceModel = Some(string Environment.OSVersion.Platform); DeviceType = DeviceType.Desktop
          Port = 53317; MulticastAddress = IPAddress.Parse "224.0.0.167"
          NetworkWhitelist = None; NetworkBlacklist = None; EnableHttps = true; ReceivePin = None
          MaxConcurrentTransfers = 4; MaxConcurrentFileUploads = 8
          DiscoveryTimeout = TimeSpan.FromMilliseconds 500.; RequestTimeout = TimeSpan.FromSeconds 30.
          UploadTimeout = TimeSpan.FromHours 12.; CancelRequestTimeout = TimeSpan.FromSeconds 5.
          IncomingDecisionTimeout = TimeSpan.FromMinutes 2.; IncomingTransferTimeout = TimeSpan.FromMinutes 30.
          PinLockoutDuration = TimeSpan.FromMinutes 1.; AnnouncementInterval = TimeSpan.FromSeconds 30.
          DeviceExpiration = TimeSpan.FromMinutes 2.; MaxIncomingItemsPerTransfer = 10000
          MaxIncomingTransferBytes = Int64.MaxValue; MaxPrepareRequestBytes = 4L * 1024L * 1024L }

    let validate (options: NodeOptions) =
        // These checks are independent: report all invalid fields in one pass.
        let errors =
            [ for field, value in [ "Alias", options.Alias; "DataDirectory", options.DataDirectory; "DownloadDirectory", options.DownloadDirectory ] do
                  if String.IsNullOrWhiteSpace value then yield { Field = field; Message = (CoreText.CannotBlank) }
              if options.Port < 1 || options.Port > 65535 then
                  yield { Field = "Port"; Message = (CoreText.PortRange) }
              for field, value in
                  [ "MaxConcurrentTransfers", int64 options.MaxConcurrentTransfers
                    "MaxConcurrentFileUploads", int64 options.MaxConcurrentFileUploads
                    "MaxIncomingItemsPerTransfer", int64 options.MaxIncomingItemsPerTransfer
                    "MaxIncomingTransferBytes", options.MaxIncomingTransferBytes
                    "MaxPrepareRequestBytes", options.MaxPrepareRequestBytes ] do
                  if value <= 0L then yield { Field = field; Message = (CoreText.MustPositive) }
              for field, value in
                  [ "DiscoveryTimeout", options.DiscoveryTimeout; "RequestTimeout", options.RequestTimeout
                    "UploadTimeout", options.UploadTimeout; "CancelRequestTimeout", options.CancelRequestTimeout
                    "IncomingDecisionTimeout", options.IncomingDecisionTimeout; "IncomingTransferTimeout", options.IncomingTransferTimeout ] do
                  if value <= TimeSpan.Zero && value <> Timeout.InfiniteTimeSpan then
                      yield { Field = field; Message = (CoreText.TimeoutPositive) }
              for field, value in
                  [ "PinLockoutDuration", options.PinLockoutDuration; "AnnouncementInterval", options.AnnouncementInterval
                    "DeviceExpiration", options.DeviceExpiration ] do
                  if value <= TimeSpan.Zero then yield { Field = field; Message = (CoreText.MustPositive) }
              if options.DeviceExpiration <= options.AnnouncementInterval then
                  yield { Field = "DeviceExpiration"; Message = (CoreText.ExpirationGreater) }
              if options.MulticastAddress.AddressFamily <> AddressFamily.InterNetwork then
                  yield { Field = "MulticastAddress"; Message = (CoreText.MulticastIPv4) }
              match options.ReceivePin with
              | Some pin when String.IsNullOrWhiteSpace pin -> yield { Field = "ReceivePin"; Message = (CoreText.PinBlank) }
              | Some _ | None -> () ]
        if List.isEmpty errors then Ok(ValidatedOptions options) else Error errors

    let internal value (ValidatedOptions options) = options
