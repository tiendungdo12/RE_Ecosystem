using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;

namespace RE_CommandCenter.Core.Settings
{
    public class GlobalSettings
    {
        [JsonPropertyName("app_theme")]
        public ElementTheme AppTheme { get; set; } = ElementTheme.Default;

        [JsonPropertyName("backdrop_material")]
        public string BackdropMaterial { get; set; } = "Mica"; // Options: "Mica", "MicaAlt", "Acrylic", "None"

        [JsonPropertyName("use_system_accent_color")]
        public bool UseSystemAccentColor { get; set; } = true;

        [JsonPropertyName("custom_accent_color")]
        public string CustomAccentColor { get; set; } = "#FF0078D7"; // Default Windows Blue
    }
}
