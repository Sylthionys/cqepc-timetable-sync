using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Presentation.Wpf.Tests;

public sealed class BrandAssetTests
{
    [Fact]
    public void AppIconIncludesExplorerFriendlyMultiSizeFrames()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var iconPath = Path.Combine(
            repositoryRoot,
            "src",
            "CQEPC.TimetableSync.Presentation.Wpf",
            "Assets",
            "Brand",
            "app-icon.ico");

        File.Exists(iconPath).Should().BeTrue();

        var bytes = File.ReadAllBytes(iconPath);
        var imageCount = BitConverter.ToUInt16(bytes, 4);
        imageCount.Should().BeGreaterThanOrEqualTo((ushort)7);

        var sizes = new HashSet<int>();
        for (var index = 0; index < imageCount; index++)
        {
            var entryOffset = 6 + (index * 16);
            var width = bytes[entryOffset] == 0 ? 256 : bytes[entryOffset];
            var height = bytes[entryOffset + 1] == 0 ? 256 : bytes[entryOffset + 1];
            width.Should().Be(height, "the icon should expose square frames for shell scaling");
            sizes.Add(width);
        }

        sizes.Should().Contain([16, 24, 32, 48, 64, 128, 256]);
    }
}
