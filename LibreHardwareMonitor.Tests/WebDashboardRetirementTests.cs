using System;
using System.IO;
using System.Linq;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public class WebDashboardRetirementTests
{
    [Fact]
    public void RootDashboard_ExposesViewThemeAndNoPreviewLink()
    {
        string html = ReadResource("LibreHardwareMonitor.Windows.Forms.Resources.Web.index.html");

        Assert.Contains("id=\"viewTheme\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"standard\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"cardTruth\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/data.json\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/metrics\"", html, StringComparison.Ordinal);
        Assert.False(html.Contains("/dash/cardtruth", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CardTruthPreviewAssets_AreNotEmbedded()
    {
        string[] names = typeof(HttpServer).Assembly.GetManifestResourceNames();

        Assert.DoesNotContain(names, name => name.Contains(".Resources.WebDash.cardtruth.", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("/dash/cardtruth")]
    [InlineData("/dash/cardtruth/")]
    public void CardTruthPreviewPaths_MapOnlyToMissingStableResources(string path)
    {
        Assert.True(HttpServer.TryMapStableWebResource(path, out string resource, out _));
        Assert.StartsWith("Web.dash.cardtruth", resource, StringComparison.Ordinal);
        Assert.DoesNotContain(".WebDash.", resource, StringComparison.OrdinalIgnoreCase);

        string fullResourceName = typeof(HttpServer).Assembly.GetName().Name + ".Resources." + resource;
        string[] names = typeof(HttpServer).Assembly.GetManifestResourceNames();
        Assert.DoesNotContain(names, name => string.Equals(name.Replace('\\', '.'), fullResourceName, StringComparison.Ordinal));
    }

    private static string ReadResource(string name)
    {
        using Stream stream = typeof(HttpServer).Assembly.GetManifestResourceStream(name);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
