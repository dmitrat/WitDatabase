using System.Text.Json.Serialization;

namespace OutWit.Database.Core.Indexes;

[JsonSerializable(typeof(IndexMetadata))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class IndexMetadataJsonContext : JsonSerializerContext;
