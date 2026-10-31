using ArteInventariado.Infrastructure.Installation;

namespace ArteInventariado.Tests;

public sealed class MachineWideBootstrapArgumentReaderTests
{
    [Fact]
    public void InstallSourcePathWithSpaces_IsReassembledUntilNextSwitch()
    {
        var value = MachineWideBootstrapArgumentReader.GetPathValue(
        [
            "--machine-bootstrap",
            "--install-source",
            @"C:\PortfolioDemo",
            "Documents",
            "Installer",
            "Packages",
            "extracted",
            "--desktop-directory",
            @"C:\PortfolioDemo",
            "Desktop"
        ],
            "--install-source");

        Assert.Equal(
            @"C:\PortfolioDemo Documents Installer Packages extracted",
            value);
    }

    [Fact]
    public void QuotedPathValue_RemainsUnquotedAndDoesNotConsumeFollowingSwitch()
    {
        var value = MachineWideBootstrapArgumentReader.GetPathValue(
        [
            "--install-source",
            @"""C:\PortfolioDemo\Documents\Installer Packages\extracted""",
            "--legacy-root",
            @"C:\legacy"
        ],
            "--install-source");

        Assert.Equal(@"C:\PortfolioDemo\Documents\Installer Packages\extracted", value);
    }
}
