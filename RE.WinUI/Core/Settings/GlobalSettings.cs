using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;

namespace RE.WinUI.Core.Settings
{
    public class GlobalSettings
    {
        [JsonPropertyName("app_theme")] public ElementTheme AppTheme { get; set; } = ElementTheme.Default;

        [JsonPropertyName("backdrop_material")]
        public string BackdropMaterial { get; set; } = "Mica"; // Options: "Mica", "MicaAlt", "Acrylic", "None"

        [JsonPropertyName("use_system_accent_color")]
        public bool UseSystemAccentColor { get; set; } = true;

        [JsonPropertyName("custom_accent_color")]
        public string CustomAccentColor { get; set; } = "#FF0078D7"; // Default Windows Blue

        [JsonPropertyName("app_language")]
        public string AppLanguage { get; set; } = "default"; // Options: "default", "en-US", "vi-VN"

        [JsonPropertyName("ai_provider")]
        public string AiProvider { get; set; } = "Antigravity"; // Options: "Antigravity", "Ollama"

        [JsonPropertyName("ollama_port")] public string OllamaPort { get; set; } = "11434";

        [JsonPropertyName("ollama_model")] public string OllamaModel { get; set; } = "deepseek-v4-flash";

        [JsonPropertyName("antigravity_model")]
        public string AntigravityModel { get; set; } = "flash"; // Options: "flash_lite", "flash", "pro"

        [JsonPropertyName("is_antigravity_logged_in")]
        public bool IsAntigravityLoggedIn { get; set; } = false;

        [JsonPropertyName("antigravity_account_email")]
        public string AntigravityAccountEmail { get; set; } = string.Empty;

        [JsonPropertyName("last_selected_provider")]
        public string LastSelectedProvider { get; set; } = "Antigravity";

        [JsonPropertyName("last_selected_model")]
        public string LastSelectedModel { get; set; } = string.Empty;

        [JsonPropertyName("antigravity_cached_models")]
        public string AntigravityCachedModelsJson { get; set; } = "[]";

        [JsonPropertyName("ollama_cached_models")]
        public string OllamaCachedModelsJson { get; set; } = "[]";

        [JsonPropertyName("api_keys")] public System.Collections.Generic.List<ApiKeyItem> ApiKeys { get; set; } = new();
    }

    public class ApiKeyItem
    {
        [JsonPropertyName("id")] public string Id { get; set; } = System.Guid.NewGuid().ToString();

        [JsonPropertyName("provider")]
        public string Provider { get; set; } =
            "Ollama Cloud"; // "Ollama Cloud", "OpenAI", "Anthropic", "Google AI", "Groq", "Custom"

        [JsonIgnore] public string Key { get; set; } = string.Empty;
        [JsonPropertyName("secret_reference")] public string SecretReference { get; set; } = string.Empty;

        [JsonPropertyName("note")] public string Note { get; set; } = string.Empty;

        [JsonPropertyName("created_at")] public System.DateTime CreatedAt { get; set; } = System.DateTime.Now;
    }
}
