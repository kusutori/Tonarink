using System.Text.Json.Serialization;

namespace Tonarink.Application;

[JsonSerializable(typeof(TonarinkSettings))]
[JsonSerializable(typeof(IosSharePayload))]
public sealed partial class TonarinkJsonContext : JsonSerializerContext;
