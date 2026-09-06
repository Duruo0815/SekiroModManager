using System.Text.Encodings.Web;
using System.Text.Json;

namespace SekiroModManager.Core;

/// <summary>全局 JSON 序列化选项（camelCase、中文不转义、缩进）。</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
