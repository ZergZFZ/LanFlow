using System.IO;
using System.Text;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;
using Xunit;

namespace LanFlow.Core.Tests;

public class ConfigDocumentSerializerTests
{
    [Fact]
    public void Serialize_ProducesUtf8WithoutBom()
    {
        var config = new AppConfig();
        config.Settings.Hotkey = "Ctrl+Alt+Space";

        byte[] bytes = ConfigDocumentSerializer.Serialize(config);

        Assert.NotEmpty(bytes);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.StartsWith("{", Encoding.UTF8.GetString(bytes).TrimStart());
    }

    [Fact]
    public void SerializeThenDeserialize_RoundTripsSettings()
    {
        var config = new AppConfig();
        config.Settings.Hotkey = "Ctrl+Shift+X";
        config.Settings.GroupHoverDelayMs = 320;
        config.Settings.IconSize = 56;

        byte[] bytes = ConfigDocumentSerializer.Serialize(config);
        using var stream = new MemoryStream(bytes);
        AppConfig restored = ConfigDocumentSerializer.Deserialize(stream);

        Assert.Equal("Ctrl+Shift+X", restored.Settings.Hotkey);
        Assert.Equal(320, restored.Settings.GroupHoverDelayMs);
        Assert.Equal(56, restored.Settings.IconSize);
    }

    [Fact]
    public void Deserialize_EmptyJsonObject_ReturnsDefaults()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{}"));

        AppConfig config = ConfigDocumentSerializer.Deserialize(stream);

        Assert.NotNull(config.Settings);
    }

    [Fact]
    public void SerializedPayload_MatchesConfigStoreOutput()
    {
        string directory = Path.Combine(Path.GetTempPath(), "lanflow-serializer-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConfigStore(configDirectory: directory);
            var config = store.Load();
            config.Settings.GroupHoverDelayMs = 180;
            store.Save(config);

            byte[] written = File.ReadAllBytes(store.ConfigPath);
            byte[] serialized = ConfigDocumentSerializer.Serialize(config);

            Assert.Equal(serialized, written);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
