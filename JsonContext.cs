using System.Text.Json.Serialization;
using RemoteCtl.Models;

namespace RemoteCtl;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(List<Server>))]
[JsonSerializable(typeof(List<RecentEntry>))]
internal partial class JsonContext : JsonSerializerContext { }
