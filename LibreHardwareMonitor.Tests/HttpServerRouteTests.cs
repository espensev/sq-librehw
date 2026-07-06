using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public class HttpServerRouteTests
{
    [Theory]
    [InlineData("/dash/cardtruth/", "WebDash.cardtruth.index.html", "html")]
    [InlineData("/dash/cardtruth", "WebDash.cardtruth.index.html", "html")]
    [InlineData("/dash/cardtruth/console.js", "WebDash.cardtruth.console.js", "js")]
    [InlineData("/dash/cardtruth/console.css", "WebDash.cardtruth.console.css", "css")]
    [InlineData("/dash/cardtruth/SensorPanel.js", "WebDash.cardtruth.SensorPanel.js", "js")]
    public void PreviewRoute_MapsCardTruthAssets(string path, string expectedResource, string expectedExt)
    {
        Assert.True(HttpServer.TryMapDashboardPreviewResource(path, out string resource, out string ext));
        Assert.Equal(expectedResource, resource);
        Assert.Equal(expectedExt, ext);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/console.js")]
    [InlineData("/data.json")]
    [InlineData("/metrics")]
    [InlineData("/Sensor")]
    [InlineData("/ResetAllMinMax")]
    [InlineData("/images_icon/computer.png")]
    public void PreviewRoute_IgnoresStableAndApiPaths(string path)
    {
        Assert.False(HttpServer.TryMapDashboardPreviewResource(path, out _, out _));
    }

    [Theory]
    [InlineData("/dash/missing/")]
    [InlineData("/dash/card-truth/")]
    [InlineData("/dash/cardtruth/../console.js")]
    public void PreviewRoute_InvalidOrMissingPaths_MapToMissingResource(string path)
    {
        Assert.True(HttpServer.TryMapDashboardPreviewResource(path, out string resource, out string ext));
        Assert.StartsWith("WebDash.__missing__.", resource);
        Assert.Equal("html", ext);
    }
}
