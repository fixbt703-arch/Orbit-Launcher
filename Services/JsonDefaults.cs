using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrbitLauncher.Services;

internal static class JsonDefaults
{
	public static JsonSerializerOptions Options { get; } = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};
}
