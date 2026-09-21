using System.Text.Json.Serialization;

namespace SDA.Desktop.Models
{
    public sealed class AppSettings
    {
        [JsonPropertyName("maFilesDirectory")]
        public string MaFilesDirectory { get; set; }
    }
}
