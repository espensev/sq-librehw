using System;
using System.IO;
using System.Linq;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public class WebDashboardRetirementTests
{
    [Fact]
    public void RootDashboard_ExposesStandardAndStudioAndNoPreviewLink()
    {
        string html = ReadResource("LibreHardwareMonitor.Windows.Forms.Resources.Web.index.html");

        Assert.Contains("id=\"viewTheme\"", html, StringComparison.Ordinal);
        Assert.Contains("<span>Dashboard</span>", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Dashboard\"", html, StringComparison.Ordinal);
        Assert.Contains("<option value=\"standard\">Standard</option>", html, StringComparison.Ordinal);
        Assert.Contains("<option value=\"cardTruth\">Studio</option>", html, StringComparison.Ordinal);
        string[] studioIds =
        {
            "studioView",
            "studioHealth",
            "studioAlertStatus",
            "studioFocus",
            "studioSystems",
            "studioNetwork",
            "studioCustomize",
            "studioAccent",
            "studioCanvas",
            "studioCanvasOpacity",
            "studioCanvasOpacityValue",
            "studioDensity",
            "studioFocusLayout",
            "studioFocusCount",
            "studioShowSparklines",
            "studioShowSystems",
            "studioShowNetwork",
            "studioReset",
        };
        foreach (string id in studioIds)
            Assert.Contains($"id=\"{id}\"", html, StringComparison.Ordinal);
        Assert.Contains("<option value=\"coral\">Coral</option>", html, StringComparison.Ordinal);
        Assert.Contains("<option value=\"rose\">Rose</option>", html, StringComparison.Ordinal);
        Assert.Contains("<option value=\"amber\">Amber</option>", html, StringComparison.Ordinal);
        Assert.Contains("<option value=\"plum\">Plum</option>", html, StringComparison.Ordinal);
        Assert.Contains("<option value=\"ember\">Ember</option>", html, StringComparison.Ordinal);
        Assert.Contains("<option value=\"strata\">Strata</option>", html, StringComparison.Ordinal);
        Assert.Contains("<option value=\"plain\">Plain</option>", html, StringComparison.Ordinal);
        Assert.Contains("id=\"studioCanvasOpacity\" min=\"0\" max=\"100\"", html, StringComparison.Ordinal);
        Assert.Contains("<option value=\"spotlight\">Spotlight</option>", html, StringComparison.Ordinal);
        Assert.Contains("<option value=\"grid\">Even grid</option>", html, StringComparison.Ordinal);
        Assert.Contains("id=\"studioAlertStatus\" role=\"status\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/data.json\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/metrics\"", html, StringComparison.Ordinal);
        Assert.False(html.Contains("/dash/cardtruth", StringComparison.OrdinalIgnoreCase));

        string css = ReadResource("LibreHardwareMonitor.Windows.Forms.Resources.Web.console.css");
        Assert.Contains("data-studio-canvas=\"ember\"", css, StringComparison.Ordinal);
        Assert.Contains("data-studio-canvas=\"strata\"", css, StringComparison.Ordinal);
        Assert.Contains("data-studio-canvas=\"plain\"", css, StringComparison.Ordinal);
        Assert.Contains("data-view-theme=\"cardTruth\"][data-studio-canvas=\"strata\"] header", css, StringComparison.Ordinal);
        Assert.Contains("data-view-theme=\"cardTruth\"][data-studio-canvas=\"plain\"] header", css, StringComparison.Ordinal);
        Assert.Contains("data-studio-focus-layout=\"spotlight\"", css, StringComparison.Ordinal);
        Assert.Contains("data-studio-sparklines=\"false\"", css, StringComparison.Ordinal);
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
