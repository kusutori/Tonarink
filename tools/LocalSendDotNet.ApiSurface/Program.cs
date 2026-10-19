using System.Security.Cryptography;
using System.Text;
using LocalSendDotNet;
using LocalSendDotNet.ApiSurface;

var surface = PublicApiSurface.Create(typeof(LocalSendNode).Assembly);
Console.Write(args.Contains("--hash", StringComparer.Ordinal)
    ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(surface)))
    : surface);
